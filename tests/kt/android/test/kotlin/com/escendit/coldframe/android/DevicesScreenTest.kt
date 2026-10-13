package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isDialog
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.isSelectable
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.devices.DevicesNotice
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.core.devices.LotOption
import com.escendit.coldframe.core.devices.NodeActionFailure
import com.escendit.coldframe.core.devices.NodeActionNotice
import com.escendit.coldframe.core.devices.NodeSummary
import com.escendit.coldframe.core.devices.SilenceWindowFailure
import com.escendit.coldframe.core.devices.SilenceWindowNotice
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.PauseFailure
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.PauseState
import com.escendit.coldframe.core.sites.PauseTarget
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
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** The Devices tab (UX-DR30, UX-DR65, UX-DR84), Story 3.7: the Hubs of the current Site. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class DevicesScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var devices by mutableStateOf<DevicesState>(DevicesState.Idle)
    private var pause by mutableStateOf(PauseState())
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
                devicesActions =
                    DevicesActions(
                        load = { calls += "load" },
                        moveNode = { node, lot -> calls += "move $node $lot" },
                        unassignNode = { calls += "unassign $it" },
                        setSilenceWindow = { device, seconds -> calls += "window $device ${seconds ?: "default"}" },
                        pause = PauseFixtures.recording(calls),
                    ),
                devicesPause = pause,
                nodeSetupActions = NodeSetupActions(open = { calls += "openNode $it" }),
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

    private val tomatoesNode =
        NodeSummary("7c19a1b2c3d4e5f6", "lot-t", "Tomatoes", 62, false, ChargeState.Charging, seenAt)
    private val flatNode =
        NodeSummary("7c19000000000002", "lot-b", "Beans", 14, true, ChargeState.NotCharging, null)
    private val unassigned = NodeSummary("7c19000000000003", null, null, null, false, null, null)

    private fun withNodes(vararg nodes: NodeSummary) = DevicesState.Ready(homeSite(), listOf(online), nodes.toList())

    @Test
    fun `UX-DR30 a Nodes section follows Hubs with the Lot, last seen, battery and charging of each Node`() {
        show(SiteRole.Owner, withNodes(tomatoesNode, flatNode))

        compose.onNode(isHeading().and(hasText("Hubs"))).assertExists()
        compose.onNode(isHeading().and(hasText("Nodes"))).assertExists()
        val hubs =
            compose
                .onNode(isHeading().and(hasText("Hubs")))
                .fetchSemanticsNode()
                .positionInRoot.y
        val nodes =
            compose
                .onNode(isHeading().and(hasText("Nodes")))
                .fetchSemanticsNode()
                .positionInRoot.y
        assertTrue(hubs < nodes)
        val spoken =
            compose
                .onAllNodes(hasText("7c19", substring = true))
                .fetchSemanticsNodes()
                .map { it.spokenLabel() }
        assertTrue(
            Regex(
                """7c19a1b2c3d4e5f6 Tomatoes Last seen 7:02[\s\u202F]AM 62 % charging""",
            ).matches(spoken[0]),
            spoken[0],
        )
        assertEquals("7c19000000000002 Beans Not seen yet 14 % not charging", spoken[1])
    }

    @Test
    fun `UX-DR30 Nodes keep the Server's order and an unassigned Node says it is not in a Lot`() {
        show(SiteRole.Owner, withNodes(flatNode, tomatoesNode, unassigned))

        val ids =
            compose
                .onAllNodes(hasText("7c19", substring = true))
                .fetchSemanticsNodes()
                .map { it.spokenLabel().substringBefore(' ') }
        assertEquals(listOf("7c19000000000002", "7c19a1b2c3d4e5f6", "7c19000000000003"), ids)
        compose.onNodeWithText("Not in a Lot").assertExists()
    }

    @Test
    fun `UX-DR30 a Node without a report shows neither battery nor charging, and a Member sees the same rows`() {
        show(SiteRole.Member, withNodes(unassigned))

        assertEquals(
            "7c19000000000003 Not in a Lot Not seen yet",
            compose.onNodeWithText("7c19000000000003").fetchSemanticsNode().spokenLabel(),
        )
        compose.onAllNodesWithText("charging", substring = true).assertCountEquals(0)
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
    fun `UX-DR30 UX-DR67 an Administrator gets Add a Node beside Add a Hub, which opens the Node flow`() {
        show(SiteRole.Administrator, ready(SiteRole.Administrator, online))

        val addNode = compose.allNodes().single { it.isClickable && it.spokenLabel() == "ADD A NODE" }
        val addHub = compose.allNodes().single { it.isClickable && it.spokenLabel() == "ADD A HUB" }
        assertEquals(Role.Button, addNode.role)
        // Add a Hub first, Add a Node after it.
        assertTrue(
            addHub.positionInRoot.x < addNode.positionInRoot.x || addHub.positionInRoot.y < addNode.positionInRoot.y,
        )
        compose.onNodeWithText("ADD A NODE").performClick()

        assertTrue("openNode null" in calls)
        assertFalse("open" in calls)
    }

    @Test
    fun `UX-DR67 Add a Node shows only on the Devices tab and is hidden for a Member`() {
        show(SiteRole.Owner, ready(SiteRole.Owner, online))
        compose.onNodeWithText("ADD A NODE").assertExists()

        compose.onNodeWithText("Alerts").performClick()

        compose.onAllNodesWithText("ADD A NODE").assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Member sees the list without Add a Hub, hidden and not disabled`() {
        show(SiteRole.Member, ready(SiteRole.Member, online))

        compose.onNodeWithText("3f2a9c0d1e4b5a67").assertExists()
        compose.onAllNodesWithText("ADD A HUB").assertCountEquals(0)
        compose.onAllNodesWithText("Add a Hub").assertCountEquals(0)
        compose.onAllNodesWithText("ADD A NODE").assertCountEquals(0)
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

    private val garden =
        listOf(
            LotOption("lot-t", "Tomatoes", hasNode = true),
            LotOption("lot-b", "Beans", hasNode = true),
            LotOption("lot-basil", "Basil", hasNode = false),
        )

    private fun managing(
        role: SiteRole,
        failure: NodeActionFailure? = null,
    ) = DevicesState.Ready(
        homeSite(role),
        listOf(online),
        listOf(tomatoesNode, unassigned),
        garden,
        failure = failure,
    )

    @Test
    fun `UX-DR31 an Administrator sees Move on every Node and Unassign on a Node in a Lot`() {
        show(SiteRole.Administrator, managing(SiteRole.Administrator))

        compose.onAllNodesWithText("MOVE").assertCountEquals(2)
        compose.onAllNodesWithText("UNASSIGN").assertCountEquals(1)
        compose.onNodeWithContentDescription("Move Node 7c19a1b2c3d4e5f6").assertExists()
        compose.onNodeWithContentDescription("Unassign Node 7c19a1b2c3d4e5f6").assertExists()
        compose.onNodeWithContentDescription("Unassign Node 7c19000000000003").assertDoesNotExist()
    }

    @Test
    fun `UX-DR31 a Member sees neither Move nor Unassign, hidden and not disabled`() {
        show(SiteRole.Member, managing(SiteRole.Member))

        compose.onAllNodesWithText("MOVE").assertCountEquals(0)
        compose.onAllNodesWithText("UNASSIGN").assertCountEquals(0)
        // Nothing in the tab can be pressed: only the four tabs of the shell.
        assertEquals(4, compose.allNodes().count { it.isClickable })
    }

    @Test
    fun `UX-DR31 the actions are also custom accessibility actions of the Node row`() {
        show(SiteRole.Owner, managing(SiteRole.Owner))

        val row = compose.onNodeWithText("7c19a1b2c3d4e5f6").fetchSemanticsNode()
        val labels = row.config[androidx.compose.ui.semantics.SemanticsActions.CustomActions].map { it.label }
        assertEquals(
            listOf(
                "Pause Node 7c19a1b2c3d4e5f6",
                "Move Node 7c19a1b2c3d4e5f6",
                "Unassign Node 7c19a1b2c3d4e5f6",
                "Silence Window for Node 7c19a1b2c3d4e5f6",
            ),
            labels,
        )

        val spare = compose.onNodeWithText("7c19000000000003").fetchSemanticsNode()
        assertEquals(
            listOf(
                "Pause Node 7c19000000000003",
                "Move Node 7c19000000000003",
                "Silence Window for Node 7c19000000000003",
            ),
            spare.config[androidx.compose.ui.semantics.SemanticsActions.CustomActions].map { it.label },
        )
        // A Hub row has the two actions a Hub has.
        val hub = compose.onNodeWithText("3f2a9c0d1e4b5a67").fetchSemanticsNode()
        assertEquals(
            listOf("Pause Hub 3f2a9c0d1e4b5a67", "Silence Window for Hub 3f2a9c0d1e4b5a67"),
            hub.config[androidx.compose.ui.semantics.SemanticsActions.CustomActions].map { it.label },
        )
    }

    @Test
    fun `UX-DR31 Unassign requires confirmation in a dialog naming the Node, and Cancel changes nothing`() {
        show(SiteRole.Administrator, managing(SiteRole.Administrator))

        compose.onNodeWithContentDescription("Unassign Node 7c19a1b2c3d4e5f6").performClick()

        compose.onNodeWithText("Unassign Node 7c19a1b2c3d4e5f6?").assertExists()
        compose.onNodeWithText("Tomatoes will have no Node. Its Readings stay in Coldframe.").assertExists()
        assertFalse(calls.any { it.startsWith("unassign") })

        compose.onNode(hasText("CANCEL", ignoreCase = true).and(hasAnyAncestor(isDialog()))).performClick()
        compose.onAllNodesWithText("Unassign Node 7c19a1b2c3d4e5f6?").assertCountEquals(0)
        assertFalse(calls.any { it.startsWith("unassign") })

        compose.onNodeWithContentDescription("Unassign Node 7c19a1b2c3d4e5f6").performClick()
        compose.onNode(hasText("UNASSIGN", ignoreCase = true).and(hasAnyAncestor(isDialog()))).performClick()

        assertEquals(listOf("load", "unassign 7c19a1b2c3d4e5f6"), calls)
    }

    @Test
    fun `UX-DR31 Lots with a Node are not selectable and say Has a Node, and only a free Lot can be moved to`() {
        show(SiteRole.Administrator, managing(SiteRole.Administrator))

        compose.onNodeWithContentDescription("Move Node 7c19a1b2c3d4e5f6").performClick()

        val inDialog = hasAnyAncestor(isDialog())
        compose.onNode(hasText("Choose a Lot for Node 7c19a1b2c3d4e5f6.").and(inDialog)).assertExists()
        compose.onNodeWithContentDescription("Basil").assertExists()
        compose.onNodeWithContentDescription("Beans, has a Node").assertIsNotEnabled()
        // The Node's own Lot says so and cannot be picked either.
        compose.onNodeWithContentDescription("Tomatoes, current Lot").assertIsNotEnabled()
        compose.onNode(hasText("MOVE NODE 7C19A1B2C3D4E5F6", ignoreCase = true).and(inDialog)).assertIsNotEnabled()

        compose.onNodeWithContentDescription("Beans, has a Node").performClick()
        compose.onNode(hasText("MOVE NODE 7C19A1B2C3D4E5F6", ignoreCase = true).and(inDialog)).assertIsNotEnabled()

        compose.onNodeWithContentDescription("Basil").performClick()
        compose
            .onNode(
                hasText("MOVE NODE 7C19A1B2C3D4E5F6", ignoreCase = true).and(inDialog),
            ).assertIsEnabled()
            .performClick()

        assertEquals(listOf("load", "move 7c19a1b2c3d4e5f6 lot-basil"), calls)
    }

    @Test
    fun `UX-DR31 a refused move says why under that Node`() {
        show(
            SiteRole.Administrator,
            managing(SiteRole.Administrator, NodeActionFailure("7c19a1b2c3d4e5f6", NodeActionNotice.LotTaken)),
        )

        compose.onNodeWithText("That Lot already has a Node. Choose another Lot.").assertExists()
    }

    // Story 7.2: silent Devices and the Silence Window.

    private val hubId = "3f2a9c0d1e4b5a67"
    private val nodeId = "9c0d1e4b5a673f2a"

    /** Hub 3F2A, last heard 12 min before [now], on the default window of a Hub. */
    private val silentHub =
        HubSummary(
            hubId,
            online = false,
            lastSeenAtEpochMs = Instant.parse("2026-10-06T06:52:00Z").toEpochMilli(),
            silentSinceEpochMs = Instant.parse("2026-10-06T06:52:00Z").toEpochMilli(),
            silenceWindowSeconds = 300,
        )

    /** A Node on Beans, last heard 6 h 4 min before [now], with its own window of 8 h. */
    private val silentNode =
        NodeSummary(
            nodeId,
            "lot-b",
            "Beans",
            62,
            false,
            ChargeState.NotCharging,
            Instant.parse("2026-10-06T01:00:00Z").toEpochMilli(),
            silentSinceEpochMs = Instant.parse("2026-10-06T01:00:00Z").toEpochMilli(),
            silenceWindowSeconds = 28_800,
            silenceWindowOverridden = true,
        )

    private fun silence(
        role: SiteRole,
        failure: SilenceWindowFailure? = null,
    ) = DevicesState.Ready(
        homeSite(role),
        listOf(silentHub),
        listOf(silentNode, tomatoesNode),
        silenceWindowFailure = failure,
    )

    private val inDialog = hasAnyAncestor(isDialog())

    private fun dialogButton(label: String) = compose.onNode(hasText(label, ignoreCase = true).and(inDialog))

    @Test
    fun `UX-DR30 a silent Hub and a silent Node say Silent and for how long beside their last seen`() {
        show(SiteRole.Member, silence(SiteRole.Member))

        // Sentence case in the catalogue, upper case by style.
        compose.onNodeWithText("SILENT 12 MIN").assertExists()
        compose.onNodeWithText("SILENT 6 H").assertExists()
        assertEquals(
            listOf(
                "3f2a9c0d1e4b5a67 Offline SILENT 12 MIN Last seen 6:52 AM Silence Window · 5 min",
                "9c0d1e4b5a673f2a Beans SILENT 6 H Last seen 1:00 AM 62 % not charging Silence Window · 8 h",
                "7c19a1b2c3d4e5f6 Tomatoes Last seen 7:02 AM 62 % charging",
            ),
            rows().map { it.replace('\u202F', ' ') },
        )
    }

    @Test
    fun `UX-DR30 a Device that is not silent is a row as before`() {
        show(SiteRole.Owner, withNodes(tomatoesNode))

        compose.onAllNodes(hasText("SILENT", substring = true)).assertCountEquals(0)
        // No Silence Window from the Server: nothing is said about it, and the action still shows.
        compose.onAllNodes(hasText("Silence Window ·", substring = true)).assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Member sees silence and last seen but no Silence Window control, hidden and not disabled`() {
        show(SiteRole.Member, silence(SiteRole.Member))

        compose.onNodeWithText("Silence Window · 8 h").assertExists()
        compose.onAllNodesWithText("SILENCE WINDOW").assertCountEquals(0)
        compose.onNodeWithContentDescription("Silence Window for Hub $hubId").assertDoesNotExist()
        compose.onNodeWithContentDescription("Silence Window for Node $nodeId").assertDoesNotExist()
        // Nothing in the tab can be pressed: only the four tabs of the shell.
        assertEquals(4, compose.allNodes().count { it.isClickable })
        val row = compose.onNodeWithText(hubId).fetchSemanticsNode()
        assertFalse(row.config.contains(androidx.compose.ui.semantics.SemanticsActions.CustomActions))
    }

    @Test
    fun `UX-DR84 UX-DR31 an Administrator and an Owner get the Silence Window action on every Hub and Node`() {
        for (role in listOf(SiteRole.Administrator, SiteRole.Owner)) {
            assertTrue(DevicesState.canSetSilenceWindow(role))
        }
        show(SiteRole.Administrator, silence(SiteRole.Administrator))

        compose.onAllNodesWithText("SILENCE WINDOW").assertCountEquals(3)
        compose.onNodeWithContentDescription("Silence Window for Hub $hubId").assertExists()
        compose.onNodeWithContentDescription("Silence Window for Node $nodeId").assertExists()
        compose.onNodeWithContentDescription("Silence Window for Node 7c19a1b2c3d4e5f6").assertExists()
    }

    @Test
    fun `UX-DR31 the Silence Window dialog shows the current value and Save sends whole seconds`() {
        show(SiteRole.Administrator, silence(SiteRole.Administrator))

        compose.onNodeWithContentDescription("Silence Window for Node $nodeId").performScrollTo().performClick()

        compose.onNode(hasText("Silence Window for Node $nodeId").and(inDialog)).assertExists()
        compose.onNode(hasText("Silence Window · 8 h").and(inDialog)).assertExists()
        compose
            .onNode(
                hasText("From 1 min to 30 d. Without its own value a Node uses 6 h and a Hub 5 min.").and(inDialog),
            ).assertExists()
        // The field holds the number, the unit its unit.
        compose.onNode(hasSetTextAction()).assertTextEquals("8")
        dialogButton("h").assertIsSelected()

        compose.onNode(hasSetTextAction()).performTextReplacement("12")
        dialogButton("d").performClick()
        dialogButton("d").assertIsSelected()
        dialogButton("Save").performClick()

        assertEquals(listOf("load", "window $nodeId 1036800"), calls)
        compose.onAllNodes(isDialog()).assertCountEquals(0)
    }

    @Test
    fun `UX-DR31 a value outside 1 min to 30 days is refused in the dialog with the range, and nothing is sent`() {
        show(SiteRole.Owner, silence(SiteRole.Owner))
        compose.onNodeWithContentDescription("Silence Window for Hub $hubId").performClick()
        compose.onNode(hasSetTextAction()).assertTextEquals("5")
        dialogButton("min").assertIsSelected()

        for (text in listOf("0", "90.5", "", "43201")) {
            compose.onNode(hasSetTextAction()).performTextReplacement(text)
            dialogButton("Save").performClick()

            compose.onNode(hasText("Enter a whole number from 1 min to 30 d.").and(inDialog)).assertExists()
        }
        dialogButton("d").performClick()
        compose.onNode(hasSetTextAction()).performTextReplacement("31")
        dialogButton("Save").performClick()
        compose.onNode(hasText("Enter a whole number from 1 min to 30 d.").and(inDialog)).assertExists()
        assertEquals(listOf("load"), calls)

        // Typing again takes the message away; the largest value is sent.
        compose.onNode(hasSetTextAction()).performTextReplacement("30")
        compose.onAllNodes(hasText("Enter a whole number from 1 min to 30 d.")).assertCountEquals(0)
        dialogButton("Save").performClick()
        assertEquals(listOf("load", "window $hubId 2592000"), calls)
    }

    @Test
    fun `UX-DR31 Use default shows only for a Device with its own window and sends no value`() {
        show(SiteRole.Administrator, silence(SiteRole.Administrator))

        // The Hub follows the default of its kind: there is nothing to return to.
        compose.onNodeWithContentDescription("Silence Window for Hub $hubId").performClick()
        compose.onAllNodes(hasText("Use default", ignoreCase = true)).assertCountEquals(0)
        dialogButton("Cancel").performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(0)
        assertEquals(listOf("load"), calls)

        compose.onNodeWithContentDescription("Silence Window for Node $nodeId").performScrollTo().performClick()
        dialogButton("Use default").performClick()

        assertEquals(listOf("load", "window $nodeId default"), calls)
        compose.onAllNodes(isDialog()).assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a 403 on the Silence Window says You can't change this on the Site, under that Device`() {
        show(
            SiteRole.Administrator,
            silence(SiteRole.Administrator, SilenceWindowFailure(hubId, SilenceWindowNotice.Forbidden)),
        )

        compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.").assertExists()
    }

    @Test
    fun `UX-DR31 a Silence Window that was not saved says why under that Device`() {
        val messages =
            mapOf(
                SilenceWindowNotice.OutOfRange to "Enter a whole number from 1 min to 30 d.",
                SilenceWindowNotice.NotFound to "That Device is gone.",
                SilenceWindowNotice.Unreachable to "Can't reach your Server.",
                SilenceWindowNotice.Unexpected to "That didn't work. Try again.",
            )
        show(SiteRole.Owner, silence(SiteRole.Owner))
        for ((notice, message) in messages) {
            devices = silence(SiteRole.Owner, SilenceWindowFailure(nodeId, notice))
            compose.waitForIdle()

            compose.onNodeWithText(message).assertExists()
        }
    }

    // Pause and Resume on Device rows (Story 8.2)

    private val pausedUntil = Instant.parse("2027-03-01T00:00:00Z").toEpochMilli()

    /** A Hub paused by itself. */
    private val pausedHub = online.copy(pausedBy = listOf(LotPauseSource.Device))

    /** A Hub paused with its Site only. */
    private val hubBySite =
        HubSummary(
            "1b00aa11bb22cc33",
            online = false,
            lastSeenAtEpochMs = seenAt,
            pausedBy = listOf(LotPauseSource.Site),
        )

    /** A Node paused with its Site only, until 1 Mar 2027. */
    private val nodeBySite =
        tomatoesNode.copy(pausedBy = listOf(LotPauseSource.Site), pausedUntilEpochMs = pausedUntil)

    /** A Node on no Lot, paused by itself and with its Site. */
    private val nodeByBoth = unassigned.copy(pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site))

    /** A Node that is not paused. */
    private val liveNode =
        NodeSummary("9c0d1e4b5a673f2a", "lot-b", "Beans", 62, false, ChargeState.NotCharging, seenAt)

    private fun pauses(role: SiteRole) =
        DevicesState.Ready(homeSite(role), listOf(hubBySite, pausedHub), listOf(nodeBySite, nodeByBoth, liveNode))

    @Test
    fun `UX-DR70 paused rows say Paused, or Paused by Site when the Site is a source, for anyone`() {
        show(SiteRole.Member, pauses(SiteRole.Member))

        // Sentence case in the catalogue, upper case by style.
        compose.onAllNodesWithText("PAUSED").assertCountEquals(1)
        compose.onAllNodesWithText("PAUSED BY SITE").assertCountEquals(3)
        assertEquals(
            listOf(
                "1b00aa11bb22cc33 Offline PAUSED BY SITE Last seen 7:02 AM",
                "3f2a9c0d1e4b5a67 Online PAUSED Last seen 7:02 AM",
                "7c19a1b2c3d4e5f6 Tomatoes PAUSED BY SITE Last seen 7:02 AM 62 % charging",
                "7c19000000000003 Not in a Lot PAUSED BY SITE Not seen yet",
                "9c0d1e4b5a673f2a Beans Last seen 7:02 AM 62 % not charging",
            ),
            rows().map { it.replace('\u202F', ' ') },
        )
    }

    @Test
    fun `UX-DR84 a Member sees the paused rows and no Pause, Resume or hint, hidden and not disabled`() {
        show(SiteRole.Member, pauses(SiteRole.Member))

        compose.onAllNodesWithText("PAUSE").assertCountEquals(0)
        compose.onAllNodesWithText("RESUME").assertCountEquals(0)
        compose.onAllNodes(hasText("Resume the Site", substring = true)).assertCountEquals(0)
        compose.onNodeWithContentDescription("Pause Node 9c0d1e4b5a673f2a").assertDoesNotExist()
        compose.onNodeWithContentDescription("Resume Hub 3f2a9c0d1e4b5a67").assertDoesNotExist()
        // Nothing in the tab can be pressed: only the four tabs of the shell.
        assertEquals(4, compose.allNodes().count { it.isClickable })
        val row = compose.onNodeWithText("3f2a9c0d1e4b5a67").fetchSemanticsNode()
        assertFalse(row.config.contains(androidx.compose.ui.semantics.SemanticsActions.CustomActions))
    }

    @Test
    fun `UX-DR70 an Administrator gets Pause or Resume on every row, as the Device's own Pause says`() {
        assertTrue(DevicesState.canPause(SiteRole.Administrator) && DevicesState.canPause(SiteRole.Owner))
        show(SiteRole.Administrator, pauses(SiteRole.Administrator))

        // Its own Pause: Resume, and no second Pause.
        compose.onNodeWithContentDescription("Resume Hub 3f2a9c0d1e4b5a67").assertExists()
        compose.onNodeWithContentDescription("Pause Hub 3f2a9c0d1e4b5a67").assertDoesNotExist()
        compose.onNodeWithContentDescription("Resume Node 7c19000000000003").assertExists()
        compose.onNodeWithContentDescription("Pause Node 7c19000000000003").assertDoesNotExist()
        // Paused by the Site only: no Resume for the Device, and Pause stays available.
        compose.onNodeWithContentDescription("Resume Hub 1b00aa11bb22cc33").assertDoesNotExist()
        compose.onNodeWithContentDescription("Pause Hub 1b00aa11bb22cc33").assertExists()
        compose.onNodeWithContentDescription("Resume Node 7c19a1b2c3d4e5f6").assertDoesNotExist()
        compose.onNodeWithContentDescription("Pause Node 7c19a1b2c3d4e5f6").assertExists()
        // Not paused: Pause.
        compose.onNodeWithContentDescription("Pause Node 9c0d1e4b5a673f2a").assertExists()
        compose.onNodeWithContentDescription("Resume Node 9c0d1e4b5a673f2a").assertDoesNotExist()
    }

    @Test
    fun `UX-DR70 a Device paused by the Site says Resume the Site to resume this Node, and a Hub row this Hub`() {
        show(SiteRole.Owner, pauses(SiteRole.Owner))

        // Only the Node paused by the Site alone: the one paused by both has Resume of its own instead.
        compose.onAllNodesWithText("Resume the Site to resume this Node").assertCountEquals(1)
        val both = compose.onNodeWithText("7c19000000000003").fetchSemanticsNode()
        assertFalse(both.spokenLabel().contains("Resume the Site"))
        compose.onAllNodesWithText("Resume the Site to resume this Hub").assertCountEquals(1)
        val hub = compose.onNodeWithText("1b00aa11bb22cc33").fetchSemanticsNode()
        assertTrue(hub.spokenLabel().endsWith("Resume the Site to resume this Hub"), hub.spokenLabel())
        val live = compose.onNodeWithText("9c0d1e4b5a673f2a").fetchSemanticsNode()
        assertFalse(live.spokenLabel().contains("Resume the Site"))
    }

    @Test
    fun `UX-DR70 UX-DR46 Pause on a row opens the sheet with This Device selected, and Whole Site names the Site`() {
        show(SiteRole.Administrator, pauses(SiteRole.Administrator))

        compose.onNodeWithContentDescription("Pause Node 9c0d1e4b5a673f2a").performScrollTo().performClick()
        assertEquals(listOf("load", "openForDevice 9c0d1e4b5a673f2a"), calls)

        pause = PauseFixtures.sheet(PauseFixtures.forDevice.copy(deviceId = "9c0d1e4b5a673f2a"))
        compose.waitForIdle()
        compose.onNode(isSelectable().and(hasText("THIS DEVICE"))).assertIsSelected()
        compose.onNodeWithText("PAUSE 9C0D").assertExists()

        compose.onNode(isSelectable().and(hasText("WHOLE SITE"))).performClick()
        assertEquals("scope site", calls.last())
        pause =
            PauseFixtures.sheet(PauseFixtures.forDevice.copy(deviceId = "9c0d1e4b5a673f2a", scope = PauseScope.Site))
        compose.waitForIdle()
        compose.onNodeWithText("PAUSE HOME GARDEN").performClick()
        assertEquals("save", calls.last())
    }

    @Test
    fun `UX-DR70 Resume on a row resumes with one tap and no sheet`() {
        show(SiteRole.Owner, pauses(SiteRole.Owner))

        compose.onNodeWithContentDescription("Resume Hub 3f2a9c0d1e4b5a67").performScrollTo().performClick()

        assertEquals(listOf("load", "resumeDevice 3f2a9c0d1e4b5a67"), calls)
        compose.onAllNodesWithText("WHOLE SITE").assertCountEquals(0)
        compose.onAllNodes(isDialog()).assertCountEquals(0)
    }

    @Test
    fun `UX-DR70 Pause and Resume are also custom accessibility actions of the row`() {
        show(SiteRole.Owner, pauses(SiteRole.Owner))

        fun actionsOf(deviceId: String): List<String> =
            compose
                .onNodeWithText(deviceId)
                .fetchSemanticsNode()
                .config[androidx.compose.ui.semantics.SemanticsActions.CustomActions]
                .map { it.label }
        assertEquals("Resume Hub 3f2a9c0d1e4b5a67", actionsOf("3f2a9c0d1e4b5a67").first())
        assertEquals("Pause Hub 1b00aa11bb22cc33", actionsOf("1b00aa11bb22cc33").first())
        assertEquals("Pause Node 7c19a1b2c3d4e5f6", actionsOf("7c19a1b2c3d4e5f6").first())
        assertEquals("Resume Node 7c19000000000003", actionsOf("7c19000000000003").first())

        val resume =
            compose
                .onNodeWithText("7c19000000000003")
                .fetchSemanticsNode()
                .config[androidx.compose.ui.semantics.SemanticsActions.CustomActions]
                .first()
        assertTrue(resume.action())
        assertEquals("resumeDevice 7c19000000000003", calls.last())
    }

    @Test
    fun `UX-DR84 a 403 on a Resume says You can't change this on the Site, under that Device`() {
        show(SiteRole.Administrator, pauses(SiteRole.Administrator))
        pause =
            PauseState(
                failure =
                    PauseFailure(
                        PauseTarget(PauseScope.Device, "3f2a9c0d1e4b5a67"),
                        PauseNotice.Forbidden,
                        "Home garden",
                    ),
            )
        compose.waitForIdle()

        val notice = compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.")
        notice.assertExists()
        // Under the Hub it is about, above the Nodes.
        val noticeY = notice.fetchSemanticsNode().positionInRoot.y
        assertTrue(
            noticeY >
                compose
                    .onNodeWithText("3f2a9c0d1e4b5a67")
                    .fetchSemanticsNode()
                    .positionInRoot.y,
        )
        assertTrue(
            noticeY <
                compose
                    .onNodeWithText("7c19a1b2c3d4e5f6")
                    .fetchSemanticsNode()
                    .positionInRoot.y,
        )
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR70 a Resume the Server did not answer says Can't reach your Server with Try again`() {
        show(SiteRole.Administrator, pauses(SiteRole.Administrator))
        pause =
            PauseState(
                failure =
                    PauseFailure(
                        PauseTarget(PauseScope.Device, "7c19000000000003"),
                        PauseNotice.Unreachable,
                        "Home garden",
                    ),
            )
        compose.waitForIdle()

        compose.onNodeWithText("Can't reach your Server.").assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertEquals("retry", calls.last())
    }

    @Test
    fun `UX-DR70 while a Pause or Resume runs no row starts another`() {
        show(SiteRole.Administrator, pauses(SiteRole.Administrator))
        pause = PauseState(resuming = PauseTarget(PauseScope.Device, "3f2a9c0d1e4b5a67"))
        compose.waitForIdle()

        compose.onNodeWithContentDescription("Resume Hub 3f2a9c0d1e4b5a67").assertIsNotEnabled()
        compose.onNodeWithContentDescription("Pause Node 9c0d1e4b5a673f2a").assertIsNotEnabled()
    }
}
