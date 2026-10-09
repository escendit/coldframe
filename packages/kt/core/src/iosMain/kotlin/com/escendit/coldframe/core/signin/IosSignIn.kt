package com.escendit.coldframe.core.signin

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.alerts.IosAlerts
import com.escendit.coldframe.core.calibrate.IosCalibrate
import com.escendit.coldframe.core.devices.IosDevices
import com.escendit.coldframe.core.lots.IosLotDetail
import com.escendit.coldframe.core.lots.IosLots
import com.escendit.coldframe.core.notifications.IosNotificationSettings
import com.escendit.coldframe.core.setup.IosHubSetup
import com.escendit.coldframe.core.setup.IosNodeSetup
import com.escendit.coldframe.core.setup.IosRadioState
import com.escendit.coldframe.core.setup.KableSetupRadio
import com.escendit.coldframe.core.sites.IosSites
import com.escendit.coldframe.core.sites.SitesWiring
import com.escendit.coldframe.core.thresholds.IosThresholds
import com.escendit.coldframe.core.watch
import com.russhwolf.settings.NSUserDefaultsSettings
import io.ktor.client.engine.darwin.Darwin
import kotlinx.coroutines.MainScope
import org.publicvalue.multiplatform.oidc.appsupport.IosCodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.tokenstore.IosKeychainTokenStore
import platform.Foundation.NSUserDefaults

/**
 * The iOS side of the core: `ASWebAuthenticationSession` (not ephemeral) for the browser flow,
 * the Keychain for the tokens. Swift observes it with [watch].
 */
public class IosSignIn private constructor(
    private val config: CoreConfig,
) {
    private val scope = MainScope()
    private val authFlowFactory = IosCodeAuthFlowFactory(ephemeralBrowserSession = false)

    private val engine: SignInEngine =
        SignInClients.engine(
            config = config,
            httpEngine = Darwin.create(),
            store = IosKeychainTokenStore(),
            authFlowFactory = { authFlowFactory },
            scope = scope,
        )

    private val api = SitesWiring.api(config, Darwin.create(), engine)

    /** Per-device choices and the last good Lots; not the Keychain. */
    private val settings = NSUserDefaultsSettings(NSUserDefaults.standardUserDefaults)

    private val sitesEngine =
        SitesWiring.engine(
            api = api,
            signIn = engine,
            settings = settings,
            scope = scope,
        )

    /** The Sites of the signed-in user; loads whenever the session becomes signed in. */
    public val sites: IosSites = IosSites(sitesEngine, scope)

    private val lotsEngine = SitesWiring.lots(api, sitesEngine, settings, scope, cadence = api)
    private val lotDetailEngine = SitesWiring.lotDetail(api, sitesEngine, settings, scope)
    private val devicesEngine = SitesWiring.devices(api, sitesEngine, scope)
    private val calibrateEngine = SitesWiring.calibrate(api, sitesEngine, scope)
    private val thresholdsEngine = SitesWiring.thresholds(api, sitesEngine, scope, lotDetailEngine, lotsEngine)
    private val radio = KableSetupRadio(IosRadioState())
    private val nodeSetupEngine =
        SitesWiring.nodeSetup(api, api, radio, sitesEngine, lotsEngine, devicesEngine, scope, calibrateEngine)

    /** The Lots of the current Site and Site settings; reloads whenever the current Site changes. */
    public val lots: IosLots = IosLots(lotsEngine, scope)

    /**
     * My notifications: the Notification Window, the time zone, and the mute and my Reminder cadence of the
     * current Site. It reads when the session starts and hands the device's zone to the Server; Swift calls
     * `load` on every entry of the surface.
     */
    public val notifications: IosNotificationSettings =
        IosNotificationSettings(SitesWiring.notifications(api, sitesEngine, engine, settings, scope), scope)

    /** Lot detail of the current Site: one Lot, its Sensors, Node and 30-day history. */
    public val lotDetail: IosLotDetail = IosLotDetail(lotDetailEngine, scope)

    /** Calibrate of one Lot's soil Sensor: dry, then wet, from stored Readings over REST. */
    public val calibrate: IosCalibrate = IosCalibrate(calibrateEngine, scope)

    /** Thresholds of one Lot's Sensors: a Threshold column each, edited and saved over REST. */
    public val thresholds: IosThresholds = IosThresholds(thresholdsEngine, scope)

    /** The Devices of the current Site; Swift loads it on every entry of the Devices tab. */
    public val devices: IosDevices = IosDevices(devicesEngine, scope)

    /** The Alerts of the current Site, read as soon as a Site is current; Swift refreshes on foreground. */
    public val alerts: IosAlerts = IosAlerts(SitesWiring.alerts(api, sitesEngine, scope), scope)

    /** Add a Node over Kable, on one Site; an assigned Node reloads the Lots and the Devices. */
    public val nodeSetup: IosNodeSetup = IosNodeSetup(nodeSetupEngine, scope)

    /** Add a Hub over Kable; the Bluetooth prompt appears only when the flow opens. Its outcome leads to Add a Node. */
    public val hubSetup: IosHubSetup =
        IosHubSetup(
            SitesWiring.hubSetup(config, api, radio, sitesEngine, scope, onAddNode = { nodeSetupEngine.open(it) }),
            scope,
        )

    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (SignInSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    public fun signIn() {
        engine.signIn()
    }

    public fun resume() {
        engine.resume()
    }

    public fun signOut() {
        engine.signOut()
    }

    public companion object {
        /** Unset or blank values (an unconfigured xcconfig) fall back to the `.invalid` defaults. */
        public fun create(
            serverUrl: String?,
            keycloakIssuer: String?,
            clientId: String?,
        ): IosSignIn = IosSignIn(CoreConfig.of(serverUrl, keycloakIssuer, clientId))
    }
}
