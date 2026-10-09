package com.escendit.coldframe.android

import android.app.Application
import com.escendit.coldframe.BuildConfig
import com.escendit.coldframe.android.push.FirebaseConfig
import com.escendit.coldframe.android.push.FirebasePush
import com.escendit.coldframe.core.appearance.AndroidAppearance
import com.escendit.coldframe.core.appearance.AppearanceStore
import com.escendit.coldframe.core.signin.AndroidSignIn
import com.escendit.coldframe.core.signin.CoreConfig

/**
 * Holds the one sign-in engine (with its Sites engine, `signIn.sites`) and theme store of the
 * process, so they outlive activities. A build with Firebase properties starts Firebase Cloud Messaging and hands
 * the device's registration to the core's push engine; a build without them starts none and registers nothing.
 */
class ColdframeApplication : Application() {
    lateinit var signIn: AndroidSignIn
        private set
    lateinit var appearance: AppearanceStore
        private set

    /** Whether this build has a Firebase project, and so receives push notifications. */
    var pushStarted: Boolean = false
        private set

    override fun onCreate() {
        super.onCreate()
        val config = CoreConfig.of(BuildConfig.SERVER_URL, BuildConfig.KEYCLOAK_ISSUER, BuildConfig.KEYCLOAK_CLIENT_ID)
        signIn = AndroidSignIn.create(this, config)
        appearance = AndroidAppearance.create(this)
        pushStarted = FirebasePush.start(this, FirebaseConfig.fromBuild()) { signIn.push.tokenReceived(it) }
    }
}
