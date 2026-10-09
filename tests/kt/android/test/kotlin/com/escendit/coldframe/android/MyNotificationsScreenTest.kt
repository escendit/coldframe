package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsOff
import androidx.compose.ui.test.assertIsOn
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isDialog
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.notifications.NotificationSettingsActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.notifications.NotificationControl
import com.escendit.coldframe.core.notifications.NotificationSettingsNotice
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.notifications.NotificationTimeZone
import com.escendit.coldframe.core.notifications.NotificationWindow
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.notifications.SiteNotificationSettings
import com.escendit.coldframe.core.signin.SignInState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** States of My notifications shared by the screen tests and the snapshots. */
object NotificationStates {
    /** With the current Site, the device's zone proposed and not confirmed yet. */
    val unconfirmed =
        NotificationSettingsState.Ready(
            window = NotificationWindow("07:00", "22:00"),
            draft = NotificationWindow("07:00", "22:00"),
            windowWorking = false,
            windowSaved = false,
            timeZone = NotificationTimeZone("Europe/Zurich", null, changing = false),
            site =
                SiteNotificationSettings(
                    site = homeSite(),
                    muted = false,
                    reminderCadence = null,
                    siteReminderCadence = ReminderCadence.Daily,
                ),
            notice = null,
            noticeControl = null,
        )

    /** Without a current Site, the zone chosen: only the window and the time zone. */
    val confirmedWithoutSite =
        unconfirmed.copy(
            timeZone = NotificationTimeZone("Europe/Zurich", "Europe/Zurich", changing = false),
            site = null,
        )
}

/** My notifications (Story 6.3): UX-DR72 with its controls UX-DR47, UX-DR48, UX-DR49 and UX-DR50. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class MyNotificationsScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var state by mutableStateOf<NotificationSettingsState>(NotificationSettingsState.Idle)

    private val actions =
        NotificationSettingsActions(
            load = { calls += "load" },
            retry = { calls += "retry" },
            setWindowFrom = { calls += "from $it" },
            setWindowTo = { calls += "to $it" },
            saveWindow = { calls += "save" },
            confirmTimeZone = { calls += "confirm" },
            changeTimeZone = { calls += "change" },
            pickTimeZone = { calls += "pick $it" },
            timeZones = { listOf("America/New_York", "Europe/Zurich", "Pacific/Auckland") },
            setMuted = { calls += "mute $it" },
            setReminderCadence = { calls += "cadence $it" },
        )

    private val ready = NotificationStates.unconfirmed

    private fun show(
        next: NotificationSettingsState,
        fontScale: Float = 1f,
    ) {
        state = next
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    notifications = state,
                    notificationsActions = actions,
                )
            }
        }
    }

    /** From the Garden through the Settings tab to My notifications. */
    private fun open(
        next: NotificationSettingsState,
        fontScale: Float = 1f,
    ) {
        show(next, fontScale)
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("My notifications").performClick()
        compose.onNode(isHeading().and(hasText("My notifications"))).assertExists()
    }

    private val switch = SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Switch)

    // Navigation

    @Test
    fun `UX-DR72 My notifications is the first Settings row, above Site settings`() {
        show(ready)

        compose.onNodeWithText("Settings").performClick()

        val row = compose.onNodeWithText("My notifications").fetchSemanticsNode()
        val siteSettings = compose.onNodeWithText("Site settings").fetchSemanticsNode()
        assertTrue(row.boundsInRoot.top < siteSettings.boundsInRoot.top)
        compose.onNodeWithText("Notification Window, time zone, mute and Reminders").assertExists()
        assertTrue(calls.isEmpty())
    }

    @Test
    fun `UX-DR72 every entry of My notifications reads the settings again, and back returns to Settings`() {
        open(ready)

        assertEquals(listOf("load"), calls)
        compose.onNodeWithContentDescription("Back").performClick()
        compose.onNode(isHeading().and(hasText("Settings"))).assertExists()
        compose.onNodeWithText("My notifications").performClick()
        compose.waitForIdle()

        assertEquals(listOf("load", "load"), calls)
    }

    @Test
    fun `UX-DR72 the surface holds the window, the time zone, the mute and my Reminder cadence, in that order`() {
        open(ready)

        val tops =
            listOf("Notification Window", "Time zone", "Mute Home garden", "My Reminder cadence").map {
                compose
                    .onNodeWithText(it)
                    .fetchSemanticsNode()
                    .positionInRoot.y
            }
        assertEquals(tops.sorted(), tops)
        compose.onNode(isHeading().and(hasText("Notification Window"))).assertExists()
    }

    @Test
    fun `UX-DR72 without a current Site only the window and the time zone show`() {
        open(NotificationStates.confirmedWithoutSite)

        compose.onNodeWithText("Notification Window").assertExists()
        compose.onNodeWithText("Your time zone is Europe/Zurich.").assertExists()
        compose.onAllNodes(switch).assertCountEquals(0)
        compose.onAllNodesWithText("My Reminder cadence").assertCountEquals(0)
        compose.onAllNodesWithText("Mute", substring = true).assertCountEquals(0)
    }

    @Test
    fun `UX-DR72 loading shows only the background and a failed load its notice with Try again`() {
        open(NotificationSettingsState.Loading)
        compose.onAllNodesWithText("Notification Window").assertCountEquals(0)

        state = NotificationSettingsState.Failed(NotificationSettingsNotice.Unreachable)
        compose
            .onNodeWithText("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.")
            .assertExists()
        compose.onNodeWithText("TRY AGAIN").performClick()
        assertEquals(listOf("load", "retry"), calls)

        state = NotificationSettingsState.Failed(NotificationSettingsNotice.Certificate)
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)

        state = NotificationSettingsState.Failed(NotificationSettingsNotice.Unexpected)
        compose
            .onNodeWithText("Your Server couldn't read your notification settings. Nothing was changed.")
            .assertExists()
    }

    // Notification Window control (UX-DR47)

    @Test
    fun `UX-DR47 the Notification Window control shows both times, the big range and the helper naming its start`() {
        open(ready.copy(draft = NotificationWindow("06:30", "21:00")))

        compose.onNode(hasText("From").and(hasText("06:30")).and(hasClickAction())).assertExists()
        compose.onNode(hasText("To").and(hasText("21:00")).and(hasClickAction())).assertExists()
        compose.onNodeWithText("06:30 to 21:00").assertExists()
        compose.onNodeWithText("Outside this window, anything waits for one summary at 06:30.").assertExists()
    }

    @Test
    fun `UX-DR47 the 24 h bar is decorative and hidden from TalkBack`() {
        open(ready)

        // As TalkBack gets it: the merged tree, where the bar is one node that says nothing.
        val bar = compose.onNodeWithTag("notification-window-bar").fetchSemanticsNode()
        assertTrue(bar.size.width > 0 && bar.size.height > 0)
        assertEquals("", bar.spokenLabel())
        assertTrue(bar.children.isEmpty())
        // The axis is part of the picture: no hour is read out.
        for (hour in listOf("00", "06", "12", "18", "24")) compose.onAllNodesWithText(hour).assertCountEquals(0)
    }

    @Test
    fun `UX-DR47 a time field opens a picker, and Set time sends the time to the core`() {
        open(ready)

        compose.onNode(hasText("From").and(hasClickAction())).performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(1)
        compose.onNodeWithText("Window starts at").assertExists()
        compose.onNodeWithText("SET TIME").performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(0)

        compose.onNode(hasText("To").and(hasClickAction())).performClick()
        compose.onNodeWithText("Window ends at").assertExists()
        compose.onNodeWithText("CANCEL").performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(0)

        assertEquals(listOf("load", "from 07:00"), calls)
    }

    @Test
    fun `UX-DR47 Save sends the window, reads Saving in place while working and then says Saved`() {
        open(ready.copy(draft = NotificationWindow("06:30", "22:00")))

        compose.onNodeWithText("SAVE").performScrollTo().performClick()
        assertEquals(listOf("load", "save"), calls)

        state = ready.copy(draft = NotificationWindow("06:30", "22:00"), windowWorking = true)
        compose.onNodeWithText("SAVING…").assertExists()
        compose.onAllNodesWithText("SAVE").assertCountEquals(0)

        state = ready.copy(window = NotificationWindow("06:30", "22:00"), windowSaved = true)
        val saved = compose.onNodeWithText("Saved.").fetchSemanticsNode()
        assertEquals(LiveRegionMode.Polite, saved.config.getOrNull(SemanticsProperties.LiveRegion))
    }

    @Test
    fun `UX-DR47 a window that does not start before it ends says so`() {
        open(ready.copy(draft = NotificationWindow("22:00", "07:00")))

        compose.onNodeWithText("The start must be before the end.").assertExists()
    }

    @Test
    fun `UX-DR47 a window that was not saved shows its notice with Try again`() {
        open(ready.copy(notice = NotificationSettingsNotice.NotSaved, noticeControl = NotificationControl.Window))

        compose.onNodeWithText("Not saved: your Server could not be reached. Nothing was changed.").assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertEquals(listOf("load", "retry"), calls)

        state = ready.copy(notice = NotificationSettingsNotice.Invalid, noticeControl = NotificationControl.Window)
        compose.onNodeWithText("Your Server did not accept this window. Nothing was changed.").assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    // Time-zone confirm panel (UX-DR48)

    @Test
    fun `UX-DR48 the time-zone panel proposes the device zone, and Confirm and Change go to the core`() {
        open(ready)

        compose.onNodeWithText("Is your time zone Europe/Zurich?").assertExists()
        compose.onNodeWithText("Used for your Notification Window.").assertExists()
        compose.onNodeWithText("CONFIRM").performScrollTo().performClick()
        compose.onNodeWithText("CHANGE").performScrollTo().performClick()

        assertEquals(listOf("load", "confirm", "change"), calls)
    }

    @Test
    fun `UX-DR48 a zone the User chose is named and is not asked again`() {
        open(ready.copy(timeZone = NotificationTimeZone("Europe/Zurich", "Pacific/Auckland", changing = false)))

        compose.onNodeWithText("Your time zone is Pacific/Auckland.").assertExists()
        compose.onAllNodesWithText("CONFIRM").assertCountEquals(0)
        compose.onNodeWithText("CHANGE").assertExists()
    }

    @Test
    fun `UX-DR48 Change shows the searchable list of IANA zones and a pick goes to the core`() {
        open(ready.copy(timeZone = NotificationTimeZone("Europe/Zurich", null, changing = true)))

        compose.onNodeWithText("America/New_York").assertExists()
        compose
            .onNode(hasSetTextAction().and(hasContentDescription("Search time zones")))
            .performTextReplacement("auck")
        compose.onAllNodesWithText("America/New_York").assertCountEquals(0)
        compose
            .onNode(hasText("Pacific/Auckland").and(hasClickAction()))
            .performSemanticsAction(SemanticsActions.OnClick)

        assertEquals(listOf("load", "pick Pacific/Auckland"), calls)
    }

    @Test
    fun `UX-DR48 without a detected zone the panel says so and shows the list at once`() {
        open(ready.copy(timeZone = NotificationTimeZone(null, null, changing = false)))

        compose.onNodeWithText("Your time zone could not be detected. Choose it from the list.").assertExists()
        compose.onAllNodesWithText("CONFIRM").assertCountEquals(0)
        compose.onNodeWithText("Europe/Zurich").assertExists()
    }

    @Test
    fun `UX-DR48 a zone the Server does not know says so under the panel`() {
        open(ready.copy(notice = NotificationSettingsNotice.Invalid, noticeControl = NotificationControl.TimeZone))

        compose
            .onNodeWithText("Your Server does not know this time zone. Nothing was changed. Choose another one.")
            .assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    // Mute toggle (UX-DR49)

    @Test
    fun `UX-DR49 Mute names the Site, is a Material switch and applies at once`() {
        open(ready)

        compose.onNode(switch).assert(hasText("Mute Home garden")).assertIsOff()
        compose.onNode(switch).performScrollTo().performClick()
        assertEquals(listOf("load", "mute true"), calls)

        state = ready.copy(site = ready.site!!.copy(muted = true))
        compose.onNode(switch).assertIsOn()
        compose.onNode(switch).performClick()
        assertEquals(listOf("load", "mute true", "mute false"), calls)
    }

    @Test
    fun `UX-DR49 a mute that was not saved shows its notice with Try again`() {
        open(ready.copy(notice = NotificationSettingsNotice.NotSaved, noticeControl = NotificationControl.Mute))

        compose.onNodeWithText("Not saved: your Server could not be reached. Nothing was changed.").assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertEquals(listOf("load", "retry"), calls)

        state = ready.copy(notice = NotificationSettingsNotice.SiteRefused, noticeControl = NotificationControl.Mute)
        compose.onNodeWithText("Home garden is no longer one of your Sites. Nothing was changed.").assertExists()
    }

    // My Reminder cadence (UX-DR50)

    @Test
    fun `UX-DR50 my Reminder cadence offers Use Site setting, Daily and Every 2 days, and names the Site setting`() {
        open(ready)

        compose.onNodeWithText("USE SITE SETTING").assertIsSelected()
        compose.onNodeWithText("DAILY").assertIsNotSelected()
        compose.onNodeWithText("EVERY 2 DAYS").assertIsNotSelected()
        compose.onNodeWithText("Site setting: Daily").assertExists()
        compose.onAllNodesWithText("NEVER").assertCountEquals(0)

        compose.onNodeWithText("EVERY 2 DAYS").performScrollTo().performClick()
        assertEquals(listOf("load", "cadence Every2Days"), calls)

        state =
            ready.copy(
                site =
                    ready.site!!.copy(
                        reminderCadence = ReminderCadence.Every2Days,
                        siteReminderCadence = ReminderCadence.Every2Days,
                    ),
            )
        compose.onNodeWithText("EVERY 2 DAYS").assertIsSelected()
        compose.onNodeWithText("Site setting: Every 2 days").assertExists()
        compose.onNodeWithText("USE SITE SETTING").performClick()
        assertEquals(listOf("load", "cadence Every2Days", "cadence null"), calls)
    }

    @Test
    fun `UX-DR50 a cadence that was not saved shows its notice with Try again`() {
        open(
            ready.copy(
                notice = NotificationSettingsNotice.NotSaved,
                noticeControl = NotificationControl.ReminderCadence,
            ),
        )

        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertEquals(listOf("load", "retry"), calls)
    }

    // Accessibility

    @Test
    fun `UX-DR96 UX-DR126 at font scale 2 nothing on My notifications is truncated or clipped`() {
        open(ready.copy(notice = NotificationSettingsNotice.NotSaved, noticeControl = NotificationControl.Window), 2f)

        compose.assertNothingOverflows("My notifications")
    }

    @Test
    fun `UX-DR98 UX-DR100 every control on My notifications has a role and a label and is at least 48 dp`() {
        open(ready)

        val minimum = with(compose.density) { 48.dp.toPx() } - 0.5f
        val controls =
            compose.allNodes().filter {
                it.isClickable || it.config.getOrNull(SemanticsProperties.ToggleableState) != null
            }
        assertTrue(controls.size >= 8)
        for (control in controls) {
            val label = control.spokenLabel()
            assertTrue(label.isNotBlank(), "a control without a label")
            assertTrue(control.role != null, "\"$label\" has no role")
            // The laid-out size: the surface scrolls, so a control below the fold has no touch bounds yet.
            val size = control.size
            assertTrue(size.width >= minimum && size.height >= minimum, "\"$label\" is $size px")
        }
    }
}
