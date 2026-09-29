package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.TimeZoneProposal
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Roborazzi snapshots of Create Site, the Garden with Lots, Site settings and every Add a Hub
 * step and outcome, light and dark, at
 * the largest font scale (2×). Baselines live in tests/kt/android/snapshots; `check` compares against them and
 * `./gradlew :android:recordRoborazziDebug` rewrites them.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h914dp-mdpi")
class SnapshotTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val createSite =
        SitesState.NeedsSite(
            CreateSiteForm(
                name = "Home",
                nameError = null,
                working = false,
                notice = null,
                idempotencyKey = "key-1",
                timeZone = TimeZoneProposal("Europe/Zurich", null, false),
                cancellable = false,
            ),
        )

    /** "Tomatoes" and "Beans", created in that order, both without a Node (Story 1.9). */
    private fun lotsOf(role: SiteRole) =
        LotsState.Ready(
            site = homeSite(role),
            lots =
                listOf(
                    LotSummary("lot-t", "Tomatoes", LotStatus.NoNode),
                    LotSummary("lot-b", "Beans", LotStatus.NoNode),
                ),
            siteName = SiteNameForm("Home garden", null, false),
            create = CreateLotForm("", null, false, "key-1"),
            renaming = null,
            removing = null,
            notice = null,
        )

    private fun snapshot(
        name: String,
        sites: SitesState,
        theme: ThemePreference,
        lots: LotsState = (sites as? SitesState.Ready)?.let { lotsOf(it.current.role) } ?: LotsState.Idle,
        siteSettings: Boolean = false,
        scrollTo: String? = null,
        hubSetup: HubSetupState = HubSetupState.CLOSED,
    ) {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = sites,
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    sitesActions = SitesActions.None,
                    lots = lots,
                    hubSetup = hubSetup,
                )
            }
        }
        if (siteSettings) {
            compose.onNodeWithText("Settings").performClick()
            compose.onNodeWithText("Site settings").performClick()
            compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
        }
        scrollTo?.let { compose.onNodeWithContentDescription(it).performScrollTo() }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR61 Create Site, light, font scale 2`() = snapshot("create-site-light", createSite, ThemePreference.Light)

    @Test
    fun `UX-DR61 Create Site, dark, font scale 2`() = snapshot("create-site-dark", createSite, ThemePreference.Dark)

    @Test
    fun `UX-DR62 UX-DR82 UX-DR18 Garden with no-Node Lots, light, font scale 2`() =
        snapshot("garden-light", readySites(), ThemePreference.Light)

    @Test
    fun `UX-DR62 UX-DR82 UX-DR18 Garden with no-Node Lots, dark, font scale 2`() =
        snapshot("garden-dark", readySites(), ThemePreference.Dark)

    @Test
    fun `UX-DR18 UX-DR20 the no-Node Lot tiles in one column, light, font scale 2`() =
        snapshot(
            "garden-lots-light",
            readySites(),
            ThemePreference.Light,
            scrollTo = "Beans, no Node, add a Node",
        )

    @Test
    fun `UX-DR54 UX-DR20 Garden with Lots for a Member, light, font scale 2`() =
        snapshot("garden-member-light", readySites(homeSite(SiteRole.Member)), ThemePreference.Light)

    @Test
    fun `UX-DR74 Site settings for an Owner, light, font scale 2`() =
        snapshot("site-settings-light", readySites(), ThemePreference.Light, siteSettings = true)

    @Test
    fun `UX-DR74 Site settings for an Owner, dark, font scale 2`() =
        snapshot("site-settings-dark", readySites(), ThemePreference.Dark, siteSettings = true)

    @Test
    fun `UX-DR84 Site settings for a Member, light, font scale 2`() =
        snapshot(
            "site-settings-member-light",
            readySites(homeSite(SiteRole.Member)),
            ThemePreference.Light,
            siteSettings = true,
        )

    private fun hub(
        name: String,
        state: HubSetupState,
    ) {
        snapshot("$name-light", readySites(), ThemePreference.Light, hubSetup = state)
    }

    private fun hubDark(
        name: String,
        state: HubSetupState,
    ) {
        snapshot("$name-dark", readySites(), ThemePreference.Dark, hubSetup = state)
    }

    @Test
    fun `UX-DR39 UX-DR37 Add a Hub step 1, light, font scale 2`() = hub("add-hub-scan", HubStates.scan)

    @Test
    fun `UX-DR39 UX-DR37 Add a Hub step 1, dark, font scale 2`() = hubDark("add-hub-scan", HubStates.scan)

    @Test
    fun `UX-DR41 Add a Hub step 2, light, font scale 2`() = hub("add-hub-code", HubStates.wrongCode)

    @Test
    fun `UX-DR41 Add a Hub step 2, dark, font scale 2`() = hubDark("add-hub-code", HubStates.code)

    @Test
    fun `UX-DR42 Add a Hub step 3, light, font scale 2`() = hub("add-hub-wifi", HubStates.wifi)

    @Test
    fun `UX-DR42 Add a Hub step 3, dark, font scale 2`() = hubDark("add-hub-wifi", HubStates.wifi)

    @Test
    fun `UX-DR66 Add a Hub step 4, light, font scale 2`() = hub("add-hub-site", HubStates.site)

    @Test
    fun `UX-DR66 Add a Hub step 4, dark, font scale 2`() = hubDark("add-hub-site", HubStates.site)

    @Test
    fun `UX-DR40 Add a Hub step 5, light, font scale 2`() = hub("add-hub-progress", HubStates.progress)

    @Test
    fun `UX-DR40 Add a Hub step 5, dark, font scale 2`() = hubDark("add-hub-progress", HubStates.progress)

    @Test
    fun `UX-DR55 Hub is online, light, font scale 2`() = hub("add-hub-online", HubStates.online)

    @Test
    fun `UX-DR55 Hub is online, dark, font scale 2`() = hubDark("add-hub-online", HubStates.online)

    @Test
    fun `UX-DR55 UX-DR95 wrong Wi-Fi password, light, font scale 2`() =
        hub("add-hub-wrong-password", HubStates.wrongPassword)

    @Test
    fun `UX-DR55 UX-DR95 wrong Wi-Fi password, dark, font scale 2`() =
        hubDark("add-hub-wrong-password", HubStates.wrongPassword)
}
