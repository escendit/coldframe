package com.escendit.coldframe.core.signin

import android.content.Context
import androidx.activity.ComponentActivity
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesWiring
import com.russhwolf.settings.SharedPreferencesSettings
import io.ktor.client.engine.okhttp.OkHttp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.publicvalue.multiplatform.oidc.appsupport.AndroidCodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.tokenstore.AndroidSettingsTokenStore

/**
 * The Android side of the core: Custom Tabs for the browser flow (never a WebView by choice),
 * `AndroidSettingsTokenStore` for the tokens. Create one per process, in the Application.
 */
public class AndroidSignIn private constructor(
    context: Context,
    config: CoreConfig,
    scope: CoroutineScope,
) {
    private val authFlowFactory = AndroidCodeAuthFlowFactory(useWebView = false, ephemeralSession = false)

    /** The session engine the Compose shell observes. */
    public val engine: SignInEngine =
        SignInClients.engine(
            config = config,
            httpEngine = OkHttp.create(),
            store = AndroidSettingsTokenStore(context.applicationContext),
            authFlowFactory = { authFlowFactory },
            scope = scope,
        )

    /** The Sites of the signed-in user; loads whenever the session becomes signed in. */
    public val sites: SitesEngine =
        SitesWiring.engine(
            config = config,
            httpEngine = OkHttp.create(),
            signIn = engine,
            settings =
                SharedPreferencesSettings(
                    context.applicationContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE),
                ),
            scope = scope,
        )

    /** Call in every `onCreate` of the activity that starts sign-in, before it is started. */
    public fun registerActivity(activity: ComponentActivity) {
        authFlowFactory.registerActivity(activity)
    }

    public companion object {
        /** Per-device choices: the current Site and the chosen time zone (not the token store). */
        private const val PREFERENCES = "com.escendit.coldframe.sites"

        public fun create(
            context: Context,
            config: CoreConfig,
            scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
        ): AndroidSignIn = AndroidSignIn(context, config, scope)
    }
}
