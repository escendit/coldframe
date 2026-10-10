package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.notifications.PushActions
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.notifications.NotificationSettingsNotice
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.push.PushPermission
import com.escendit.coldframe.core.push.PushRoute
import com.escendit.coldframe.core.push.PushState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SiteRole
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** States of push shared by the screen tests and the snapshots. */
object PushStates {
    /** The first landing on a Site overview: the one line of why, then the OS prompt. */
    val promptDue = PushState(PushPermission.Unknown, promptDue = true, route = null)

    /** The permission is denied or revoked. */
    val denied = PushState(PushPermission.Denied, promptDue = false, route = null)

    val granted = PushState(PushPermission.Granted, promptDue = false, route = null)
}

/**
 * Story 6.5 in the shell: the one line of why before the OS prompt (UX-DR122), the notifications-off notice in My
 * notifications and on the overview (UX-DR88), and the route of a tapped notification (UX-DR120). What shows is
 * the core's [PushState]; the shell only draws it.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class PushNoticesTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var push by mutableStateOf(PushState.None)
    private var lotDetail by mutableStateOf<LotDetailState>(LotDetailState.Idle)
    private var notifications by mutableStateOf<NotificationSettingsState>(NotificationStates.unconfirmed)

    private val actions =
        PushActions(
            ask = { calls += "ask" },
            openSettings = { calls += "settings" },
            routeHandled = {
                calls += "handled"
                push = push.copy(route = null)
            },
        )

    private val lotDetailActions =
        LotDetailActions(
            open = { lotId, name ->
                calls += "open $lotId $name"
                lotDetail = LotDetailFixtures.ready(LotDetailFixtures.ok)
            },
            close = {
                calls += "close"
                lotDetail = LotDetailState.Idle
            },
        )

    private fun show(
        next: PushState,
        role: SiteRole = SiteRole.Owner,
    ) {
        push = next
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(homeSite(role)),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                lots = LotFixtures.ready(role = role),
                now = { LotFixtures.now },
                lotDetail = lotDetail,
                lotDetailActions = lotDetailActions,
                notifications = notifications,
                push = push,
                pushActions = actions,
            )
        }
    }

    private fun openMyNotifications() {
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("My notifications").performClick()
        compose.onNode(isHeading().and(hasText("My notifications"))).assertExists()
    }

    private fun tab(label: String) =
        compose.onNode(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Tab).and(hasText(label)))

    private val why = "Coldframe tells you when a Lot needs water."
    private val off = "Notifications are off for Coldframe on this phone. You won't get Alerts."

    // The one line of why

    @Test
    fun `UX-DR122 the first landing on a Site overview shows the one line of why, and Continue asks`() {
        show(PushStates.promptDue)

        compose.onNodeWithText(why).assertExists()
        compose.onAllNodesWithText(off).assertCountEquals(0)

        compose.onNodeWithText("CONTINUE").performClick()

        assertEquals(listOf("ask"), calls)
    }

    @Test
    fun `UX-DR122 the why-line stands above the Lot tiles`() {
        show(PushStates.promptDue)

        val line = compose.onNodeWithText(why).fetchSemanticsNode()
        val tile = compose.onNodeWithText("Tomatoes", useUnmergedTree = true).fetchSemanticsNode()

        // Positions in the scrolling content: a tile below the window still has one.
        assertTrue(line.positionInRoot.y + line.size.height <= tile.positionInRoot.y)
    }

    @Test
    fun `UX-DR122 once the prompt was answered the why-line is gone, and a Member is asked like anyone`() {
        show(PushStates.promptDue, role = SiteRole.Member)
        compose.onNodeWithText(why).assertExists()

        push = PushStates.granted
        compose.waitForIdle()

        compose.onAllNodesWithText(why).assertCountEquals(0)
        compose.onAllNodesWithText(off).assertCountEquals(0)
    }

    @Test
    fun `UX-DR122 nothing about notifications shows before the core says so, and never outside the overview`() {
        show(PushState.None)
        compose.onAllNodesWithText(why).assertCountEquals(0)
        compose.onAllNodesWithText(off).assertCountEquals(0)

        push = PushStates.promptDue
        compose.onNodeWithText("Alerts").performClick()
        compose.onAllNodesWithText(why).assertCountEquals(0)
        compose.onNodeWithText("Devices").performClick()
        compose.onAllNodesWithText(why).assertCountEquals(0)
        openMyNotifications()
        compose.onAllNodesWithText(why).assertCountEquals(0)
    }

    // Notifications off

    @Test
    fun `UX-DR88 notifications off shows the notice above the tiles of the overview, with Open Settings`() {
        show(PushStates.denied)

        val notice = compose.onNodeWithText(off).fetchSemanticsNode()
        val tile = compose.onNodeWithText("Tomatoes", useUnmergedTree = true).fetchSemanticsNode()
        assertTrue(notice.positionInRoot.y + notice.size.height <= tile.positionInRoot.y)
        compose.onAllNodesWithText(why).assertCountEquals(0)

        compose.onNodeWithText("OPEN SETTINGS").performClick()

        assertEquals(listOf("settings"), calls)
        // Not dismissable: its one action leaves it where it is.
        compose.onNodeWithText(off).assertExists()
    }

    @Test
    fun `UX-DR88 My notifications shows the persistent notice with Open Settings`() {
        show(PushStates.denied)
        openMyNotifications()

        compose.onNodeWithText(off).assertExists()
        compose.onNodeWithText("OPEN SETTINGS").performClick()

        assertEquals(listOf("settings"), calls)
        compose.onNodeWithText(off).assertExists()
        // The settings stay usable below it.
        compose.onNodeWithText("Notification Window").assertExists()
    }

    @Test
    fun `UX-DR88 the notice stays in My notifications when the settings could not be read`() {
        notifications = NotificationSettingsState.Failed(NotificationSettingsNotice.Unreachable)
        show(PushStates.denied)
        openMyNotifications()

        compose.onNodeWithText(off).assertExists()
        compose.onNodeWithText("TRY AGAIN").assertExists()
    }

    @Test
    fun `UX-DR88 both notices are gone at the next foreground that finds the permission granted`() {
        show(PushStates.denied)
        compose.onNodeWithText(off).assertExists()

        // The activity reports the permission on every start; the core's state follows.
        push = PushStates.granted
        compose.waitForIdle()

        compose.onAllNodesWithText(off).assertCountEquals(0)
        openMyNotifications()
        compose.onAllNodesWithText(off).assertCountEquals(0)
        compose.onAllNodesWithText("OPEN SETTINGS").assertCountEquals(0)
    }

    @Test
    fun `UX-DR88 with notifications on there is no notice in My notifications`() {
        show(PushStates.granted)
        openMyNotifications()

        compose.onAllNodesWithText(off).assertCountEquals(0)
    }

    @Test
    fun `UX-DR88 UX-DR96 the notices do not overflow at font scale 2`() {
        push = PushStates.denied
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    push = push,
                    pushActions = actions,
                )
            }
        }
        compose.assertNothingOverflows("overview, notifications off")

        push = PushStates.promptDue
        compose.waitForIdle()
        compose.assertNothingOverflows("overview, why-line")
    }

    // The route of a tapped notification

    @Test
    fun `UX-DR120 a tapped Alert opens Lot detail on the Garden tab, from any tab`() {
        show(PushStates.granted)
        compose.onNodeWithText("Devices").performClick()
        tab("Devices").assertIsSelected()

        push = PushStates.granted.copy(route = PushRoute.LotDetail("site-home", "lot-t", "Tomatoes"))
        compose.waitForIdle()

        assertEquals(listOf("open lot-t Tomatoes", "handled"), calls)
        tab("Garden").assertIsSelected()
        // Handed over once: the core's route is gone, and nothing is opened a second time.
        assertEquals(null, push.route)
    }

    @Test
    fun `UX-DR120 a tapped summary opens the Site overview, closing a Lot detail that was open`() {
        lotDetail = LotDetailFixtures.ready(LotDetailFixtures.ok)
        show(PushStates.granted)
        compose.onNodeWithText("Settings").performClick()

        push = PushStates.granted.copy(route = PushRoute.Overview("site-home"))
        compose.waitForIdle()

        assertEquals(listOf("close", "handled"), calls)
        tab("Garden").assertIsSelected()
        compose.onNode(isHeading().and(hasText("Garden"))).assertExists()
        compose.onNodeWithText("Tomatoes", useUnmergedTree = true).assertExists()
    }

    @Test
    fun `UX-DR120 a tapped summary on the overview itself changes nothing but is handed over`() {
        show(PushStates.granted)

        push = PushStates.granted.copy(route = PushRoute.Overview("site-home"))
        compose.waitForIdle()

        assertEquals(listOf("handled"), calls)
        tab("Garden").assertIsSelected()
    }
}
