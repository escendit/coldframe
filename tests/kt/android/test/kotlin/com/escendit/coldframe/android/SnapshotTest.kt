package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onRoot
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.core.appearance.ThemePreference
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
 * Roborazzi snapshots of Create Site and the empty Garden, light and dark, at the largest font
 * scale (2×). Baselines live in tests/kt/android/snapshots; `check` compares against them and
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

    private fun snapshot(
        name: String,
        sites: SitesState,
        theme: ThemePreference,
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
                )
            }
        }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR61 Create Site, light, font scale 2`() = snapshot("create-site-light", createSite, ThemePreference.Light)

    @Test
    fun `UX-DR61 Create Site, dark, font scale 2`() = snapshot("create-site-dark", createSite, ThemePreference.Dark)

    @Test
    fun `UX-DR62 UX-DR82 empty Garden, light, font scale 2`() =
        snapshot("garden-light", readySites(), ThemePreference.Light)

    @Test
    fun `UX-DR62 UX-DR82 empty Garden, dark, font scale 2`() =
        snapshot("garden-dark", readySites(), ThemePreference.Dark)

    @Test
    fun `UX-DR54 empty Garden for a Member, light, font scale 2`() =
        snapshot("garden-member-light", readySites(homeSite(SiteRole.Member)), ThemePreference.Light)
}
