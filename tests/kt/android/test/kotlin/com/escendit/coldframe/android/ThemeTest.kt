package com.escendit.coldframe.android

import android.content.Context
import androidx.activity.ComponentActivity
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.core.appearance.AndroidAppearance
import com.escendit.coldframe.core.appearance.AppearanceStore
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.designtokens.ColorTokens
import com.russhwolf.settings.MapSettings
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ThemeTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val lightBackground = Color(ColorTokens.background.light.toInt())
    private val darkBackground = Color(ColorTokens.background.dark.toInt())

    /** The colour right under the top bar, where only the screen background shows. */
    private fun backgroundColour(): Color {
        val pixels = compose.onRoot().captureToImage().toPixelMap()
        return pixels[pixels.width / 2, pixels.height / 3]
    }

    private fun showShell(
        store: AppearanceStore,
        systemIsDark: Boolean = false,
    ) {
        compose.setContent {
            val theme by store.theme.collectAsState()
            ColdframeRoot(
                state = SignInState.SignedIn(null),
                theme = theme,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = store::select,
                systemIsDark = systemIsDark,
            )
        }
    }

    private fun openAppearance() {
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()
    }

    @Test
    fun `UX-DR15 UX-DR53 choosing Dark recolours from the dark tokens at once, with no Save`() {
        val store = AppearanceStore(MapSettings())
        showShell(store)
        openAppearance()
        assertEquals(lightBackground, backgroundColour())

        compose.onNodeWithText("DARK").performClick()

        compose.onNodeWithText("DARK").assertIsSelected()
        assertEquals(ThemePreference.Dark, store.theme.value)
        assertEquals(darkBackground, backgroundColour())
    }

    @Test
    fun `UX-DR15 System follows the OS appearance`() {
        showShell(AppearanceStore(MapSettings()), systemIsDark = true)

        assertEquals(darkBackground, backgroundColour())
    }

    @Test
    fun `UX-DR15 Light overrides a dark OS`() {
        val store = AppearanceStore(MapSettings()).apply { select(ThemePreference.Light) }
        showShell(store, systemIsDark = true)

        assertEquals(lightBackground, backgroundColour())
    }

    @Test
    fun `UX-DR15 the choice survives activity recreation and a new store over the same preferences`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        AndroidAppearance.create(context).select(ThemePreference.Dark)
        showShell(AndroidAppearance.create(context))

        compose.activityRule.scenario.recreate()
        showShell(AndroidAppearance.create(context))

        assertEquals(darkBackground, backgroundColour())
        assertEquals(ThemePreference.Dark, AndroidAppearance.create(context).theme.value)
        AndroidAppearance.create(context).select(ThemePreference.System)
    }
}
