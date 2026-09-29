package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
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
}
