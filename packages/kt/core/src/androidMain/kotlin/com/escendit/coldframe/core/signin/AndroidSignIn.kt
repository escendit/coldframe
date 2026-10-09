package com.escendit.coldframe.core.signin

import android.content.Context
import androidx.activity.ComponentActivity
import com.escendit.coldframe.core.alerts.AlertsEngine
import com.escendit.coldframe.core.calibrate.CalibrateEngine
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotDetailEngine
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.notifications.NotificationSettingsEngine
import com.escendit.coldframe.core.setup.AndroidRadioState
import com.escendit.coldframe.core.setup.HubSetupEngine
import com.escendit.coldframe.core.setup.KableSetupRadio
import com.escendit.coldframe.core.setup.NodeSetupEngine
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesWiring
import com.escendit.coldframe.core.thresholds.ThresholdsEngine
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

    /** Per-device choices and the last good Lots; not the token store. */
    private val settings =
        SharedPreferencesSettings(
            context.applicationContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE),
        )

    /** The Sites of the signed-in user; loads whenever the session becomes signed in. */
    public val sites: SitesEngine =
        SitesWiring.engine(
            api = api,
            signIn = engine,
            settings = settings,
            scope = scope,
        )

    /**
     * The Lots of the current Site and Site settings; reloads whenever the current Site changes.
     * The shell calls `refresh` on pull-to-refresh and whenever the overview comes to the front.
     */
    public val lots: LotsEngine = SitesWiring.lots(api, sites, settings, scope, cadence = api)

    /**
     * My notifications: the Notification Window, the time zone, and the mute and my Reminder cadence of the
     * current Site. It reads when the session starts and hands the device's zone to the Server; the shell calls
     * `load` on every entry of the surface.
     */
    public val notifications: NotificationSettingsEngine =
        SitesWiring.notifications(
            api,
            sites,
            engine,
            settings,
            scope,
        )

    /** Lot detail of the current Site: one Lot, its Sensors, Node and 30-day history; the shell opens and closes it. */
    public val lotDetail: LotDetailEngine = SitesWiring.lotDetail(api, sites, settings, scope)

    /** The Devices of the current Site; the shell loads it on every entry of the Devices tab. */
    public val devices: DevicesEngine = SitesWiring.devices(api, sites, scope)

    /** The Alerts of the current Site, read as soon as a Site is current; the shell refreshes on foreground. */
    public val alerts: AlertsEngine = SitesWiring.alerts(api, sites, scope)

    /** Calibrate of one Lot's soil Sensor: dry, then wet, from stored Readings over REST. */
    public val calibrate: CalibrateEngine = SitesWiring.calibrate(api, sites, scope)

    /** Thresholds of one Lot's Sensors; a saved change makes Lot detail and the Lots read again. */
    public val thresholds: ThresholdsEngine = SitesWiring.thresholds(api, sites, scope, lotDetail, lots)

    private val radio = KableSetupRadio(AndroidRadioState(context))

    /** Add a Node over Kable, on one Site; an assigned Node reloads the Lots and the Devices. */
    public val nodeSetup: NodeSetupEngine =
        SitesWiring.nodeSetup(
            api,
            api,
            radio,
            sites,
            lots,
            devices,
            scope,
            calibrate,
        )

    /** Add a Hub over Kable; Bluetooth is only touched once the flow opens. Its outcome leads to Add a Node. */
    public val hubSetup: HubSetupEngine =
        SitesWiring.hubSetup(config, api, radio, sites, scope, onAddNode = { nodeSetup.open(it) })

    /** Call in every `onCreate` of the activity that starts sign-in, before it is started. */
    public fun registerActivity(activity: ComponentActivity) {
        authFlowFactory.registerActivity(activity)
    }

    public companion object {
        /** Per-device choices (current Site, chosen time zone) and the last good Lots per Site. */
        private const val PREFERENCES = "com.escendit.coldframe.sites"

        public fun create(
            context: Context,
            config: CoreConfig,
            scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
        ): AndroidSignIn = AndroidSignIn(context, config, scope)
    }
}
