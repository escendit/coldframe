package com.escendit.coldframe.core.signin

import android.content.Context
import androidx.activity.ComponentActivity
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

    /** Call in every `onCreate` of the activity that starts sign-in, before it is started. */
    public fun registerActivity(activity: ComponentActivity) {
        authFlowFactory.registerActivity(activity)
    }

    public companion object {
        public fun create(
            context: Context,
            config: CoreConfig,
            scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
        ): AndroidSignIn = AndroidSignIn(context, config, scope)
    }
}
