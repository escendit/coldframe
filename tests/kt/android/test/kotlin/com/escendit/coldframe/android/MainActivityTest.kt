package com.escendit.coldframe.android

import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/** The real activity over the real core: an empty store in an unconfigured build. */
@RunWith(AndroidJUnit4::class)
class MainActivityTest {
    @get:Rule
    val compose = createAndroidComposeRule<MainActivity>()

    @Test
    fun `UX-DR60 a cold start with an empty store resumes the session and lands on SIGN IN`() {
        compose.waitUntil(timeoutMillis = 10_000) {
            compose.onAllNodes(hasText("SIGN IN")).fetchSemanticsNodes().isNotEmpty()
        }
        compose.onNodeWithText("SIGN IN").assertExists()
    }
}
