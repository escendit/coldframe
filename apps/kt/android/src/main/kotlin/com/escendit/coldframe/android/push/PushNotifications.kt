package com.escendit.coldframe.android.push

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Bundle
import com.escendit.coldframe.R
import com.escendit.coldframe.android.MainActivity
import com.escendit.coldframe.core.push.PushPayload

/**
 * Builds the notification of one push from its data (UX-DR121). The text is the Server's, shown as it is.
 *
 * - **Group**: the Site (`siteId`). A group summary named by `siteName` goes with it, because Android only shows
 *   a group that has one.
 * - **Tag**: `collapseId`, so a send that is repeated replaces the earlier notification, without sounding again.
 * - **Channel** [CHANNEL]: default importance, which is the standard interruption level, and no badge.
 * - No badge number and no actions. A tap opens [MainActivity] with the routing keys ([PushIntents]).
 */
object PushNotifications {
    /** The one notification channel: Threshold Alerts, their Reminders and the morning summary. */
    const val CHANNEL = "alerts"

    /** Every notification has the same ID; its tag tells them apart. */
    const val NOTIFICATION_ID = 1
    const val GROUP_SUMMARY_ID = 2

    /** The tag of the group summary of [siteId]. */
    fun groupTag(siteId: String): String = "site:$siteId"

    /** Shows the push of [data]; false when it is not a Coldframe push or carries no text. */
    fun show(
        context: Context,
        data: Map<String, String>,
    ): Boolean {
        val payload = PushPayload.parse(data) ?: return false
        val title = payload.title ?: return false
        val manager = context.getSystemService(NotificationManager::class.java) ?: return false
        ensureChannel(context, manager)
        val tag = payload.collapseId ?: payload.alertId ?: "${payload.kind}:${payload.siteId}"
        val notification =
            Notification
                .Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(payload.body)
                .setStyle(Notification.BigTextStyle().bigText(payload.body))
                .setGroup(payload.siteId)
                .setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN)
                // A send that is repeated replaces the notification in silence: it sounded the first time.
                .setOnlyAlertOnce(true)
                .setAutoCancel(true)
                .setContentIntent(PushIntents.pending(context, tag, data.filterKeys { it in PushPayload.ROUTING_KEYS }))
                .build()
        manager.notify(tag, NOTIFICATION_ID, notification)
        manager.notify(groupTag(payload.siteId), GROUP_SUMMARY_ID, groupSummary(context, payload))
        return true
    }

    /** The channel exists before the first notification, and its settings can be opened before one arrived. */
    fun ensureChannel(
        context: Context,
        manager: NotificationManager,
    ) {
        if (manager.getNotificationChannel(CHANNEL) != null) return
        val channel =
            NotificationChannel(
                CHANNEL,
                context.getString(R.string.push_channel_alerts),
                NotificationManager.IMPORTANCE_DEFAULT,
            )
        channel.setShowBadge(false)
        manager.createNotificationChannel(channel)
    }

    /** The summary of the Site's group: it names the Site, makes no sound of its own, and opens the overview. */
    private fun groupSummary(
        context: Context,
        payload: PushPayload,
    ): Notification {
        val overview = mapOf(PushPayload.KIND to PushPayload.KIND_SUMMARY, PushPayload.SITE_ID to payload.siteId)
        return Notification
            .Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(payload.siteName)
            .setSubText(payload.siteName)
            .setGroup(payload.siteId)
            .setGroupSummary(true)
            .setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN)
            .setAutoCancel(true)
            .setContentIntent(PushIntents.pending(context, groupTag(payload.siteId), overview))
            .build()
    }
}

/** The intent a tapped notification starts [MainActivity] with, and how the activity reads it. */
object PushIntents {
    const val ACTION_OPEN = "com.escendit.coldframe.push.OPEN"
    const val EXTRA_ROUTING = "com.escendit.coldframe.push.ROUTING"

    /** Opens the one activity, or brings it to the front, with the [routing] keys of the notification. */
    fun open(
        context: Context,
        routing: Map<String, String>,
    ): Intent {
        val extras = Bundle()
        routing.forEach { (key, value) -> extras.putString(key, value) }
        return Intent(context, MainActivity::class.java)
            .setAction(ACTION_OPEN)
            .addFlags(
                Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP,
            ).putExtra(EXTRA_ROUTING, extras)
    }

    /** One pending intent per notification [tag]; a replaced notification replaces its intent too. */
    fun pending(
        context: Context,
        tag: String,
        routing: Map<String, String>,
    ): PendingIntent =
        PendingIntent.getActivity(
            context,
            tag.hashCode(),
            open(context, routing),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

    /**
     * The routing keys [intent] carries, or `null` when it is not a tapped notification. An activity brought back
     * from Recents is started with its old intent again: that tap was handled already.
     */
    fun routing(intent: Intent?): Map<String, String>? {
        if (intent?.action != ACTION_OPEN) return null
        if (intent.flags and Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY != 0) return null
        val extras = intent.getBundleExtra(EXTRA_ROUTING) ?: return null
        return extras
            .keySet()
            .mapNotNull { key -> extras.getString(key)?.let { key to it } }
            .toMap()
            .takeIf { it.isNotEmpty() }
    }
}
