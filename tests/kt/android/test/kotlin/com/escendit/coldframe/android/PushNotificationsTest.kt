package com.escendit.coldframe.android

import android.app.Application
import android.app.Notification
import android.app.NotificationManager
import android.content.Intent
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.push.ColdframeMessagingService
import com.escendit.coldframe.android.push.PushIntents
import com.escendit.coldframe.android.push.PushNotifications
import com.escendit.coldframe.core.push.PushPayload
import com.escendit.coldframe.core.push.PushTap
import com.google.firebase.FirebaseApp
import com.google.firebase.messaging.RemoteMessage
import org.json.JSONObject
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.Shadows.shadowOf
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * Story 6.5: the notification the Android app builds from the Server's FCM data message. The messages are the
 * fixtures of `packages/asyncapi`, which the Server's FCM channel is held to; no Firebase project is involved.
 */
@RunWith(AndroidJUnit4::class)
class PushNotificationsTest {
    private val context: Application = ApplicationProvider.getApplicationContext()
    private val manager = context.getSystemService(NotificationManager::class.java)
    private val shadow = shadowOf(manager)

    private fun fixture(kind: String): JSONObject =
        JSONObject(Repo.file("packages/asyncapi/fixtures/push.$kind.json").readText())

    private fun data(kind: String): Map<String, String> {
        val data = fixture(kind).getJSONObject("fcm").getJSONObject("message").getJSONObject("data")
        return data.keys().asSequence().associateWith { data.getString(it) }
    }

    private fun route(kind: String): PushTap {
        val route = fixture(kind).getJSONObject("route")
        return PushTap(route.getString("kind"), route.getString("siteId"), route.optString("lotId").ifEmpty { null })
    }

    private fun shown(kind: String): Notification {
        val data = data(kind)
        assertTrue(PushNotifications.show(context, data))
        return assertNotNull(shadow.getNotification(data.getValue("collapseId"), PushNotifications.NOTIFICATION_ID))
    }

    private fun Notification.text(key: String): String? = extras.getCharSequence(key)?.toString()

    @Test
    fun `UX-DR115 the notification shows the Server's title and body as they are`() {
        for (kind in listOf("alert", "reminder", "summary")) {
            val data = data(kind)

            val notification = shown(kind)

            assertEquals(data.getValue("title"), notification.text(Notification.EXTRA_TITLE), kind)
            assertEquals(data.getValue("body"), notification.text(Notification.EXTRA_TEXT), kind)
            // Every line of a summary can be read when the notification is expanded.
            assertEquals(data.getValue("body"), notification.text(Notification.EXTRA_BIG_TEXT), kind)
        }
        assertEquals("Tomatoes needs water", shown("alert").text(Notification.EXTRA_TITLE))
        assertEquals("Still ~20 % in the soil, your low is 30 %.", shown("reminder").text(Notification.EXTRA_TEXT))
    }

    @Test
    fun `UX-DR121 notifications are grouped per Site, with a group summary that names the Site`() {
        val siteId = data("alert").getValue("siteId")

        val alert = shown("alert")
        val summaryOfTheMorning = shown("summary")

        assertEquals(siteId, alert.group)
        assertEquals(siteId, summaryOfTheMorning.group)
        assertEquals(0, alert.flags and Notification.FLAG_GROUP_SUMMARY)
        val group =
            assertNotNull(
                shadow.getNotification(PushNotifications.groupTag(siteId), PushNotifications.GROUP_SUMMARY_ID),
            )
        assertEquals(siteId, group.group)
        assertTrue(group.flags and Notification.FLAG_GROUP_SUMMARY != 0)
        assertEquals("Home garden", group.text(Notification.EXTRA_SUB_TEXT))
        // One summary per Site, however many notifications it holds; it makes no sound of its own.
        assertEquals(3, shadow.size())
        assertEquals(Notification.GROUP_ALERT_CHILDREN, group.groupAlertBehavior)
    }

    @Test
    fun `UX-DR121 a repeated send replaces the earlier notification, its tag is the collapse identity`() {
        val data = data("alert")
        PushNotifications.show(context, data)

        PushNotifications.show(context, data + ("body" to "~15 % in the soil, your low is 30 %."))

        // The Alert and the Site's group summary.
        assertEquals(2, shadow.size())
        val notification = shadow.getNotification(data.getValue("collapseId"), PushNotifications.NOTIFICATION_ID)
        assertEquals("~15 % in the soil, your low is 30 %.", notification.text(Notification.EXTRA_TEXT))
        // It sounded when it first arrived, not again when it is replaced.
        assertEquals(Notification.FLAG_ONLY_ALERT_ONCE, notification.flags and Notification.FLAG_ONLY_ALERT_ONCE)

        // The Reminder of the next day has another collapse identity: it is a notification of its own.
        shown("reminder")
        assertEquals(3, shadow.size())
    }

    @Test
    fun `UX-DR121 the channel has the standard interruption level and shows no badge`() {
        val notification = shown("alert")

        val channel = assertNotNull(manager.getNotificationChannel(PushNotifications.CHANNEL))
        assertEquals(PushNotifications.CHANNEL, notification.channelId)
        assertEquals(NotificationManager.IMPORTANCE_DEFAULT, channel.importance)
        assertFalse(channel.canShowBadge())
        assertEquals("Alerts", channel.name.toString())
        assertEquals(1, manager.notificationChannels.size)
    }

    @Test
    fun `UX-DR121 UX-DR119 a notification carries no badge number and no actions`() {
        for (kind in listOf("alert", "reminder", "summary")) {
            val notification = shown(kind)

            assertEquals(0, notification.number, kind)
            assertEquals(Notification.BADGE_ICON_NONE, notification.badgeIconType, kind)
            assertNull(notification.actions, kind)
            assertTrue(notification.flags and Notification.FLAG_AUTO_CANCEL != 0, kind)
        }
    }

    @Test
    fun `UX-DR120 a tap starts the activity with the routing keys of the notification`() {
        for (kind in listOf("alert", "reminder", "summary")) {
            val intent = shadowOf(shown(kind).contentIntent).savedIntent

            assertEquals(MainActivity::class.java.name, intent.component?.className, kind)
            assertTrue(intent.flags and Intent.FLAG_ACTIVITY_SINGLE_TOP != 0, kind)
            val routing = assertNotNull(PushIntents.routing(intent), kind)
            assertEquals(data(kind).filterKeys { it in PushPayload.ROUTING_KEYS }, routing, kind)
            // What the core reads from the tap is where the contract says the tap must lead.
            assertEquals(route(kind), PushPayload.parse(routing)?.tap, kind)
        }
    }

    @Test
    fun `UX-DR120 a tap on the group summary leads to the overview of its Site`() {
        val siteId = data("alert").getValue("siteId")
        shown("alert")

        val group = shadow.getNotification(PushNotifications.groupTag(siteId), PushNotifications.GROUP_SUMMARY_ID)
        val tap = PushPayload.parse(PushIntents.routing(shadowOf(group.contentIntent).savedIntent).orEmpty())?.tap

        assertEquals(PushTap("summary", siteId, null), tap)
        assertFalse(tap!!.opensLot)
    }

    @Test
    fun `UX-DR120 only a tapped notification is read as one, and not again from Recents`() {
        val routing = mapOf("kind" to "summary", "siteId" to "site-home")

        assertEquals(routing, PushIntents.routing(PushIntents.open(context, routing)))
        assertNull(PushIntents.routing(null))
        assertNull(PushIntents.routing(Intent(Intent.ACTION_MAIN)))
        assertNull(PushIntents.routing(Intent(context, MainActivity::class.java).setAction(PushIntents.ACTION_OPEN)))
        val fromRecents = PushIntents.open(context, routing).addFlags(Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY)
        assertNull(PushIntents.routing(fromRecents))
    }

    @Test
    fun `UX-DR119 a message that is not a Coldframe push, or has no text, shows nothing`() {
        assertFalse(PushNotifications.show(context, mapOf("google.message_id" to "1")))
        assertFalse(PushNotifications.show(context, data("alert") - "title"))
        assertFalse(PushNotifications.show(context, data("alert") - "siteId"))

        assertEquals(0, shadow.size())
    }

    @Test
    fun `UX-DR115 the messaging service builds the notification from an FCM data message, without Firebase`() {
        val data = data("alert")
        val service = Robolectric.buildService(ColdframeMessagingService::class.java).create().get()

        service.onMessageReceived(RemoteMessage.Builder("coldframe@fcm.googleapis.com").setData(data).build())

        val notification =
            assertNotNull(shadow.getNotification(data.getValue("collapseId"), PushNotifications.NOTIFICATION_ID))
        assertEquals("Tomatoes needs water", notification.text(Notification.EXTRA_TITLE))
        assertEquals("~20 % in the soil, your low is 30 %.", notification.text(Notification.EXTRA_TEXT))
        assertEquals(data.getValue("siteId"), notification.group)
        // The test build has no Firebase properties: no Firebase app exists, and the service still works.
        assertTrue(FirebaseApp.getApps(context).isEmpty())
    }

    @Test
    fun `UX-DR115 a new FCM registration goes to the core, which is signed out and sends nothing`() {
        val service = Robolectric.buildService(ColdframeMessagingService::class.java).create().get()

        service.onNewToken("fcm-registration")

        assertEquals(0, shadow.size())
    }
}
