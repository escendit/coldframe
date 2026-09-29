package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.isSelectable
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextReplacement
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.TimeZoneProposal
import com.escendit.coldframe.designtokens.ColorTokens
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Create Site, the empty Garden, the Site switcher and the Site menu (Story 1.8). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SitesScreensTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var sites by mutableStateOf<SitesState>(SitesState.Loading)

    private val actions =
        SitesActions(
            load = { calls += "load" },
            select = { calls += "select $it" },
            newSite = { calls += "newSite" },
            cancelNewSite = { calls += "cancel" },
            setName = { calls += "name $it" },
            confirmTimeZone = { calls += "confirm" },
            changeTimeZone = { calls += "change" },
            pickTimeZone = { calls += "pick $it" },
            submit = { calls += "submit" },
            timeZones = { listOf("America/New_York", "Europe/Zurich", "Pacific/Auckland") },
        )

    private val home = homeSite()
    private val allotment = SiteSummary("site-allotment", "Allotment", SiteRole.Member)

    private fun form(
        name: String = "",
        nameError: NameError? = null,
        working: Boolean = false,
        notice: SitesNotice? = null,
        timeZone: TimeZoneProposal = TimeZoneProposal("Europe/Zurich", null, false),
        cancellable: Boolean = false,
    ) = CreateSiteForm(name, nameError, working, notice, "key-1", timeZone, cancellable)

    private fun show(initial: SitesState) {
        sites = initial
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = sites,
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                sitesActions = actions,
            )
        }
    }

    // Create Site

    @Test
    fun `UX-DR61 no Membership shows Create Site instead of the tabs, with the name field and the detected zone`() {
        show(SitesState.NeedsSite(form()))

        compose.onNode(isHeading().and(hasText("Create Site"))).assertExists()
        compose.onNodeWithText("Site name").assertExists()
        compose.onNodeWithText("For example Home garden. Up to 100 characters.").assertExists()
        compose.onNodeWithText("Is your time zone Europe/Zurich?").assertExists()
        compose.onAllNodesWithText("Garden").assertCountEquals(0)
        compose.onAllNodesWithText("CANCEL").assertCountEquals(0)
    }

    @Test
    fun `UX-DR61 typing forwards the name and CREATE SITE submits`() {
        show(SitesState.NeedsSite(form()))

        compose.onNode(hasSetTextAction()).performTextInput("Home")
        compose.onNodeWithText("CREATE SITE").performScrollTo().performClick()

        assertEquals(listOf("name Home", "submit"), calls)
    }

    @Test
    fun `UX-DR61 while working the button shows Creating Site in place and ignores presses`() {
        show(SitesState.NeedsSite(form(name = "Home", working = true)))

        compose.onNodeWithText("CREATING SITE…").assertIsNotEnabled()
        compose.onAllNodesWithText("CREATE SITE").assertCountEquals(0)
    }

    @Test
    fun `UX-DR61 an invalid name shows its one-line reason under the field`() {
        show(SitesState.NeedsSite(form(nameError = NameError.Blank)))
        compose.onNodeWithText("Enter a name for the Site.").assertExists()

        sites = SitesState.NeedsSite(form(nameError = NameError.TooLong))
        compose.onNodeWithText("Use at most 100 characters.").assertExists()
        val field = compose.onNode(hasSetTextAction()).fetchSemanticsNode()
        assertEquals("Use at most 100 characters.", field.config[SemanticsProperties.Error])
    }

    @Test
    fun `UX-DR61 a 503 says the Site was not created and Try again submits again`() {
        show(SitesState.NeedsSite(form(name = "Home", notice = SitesNotice.IdentityProviderUnavailable)))

        compose
            .onNodeWithText(
                "The Site was not created: your Server's sign-in service didn't answer. Try again.",
            ).assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()

        assertEquals(listOf("submit"), calls)
    }

    @Test
    fun `UX-DR61 a reused key and an unreachable Server have their notices, a certificate failure has no action`() {
        show(SitesState.NeedsSite(form(notice = SitesNotice.KeyReused)))
        compose.onNodeWithText("The Site was not created. Try again.").assertExists()

        sites = SitesState.NeedsSite(form(notice = SitesNotice.Unreachable))
        compose
            .onNodeWithText("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.")
            .assertExists()

        sites = SitesState.NeedsSite(form(notice = SitesNotice.Certificate))
        compose.onNodeWithText("Your Server's certificate isn't trusted", substring = true).assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR54 UX-DR61 Confirm and Change on the time-zone panel go to the core`() {
        show(SitesState.NeedsSite(form()))

        compose.onNodeWithText("CONFIRM").performScrollTo().performClick()
        compose.onNodeWithText("CHANGE").performScrollTo().performClick()

        assertEquals(listOf("confirm", "change"), calls)
    }

    @Test
    fun `UX-DR61 Change shows a searchable list of IANA zones and a pick goes to the core`() {
        show(SitesState.NeedsSite(form(timeZone = TimeZoneProposal("Europe/Zurich", null, true))))

        compose.onNodeWithText("America/New_York").assertExists()
        compose
            .onNode(
                hasSetTextAction().and(hasContentDescription("Search time zones")),
            ).performTextReplacement("auck")
        compose.onAllNodesWithText("America/New_York").assertCountEquals(0)
        // The list scrolls inside the panel, inside the scrolling form; tap it through its action.
        compose
            .onNode(
                hasText("Pacific/Auckland").and(hasClickAction()),
            ).performSemanticsAction(SemanticsActions.OnClick)

        assertEquals(listOf("pick Pacific/Auckland"), calls)
    }

    @Test
    fun `UX-DR61 a chosen zone is named and marked selected in the list`() {
        show(SitesState.NeedsSite(form(timeZone = TimeZoneProposal("Europe/Zurich", "Pacific/Auckland", true))))

        compose.onNodeWithText("Your time zone is Pacific/Auckland.").assertExists()
        compose.onAllNodesWithText("CONFIRM").assertCountEquals(0)
        compose.onNode(hasText("Pacific/Auckland").and(hasClickAction())).assertIsSelected()
        compose.onNode(hasText("Europe/Zurich").and(hasClickAction())).assertIsNotSelected()
    }

    @Test
    fun `UX-DR23 UX-DR61 New Site opens Create Site over the Garden and Cancel returns`() {
        show(readySites().copy(creating = form(cancellable = true)))

        compose.onNode(isHeading().and(hasText("Create Site"))).assertExists()
        compose.onNodeWithText("CANCEL").performClick()

        assertEquals(listOf("cancel"), calls)
    }

    // Loading the Sites

    @Test
    fun `loading the Sites shows only the background`() {
        show(SitesState.Loading)

        compose.onAllNodes(hasClickAction()).assertCountEquals(0)
        compose.onAllNodesWithText("Garden").assertCountEquals(0)
    }

    @Test
    fun `a failed load shows its notice and Try again loads, except for a certificate failure`() {
        show(SitesState.Failed(SitesNotice.Unreachable))
        compose.onNodeWithText("TRY AGAIN").performClick()
        assertEquals(listOf("load"), calls)

        sites = SitesState.Failed(SitesNotice.Unexpected)
        compose.onNodeWithText("Your Server couldn't list your Sites. Nothing was changed.").assertExists()

        sites = SitesState.Failed(SitesNotice.Certificate)
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    // The empty Garden

    @Test
    fun `UX-DR21 UX-DR62 UX-DR82 the empty Garden shows the Site name, No Readings yet as a heading and the subline`() {
        show(readySites())

        compose.onNodeWithText("Home garden").assertExists()
        compose.onNode(isHeading().and(hasText("No Readings yet"))).assertExists()
        compose.onNodeWithText("Nothing is measuring, so there's no status to show.").assertExists()
    }

    @Test
    fun `UX-DR54 UX-DR82 UX-DR66 four step tiles, only Add a Hub acts, no notice for an Owner`() {
        show(readySites())

        listOf("STEP 1", "STEP 2", "STEP 3", "STEP 4").forEach { compose.onNodeWithText(it).assertExists() }
        compose.onAllNodes(hasText("Add a Hub").and(hasClickAction())).assertCountEquals(1)
        listOf("Add a Node", "Calibrate", "Set a low Threshold").forEach {
            compose.onNodeWithText(it).assertExists()
            compose.onAllNodes(hasText(it).and(hasClickAction())).assertCountEquals(0)
        }
        compose.onNodeWithText("NEXT").assertExists()
        compose.onAllNodesWithText("LATER").assertCountEquals(3)
        compose.onAllNodesWithText("Only Owners and Administrators can add Devices.").assertCountEquals(0)
    }

    @Test
    fun `UX-DR54 an Administrator sees the tiles without the Member notice`() {
        show(readySites(homeSite(SiteRole.Administrator)))

        compose.onNodeWithText("Add a Hub").assertExists()
        compose.onAllNodesWithText("Only Owners and Administrators can add Devices.").assertCountEquals(0)
    }

    @Test
    fun `UX-DR54 a Member sees the tiles without actions and the read-only notice`() {
        show(readySites(homeSite(SiteRole.Member)))

        compose.onNodeWithText("Add a Hub").assertExists()
        compose.onNodeWithText("Only Owners and Administrators can add Devices.").assertExists()
    }

    @Test
    fun `UX-DR54 the next step is solid primary and a later step is not filled`() {
        show(readySites())

        fun inside(text: String): Color {
            val pixels =
                compose
                    .onNode(hasText(text))
                    .performScrollTo()
                    .captureToImage()
                    .toPixelMap()
            return pixels[pixels.width - 4, pixels.height - 4]
        }
        val tile = { text: String -> compose.onAllNodes(hasText(text)) }
        tile("Add a Hub").assertCountEquals(1)
        assertEquals(Color(ColorTokens.primary.light.toInt()), inside("Add a Hub"))
        assertEquals(Color(ColorTokens.background.light.toInt()), inside("Calibrate"))
    }

    // The Site switcher

    @Test
    fun `UX-DR23 the switcher lists the Sites in Server order with Role, current selected, New Site last`() {
        show(readySites(current = allotment, sites = listOf(home, allotment)))

        compose.onNodeWithContentDescription("Allotment, switch Site").performClick()

        val rows =
            compose.onAllNodes(
                hasClickAction().and(hasText("Home garden").or(hasText("Allotment")).or(hasText("New Site"))),
            )
        val labels = rows.fetchSemanticsNodes().map { it.spokenLabel() }.filterNot { it.contains("switch Site") }
        assertEquals(listOf("Home garden OWNER", "Allotment MEMBER", "New Site"), labels)
        compose.onNode(isSelectable().and(hasText("Allotment"))).assertIsSelected()
        compose.onNode(isSelectable().and(hasText("Home garden"))).assertIsNotSelected()
    }

    @Test
    fun `UX-DR23 picking a Site switches to it and New Site opens Create Site`() {
        show(readySites(current = home, sites = listOf(home, allotment)))

        compose.onNodeWithContentDescription("Home garden, switch Site").performClick()
        compose.onNode(isSelectable().and(hasText("Allotment"))).performClick()
        compose.onNodeWithContentDescription("Home garden, switch Site").performClick()
        compose.onNodeWithText("New Site").performClick()

        assertEquals(listOf("select site-allotment", "newSite"), calls)
    }

    @Test
    fun `UX-DR23 with a single Site the switcher still offers New Site`() {
        show(readySites())

        compose.onNodeWithContentDescription("Home garden, switch Site").performClick()

        compose.onNodeWithText("New Site").assertExists()
    }

    // The Site menu

    private fun assertSiteMenuHoldsOnlySiteSettings(role: SiteRole) {
        show(readySites(homeSite(role)))

        compose.onNodeWithContentDescription("Site menu for Home garden").performClick()

        compose.onNodeWithText("Site settings").assertExists()
        compose.onAllNodesWithText("Pause Home garden").assertCountEquals(0)
        compose.onAllNodesWithText("Resume Home garden").assertCountEquals(0)
        compose.onNodeWithText("Site settings").performClick()
        compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
        assertTrue(calls.isEmpty())
    }

    @Test
    fun `UX-DR22 UX-DR74 an Owner's Site menu holds only Site settings, which opens Site settings`() {
        assertSiteMenuHoldsOnlySiteSettings(SiteRole.Owner)
    }

    @Test
    fun `UX-DR22 an Administrator's Site menu holds only Site settings in this story`() {
        assertSiteMenuHoldsOnlySiteSettings(SiteRole.Administrator)
    }

    @Test
    fun `UX-DR22 a Member's Site menu has no Pause and holds Site settings`() {
        assertSiteMenuHoldsOnlySiteSettings(SiteRole.Member)
    }
}
