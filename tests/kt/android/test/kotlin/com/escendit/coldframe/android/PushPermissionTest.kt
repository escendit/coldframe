package com.escendit.coldframe.android

import android.Manifest
import android.app.Application
import android.app.NotificationManager
import android.provider.Settings
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.BuildConfig
import com.escendit.coldframe.android.push.FirebaseConfig
import com.escendit.coldframe.android.push.FirebasePush
import com.escendit.coldframe.android.push.notificationPermission
import com.escendit.coldframe.android.push.openNotificationSettings
import com.escendit.coldframe.core.push.OsPermission
import com.google.firebase.FirebaseApp
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Story 6.5: what the Android shell reads from the OS for the core, and Firebase without credentials. */
@RunWith(AndroidJUnit4::class)
class PushPermissionTest {
    private val context: Application = ApplicationProvider.getApplicationContext()
    private val notifications = shadowOf(context.getSystemService(NotificationManager::class.java))

    @Test
    fun `UX-DR122 from Android 13 a permission that is not held is not determined, the core knows if it was asked`() {
        assertEquals(OsPermission.NotDetermined, context.notificationPermission())
    }

    @Test
    fun `UX-DR122 from Android 13 a held permission is granted`() {
        shadowOf(context).grantPermissions(Manifest.permission.POST_NOTIFICATIONS)

        assertEquals(OsPermission.Granted, context.notificationPermission())
    }

    @Test
    fun `UX-DR88 notifications turned off in the system settings are denied`() {
        shadowOf(context).grantPermissions(Manifest.permission.POST_NOTIFICATIONS)
        notifications.setNotificationsEnabled(false)

        assertEquals(OsPermission.Denied, context.notificationPermission())
    }

    @Test
    @Config(sdk = [32])
    fun `UX-DR122 Android below 13 has no prompt, notifications count as granted`() {
        assertEquals(OsPermission.Granted, context.notificationPermission())
    }

    @Test
    @Config(sdk = [32])
    fun `UX-DR88 Android below 13 with notifications turned off is denied`() {
        notifications.setNotificationsEnabled(false)

        assertEquals(OsPermission.Denied, context.notificationPermission())
    }

    @Test
    fun `UX-DR88 Open Settings opens this app's notification settings`() {
        context.openNotificationSettings()

        val intent = shadowOf(context).nextStartedActivity
        assertEquals(Settings.ACTION_APP_NOTIFICATION_SETTINGS, intent.action)
        assertEquals(context.packageName, intent.getStringExtra(Settings.EXTRA_APP_PACKAGE))
    }

    @Test
    fun `UX-DR115 a build without Firebase properties has no Firebase configuration`() {
        assertEquals("", BuildConfig.FIREBASE_PROJECT_ID)
        assertEquals("", BuildConfig.FIREBASE_APPLICATION_ID)
        assertEquals("", BuildConfig.FIREBASE_API_KEY)
        assertEquals("", BuildConfig.FIREBASE_SENDER_ID)
        assertNull(FirebaseConfig.fromBuild())
        assertNull(FirebaseConfig.of("project", "1:1:android:1", "", "1"))
        assertNull(FirebaseConfig.of("project", "1:1:android:1", "key", null))
        assertNull(FirebaseConfig.of(" ", "1:1:android:1", "key", "1"))
    }

    @Test
    fun `UX-DR115 an app built without Firebase properties starts no Firebase and registers nothing`() {
        var registrations = 0

        val started = FirebasePush.start(context, FirebaseConfig.fromBuild()) { registrations++ }

        assertFalse(started)
        assertFalse((context as ColdframeApplication).pushStarted)
        assertTrue(FirebaseApp.getApps(context).isEmpty())
        assertEquals(0, registrations)
    }

    @Test
    fun `UX-DR115 the four build properties are the options a google-services file would give`() {
        val config = FirebaseConfig.of(" coldframe-demo ", "1:1234:android:abcd", "key", "1234")

        val options = config!!.options()

        assertEquals("coldframe-demo", options.projectId)
        assertEquals("1:1234:android:abcd", options.applicationId)
        assertEquals("key", options.apiKey)
        assertEquals("1234", options.gcmSenderId)
    }

    @Test
    fun `UX-DR115 no Firebase credential or google-services file is in the repository`() {
        val found =
            Repo
                .file("apps/kt")
                .walkTopDown()
                .onEnter { it.name != "build" }
                .filter { it.isFile && it.name == "google-services.json" }
                .toList()
        assertEquals(emptyList(), found)
        val build = Repo.file("apps/kt/android/build.gradle.kts").readText() + Repo.file("build.gradle.kts").readText()
        assertFalse(build.contains("google-services") && build.contains("com.google.gms"))
        assertFalse(Repo.file("gradle/libs.versions.toml").readText().contains("com.google.gms"))
    }
}
