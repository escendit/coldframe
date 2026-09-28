package com.escendit.coldframe.android

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.BuildConfig
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.CoreConfig
import com.escendit.coldframe.core.signin.Notice
import com.escendit.coldframe.core.signin.SignInState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class SignInScreenTest {
    @get:Rule
    val compose = createComposeRule()

    private var signIns = 0
    private var state by mutableStateOf<SignInState>(SignInState.SignedOut(null))

    private fun show(initial: SignInState) {
        state = initial
        compose.setContent {
            ColdframeRoot(
                state = state,
                theme = ThemePreference.Light,
                onSignIn = { signIns++ },
                onSignOut = {},
                onSelectTheme = {},
            )
        }
    }

    @Test
    fun `UX-DR59 the Sign-in surface has exactly one button, SIGN IN, and no text field`() {
        show(SignInState.SignedOut(null))

        compose.onAllNodes(hasClickAction()).assertCountEquals(1)
        compose.onAllNodes(hasSetTextAction()).assertCountEquals(0)
        compose.onNodeWithText("SIGN IN").assertIsEnabled()
        compose.onNodeWithText("Coldframe").assertExists()
    }

    @Test
    fun `UX-DR59 an unconfigured build falls back to the never-resolvable defaults`() {
        val config = CoreConfig.of(BuildConfig.SERVER_URL, BuildConfig.KEYCLOAK_ISSUER, BuildConfig.KEYCLOAK_CLIENT_ID)

        assertEquals(BuildConfig.SERVER_URL.ifBlank { CoreConfig.DEFAULT_SERVER_URL }, config.serverUrl)
        assertEquals(BuildConfig.KEYCLOAK_ISSUER.ifBlank { CoreConfig.DEFAULT_KEYCLOAK_ISSUER }, config.keycloakIssuer)
        assertEquals(BuildConfig.KEYCLOAK_CLIENT_ID.ifBlank { "coldframe-mobile" }, config.clientId)
    }

    @Test
    fun `UX-DR60 SIGN IN hands off to the core`() {
        show(SignInState.SignedOut(null))

        compose.onNodeWithText("SIGN IN").performClick()

        assertEquals(1, signIns)
    }

    @Test
    fun `UX-DR34 while working the button reads Signing in in place and ignores presses`() {
        show(SignInState.SignedOut(null))
        state = SignInState.Working
        compose.waitForIdle()

        compose.onNodeWithText("SIGNING IN…").assertIsNotEnabled()
        compose.onNodeWithText("SIGNING IN…").performClick()
        compose.onAllNodesWithText("SIGN IN").assertCountEquals(0)
        assertEquals(0, signIns)
    }

    @Test
    fun `UX-DR92 UX-DR56 the unreachable notice offers Try again, which signs in`() {
        show(SignInState.SignedOut(Notice.Unreachable))

        compose
            .onNodeWithText(
                "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.",
            ).assertExists()
        compose.onNodeWithText("TRY AGAIN").performClick()
        assertEquals(1, signIns)
        compose.onAllNodes(hasClickAction()).assertCountEquals(2)
    }

    @Test
    fun `UX-DR92 the certificate notice has no action and no way around it`() {
        show(SignInState.SignedOut(Notice.Certificate))

        compose
            .onNodeWithText(
                "Your Server's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain.",
            ).assertExists()
        compose.onAllNodes(hasClickAction()).assertCountEquals(1)
        compose.onAllNodes(hasText("continue", substring = true, ignoreCase = true)).assertCountEquals(0)
    }

    @Test
    fun `UX-DR92 the Keycloak notice offers Try again`() {
        show(SignInState.SignedOut(Notice.Keycloak))

        compose
            .onNodeWithText(
                "Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed.",
            ).assertExists()
        compose.onNodeWithText("TRY AGAIN").assertExists()
    }

    @Test
    fun `UX-DR92 cancelling shows the Sign-in surface without a notice`() {
        show(SignInState.Working)
        state = SignInState.SignedOut(null)
        compose.waitForIdle()

        compose.onAllNodes(hasClickAction()).assertCountEquals(1)
        compose.onNodeWithText("SIGN IN").assertIsEnabled()
    }

    @Test
    fun `UX-DR93 the signed-out notice offers Sign in`() {
        show(SignInState.SignedOut(Notice.SignedOut))

        compose.onNodeWithText("You're signed out. Sign in again to see live data.").assertExists()
        compose.onAllNodesWithText("SIGN IN").assertCountEquals(2)
        compose.onAllNodesWithText("SIGN IN")[0].performClick()
        assertEquals(1, signIns)
    }

    @Test
    fun `UX-DR104 failures are announced assertively and the signed-out notice politely`() {
        show(SignInState.SignedOut(Notice.Keycloak))
        val keycloak = compose.onNodeWithText("Sign-in didn't finish", substring = true).fetchSemanticsNode()
        assertEquals(LiveRegionMode.Assertive, keycloak.config.getOrNull(SemanticsProperties.LiveRegion))

        state = SignInState.SignedOut(Notice.SignedOut)
        compose.waitForIdle()
        val signedOut = compose.onNodeWithText("You're signed out", substring = true).fetchSemanticsNode()
        assertEquals(LiveRegionMode.Polite, signedOut.config.getOrNull(SemanticsProperties.LiveRegion))
    }

    @Test
    fun `restoring shows only the background, never the Sign-in surface`() {
        show(SignInState.Restoring)

        compose.onAllNodes(hasClickAction()).assertCountEquals(0)
        compose.onAllNodesWithText("SIGN IN").assertCountEquals(0)
    }
}
