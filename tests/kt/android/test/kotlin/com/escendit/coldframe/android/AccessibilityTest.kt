package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.core.signin.Notice
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.InviteForm
import com.escendit.coldframe.core.sites.Member
import com.escendit.coldframe.core.sites.MemberEditor
import com.escendit.coldframe.core.sites.MembersState
import com.escendit.coldframe.core.sites.SiteRole
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AccessibilityTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private var state by mutableStateOf<SignInState>(SignInState.Restoring)
    private var members by mutableStateOf<MembersState>(MembersState.Hidden)
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
            sites = readySites(),
            theme = ThemePreference.Light,
            onSignIn = {},
            onSignOut = {},
            onSelectTheme = {},
            devices =
                DevicesState.Ready(
                    homeSite(),
                    listOf(
                        HubSummary("3f2a9c0d1e4b5a67", online = true, lastSeenAtEpochMs = 1_791_270_120_000L),
                        HubSummary("7c19000000000001", online = false, lastSeenAtEpochMs = null),
                    ),
                ),
            members = members,
        )

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

    /** The touch bounds of [control] after scrolling it into view, when something above it scrolls. */
    private fun inView(control: SemanticsNode): Rect {
        val node = compose.onNode(SemanticsMatcher("is node ${control.id}") { it.id == control.id }, true)
        val scrolls = generateSequence(control.parent) { it.parent }.any { SemanticsActions.ScrollBy in it.config }
        if (scrolls) node.performScrollTo()
        return node.fetchSemanticsNode().touchBoundsInRoot
    }

    private fun assertTouchTargetsAreAtLeast48dp(screen: String) {
        val minimum = with(compose.density) { 48.dp.toPx() } - 0.5f
        for (control in compose.allNodes().filter { it.isClickable }) {
            // A control below the fold has its touch bounds clipped away: it is measured once it is in view.
            val bounds =
                control.touchBoundsInRoot.takeIf { it.width >= minimum && it.height >= minimum } ?: inView(control)
            assertTrue(
                bounds.width >= minimum && bounds.height >= minimum,
                "$screen: \"${control.spokenLabel()}\" is ${bounds.width}x${bounds.height} px",
            )
        }
    }

    @Test
    fun `UX-DR96 at font scale 2 nothing on the Sign-in surface is truncated or clipped`() {
        show(SignInState.SignedOut(Notice.Certificate), fontScale = 2f)
        compose.assertNothingOverflows("Sign in with a notice")
    }

    @Test
    fun `UX-DR96 UX-DR126 at font scale 2 the working label and Try again grow instead of clipping`() {
        show(SignInState.SignedOut(Notice.Unreachable), fontScale = 2f)
        compose.assertNothingOverflows("Sign in, unreachable")
        show(SignInState.Working)
        compose.onNodeWithText("SIGNING IN…").assertExists()
        compose.assertNothingOverflows("Sign in, working")
    }

    @Test
    fun `UX-DR96 at font scale 2 nothing in the shell or Settings is truncated or clipped`() {
        show(SignInState.SignedIn(null), fontScale = 2f)
        compose.assertNothingOverflows("Garden")
        compose.onNodeWithText("Settings").performClick()
        compose.assertNothingOverflows("Settings")
    }

    @Test
    fun `UX-DR96 UX-DR126 at font scale 2 the theme switcher wraps instead of clipping`() {
        show(SignInState.SignedIn(null), fontScale = 2f)
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()
        compose.assertNothingOverflows("Appearance")
    }

    @Test
    fun `UX-DR96 UX-DR98 UX-DR100 at font scale 2 Devices clips nothing and Add a Hub is a labelled 48 dp button`() {
        show(SignInState.SignedIn(null), fontScale = 2f)
        compose.onNodeWithText("Devices").performClick()

        compose.assertNothingOverflows("Devices")
        assertControlsAreLabelledWithRole("Devices")
        assertTouchTargetsAreAtLeast48dp("Devices")
        val addHub = compose.allNodes().single { it.isClickable && it.spokenLabel() == "ADD A HUB" }
        assertEquals(Role.Button, addHub.role)
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

    @Test
    fun `UX-DR51 UX-DR84 UX-DR100 Member tiles and the editor are labelled 48 dp buttons, the last Owner's is none`() {
        val simon = Member("u-simon", "Simon Novak", null, SiteRole.Owner, you = true, lastOwner = true)
        val ana = Member("u-ana", "Ana Costa", null, SiteRole.Member, you = false)
        members =
            MembersState.Ready(
                "Home garden",
                listOf(simon, ana),
                InviteForm(),
                editor = MemberEditor(ana, SiteRole.Member),
            )
        show(SignInState.SignedIn(null))
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Members").performClick()

        assertTouchTargetsAreAtLeast48dp("Members")
        // The email field is a text field, not a button; every other control is a labelled button or segment.
        val buttons = compose.allNodes().filter { it.isClickable && SemanticsProperties.EditableText !in it.config }
        for (button in buttons) {
            assertNotNull(button.role, "Members: a control without a role: ${button.spokenLabel()}")
            assertTrue(button.spokenLabel().isNotBlank(), "Members: a control without a label")
        }
        val clickable = buttons.map { it.spokenLabel() }
        assertTrue(clickable.any { it.contains("Ana Costa, Member · change") }, clickable.toString())
        assertTrue(clickable.none { it.contains("Simon Novak") }, clickable.toString())
    }
}
