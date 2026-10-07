package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasNoClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isDialog
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsNotice
import com.escendit.coldframe.core.lots.LotsNoticeKind
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.RemoveLotConfirmation
import com.escendit.coldframe.core.lots.RenameLotForm
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesNotice
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Site settings (UX-DR74, UX-DR84) and the Garden Lot tiles (UX-DR18, UX-DR20), Story 1.9. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SiteSettingsScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var lots by mutableStateOf<LotsState>(LotsState.Idle)

    private val actions =
        LotsActions(
            load = { calls += "load" },
            setSiteName = { calls += "siteName $it" },
            renameSite = { calls += "renameSite" },
            setNewLotName = { calls += "newLot $it" },
            createLot = { calls += "create" },
            startRename = { calls += "startRename $it" },
            setRename = { calls += "setRename $it" },
            rename = { calls += "rename" },
            cancelRename = { calls += "cancelRename" },
            askRemove = { calls += "askRemove $it" },
            confirmRemove = { calls += "confirmRemove" },
            cancelRemove = { calls += "cancelRemove" },
        )

    private val tomatoes = LotSummary("lot-t", "Tomatoes", LotStatus.NoNode)
    private val beans = LotSummary("lot-b", "Beans", LotStatus.NoNode)

    private fun ready(
        role: SiteRole = SiteRole.Owner,
        lots: List<LotSummary> = listOf(tomatoes, beans),
        siteName: SiteNameForm = SiteNameForm("Home garden", null, false),
        create: CreateLotForm = CreateLotForm("", null, false, "key-1"),
        renaming: RenameLotForm? = null,
        removing: RemoveLotConfirmation? = null,
        notice: LotsNotice? = null,
    ) = LotsState.Ready(homeSite(role), lots, siteName, create, renaming, removing, notice)

    private fun show(
        state: LotsState,
        role: SiteRole = (state as? LotsState.Ready)?.site?.role ?: SiteRole.Owner,
    ) {
        lots = state
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(homeSite(role)),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                lots = lots,
                lotsActions = actions,
            )
        }
    }

    /** From the Garden through the Settings tab to Site settings. */
    private fun openSiteSettings(state: LotsState) {
        show(state)
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Site settings").performClick()
        compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
    }

    // Navigation

    @Test
    fun `UX-DR74 Site settings is the first Settings row and names the current Site`() {
        show(ready())

        compose.onNodeWithText("Settings").performClick()

        val rows = compose.onAllNodesWithText("Site settings").fetchSemanticsNodes()
        val appearance = compose.onNodeWithText("Appearance").fetchSemanticsNode()
        assertEquals(1, rows.size)
        assertTrue(rows.single().boundsInRoot.top < appearance.boundsInRoot.top)
        compose.onNodeWithText("Name and Lots of Home garden").assertExists()
    }

    @Test
    fun `UX-DR74 the Site menu opens Site settings, and back returns to the Settings index`() {
        show(ready())

        compose.onNodeWithContentDescription("Site menu for Home garden").performClick()
        compose.onNodeWithText("Site settings").performClick()

        compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
        compose.onNodeWithContentDescription("Back").performClick()
        compose.onNode(isHeading().and(hasText("Settings"))).assertExists()
    }

    // Owner

    @Test
    fun `UX-DR74 an Owner renames the Site with the name field and Rename Site`() {
        openSiteSettings(ready())

        compose.onNode(hasSetTextAction().and(hasContentDescription("Site name"))).performTextReplacement("Home")
        compose.onNodeWithText("RENAME SITE").performClick()

        assertEquals(listOf("siteName Home", "renameSite"), calls)
    }

    @Test
    fun `UX-DR74 while renaming the Site the button reads Renaming Site in place`() {
        openSiteSettings(ready(siteName = SiteNameForm("Home", null, working = true)))

        compose.onNodeWithText("RENAMING SITE…").assertIsNotEnabled()
        compose.onAllNodesWithText("RENAME SITE").assertCountEquals(0)
    }

    @Test
    fun `UX-DR74 an invalid Site name shows its reason under the field`() {
        openSiteSettings(ready(siteName = SiteNameForm("", NameError.Blank, false)))

        compose.onNodeWithText("Enter a name for the Site.").assertExists()
    }

    // Lots

    @Test
    fun `UX-DR20 UX-DR74 the Lots are listed in the Server's order`() {
        openSiteSettings(ready(lots = listOf(beans, tomatoes)))

        val beansTop =
            compose
                .onNodeWithText("Beans")
                .fetchSemanticsNode()
                .positionInRoot.y
        val tomatoesTop =
            compose
                .onNodeWithText("Tomatoes")
                .fetchSemanticsNode()
                .positionInRoot.y
        assertTrue(beansTop < tomatoesTop)
    }

    @Test
    fun `UX-DR74 Create Lot forwards the name and shows Creating Lot while working`() {
        openSiteSettings(ready(SiteRole.Administrator))

        compose.onNode(hasSetTextAction().and(hasContentDescription("Lot name"))).performTextReplacement("Peppers")
        compose.onNodeWithText("CREATE LOT").performScrollTo().performClick()
        assertEquals(listOf("newLot Peppers", "create"), calls)

        lots = ready(SiteRole.Administrator, create = CreateLotForm("Peppers", null, true, "key-1"))
        compose.onNodeWithText("CREATING LOT…").assertIsNotEnabled()
    }

    @Test
    fun `UX-DR74 a blank Lot name shows its reason`() {
        openSiteSettings(ready(create = CreateLotForm("", NameError.Blank, false, "key-1")))

        compose.onNodeWithText("Enter a name for the Lot.").assertExists()
    }

    @Test
    fun `UX-DR74 Rename Lot opens for that Lot and the dialog renames it`() {
        openSiteSettings(ready(SiteRole.Administrator))

        compose.onAllNodesWithText("RENAME LOT")[1].performScrollTo().performClick()
        assertEquals(listOf("startRename lot-b"), calls)

        lots = ready(SiteRole.Administrator, renaming = RenameLotForm("lot-b", "Beans", null, false))
        compose
            .onNode(hasSetTextAction().and(hasContentDescription("Lot name")).and(hasText("Beans")))
            .performTextReplacement("Peppers")
        compose.onNode(hasText("RENAME LOT").and(hasAnyAncestor(isDialog()))).performClick()
        assertEquals(listOf("startRename lot-b", "setRename Peppers", "rename"), calls)
    }

    @Test
    fun `UX-DR74 removing a Lot asks in a dialog naming it, and Cancel sends nothing`() {
        openSiteSettings(ready())

        compose.onAllNodesWithText("REMOVE LOT")[0].performScrollTo().performClick()
        assertEquals(listOf("askRemove lot-t"), calls)

        lots = ready(removing = RemoveLotConfirmation("lot-t", "Tomatoes", false))
        compose.onNodeWithText("Remove Lot Tomatoes?").assertExists()
        compose.onNodeWithText("Its history stays in Coldframe.").assertExists()
        compose.onNodeWithText("CANCEL").performClick()
        assertEquals(listOf("askRemove lot-t", "cancelRemove"), calls)
    }

    @Test
    fun `UX-DR74 confirming Remove Lot removes it, with Removing Lot while working`() {
        openSiteSettings(ready(removing = RemoveLotConfirmation("lot-t", "Tomatoes", false)))

        compose
            .onNode(hasText("REMOVE LOT").and(hasAnyAncestor(isDialog())))
            .performClick()
        assertEquals(listOf("confirmRemove"), calls)

        lots = ready(removing = RemoveLotConfirmation("lot-t", "Tomatoes", true))
        compose.onNodeWithText("REMOVING LOT…").assertIsNotEnabled()
        compose.onAllNodesWithText("CANCEL").assertCountEquals(0)
    }

    @Test
    fun `UX-DR74 a Lot holding a Node says to move or unassign it first`() {
        openSiteSettings(ready(notice = LotsNotice(LotsNoticeKind.LotClaimed, "Tomatoes")))

        compose.onNodeWithText("Move or unassign the Node on Tomatoes first.").assertExists()
        compose.onNodeWithText("Tomatoes").assertExists()
    }

    @Test
    fun `UX-DR74 Keycloak down on Rename Site says nothing was renamed`() {
        openSiteSettings(ready(notice = LotsNotice(LotsNoticeKind.RenameSiteUnavailable)))

        compose
            .onNodeWithText("The Site was not renamed: your Server's sign-in service didn't answer. Try again.")
            .assertExists()
    }

    @Test
    fun `UX-DR74 a failed load shows its notice with Try again`() {
        show(LotsState.Failed(homeSite(), SitesNotice.Unreachable))
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Site settings").performClick()

        compose.onNodeWithText("TRY AGAIN").performClick()

        assertEquals(listOf("load"), calls)
    }

    // Role gating

    @Test
    fun `UX-DR84 an Administrator sees the Site name as text and no Rename Site, but edits Lots`() {
        openSiteSettings(ready(SiteRole.Administrator))

        compose.onAllNodesWithText("RENAME SITE").assertCountEquals(0)
        compose.onAllNodes(hasSetTextAction().and(hasContentDescription("Site name"))).assertCountEquals(0)
        compose.onNodeWithText("Home garden").assertExists()
        compose.onNodeWithText("CREATE LOT").assertExists()
        compose.onAllNodesWithText("RENAME LOT").assertCountEquals(2)
        compose.onAllNodesWithText("REMOVE LOT").assertCountEquals(2)
        compose.onAllNodesWithText("Only Owners and Administrators can change Lots.").assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Member sees the name and Lots read-only with one notice and no controls`() {
        openSiteSettings(ready(SiteRole.Member))

        compose.onNodeWithText("Only Owners and Administrators can change Lots.").assertExists()
        compose.onNodeWithText("Tomatoes").assertExists()
        compose.onNodeWithText("Beans").assertExists()
        compose.onAllNodes(hasSetTextAction()).assertCountEquals(0)
        for (control in listOf("RENAME SITE", "CREATE LOT", "RENAME LOT", "REMOVE LOT")) {
            compose.onAllNodesWithText(control).assertCountEquals(0)
        }
    }

    @Test
    fun `UX-DR84 a 403 race names the Site and asks for an Owner or Administrator`() {
        openSiteSettings(ready(notice = LotsNotice(LotsNoticeKind.Forbidden, "Home garden")))

        compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.").assertExists()
    }

    @Test
    fun `UX-DR74 with no Lots the list says so`() {
        openSiteSettings(ready(lots = emptyList()))

        compose.onNodeWithText("No Lots yet.").assertExists()
    }

    // Garden Lot tiles

    @Test
    fun `UX-DR18 each no-Node Lot is one tile spoken as its name, no Node, add a Node`() {
        show(ready(lots = listOf(tomatoes, beans)))

        compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").assertExists()
        compose.onNodeWithContentDescription("Beans, no Node, add a Node").assertExists()
        compose.onNode(isHeading().and(hasText("Lots"))).assertExists()
        // One element per tile, spoken as a whole. For an Owner it starts Add a Node (Story 4.3).
        compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").assertHasClickAction()
    }

    @Test
    fun `UX-DR18 UX-DR84 a Member's no-Node tile is spoken the same`() {
        show(ready(role = SiteRole.Member, lots = listOf(tomatoes)))

        compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").assertExists()
    }

    @Test
    fun `UX-DR18 the Garden keeps the Story 1_8 content above the Lot grid`() {
        show(ready())

        val headline =
            compose
                .onNodeWithText("No Readings yet")
                .fetchSemanticsNode()
                .positionInRoot.y
        val steps =
            compose
                .onNodeWithText("First steps")
                .fetchSemanticsNode()
                .positionInRoot.y
        val lotsHeading =
            compose
                .onNode(isHeading().and(hasText("Lots")))
                .fetchSemanticsNode()
                .positionInRoot.y
        assertTrue(headline < steps && steps < lotsHeading)
    }

    @Test
    fun `UX-DR20 the tiles keep the Server's order`() {
        show(ready(role = SiteRole.Member, lots = listOf(beans, tomatoes)))

        val beansTile = compose.onNodeWithContentDescription("Beans, no Node, add a Node").fetchSemanticsNode()
        val tomatoesTile = compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").fetchSemanticsNode()
        assertTrue(beansTile.positionInRoot.x < tomatoesTile.positionInRoot.x)
        assertEquals(beansTile.positionInRoot.y, tomatoesTile.positionInRoot.y)
    }

    @Test
    fun `UX-DR18 a Lot with a Node that has not reported is an unknown tile, never only a name`() {
        show(ready(lots = listOf(LotSummary("lot-h", "Herbs", LotStatus.Unknown))))

        compose.onNodeWithContentDescription("Herbs, unknown, no Readings yet").assertExists()
        compose.onAllNodesWithContentDescription("Herbs, no Node, add a Node").assertCountEquals(0)
    }

    @Test
    fun `UX-DR20 at font scale 2 the Lot grid is one column`() {
        lots = ready()
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lots,
                    lotsActions = actions,
                )
            }
        }

        val first = compose.onNodeWithContentDescription("Tomatoes, no Node, add a Node").fetchSemanticsNode()
        val second = compose.onNodeWithContentDescription("Beans, no Node, add a Node").fetchSemanticsNode()
        assertEquals(first.positionInRoot.x, second.positionInRoot.x)
        assertTrue(second.positionInRoot.y >= first.positionInRoot.y + first.size.height)
    }

    @Test
    fun `UX-DR20 a failed Lot load shows the Unreachable notice in place of the grid`() {
        show(LotsState.Failed(homeSite(), SitesNotice.Unreachable))

        compose
            .onNodeWithText("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.")
            .assertExists()
        compose.onAllNodes(isHeading().and(hasText("Lots"))).assertCountEquals(0)
    }
}
