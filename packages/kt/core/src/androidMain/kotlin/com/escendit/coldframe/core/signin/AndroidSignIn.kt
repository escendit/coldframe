package com.escendit.coldframe.core.signin

import android.content.Context
import androidx.activity.ComponentActivity
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.setup.AndroidRadioState
import com.escendit.coldframe.core.setup.HubSetupEngine
import com.escendit.coldframe.core.setup.KableSetupRadio
import com.escendit.coldframe.core.setup.NodeSetupEngine
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

    private val api = SitesWiring.api(config, OkHttp.create(), engine)

    /** The Sites of the signed-in user; loads whenever the session becomes signed in. */
    public val sites: SitesEngine =
        SitesWiring.engine(
            api = api,
            signIn = engine,
            settings =
                SharedPreferencesSettings(
                    context.applicationContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE),
                ),
            scope = scope,
        )

    /** The Lots of the current Site and Site settings; reloads whenever the current Site changes. */
    public val lots: LotsEngine = SitesWiring.lots(api, sites, scope)

    /** The Devices of the current Site; the shell loads it on every entry of the Devices tab. */
    public val devices: DevicesEngine = SitesWiring.devices(api, sites, scope)

    private val radio = KableSetupRadio(AndroidRadioState(context))

    /** Add a Node over Kable, on one Site; an assigned Node reloads the Lots and the Devices. */
    public val nodeSetup: NodeSetupEngine = SitesWiring.nodeSetup(api, api, radio, sites, lots, devices, scope)

    /** Add a Hub over Kable; Bluetooth is only touched once the flow opens. Its outcome leads to Add a Node. */
    public val hubSetup: HubSetupEngine =
        SitesWiring.hubSetup(config, api, radio, sites, scope, onAddNode = { nodeSetup.open(it) })

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
