package com.escendit.coldframe.android

import android.app.Application
import com.escendit.coldframe.BuildConfig
import com.escendit.coldframe.core.appearance.AndroidAppearance
import com.escendit.coldframe.core.appearance.AppearanceStore
import com.escendit.coldframe.core.signin.AndroidSignIn
import com.escendit.coldframe.core.signin.CoreConfig

/** Holds the one sign-in engine and theme store of the process, so they outlive activities. */
class ColdframeApplication : Application() {
    lateinit var signIn: AndroidSignIn
        private set
    lateinit var appearance: AppearanceStore
        private set

    override fun onCreate() {
        super.onCreate()
        val config = CoreConfig.of(BuildConfig.SERVER_URL, BuildConfig.KEYCLOAK_ISSUER, BuildConfig.KEYCLOAK_CLIENT_ID)
        signIn = AndroidSignIn.create(this, config)
        appearance = AndroidAppearance.create(this)
    }
}
