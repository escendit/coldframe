package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.components.hatched
import com.escendit.coldframe.android.ui.components.plate
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.designtokens.ColorTokens
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ComponentsTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `UX-DR35 the text input has its label above, helper below, and an error that replaces the helper`() {
        var error by mutableStateOf<String?>(null)
        compose.setContent {
            ColdframeTheme(isDark = false) {
                var value by remember { mutableStateOf("") }
                TextInput(label = "Site name", value = value, onValueChange = {
                    value = it
                }, helper = "Shown to Members", error = error)
            }
        }

        compose.onNode(hasSetTextAction()).performTextInput("Home")
        compose.onNodeWithText("Shown to Members").assertExists()
        error = "A Site needs a name."
        compose.waitForIdle()

        compose.onNodeWithText("A Site needs a name.").assertExists()
        compose.onNodeWithText("Shown to Members").assertDoesNotExist()
        val field = compose.onNode(hasSetTextAction()).fetchSemanticsNode()
        assertEquals("A Site needs a name.", field.config.getOrNull(SemanticsProperties.Error))
    }

    @Test
    fun `UX-DR35 a password field reveals and hides its value with a labelled toggle`() {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                var value by remember { mutableStateOf("") }
                TextInput(label = "Wi-Fi password", value = value, onValueChange = { value = it }, password = true)
            }
        }

        compose.onNodeWithContentDescription("Show password").performClick()
        compose.onNodeWithContentDescription("Hide password").assertExists()
    }

    @Test
    fun `UX-DR36 the segmented choice selects one segment, exposes it as selected and applies at once`() {
        val chosen = mutableListOf<String>()
        compose.setContent {
            ColdframeTheme(isDark = false) {
                var selected by remember { mutableStateOf("daily") }
                Column {
                    SegmentedChoice(
                        label = "Reminder",
                        segments = listOf(Segment("daily", "Daily"), Segment("two", "Every 2 days")),
                        selected = selected,
                        onSelect = {
                            selected = it
                            chosen += it
                        },
                    )
                }
            }
        }

        compose.onNodeWithText("DAILY").assertIsSelected()
        compose.onNodeWithText("EVERY 2 DAYS").assertIsNotSelected().performClick()
        compose.onNodeWithText("EVERY 2 DAYS").assertIsSelected()
        compose.onNodeWithText("DAILY").assertIsNotSelected().performClick()
        compose.onNodeWithText("DAILY").performClick()
        assertEquals(listOf("two", "daily"), chosen)
    }

    @Test
    fun `UX-DR12 the hatch draws 135 degree lines every 8 dp on its ground, and a plate keeps them off the text`() {
        val ground = Color(ColorTokens.statusHatchGround.light.toInt())
        val line = Color(ColorTokens.statusHatchLine.light.toInt())
        compose.setContent {
            ColdframeTheme(isDark = false) {
                Box(Modifier.size(96.dp).hatched(ground, line).testTag("hatch"), contentAlignment = Alignment.Center) {
                    Box(
                        Modifier.testTag("plate").plate(on = true, ground = ground),
                    ) { Box(Modifier.size(40.dp, 24.dp)) }
                }
            }
        }

        val hatch = compose.onNodeWithTag("hatch").captureToImage().toPixelMap()

        // One row above the plate: ground between the lines, and a line every 8 px (mdpi).
        fun lineStarts(y: Int): List<Int> =
            (0 until hatch.width - 1).filter { hatch[it, y] == ground && hatch[it + 1, y] != ground }
        val starts = lineStarts(8)
        assertTrue(starts.size in 11..13, "${starts.size} lines in 96 dp")
        assertEquals(setOf(8), starts.zipWithNext { left, right -> right - left }.toSet())
        // 135°: a line moves one pixel to the left for every pixel down.
        assertEquals(starts.filter { it > 0 }.map { it - 1 }, lineStarts(9).filter { it < starts.last() })
        // A line is the line token, blended with the ground at its edges.
        val row = (0 until hatch.width).map { hatch[it, 8] }
        assertTrue(row.any { it != ground && it.red <= ground.red && it.red >= line.red })

        // The plate is solid ground: no line crosses it. It is the content plus 2 dp on each side.
        val plate = compose.onNodeWithTag("plate").captureToImage().toPixelMap()
        assertEquals(44, plate.width)
        for (x in 0 until plate.width) for (y in 0 until plate.height) assertEquals(ground, plate[x, y])
    }

    @Test
    fun `UX-DR12 a plate that is off leaves its content as it is`() {
        val ground = Color(ColorTokens.statusHatchGround.light.toInt())
        compose.setContent {
            ColdframeTheme(isDark = false) {
                Box(Modifier.testTag("plate").plate(on = false, ground = ground)) { Box(Modifier.size(40.dp, 24.dp)) }
            }
        }

        val plate = compose.onNodeWithTag("plate").captureToImage().toPixelMap()
        assertEquals(40, plate.width)
        assertFalse(ground == plate[2, 2])
    }

    @Test
    fun `UX-DR12 the Wi-Fi row and the Lot tiles share the one hatch primitive`() {
        val sources = Repo.kotlinSources("apps/kt/android/src/main/kotlin")
        val definitions = sources.filter { it.readText().contains("fun Modifier.hatched(") }
        assertEquals(listOf("Hatch.kt"), definitions.map { it.name })
        for (user in listOf("WifiNetworkRow.kt", "LotTile.kt")) {
            val text = sources.single { it.name == user }.readText()
            assertTrue(text.contains("import com.escendit.coldframe.android.ui.components.hatched"), user)
            assertTrue(text.contains("import com.escendit.coldframe.android.ui.components.plate"), user)
            assertFalse(text.contains("drawLine("), user)
        }
    }
}
