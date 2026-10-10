package com.escendit.coldframe.android

import android.Manifest
import android.app.Application
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.core.push.PushPermission
import org.junit.Rule
import org.junit.Test
import org.junit.rules.ExternalResource
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf

/** The real activity over the real core: an empty store in an unconfigured build. */
@RunWith(AndroidJUnit4::class)
class MainActivityTest {
    /** Before the activity starts: the User allowed notifications earlier. */
    @get:Rule(order = 0)
    val granted =
        object : ExternalResource() {
            override fun before() {
                shadowOf(ApplicationProvider.getApplicationContext<Application>())
                    .grantPermissions(Manifest.permission.POST_NOTIFICATIONS)
            }
        }

    @get:Rule(order = 1)
    val compose = createAndroidComposeRule<MainActivity>()

    @Test
    fun `UX-DR60 a cold start with an empty store resumes the session and lands on SIGN IN`() {
        compose.waitUntil(timeoutMillis = 10_000) {
            compose.onAllNodes(hasText("SIGN IN")).fetchSemanticsNodes().isNotEmpty()
        }
        compose.onNodeWithText("SIGN IN").assertExists()
    }

    @Test
    fun `UX-DR88 the activity tells the core what Android says about notifications when it comes to the front`() {
        val push = (compose.activity.application as ColdframeApplication).signIn.push

        compose.waitUntil(timeoutMillis = 10_000) { push.state.value.permission == PushPermission.Granted }
    }
}
