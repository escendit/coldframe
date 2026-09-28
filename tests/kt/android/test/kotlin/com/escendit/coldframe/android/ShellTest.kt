package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.isDialog
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.core.appearance.AppearanceStore
import com.escendit.coldframe.core.signin.SignInState
import com.russhwolf.settings.MapSettings
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class ShellTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private var signOuts = 0
    private val appearance = AppearanceStore(MapSettings())

    private fun show() {
        compose.setContent {
            val theme by appearance.theme.collectAsState()
            ColdframeRoot(
                state = SignInState.SignedIn("Simon Novak"),
                sites = readySites(),
                theme = theme,
                onSignIn = {},
                onSignOut = { signOuts++ },
                onSelectTheme = appearance::select,
            )
        }
    }

    private fun tab(label: String) =
        compose.onNode(
            SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Tab).and(hasLabel(label)),
            useUnmergedTree = false,
        )

    private fun hasLabel(label: String) =
        androidx.compose.ui.test
            .hasText(label)

    @Test
    fun `UX-DR57 UX-DR110 the NavigationBar has Garden, Alerts, Devices, Settings with Garden selected`() {
        show()

        val tabs = compose.onAllNodes(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Tab))
        tabs.assertCountEquals(4)
        val labels = (0 until 4).map { tabs[it].fetchSemanticsNode().spokenLabel() }
        assertEquals(listOf("Garden", "Alerts", "Devices", "Settings"), labels)
        tab("Garden").assertIsSelected()
        tab("Alerts").assertIsNotSelected()
        compose.onNode(isHeading().and(hasLabel("Garden"))).assertExists()
    }

    @Test
    fun `UX-DR57 choosing a tab selects it and shows its heading only`() {
        show()

        tab("Alerts").performClick()

        tab("Alerts").assertIsSelected()
        tab("Garden").assertIsNotSelected()
        compose.onNode(isHeading().and(hasLabel("Alerts"))).assertExists()
    }

    @Test
    fun `UX-DR71 Settings lists Appearance, then Account with Sign out`() {
        show()
        tab("Settings").performClick()

        compose.onNodeWithText("Appearance").assertExists()
        compose.onNodeWithText("Theme for this phone").assertExists()
        compose.onNode(isHeading().and(hasLabel("Account"))).assertExists()
        compose.onNodeWithText("SIGN OUT").assertExists()
        val appearanceTop =
            compose
                .onNodeWithText("Appearance")
                .fetchSemanticsNode()
                .boundsInRoot.top
        val accountTop =
            compose
                .onNodeWithText("Account")
                .fetchSemanticsNode()
                .boundsInRoot.top
        assert(appearanceTop < accountTop)
    }

    @Test
    fun `UX-DR113 UX-DR76 sign out confirms in one native dialog that names the result, and Cancel changes nothing`() {
        show()
        tab("Settings").performClick()

        compose.onNodeWithText("SIGN OUT").performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(1)
        compose.onNodeWithText("Sign out of Coldframe on this phone?").assertExists()
        compose.onNodeWithText("CANCEL").performClick()
        compose.onAllNodes(isDialog()).assertCountEquals(0)
        assertEquals(0, signOuts)

        compose.onNodeWithText("SIGN OUT").performClick()
        compose.onAllNodesWithText("SIGN OUT")[1].performClick()
        assertEquals(1, signOuts)
        compose.onAllNodes(isDialog()).assertCountEquals(0)
    }

    @Test
    fun `UX-DR75 UX-DR113 Appearance hosts the theme switcher and system back returns to Settings`() {
        show()
        tab("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()

        compose.onNode(isHeading().and(hasLabel("Appearance"))).assertExists()
        compose.onNodeWithText("SYSTEM").assertIsSelected()
        compose.activityRule.scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        compose.waitForIdle()

        compose.onNodeWithText("Theme for this phone").assertExists()
    }

    @Test
    fun `UX-DR75 the back button in the top bar returns to Settings`() {
        show()
        tab("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()

        compose.onNodeWithContentDescription("Back").performClick()

        compose.onNode(isHeading().and(hasLabel("Settings"))).assertExists()
    }
}
