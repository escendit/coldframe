package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.devices.DevicesNotice
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SiteRole
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Instant
import java.util.TimeZone
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** The Devices tab (UX-DR30, UX-DR65, UX-DR84), Story 3.7: the Hubs of the current Site. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class DevicesScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var devices by mutableStateOf<DevicesState>(DevicesState.Idle)
    private var hubSetup by mutableStateOf(HubSetupState.CLOSED)

    private val now = Instant.parse("2026-10-06T07:04:00Z")
    private val seenAt = Instant.parse("2026-10-06T07:02:00Z").toEpochMilli()
    private val online = HubSummary("3f2a9c0d1e4b5a67", online = true, lastSeenAtEpochMs = seenAt)
    private val neverSeen = HubSummary("7c19000000000001", online = false, lastSeenAtEpochMs = null)

    private val defaultZone = TimeZone.getDefault()

    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private fun ready(
        role: SiteRole,
        vararg hubs: HubSummary,
    ) = DevicesState.Ready(homeSite(role), hubs.toList())

    /** Signed in on "Home garden" as [role], with the Devices tab open. */
    private fun show(
        role: SiteRole,
        state: DevicesState,
    ) {
        devices = state
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(homeSite(role)),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                hubSetup = hubSetup,
                hubSetupActions =
                    HubSetupActions(
                        open = {
                            calls += "open"
                            hubSetup = HubStates.scan
                        },
                    ),
                devices = devices,
                devicesActions = DevicesActions(load = { calls += "load" }),
                now = { now },
            )
        }
        compose.onNodeWithText("Devices").performClick()
        compose.onNode(isHeading().and(hasText("Devices"))).assertExists()
    }

    /** The merged rows TalkBack reads, in order: every node that holds a Device ID. */
    private fun rows(): List<String> =
        compose
            .onAllNodes(hasText("Last seen", substring = true).or(hasText("Not seen yet")))
            .fetchSemanticsNodes()
            .map { it.spokenLabel() }

    @Test
    fun `UX-DR30 UX-DR65 a Hubs section lists the Hub with its full Device ID, Online and its last-seen time`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))

        compose.onNode(isHeading().and(hasText("Hubs"))).assertExists()
        compose.onNodeWithText("3f2a9c0d1e4b5a67").assertExists()
        assertEquals(1, rows().size)
        assertTrue(
            Regex("""3f2a9c0d1e4b5a67 Online Last seen 7:02[\s\u202F]AM""").matches(rows().single()),
            rows().single(),
        )
        compose.onAllNodesWithText("Nodes").assertCountEquals(0)
    }

    @Test
    fun `UX-DR30 a Hub whose heartbeat stopped reads Offline with the unchanged last-seen time`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))

        devices = ready(SiteRole.Owner, online.copy(online = false))
        compose.waitForIdle()

        assertTrue(
            Regex("""3f2a9c0d1e4b5a67 Offline Last seen 7:02[\s\u202F]AM""").matches(rows().single()),
            rows().single(),
        )
        compose.onAllNodesWithText("Online").assertCountEquals(0)
    }

    @Test
    fun `UX-DR30 the status is the Server's word, not derived from the last-seen time`() {
        // Seen two minutes ago by the clock of this phone, and still offline because the Server says so.
        show(SiteRole.Owner, ready(SiteRole.Owner, online.copy(online = false, lastSeenAtEpochMs = now.toEpochMilli())))

        compose.onNodeWithText("Offline").assertExists()
        compose.onAllNodesWithText("Online").assertCountEquals(0)
    }

    @Test
    fun `UX-DR30 a Hub that never sent a heartbeat reads Offline and Not seen yet`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, neverSeen))

        assertEquals(listOf("7c19000000000001 Offline Not seen yet"), rows())
    }

    @Test
    fun `UX-DR30 last seen follows the Voice rules, a weekday for an earlier day`() {
        val sunday = Instant.parse("2026-10-04T18:00:00Z").toEpochMilli()
        show(SiteRole.Owner, ready(SiteRole.Owner, online.copy(online = false, lastSeenAtEpochMs = sunday)))

        compose.onNodeWithText("Last seen Sun").assertExists()
    }

    @Test
    fun `UX-DR30 without Devices the tab says No Devices yet`() {
        show(SiteRole.Member, ready(SiteRole.Member))

        compose.onNodeWithText("No Devices yet.").assertExists()
        compose.onAllNodesWithText("Hubs").assertCountEquals(0)
    }

    @Test
    fun `UX-DR30 UX-DR84 an Administrator gets the ghost Add a Hub header action, which opens the flow`() {
        show(SiteRole.Administrator, ready(SiteRole.Administrator))

        val addHub = compose.allNodes().single { it.isClickable && it.spokenLabel() == "ADD A HUB" }
        assertEquals(Role.Button, addHub.role)
        compose.onNodeWithText("ADD A HUB").performClick()

        assertTrue("open" in calls)
        compose.onAllNodesWithText("No Devices yet.").assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Member sees the list without Add a Hub, hidden and not disabled`() {
        show(SiteRole.Member, ready(SiteRole.Member, online))

        compose.onNodeWithText("3f2a9c0d1e4b5a67").assertExists()
        compose.onAllNodesWithText("ADD A HUB").assertCountEquals(0)
        compose.onAllNodesWithText("Add a Hub").assertCountEquals(0)
        // Nothing in the tab can be pressed: only the four tabs of the shell.
        assertEquals(4, compose.allNodes().count { it.isClickable })
    }

    @Test
    fun `UX-DR30 Add a Hub shows only on the Devices tab`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))
        compose.onNodeWithText("ADD A HUB").assertExists()

        compose.onNodeWithText("Alerts").performClick()

        compose.onAllNodesWithText("ADD A HUB").assertCountEquals(0)
    }

    @Test
    fun `UX-DR65 every entry of the Devices tab reads the list again`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))
        assertEquals(listOf("load"), calls)

        compose.onNodeWithText("Alerts").performClick()
        compose.waitForIdle()
        assertEquals(listOf("load"), calls)

        compose.onNodeWithText("Devices").performClick()
        compose.waitForIdle()
        assertEquals(listOf("load", "load"), calls)
    }

    @Test
    fun `UX-DR65 closing Add a Hub returns to Devices, which reads the list again`() {
        show(SiteRole.Owner, ready(SiteRole.Owner))
        compose.onNodeWithText("ADD A HUB").performClick()
        compose.waitForIdle()
        // The flow replaced the shell.
        compose.onAllNodesWithText("No Devices yet.").assertCountEquals(0)

        hubSetup = HubSetupState.CLOSED
        devices = ready(SiteRole.Owner, online)
        compose.waitForIdle()

        compose.onNode(isHeading().and(hasText("Devices"))).assertExists()
        compose.onNodeWithText("3f2a9c0d1e4b5a67").assertExists()
        assertEquals(listOf("load", "open", "load"), calls)
    }

    @Test
    fun `UX-DR65 a failed reload shows no rows and the notice with Try again`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))

        devices = DevicesState.Failed(homeSite(), DevicesNotice.Unreachable)
        compose.waitForIdle()

        compose.onNodeWithText("Can't reach your Server.").assertExists()
        compose.onAllNodesWithText("3f2a9c0d1e4b5a67").assertCountEquals(0)
        compose.onAllNodesWithText("Online").assertCountEquals(0)
        calls.clear()
        compose.onNodeWithText("TRY AGAIN").performClick()
        assertEquals(listOf("load"), calls)
    }

    @Test
    fun `a certificate failure shows the certificate notice without Try again`() {
        show(SiteRole.Owner, DevicesState.Failed(homeSite(), DevicesNotice.Certificate))

        compose.onNode(hasText("certificate isn't trusted", substring = true)).assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR65 nothing is listed while the Devices are read`() {
        show(SiteRole.Owner, DevicesState.Loading(homeSite()))

        compose.onAllNodesWithText("Hubs").assertCountEquals(0)
        compose.onAllNodesWithText("No Devices yet.").assertCountEquals(0)
        compose.onAllNodesWithText("Online").assertCountEquals(0)
    }
}
