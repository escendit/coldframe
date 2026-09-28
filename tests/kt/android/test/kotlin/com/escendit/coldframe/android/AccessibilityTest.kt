package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.Notice
import com.escendit.coldframe.core.signin.SignInState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertNotNull
import kotlin.test.assertTrue
import kotlin.test.fail

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AccessibilityTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private var state by mutableStateOf<SignInState>(SignInState.Restoring)
    private var started = false

    /** Shows [next]; the content is set once per test and then only its state changes. */
    private fun show(
        next: SignInState,
        fontScale: Float = 1f,
    ) {
        state = next
        if (!started) {
            started = true
            compose.setContent { AtFontScale(fontScale) { Root(state) } }
        }
        compose.waitForIdle()
    }

    @Composable
    private fun Root(state: SignInState) =
        ColdframeRoot(
            state = state,
            theme = ThemePreference.Light,
            onSignIn = {},
            onSignOut = {},
            onSelectTheme = {},
        )

    private fun assertNothingOverflows(screen: String) {
        compose.waitForIdle()
        val texts = compose.allNodes().flatMap { node -> node.textLayouts().map { node to it } }
        assertTrue(texts.isNotEmpty(), "$screen has text")
        for ((node, layout) in texts) {
            val text = layout.layoutInput.text.text
            // Clipped: cut off at the bottom, more lines than allowed, an ellipsis, or a line wider
            // than the node that shows it. (didOverflowWidth compares against the layout width.)
            if (layout.didOverflowHeight ||
                layout.multiParagraph.didExceedMaxLines
            ) {
                fail("$screen: \"$text\" is cut off")
            }
            for (line in 0 until layout.lineCount) {
                if (layout.isLineEllipsized(line)) fail("$screen: \"$text\" is truncated")
                val right = layout.getLineRight(line)
                if (right >
                    layout.size.width + 1f
                ) {
                    fail("$screen: \"$text\" is wider than its node ($right > ${layout.size.width})")
                }
            }
            assertTrue(node.size.height > 0, "$screen: \"$text\" is laid out")
        }
    }

    private fun assertControlsAreLabelledWithRole(screen: String) {
        val controls = compose.allNodes().filter { it.isClickable }
        assertTrue(controls.isNotEmpty(), "$screen has controls")
        for (control in controls) {
            val role = assertNotNull(control.role, "$screen: a control without a role: ${control.spokenLabel()}")
            assertTrue(role == Role.Button || role == Role.Tab, "$screen: unexpected role $role")
            assertTrue(control.spokenLabel().isNotBlank(), "$screen: a $role without a label")
            if (role == Role.Tab) {
                assertNotNull(
                    control.config.getOrNull(SemanticsProperties.Selected),
                    "$screen: a tab without selected state",
                )
            }
        }
    }

    private fun assertTouchTargetsAreAtLeast48dp(screen: String) {
        val minimum = with(compose.density) { 48.dp.toPx() } - 0.5f
        for (control in compose.allNodes().filter { it.isClickable }) {
            val bounds = control.touchBoundsInRoot
            assertTrue(
                bounds.width >= minimum && bounds.height >= minimum,
                "$screen: \"${control.spokenLabel()}\" is ${bounds.width}x${bounds.height} px",
            )
        }
    }

    @Test
    fun `UX-DR96 at font scale 2 nothing on the Sign-in surface is truncated or clipped`() {
        show(SignInState.SignedOut(Notice.Certificate), fontScale = 2f)
        assertNothingOverflows("Sign in with a notice")
    }

    @Test
    fun `UX-DR96 UX-DR126 at font scale 2 the working label and Try again grow instead of clipping`() {
        show(SignInState.SignedOut(Notice.Unreachable), fontScale = 2f)
        assertNothingOverflows("Sign in, unreachable")
        show(SignInState.Working)
        compose.onNodeWithText("SIGNING IN…").assertExists()
        assertNothingOverflows("Sign in, working")
    }

    @Test
    fun `UX-DR96 at font scale 2 nothing in the shell or Settings is truncated or clipped`() {
        show(SignInState.SignedIn(null), fontScale = 2f)
        assertNothingOverflows("Garden")
        compose.onNodeWithText("Settings").performClick()
        assertNothingOverflows("Settings")
    }

    @Test
    fun `UX-DR96 UX-DR126 at font scale 2 the theme switcher wraps instead of clipping`() {
        show(SignInState.SignedIn(null), fontScale = 2f)
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()
        assertNothingOverflows("Appearance")
    }

    @Test
    fun `UX-DR98 every control on Sign in, the shell, Settings and Appearance has its role, label and state`() {
        show(SignInState.SignedOut(Notice.Keycloak))
        assertControlsAreLabelledWithRole("Sign in")

        show(SignInState.SignedIn(null))
        assertControlsAreLabelledWithRole("Garden")
        compose.onNodeWithText("Settings").performClick()
        assertControlsAreLabelledWithRole("Settings")
        compose.onNodeWithText("Appearance").performClick()
        assertControlsAreLabelledWithRole("Appearance")
        compose.allNodes().filter { it.isClickable && it.spokenLabel().contains("SYSTEM") }.forEach {
            assertNotNull(it.config.getOrNull(SemanticsProperties.Selected), "segments expose selected state")
        }
    }

    @Test
    fun `UX-DR100 every control is at least 48 dp square`() {
        show(SignInState.SignedOut(Notice.Unreachable))
        assertTouchTargetsAreAtLeast48dp("Sign in")

        show(SignInState.SignedIn(null))
        assertTouchTargetsAreAtLeast48dp("Garden")
        compose.onNodeWithText("Settings").performClick()
        assertTouchTargetsAreAtLeast48dp("Settings")
        compose.onNodeWithText("Appearance").performClick()
        assertTouchTargetsAreAtLeast48dp("Appearance")
    }
}
