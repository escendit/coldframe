package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.setup.AddHubFlow
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.readingTimeMillis
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.setup.AnnouncementKind
import com.escendit.coldframe.core.setup.CodeError
import com.escendit.coldframe.core.setup.CodeForm
import com.escendit.coldframe.core.setup.HubCandidate
import com.escendit.coldframe.core.setup.HubIdentity
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.setup.NetworkSecurity
import com.escendit.coldframe.core.setup.OutcomeAction
import com.escendit.coldframe.core.setup.OutcomeKind
import com.escendit.coldframe.core.setup.ProgressState
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.core.setup.SetupAnnouncement
import com.escendit.coldframe.core.setup.SetupOutcome
import com.escendit.coldframe.core.setup.SetupStep
import com.escendit.coldframe.core.setup.SignalStrength
import com.escendit.coldframe.core.setup.SiteChoice
import com.escendit.coldframe.core.setup.SiteForm
import com.escendit.coldframe.core.setup.WifiForm
import com.escendit.coldframe.core.setup.WifiNetworkRow
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SiteRole
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** States of the Add a Hub flow as the core hands them to the shell (Story 3.6). */
object HubStates {
    val hub3f2a = HubCandidate("peripheral-1", "3F2A", -52)
    private val hub11c0 = HubCandidate("peripheral-2", "11C0", -78)

    private val networks =
        listOf(
            WifiNetworkRow("Novak-Home", NetworkSecurity.Wpa2, -45),
            WifiNetworkRow("Garden shed", NetworkSecurity.Wpa3Transition, -60),
            WifiNetworkRow("Neighbour", NetworkSecurity.Wpa3Only, -72),
        )

    val scan =
        HubSetupState.CLOSED.copy(
            open = true,
            candidates = listOf(hub3f2a, hub11c0),
            selected = hub3f2a,
            site = SiteForm(listOf(SiteChoice("site-home", "Home garden")), "site-home", null, false, null),
            serverHost = "coldframe.example.org",
        )

    val code = scan.copy(step = SetupStep.Code, code = CodeForm("K7M2-Q9XP", null, working = false, accepted = true))
    val wrongCode = code.copy(code = CodeForm("K7M2-Q9XQ", CodeError.WrongCode, working = false, accepted = false))

    val wifi =
        code.copy(
            step = SetupStep.Wifi,
            identity = HubIdentity("3f2a9c01b2d4e6f8", "0.1.0"),
            wifi = WifiForm(networks, ssid = "Novak-Home", other = false, password = "", error = null),
        )

    val site =
        wifi.copy(
            step = SetupStep.Site,
            site = wifi.site.copy(fingerprint = "7b1254d2e34519c56e47e0fab7b5c909289f2ca31323180d6712c5735614c49c"),
        )

    val progress = site.copy(step = SetupStep.Progress, progress = ProgressState(reached = 2, elapsedSeconds = 23))
    val online = progress.copy(progress = ProgressState(4, 41), outcome = SetupOutcome(OutcomeKind.Online))
    val wrongPassword = progress.copy(outcome = SetupOutcome(OutcomeKind.WrongPassword))
}

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AddHubFlowTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var state by mutableStateOf(HubStates.scan)

    private val actions =
        HubSetupActions(
            open = { calls += "open" },
            close = { calls += "close" },
            recheckRadio = { calls += "recheck" },
            announcing = { calls += "announcing $it" },
            back = { calls += "back" },
            leave = { calls += "leave" },
            confirmLeave = { calls += "confirmLeave" },
            stayInFlow = { calls += "stay" },
            select = { calls += "select $it" },
            continueFromScan = { calls += "continueFromScan" },
            setCode = { calls += "code $it" },
            submitCode = { calls += "submitCode" },
            continueFromCode = { calls += "continueFromCode" },
            chooseNetwork = { calls += "network $it" },
            chooseOtherNetwork = { calls += "other" },
            setOtherSsid = { calls += "ssid $it" },
            setPassword = { calls += "password" },
            continueFromWifi = { calls += "continueFromWifi" },
            chooseSite = { calls += "site $it" },
            retryKey = { calls += "retryKey" },
            start = { calls += "start" },
            outcomeAction = { calls += "outcome $it" },
        )

    private fun show(next: HubSetupState) {
        state = next
        if (!started) {
            started = true
            compose.setContent {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    hubSetup = state,
                    hubSetupActions = actions,
                )
            }
        }
        compose.waitForIdle()
    }

    private var started = false

    private fun liveRegionOf(description: String): LiveRegionMode? =
        compose
            .onNode(hasContentDescription(description), useUnmergedTree = true)
            .fetchSemanticsNode()
            .config
            .getOrNull(SemanticsProperties.LiveRegion)

    @Test
    fun `UX-DR66 the Add a Hub tile starts the flow for an Owner`() {
        show(HubSetupState.CLOSED)
        compose.onNode(hasText("Add a Hub").and(hasClickAction()), useUnmergedTree = false).assertHasClickAction()
        compose.onNode(hasText("Add a Hub")).performClick()
        assertEquals(listOf("open"), calls)
    }

    @Test
    fun `UX-DR66 a Member's Add a Hub tile stays non-actionable`() {
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(homeSite(SiteRole.Member)),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                hubSetupActions = actions,
            )
        }
        compose.onNode(hasText("Add a Hub")).assert(SemanticsMatcher.keyNotDefined(SemanticsActions.OnClick))
        compose.onNode(hasText("Add a Hub")).performClick()
        assertTrue(calls.isEmpty())
    }

    @Test
    fun `UX-DR39 step 1 shows 01 of 05 with Cancel and later steps show Back`() {
        show(HubStates.scan)
        compose.onNodeWithContentDescription("Step 1 of 5").assertExists()
        compose.onNodeWithText("Cancel", ignoreCase = true).performClick()
        assertEquals(listOf("leave"), calls.filter { it != "recheck" })

        show(HubStates.code)
        compose.onNodeWithContentDescription("Step 2 of 5").assertExists()
        compose.onNodeWithText("Back", ignoreCase = true).performClick()
        assertTrue("back" in calls)
    }

    @Test
    fun `UX-DR39 focus moves to the step title on each step`() {
        show(HubStates.scan)
        compose.onNode(isHeading().and(hasText("Find the Hub"))).assertIsFocused()
        show(HubStates.wifi)
        compose.onNode(isHeading().and(hasText("Choose the Wi-Fi"))).assertIsFocused()
    }

    @Test
    fun `UX-DR39 leaving mid-flow asks Stop setting up Hub 3F2A and names that nothing is saved`() {
        show(HubStates.code.copy(confirmingLeave = true))
        compose.onNodeWithText("Stop setting up Hub 3F2A?").assertExists()
        compose.onNodeWithText("Nothing is saved on the Hub.").assertExists()
        compose.onNodeWithText("Stop setting up", ignoreCase = true).performClick()
        assertTrue("confirmLeave" in calls)
    }

    @Test
    fun `UX-DR39 the screen stays awake while the flow is open`() {
        show(HubStates.scan)
        assertTrue(viewKeepsScreenOn())
        show(HubSetupState.CLOSED)
        assertFalse(viewKeepsScreenOn())
    }

    private fun viewKeepsScreenOn(): Boolean {
        val root = compose.activity.window.decorView

        fun any(view: android.view.View): Boolean =
            view.keepScreenOn ||
                (view is android.view.ViewGroup && (0 until view.childCount).any { any(view.getChildAt(it)) })
        return any(root)
    }

    @Test
    fun `UX-DR37 candidates are one element each with their signal and the selected one is selected`() {
        show(HubStates.scan)
        compose.onNodeWithContentDescription("Hub 3F2A, strong signal").assertIsSelected()
        compose.onNodeWithContentDescription("Hub 11C0, medium signal").performClick()
        assertTrue("select peripheral-2" in calls)
        compose.onNodeWithText("Still scanning…").assertExists()
        compose.onNodeWithText("Set up Hub 3F2A", ignoreCase = true).performScrollTo().performClick()
        assertTrue("continueFromScan" in calls)
    }

    @Test
    fun `UX-DR94 Bluetooth off or denied shows the notice with Open Settings`() {
        show(HubStates.scan.copy(radio = RadioState.Off, candidates = emptyList(), selected = null))
        compose.onNodeWithText("Coldframe needs Bluetooth to find the Hub.").assertExists()
        compose.onNodeWithText("Open Settings", ignoreCase = true).assertExists()
    }

    @Test
    fun `UX-DR94 no Hub in 30 s says how to wake it while scanning continues`() {
        show(HubStates.scan.copy(noHubYet = true, candidates = emptyList(), selected = null))
        compose
            .onNodeWithText(
                "No Hub in range yet. Power it on within a few metres; its LED blinks orange while it waits.",
            ).assertExists()
        compose.onNodeWithText("Still scanning…").assertExists()
    }

    @Test
    fun `UX-DR41 the accepted code shows the Accepted chip and the full Device ID`() {
        show(HubStates.code.copy(identity = HubIdentity("3f2a9c01b2d4e6f8", "0.1.0")))
        compose.onNodeWithText("Accepted", ignoreCase = true).assertExists()
        compose.onNodeWithText("3f2a9c01b2d4e6f8").assertExists()
        compose.onNodeWithText("Choose Wi-Fi", ignoreCase = true).performScrollTo().performClick()
        assertTrue("continueFromCode" in calls)
    }

    @Test
    fun `UX-DR95 a wrong setup code keeps the field and says why`() {
        show(HubStates.wrongCode)
        compose
            .onNodeWithText("That setup code doesn't match Hub 3F2A. Check its label or the serial console.")
            .assertExists()
        compose.onNodeWithText("K7M2-Q9XQ").assertExists()
        compose.onNodeWithText("Check setup code", ignoreCase = true).performScrollTo().performClick()
        assertTrue("submitCode" in calls)
    }

    @Test
    fun `UX-DR42 a WPA3-only row is hatched, disabled and says why`() {
        show(HubStates.wifi)
        compose
            .onNode(hasText("Neighbour", substring = true))
            .assertIsNotEnabled()
        compose.onNodeWithText("Not supported: the Hub needs WPA2 or mixed WPA2/WPA3.").assertExists()
        compose.onNode(hasText("Novak-Home", substring = true).and(hasClickAction())).assertIsSelected()
        compose.onNodeWithText("Other network", ignoreCase = true).performScrollTo().performClick()
        assertTrue("other" in calls)
    }

    @Test
    fun `UX-DR66 step 4 shows the key fingerprint and names the result`() {
        show(HubStates.site)
        compose.onNodeWithText("7b12 54d2", substring = true).assertExists()
        compose.onNodeWithText("Add Hub 3F2A to Home garden", ignoreCase = true).performScrollTo().performClick()
        assertTrue("start" in calls)
    }

    @Test
    fun `UX-DR40 the four segments are one progress element with elapsed time`() {
        show(HubStates.progress)
        compose.onNodeWithContentDescription("Step 3 of 4, joining Wi-Fi").assertExists()
        compose.onNodeWithText("23 s elapsed").assertExists()
        compose.onNodeWithText("Usually under a minute.").assertExists()
    }

    @Test
    fun `UX-DR55 Hub is online takes focus and Add a Node closes the flow`() {
        show(HubStates.online)
        compose.onNode(isHeading().and(hasText("Hub is online"))).assertIsFocused()
        compose.onNodeWithText("Hub 3F2A joined Novak-Home and reports to Home garden.").assertExists()
        compose.onNodeWithText("Add a Node", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${OutcomeAction.AddNode}" in calls)
    }

    @Test
    fun `UX-DR95 UX-DR55 a wrong Wi-Fi password stops step 5 with Re-enter password and Other network`() {
        show(HubStates.wrongPassword)
        compose.onNodeWithText("Step 5 stopped", ignoreCase = true).assertExists()
        compose.onNode(isHeading().and(hasText("Wrong Wi-Fi password"))).assertIsFocused()
        compose.onNodeWithText("Re-enter password", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("Other network", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${OutcomeAction.ReenterPassword}" in calls)
        assertTrue("outcome ${OutcomeAction.OtherNetwork}" in calls)
    }

    @Test
    fun `UX-DR95 the Server not reached outcome offers Try again and Help reveals the host`() {
        show(HubStates.progress.copy(outcome = SetupOutcome(OutcomeKind.NoServer)))
        compose.onNodeWithText("Hub 3F2A is on Novak-Home but can't reach your Server.").assertExists()
        compose.onNodeWithText("Help", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${OutcomeAction.Help}" in calls)

        show(HubStates.progress.copy(outcome = SetupOutcome(OutcomeKind.NoServer, helpShown = true)))
        compose
            .onNodeWithText(
                "The Hub reached Novak-Home but not coldframe.example.org. Check that this name resolves on your home network.",
            ).assertExists()
    }

    @Test
    fun `UX-DR94 lost connection and the timeout say nothing was saved and offer Try again`() {
        show(HubStates.progress.copy(outcome = SetupOutcome(OutcomeKind.LostConnection)))
        compose.onNodeWithText("Lost the connection to Hub 3F2A").assertExists()
        compose.onNodeWithText("Nothing was saved.").assertExists()
        show(HubStates.progress.copy(outcome = SetupOutcome(OutcomeKind.Timeout)))
        compose.onNodeWithText("Hub 3F2A didn't come online").assertExists()
        compose.onNodeWithText("Nothing was saved on the Hub. Keep it near your router and try again.").assertExists()
        compose.onNodeWithText("Try again", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${OutcomeAction.StartOver}" in calls)
    }

    @Test
    fun `UX-DR105 a new candidate is announced politely and an error assertively`() {
        show(
            HubStates.scan.copy(
                announcement =
                    SetupAnnouncement(
                        1,
                        AnnouncementKind.CandidateFound,
                        false,
                        "3F2A",
                        null,
                        SignalStrength.Strong,
                        null,
                    ),
            ),
        )
        assertEquals(LiveRegionMode.Polite, liveRegionOf("Hub 3F2A found, strong signal."))

        show(
            HubStates.wrongPassword.copy(
                announcement =
                    SetupAnnouncement(
                        2,
                        AnnouncementKind.Error,
                        true,
                        "3F2A",
                        "Novak-Home",
                        null,
                        OutcomeKind.WrongPassword,
                    ),
            ),
        )
        assertEquals(LiveRegionMode.Assertive, liveRegionOf("Wrong Wi-Fi password. Re-enter password."))

        show(
            HubStates.progress.copy(
                announcement = SetupAnnouncement(3, AnnouncementKind.WifiSent, false, "3F2A", "Novak-Home", null, null),
            ),
        )
        assertEquals(LiveRegionMode.Polite, liveRegionOf("Wi-Fi sent. Joining Novak-Home."))
    }

    @Test
    fun `UX-DR103 the reading-time hold is at least a second plus 60 ms per character`() {
        assertEquals(1_000L + 60L * 10, readingTimeMillis("0123456789"))
        assertTrue(readingTimeMillis("") >= 1_000L)
    }

    @Test
    fun `UX-DR66 the flow replaces the tab shell while open`() {
        show(HubStates.scan)
        compose.onNode(isHeading().and(hasText("Garden"))).assertDoesNotExist()
        show(HubSetupState.CLOSED)
        compose.onNode(isHeading().and(hasText("Garden"))).assertExists()
    }

    @Test
    fun `UX-DR96 every step fits at font scale 2`() {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeTheme(isDark = false) {
                    AddHubFlow(state = state, actions = actions)
                }
            }
        }
        for (step in listOf(
            HubStates.scan,
            HubStates.code,
            HubStates.wifi,
            HubStates.site,
            HubStates.progress,
            HubStates.online,
        )) {
            state = step
            compose.assertNothingOverflows(step.step.name)
        }
    }

    @Test
    fun `UX-DR103 an announcement holds the timeout while TalkBack reads it`() {
        val manager = compose.activity.getSystemService(android.view.accessibility.AccessibilityManager::class.java)
        org.robolectric.Shadows.shadowOf(manager).apply {
            setEnabled(true)
            setTouchExplorationEnabled(true)
        }
        compose.mainClock.autoAdvance = false
        show(
            HubStates.progress.copy(
                announcement = SetupAnnouncement(9, AnnouncementKind.WifiSent, false, "3F2A", "Novak-Home", null, null),
            ),
        )
        compose.mainClock.advanceTimeByFrame()
        compose.mainClock.advanceTimeByFrame()
        assertTrue("announcing true" in calls)
        assertFalse("announcing false" in calls)

        compose.mainClock.advanceTimeBy(readingTimeMillis("Wi-Fi sent. Joining Novak-Home.") + 100)
        assertEquals(listOf("announcing true", "announcing false"), calls.filter { it.startsWith("announcing") })
    }
}
