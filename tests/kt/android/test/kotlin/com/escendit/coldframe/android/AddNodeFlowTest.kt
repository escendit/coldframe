package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertHasNoClickAction
import androidx.compose.ui.test.assertIsFocused
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.setup.AddNodeFlow
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.setup.CodeError
import com.escendit.coldframe.core.setup.CodeForm
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.setup.LotChoice
import com.escendit.coldframe.core.setup.LotForm
import com.escendit.coldframe.core.setup.LotPickerNotice
import com.escendit.coldframe.core.setup.LotPickerNoticeKind
import com.escendit.coldframe.core.setup.NodeAnnouncement
import com.escendit.coldframe.core.setup.NodeAnnouncementKind
import com.escendit.coldframe.core.setup.NodeCandidate
import com.escendit.coldframe.core.setup.NodeOutcome
import com.escendit.coldframe.core.setup.NodeOutcomeAction
import com.escendit.coldframe.core.setup.NodeOutcomeKind
import com.escendit.coldframe.core.setup.NodeSetupState
import com.escendit.coldframe.core.setup.NodeSetupStep
import com.escendit.coldframe.core.setup.OutcomeAction
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.core.setup.SignalStrength
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** States of the Add a Node flow as the core hands them to the shell (Story 4.3). */
object NodeStates {
    val node7c19 = NodeCandidate("peripheral-1", "7C19", -52, pressedJustNow = true)
    private val node11c0 = NodeCandidate("peripheral-2", "11C0", -78, pressedJustNow = false)

    private val choices =
        listOf(
            LotChoice("lot-t", "Tomatoes", selectable = true),
            LotChoice("lot-b", "Beans", selectable = false),
            LotChoice("lot-p", "Peppers", selectable = true),
        )

    val press = NodeSetupState.CLOSED.copy(open = true, siteId = "site-home", siteName = "Home garden")

    val scan = press.copy(step = NodeSetupStep.Scan, candidates = listOf(node7c19, node11c0), selected = node7c19)

    val code =
        scan.copy(
            step = NodeSetupStep.Code,
            code = CodeForm("N4D3-C0DE", null, working = false, accepted = false),
        )
    val wrongCode = code.copy(code = CodeForm("N4D3-C0DF", CodeError.WrongCode, working = false, accepted = false))
    val accepted =
        code.copy(code = CodeForm("N4D3-C0DE", null, working = false, accepted = true), deviceId = "7c19aa01b2d4e6f8")

    val lot =
        accepted.copy(
            step = NodeSetupStep.Lot,
            lots = LotForm.EMPTY.copy(choices = choices, selectedId = "lot-t"),
        )

    val lotTaken =
        lot.copy(
            lots =
                lot.lots.copy(
                    choices = choices.map { if (it.id == "lot-t") it.copy(selectable = false) else it },
                    selectedId = null,
                    notice = LotPickerNotice(LotPickerNoticeKind.LotTaken, "Tomatoes"),
                ),
        )

    val assigned =
        lot.copy(step = NodeSetupStep.Outcome, outcome = NodeOutcome(NodeOutcomeKind.Assigned, 5))

    val stoppedListening = code.copy(outcome = NodeOutcome(NodeOutcomeKind.StoppedListening, 3))

    fun failed(
        kind: NodeOutcomeKind,
        from: NodeSetupState = code,
    ): NodeSetupState = from.copy(outcome = NodeOutcome(kind, from.step.number))
}

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AddNodeFlowTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var state by mutableStateOf(NodeStates.press)
    private var hubSetup by mutableStateOf(HubSetupState.CLOSED)

    private val actions =
        NodeSetupActions(
            open = { calls += "open $it" },
            close = { calls += "close" },
            recheckRadio = { calls += "recheck" },
            announcing = { calls += "announcing $it" },
            back = { calls += "back" },
            leave = { calls += "leave" },
            confirmLeave = { calls += "confirmLeave" },
            stayInFlow = { calls += "stay" },
            continueFromPress = { calls += "continueFromPress" },
            select = { calls += "select $it" },
            continueFromScan = { calls += "continueFromScan" },
            setCode = { calls += "code $it" },
            submitCode = { calls += "submitCode" },
            continueFromCode = { calls += "continueFromCode" },
            retryLots = { calls += "retryLots" },
            chooseLot = { calls += "lot $it" },
            openNewLot = { calls += "openNewLot" },
            setNewLotName = { calls += "newLot $it" },
            createLot = { calls += "createLot" },
            assign = { calls += "assign" },
            outcomeAction = { calls += "outcome $it" },
        )

    private val lots =
        LotsState.Ready(
            site = homeSite(),
            lots =
                listOf(
                    LotSummary("lot-t", "Tomatoes", LotStatus.NoNode),
                    LotSummary("lot-b", "Beans", LotStatus.Ok),
                ),
            siteName = SiteNameForm("Home garden", null, false),
            create = CreateLotForm("", null, false, "key-1"),
            renaming = null,
            removing = null,
            notice = null,
        )

    private var started = false

    /** Signed in on "Home garden" as [role], with [next] as the Node flow's state. */
    private fun show(
        next: NodeSetupState,
        role: SiteRole = SiteRole.Owner,
    ) {
        state = next
        if (!started) {
            started = true
            val site = homeSite(role)
            compose.setContent {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(site),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lots.copy(site = site),
                    hubSetup = hubSetup,
                    hubSetupActions = HubSetupActions(outcomeAction = { calls += "hub outcome $it" }),
                    devices = DevicesState.Ready(site, emptyList()),
                    devicesActions = DevicesActions(load = { calls += "load" }),
                    nodeSetup = state,
                    nodeSetupActions = actions,
                )
            }
        }
        compose.waitForIdle()
    }

    private fun liveRegionOf(description: String): LiveRegionMode? =
        compose
            .onNode(hasContentDescription(description), useUnmergedTree = true)
            .fetchSemanticsNode()
            .config
            .getOrNull(SemanticsProperties.LiveRegion)

    private fun liveRegionOfText(text: String): LiveRegionMode? =
        compose
            .onNode(hasText(text), useUnmergedTree = true)
            .fetchSemanticsNode()
            .config
            .getOrNull(SemanticsProperties.LiveRegion)

    // The three entry points

    @Test
    fun `UX-DR67 Add a Node in Devices opens the flow without a Lot for an Owner`() {
        show(NodeSetupState.CLOSED)
        compose.onNodeWithText("Devices").performClick()

        val addNode = compose.allNodes().single { it.isClickable && it.spokenLabel() == "ADD A NODE" }
        assertEquals(Role.Button, addNode.role)
        compose.onNodeWithText("ADD A HUB").assertExists()
        compose.onNodeWithText("ADD A NODE").performClick()

        assertTrue("open null" in calls)
    }

    @Test
    fun `UX-DR67 UX-DR18 a no-Node tile is a button that opens the flow with its Lot for an Administrator`() {
        show(NodeSetupState.CLOSED, role = SiteRole.Administrator)

        val tile = compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").fetchSemanticsNode()
        assertEquals(Role.Button, tile.role)
        compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").performScrollTo().performClick()

        assertEquals(listOf("open lot-t"), calls)
        // A Lot that has a Node is not an entry point.
        compose.onNodeWithContentDescription("Beans, OK").assertHasNoClickAction()
    }

    @Test
    fun `UX-DR67 UX-DR55 Add a Node on Hub is online goes to the core, which opens the Node flow`() {
        hubSetup = HubStates.online
        show(NodeSetupState.CLOSED)

        compose.onNodeWithText("Add a Node", ignoreCase = true).performScrollTo().performClick()
        assertEquals(listOf("hub outcome ${OutcomeAction.AddNode}"), calls.filter { it != "recheck" })

        // The core closes the Hub flow and opens the Node flow on step 1.
        hubSetup = HubSetupState.CLOSED
        show(NodeStates.press)
        compose.onNodeWithContentDescription("Step 1 of 5").assertExists()
        compose.onNode(isHeading().and(hasText("Press the setup button on the Node"))).assertExists()
    }

    @Test
    fun `UX-DR67 UX-DR84 a Member has no Add a Node action and the no-Node tile stays non-interactive`() {
        show(NodeSetupState.CLOSED, role = SiteRole.Member)

        val tile = compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").fetchSemanticsNode()
        assertFalse(tile.isClickable)
        assertEquals(null, tile.role)

        compose.onNodeWithText("Devices").performClick()
        compose.onAllNodesWithText("ADD A NODE").assertCountEquals(0)
        compose.onAllNodesWithText("ADD A HUB").assertCountEquals(0)
        assertTrue(calls.none { it.startsWith("open") })
    }

    @Test
    fun `UX-DR67 the flow replaces the tab shell while open and closing returns to it`() {
        show(NodeStates.press)
        compose.onNode(isHeading().and(hasText("Garden"))).assertDoesNotExist()
        show(NodeSetupState.CLOSED)
        compose.onNode(isHeading().and(hasText("Garden"))).assertExists()
    }

    // Shell

    @Test
    fun `UX-DR39 UX-DR67 step 1 shows 01 of 05 with Cancel and the instruction`() {
        show(NodeStates.press)
        compose.onNodeWithContentDescription("Step 1 of 5").assertExists()
        compose.onNode(isHeading().and(hasText("Press the setup button on the Node"))).assertIsFocused()
        compose.onNodeWithText("Hold the button for 3 seconds. The Node then listens for 3 minutes.").assertExists()
        compose.onNodeWithText("Look for the Node", ignoreCase = true).performClick()
        assertTrue("continueFromPress" in calls)
        compose.onNodeWithText("Cancel", ignoreCase = true).performClick()
        assertTrue("back" in calls)
    }

    @Test
    fun `UX-DR39 later steps show Back and focus moves to each step title`() {
        show(NodeStates.scan)
        compose.onNodeWithContentDescription("Step 2 of 5").assertExists()
        compose.onNode(isHeading().and(hasText("Pick the Node"))).assertIsFocused()
        compose.onNodeWithText("Back", ignoreCase = true).performClick()
        assertTrue("back" in calls)

        show(NodeStates.code)
        compose.onNodeWithContentDescription("Step 3 of 5").assertExists()
        compose.onNode(isHeading().and(hasText("Enter the setup code"))).assertIsFocused()

        show(NodeStates.lot)
        compose.onNodeWithContentDescription("Step 4 of 5").assertExists()
        compose.onNode(isHeading().and(hasText("Pick a Lot"))).assertIsFocused()
    }

    @Test
    fun `UX-DR39 system back goes to the core as leave, which asks once a Node is selected`() {
        show(NodeStates.lot)
        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.waitForIdle()
        assertTrue("leave" in calls)
    }

    @Test
    fun `UX-DR39 leaving mid-flow asks Stop setting up Node 7C19 and names that nothing is saved`() {
        show(NodeStates.lot.copy(confirmingLeave = true))
        compose.onNodeWithText("Stop setting up Node 7C19?").assertExists()
        compose.onNodeWithText("Nothing is saved on the Node.").assertExists()
        compose.onNodeWithText("Keep setting up", ignoreCase = true).performClick()
        assertTrue("stay" in calls)
        compose.onNodeWithText("Stop setting up", ignoreCase = true).performClick()
        assertTrue("confirmLeave" in calls)
    }

    @Test
    fun `UX-DR39 the screen stays awake while the flow is open`() {
        show(NodeStates.press)
        assertTrue(viewKeepsScreenOn())
        show(NodeSetupState.CLOSED)
        assertFalse(viewKeepsScreenOn())
    }

    private fun viewKeepsScreenOn(): Boolean {
        val root = compose.activity.window.decorView

        fun any(view: android.view.View): Boolean =
            view.keepScreenOn ||
                (view is android.view.ViewGroup && (0 until view.childCount).any { any(view.getChildAt(it)) })
        return any(root)
    }

    // Step 2

    @Test
    fun `UX-DR37 Nodes are one element each and only the last heard says Pressed just now`() {
        show(NodeStates.scan)
        compose.onNodeWithContentDescription("Node 7C19, pressed just now, strong signal").assertIsSelected()
        compose.onNodeWithContentDescription("Node 11C0, medium signal").assertIsNotSelected().performClick()
        assertTrue("select peripheral-2" in calls)
        // One element per tile: the badge is part of its spoken label, on one Node only.
        compose.onAllNodes(hasContentDescription("pressed just now", substring = true)).assertCountEquals(1)
        compose.onNodeWithText("Still scanning…").assertExists()
        compose.onNodeWithText("Set up Node 7C19", ignoreCase = true).performScrollTo().performClick()
        assertTrue("continueFromScan" in calls)
    }

    @Test
    fun `UX-DR94 Bluetooth off or denied shows the notice with Open Settings`() {
        show(NodeStates.scan.copy(radio = RadioState.Off, candidates = emptyList(), selected = null))
        compose.onNodeWithText("Coldframe needs Bluetooth to find the Node.").assertExists()
        compose.onNodeWithText("Open Settings", ignoreCase = true).assertExists()
        compose.onAllNodesWithText("Still scanning…").assertCountEquals(0)
    }

    @Test
    fun `UX-DR94 no Node in 30 s says how to wake it while scanning continues`() {
        show(NodeStates.scan.copy(noNodeYet = true, candidates = emptyList(), selected = null))
        compose
            .onNodeWithText("No Node in range yet. Hold its setup button for 3 seconds; it listens for 3 minutes.")
            .assertExists()
        compose.onNodeWithText("Still scanning…").assertExists()
    }

    // Step 3

    @Test
    fun `UX-DR94 a wrong setup code keeps the field and says why`() {
        show(NodeStates.wrongCode)
        compose.onNodeWithText("That setup code doesn't match Node 7C19. Check the serial console.").assertExists()
        compose.onNodeWithText("N4D3-C0DF").assertExists()
        compose.onNodeWithText("Check setup code", ignoreCase = true).performScrollTo().performClick()
        assertTrue("submitCode" in calls)
    }

    @Test
    fun `UX-DR41 the accepted code shows the Accepted chip, the full Device ID and Pick a Lot`() {
        show(NodeStates.accepted)
        compose.onNodeWithText("Accepted", ignoreCase = true).assertExists()
        compose.onNodeWithText("7c19aa01b2d4e6f8").assertExists()
        compose.onAllNodes(hasText("PICK A LOT")).assertCountEquals(1)
        compose.onNodeWithText("PICK A LOT").performScrollTo().performClick()
        assertTrue("continueFromCode" in calls)
    }

    // Step 4

    @Test
    fun `UX-DR38 a Lot with a Node is disabled and says Has a Node and the button names the result`() {
        show(NodeStates.lot)
        compose.onNodeWithContentDescription("Tomatoes").assertIsSelected()
        compose.onNodeWithContentDescription("Beans, has a Node").assertIsNotEnabled().assertHasNoClickAction()
        compose.onAllNodes(hasContentDescription(", has a Node", substring = true)).assertCountEquals(1)
        compose
            .onNodeWithContentDescription("Peppers")
            .assertIsNotSelected()
            .performScrollTo()
            .performClick()
        assertTrue("lot lot-p" in calls)

        compose.onNodeWithText("Put 7C19 in Tomatoes", ignoreCase = true).performScrollTo().performClick()
        assertTrue("assign" in calls)
    }

    @Test
    fun `UX-DR38 without a picked Lot there is no primary button and assigning shows its progress label`() {
        show(NodeStates.lot.copy(lots = NodeStates.lot.lots.copy(selectedId = null)))
        compose.onAllNodes(hasText("Put 7C19", substring = true, ignoreCase = true)).assertCountEquals(0)

        show(NodeStates.lot.copy(lots = NodeStates.lot.lots.copy(assigning = true)))
        compose.onNodeWithText("Putting 7C19 in Tomatoes…", ignoreCase = true).assertExists()
    }

    @Test
    fun `UX-DR38 New Lot opens an inline name field with the Create Lot copy`() {
        show(NodeStates.lot)
        compose.onNodeWithText("+ New Lot").performScrollTo().performClick()
        assertTrue("openNewLot" in calls)

        show(
            NodeStates.lot.copy(
                lots = NodeStates.lot.lots.copy(newLotOpen = true, newLotName = "", newLotError = NameError.Blank),
            ),
        )
        compose.onAllNodesWithText("+ New Lot").assertCountEquals(0)
        compose.onNodeWithText("Enter a name for the Lot.").assertExists()
        compose.onNodeWithContentDescription("Lot name").performScrollTo().performTextInput("Cucumbers")
        assertTrue("newLot Cucumbers" in calls)
        compose.onNodeWithText("Create Lot", ignoreCase = true).performScrollTo().performClick()
        assertTrue("createLot" in calls)
    }

    @Test
    fun `UX-DR38 unreadable Lots show the notice with Try again and loading says so`() {
        val unread = LotForm.EMPTY.copy(notice = LotPickerNotice(LotPickerNoticeKind.Unreachable))
        show(NodeStates.lot.copy(lots = unread))
        val message = "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi."
        assertEquals(LiveRegionMode.Assertive, liveRegionOfText(message))
        compose.onNodeWithText("Try again", ignoreCase = true).performClick()
        assertTrue("retryLots" in calls)

        show(NodeStates.lot.copy(lots = LotForm.EMPTY))
        compose.onNodeWithText("Fetching the Lots of Home garden…").assertExists()
    }

    @Test
    fun `UX-DR94 UX-DR105 a Lot taken meanwhile stays on step 4 and is said once, assertively`() {
        show(
            NodeStates.lotTaken.copy(
                announcement = NodeAnnouncement(4, NodeAnnouncementKind.LotTaken, true, "7C19", "Tomatoes", null, null),
            ),
        )
        compose.onNodeWithContentDescription("Step 4 of 5").assertExists()
        val message = "Tomatoes got a Node meanwhile. Pick another Lot."
        assertEquals(LiveRegionMode.Assertive, liveRegionOfText(message))
        // The notice is the announcement: the flow's own live region stays empty.
        compose.onAllNodesWithContentDescription(message).assertCountEquals(0)
        compose.onNodeWithContentDescription("Tomatoes, has a Node").assertIsNotEnabled()
        compose.onAllNodes(hasText("Put 7C19", substring = true, ignoreCase = true)).assertCountEquals(0)
    }

    @Test
    fun `UX-DR94 a Lot that is gone and an unreachable Server on assign say so on step 4`() {
        show(
            NodeStates.lot.copy(lots = NodeStates.lot.lots.copy(notice = LotPickerNotice(LotPickerNoticeKind.LotGone))),
        )
        assertEquals(LiveRegionMode.Assertive, liveRegionOfText("That Lot is gone. Pick another Lot."))

        val failed = NodeStates.lot.lots.copy(notice = LotPickerNotice(LotPickerNoticeKind.AssignFailed))
        show(NodeStates.lot.copy(lots = failed))
        assertEquals(LiveRegionMode.Assertive, liveRegionOfText("Can't reach your Server. Nothing was assigned."))
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
        // The button retries.
        compose.onNodeWithText("Put 7C19 in Tomatoes", ignoreCase = true).performScrollTo().performClick()
        assertTrue("assign" in calls)
    }

    // Outcomes

    @Test
    fun `UX-DR55 UX-DR67 Tomatoes has a Node takes focus and Done closes the flow`() {
        show(NodeStates.assigned)
        compose.onNode(isHeading().and(hasText("Tomatoes has a Node"))).assertIsFocused()
        compose
            .onNodeWithText("Node 7C19 reports for Tomatoes. Its Sensors appear with its first Readings.")
            .assertExists()
        compose.onAllNodes(hasText("stopped", substring = true, ignoreCase = true)).assertCountEquals(0)
        compose.onNodeWithText("Done", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${NodeOutcomeAction.Done}" in calls)
    }

    @Test
    fun `UX-DR94 UX-DR55 a Node that stopped listening stops step 3 and offers Try again`() {
        show(NodeStates.stoppedListening)
        compose.onNodeWithText("Step 3 stopped", ignoreCase = true).assertExists()
        compose.onNode(isHeading().and(hasText("7C19 stopped listening"))).assertIsFocused()
        compose.onNodeWithText("Press its setup button again.").assertExists()
        compose.onNodeWithText("Try again", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${NodeOutcomeAction.StartOver}" in calls)
    }

    @Test
    fun `UX-DR94 every error outcome has its headline, body and one action`() {
        val lot = NodeStates.lot
        val cases =
            listOf(
                Triple(
                    NodeStates.failed(NodeOutcomeKind.LostConnection),
                    "Lost the connection to Node 7C19",
                    "Try again",
                ),
                Triple(NodeStates.failed(NodeOutcomeKind.NodeRefused), "Node 7C19 refused the setup", "Close"),
                Triple(NodeStates.failed(NodeOutcomeKind.ServerUnreachable), "Can't reach your Server", "Try again"),
                Triple(
                    NodeStates.failed(NodeOutcomeKind.FingerprintMismatch),
                    "The Server's enrolment key doesn't check out",
                    "Close",
                ),
                Triple(
                    NodeStates.failed(NodeOutcomeKind.AlreadyAssigned, lot),
                    "Node 7C19 is already in another Lot",
                    "Close",
                ),
                Triple(NodeStates.failed(NodeOutcomeKind.OnAnotherSite, lot), "Node 7C19 is on another Site", "Close"),
                Triple(
                    NodeStates.failed(NodeOutcomeKind.NotAllowed, lot),
                    "You can't add Nodes to Home garden",
                    "Close",
                ),
            )
        for ((failed, title, action) in cases) {
            show(failed)
            compose.onNode(isHeading().and(hasText(title))).assertExists()
            compose.onNodeWithText("Step ${failed.step.number} stopped", ignoreCase = true).assertExists()
            compose.onAllNodes(hasText(action, ignoreCase = true)).assertCountEquals(1)
        }
        show(NodeStates.failed(NodeOutcomeKind.LostConnection))
        compose.onNodeWithText("Nothing was saved.").assertExists()
        show(NodeStates.failed(NodeOutcomeKind.NodeRefused))
        compose.onNodeWithText("Close", ignoreCase = true).performScrollTo().performClick()
        assertTrue("outcome ${NodeOutcomeAction.Close}" in calls)
    }

    // Announcements

    @Test
    fun `UX-DR105 a new Node and the outcome are announced politely, a wrong code and an error assertively`() {
        show(
            NodeStates.scan.copy(
                announcement =
                    NodeAnnouncement(
                        1,
                        NodeAnnouncementKind.CandidateFound,
                        false,
                        "7C19",
                        null,
                        SignalStrength.Strong,
                        null,
                    ),
            ),
        )
        assertEquals(LiveRegionMode.Polite, liveRegionOf("Node 7C19 found, strong signal."))

        show(
            NodeStates.wrongCode.copy(
                announcement = NodeAnnouncement(2, NodeAnnouncementKind.WrongCode, true, "7C19", null, null, null),
            ),
        )
        assertEquals(
            LiveRegionMode.Assertive,
            liveRegionOf("That setup code doesn't match Node 7C19. Check the serial console."),
        )

        show(
            NodeStates.stoppedListening.copy(
                announcement =
                    NodeAnnouncement(
                        3,
                        NodeAnnouncementKind.Error,
                        true,
                        "7C19",
                        null,
                        null,
                        NodeOutcomeKind.StoppedListening,
                    ),
            ),
        )
        assertEquals(LiveRegionMode.Assertive, liveRegionOf("7C19 stopped listening. Try again."))

        show(
            NodeStates.assigned.copy(
                announcement =
                    NodeAnnouncement(
                        4,
                        NodeAnnouncementKind.Assigned,
                        false,
                        "7C19",
                        "Tomatoes",
                        null,
                        null,
                    ),
            ),
        )
        assertEquals(LiveRegionMode.Polite, liveRegionOf("Tomatoes has a Node."))
    }

    @Test
    fun `UX-DR96 every step, notice and outcome fits at font scale 2`() {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeTheme(isDark = false) {
                    AddNodeFlow(state = state, actions = actions)
                }
            }
        }
        val newLot = NodeStates.lot.lots.copy(newLotOpen = true, newLotName = "Cucumbers")
        for (step in listOf(
            NodeStates.press,
            NodeStates.scan,
            NodeStates.wrongCode,
            NodeStates.accepted,
            NodeStates.lot,
            NodeStates.lotTaken,
            NodeStates.lot.copy(lots = newLot),
            NodeStates.assigned,
            NodeStates.stoppedListening,
        )) {
            state = step
            compose.assertNothingOverflows("${step.step.name} ${step.outcome?.kind}")
        }
    }
}
