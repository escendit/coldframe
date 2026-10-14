package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.TimeZoneProposal
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/** Create Site while an accepted invitation is on its way (Story 9.1, UX-DR90). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class CreateSiteScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val hint = "Accepted an invitation? It can take a minute to appear."

    private fun form(cancellable: Boolean) =
        CreateSiteForm("", null, false, null, "key-1", TimeZoneProposal("Europe/Zurich", null, false), cancellable)

    private fun show(sites: SitesState) {
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = sites,
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                sitesActions = SitesActions.None,
            )
        }
    }

    @Test
    fun `UX-DR90 without a Membership Create Site says an accepted invitation can take a minute`() {
        show(SitesState.NeedsSite(form(cancellable = false)))

        compose.onNode(isHeading().and(hasText("Create Site"))).assertExists()
        compose.onNodeWithText(hint).assertExists()
    }

    @Test
    fun `UX-DR90 New Site from the switcher does not say it`() {
        show(readySites().copy(creating = form(cancellable = true)))

        compose.onNode(isHeading().and(hasText("Create Site"))).assertExists()
        compose.onAllNodesWithText(hint).assertCountEquals(0)
    }
}
