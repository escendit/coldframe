package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.alerts.AlertsApi
import com.escendit.coldframe.core.alerts.AlertsEngine
import com.escendit.coldframe.core.api.ColdframeApi
import com.escendit.coldframe.core.calibrate.CalibrateApi
import com.escendit.coldframe.core.calibrate.CalibrateEngine
import com.escendit.coldframe.core.devices.DevicesApi
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotDetailApi
import com.escendit.coldframe.core.lots.LotDetailEngine
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.notifications.NotificationSettingsApi
import com.escendit.coldframe.core.notifications.NotificationSettingsEngine
import com.escendit.coldframe.core.notifications.SiteReminderCadenceApi
import com.escendit.coldframe.core.push.PushApi
import com.escendit.coldframe.core.push.PushEngine
import com.escendit.coldframe.core.push.PushPlatform
import com.escendit.coldframe.core.setup.EnrolmentApi
import com.escendit.coldframe.core.setup.HubSetupEngine
import com.escendit.coldframe.core.setup.NodeSetupEngine
import com.escendit.coldframe.core.setup.SetupRadio
import com.escendit.coldframe.core.signin.CoreConfig
import com.escendit.coldframe.core.signin.SignInEngine
import com.escendit.coldframe.core.thresholds.ThresholdsApi
import com.escendit.coldframe.core.thresholds.ThresholdsEngine
import com.russhwolf.settings.Settings
import io.ktor.client.engine.HttpClientEngine
import kotlinx.coroutines.CoroutineScope

/** Builds the Sites, Lots, Devices, Alerts, notification settings and push engines over the Server API, with tokens from the sign-in engine. */
public object SitesWiring {
    public fun engine(
        config: CoreConfig,
        httpEngine: HttpClientEngine,
        signIn: SignInEngine,
        settings: Settings,
        scope: CoroutineScope,
    ): SitesEngine = engine(api(config, httpEngine, signIn), signIn, settings, scope)

    /** The Sites engine over an [api] shared with the Lots engine. */
    public fun engine(
        api: ColdframeApi,
        signIn: SignInEngine,
        settings: Settings,
        scope: CoroutineScope,
    ): SitesEngine =
        SitesEngine(
            api = api,
            choices = DeviceChoices(settings),
            scope = scope,
            signIn = signIn.state,
        )

    /**
     * The Lots engine, following the current Site of [sites]. It keeps the last good Lots of
     * each Site in [settings] (keys `lots.lastGood.‹siteId›`) and decides stale mode. With
     * [cadence] its Site settings read and set the Site's Reminder cadence.
     */
    public fun lots(
        api: LotsApi,
        sites: SitesEngine,
        settings: Settings,
        scope: CoroutineScope,
        cadence: SiteReminderCadenceApi?,
    ): LotsEngine = LotsEngine(api = api, sites = sites, settings = settings, scope = scope, cadenceApi = cadence)

    /**
     * My notifications, following the session of [signIn] and the current Site of [sites]. [settings] are the
     * ones the Sites engine was built with: the zone confirmed on Create Site waits there until it is handed
     * to the Server.
     */
    public fun notifications(
        api: NotificationSettingsApi,
        sites: SitesEngine,
        signIn: SignInEngine,
        settings: Settings,
        scope: CoroutineScope,
    ): NotificationSettingsEngine =
        NotificationSettingsEngine(
            api = api,
            sites = sites,
            choices = DeviceChoices(settings),
            scope = scope,
            signIn = signIn.state,
        )

    /**
     * Push on this phone, for the [platform] of the app: it registers the device's token while [signIn] is signed
     * in, removes the registration at the start of a deliberate sign-out, while the session is still valid, and
     * routes a tapped notification through [sites] and [lots]. [settings] are the ones the Sites engine was built
     * with: the installation ID and "the prompt was answered" are kept there and outlive every session.
     */
    public fun push(
        api: PushApi,
        sites: SitesEngine,
        lots: LotsEngine,
        signIn: SignInEngine,
        settings: Settings,
        scope: CoroutineScope,
        platform: PushPlatform,
    ): PushEngine {
        val engine =
            PushEngine(
                api = api,
                sites = sites,
                lots = lots.state,
                choices = DeviceChoices(settings),
                platform = platform,
                scope = scope,
                signIn = signIn.state,
            )
        signIn.onSigningOut { engine.unregister() }
        return engine
    }

    /** Lot detail, following the current Site of [sites]; keeps the last good detail of each Lot in [settings]. */
    public fun lotDetail(
        api: LotDetailApi,
        sites: SitesEngine,
        settings: Settings,
        scope: CoroutineScope,
    ): LotDetailEngine = LotDetailEngine(api = api, sites = sites, settings = settings, scope = scope)

    /** Calibrate of one Lot's soil Sensor, following the current Site of [sites]. */
    public fun calibrate(
        api: CalibrateApi,
        sites: SitesEngine,
        scope: CoroutineScope,
    ): CalibrateEngine = CalibrateEngine(api = api, sites = sites, scope = scope)

    /**
     * Thresholds of one Lot's Sensors, following the current Site of [sites]. A saved change makes [lotDetail] and
     * [lots] read again, so the chart band and the tile move at once.
     */
    public fun thresholds(
        api: ThresholdsApi,
        sites: SitesEngine,
        scope: CoroutineScope,
        lotDetail: LotDetailEngine? = null,
        lots: LotsEngine? = null,
    ): ThresholdsEngine =
        ThresholdsEngine(
            api = api,
            sites = sites,
            scope = scope,
            onSaved = {
                lotDetail?.refresh()
                lots?.load()
            },
        )

    /** The Devices engine, following the current Site of [sites]. */
    public fun devices(
        api: DevicesApi,
        sites: SitesEngine,
        scope: CoroutineScope,
    ): DevicesEngine = DevicesEngine(api = api, sites = sites, scope = scope)

    /** The Alerts engine, following the current Site of [sites]: it reads as soon as a Site is current. */
    public fun alerts(
        api: AlertsApi,
        sites: SitesEngine,
        scope: CoroutineScope,
    ): AlertsEngine = AlertsEngine(api = api, sites = sites, scope = scope)

    /**
     * Add a Hub over [radio], enrolling on the Sites of [sites]; the Hub reports to the Server of
     * [config]. "Add a Node" on "Hub is online" calls [onAddNode] with the Hub's Site.
     */
    public fun hubSetup(
        config: CoreConfig,
        api: EnrolmentApi,
        radio: SetupRadio,
        sites: SitesEngine,
        scope: CoroutineScope,
        onAddNode: (siteId: String) -> Unit = {},
    ): HubSetupEngine =
        HubSetupEngine(
            radio = radio,
            api = api,
            sites = sites.state,
            serverUrl = config.serverUrl,
            scope = scope,
            onAddNode = onAddNode,
        )

    /**
     * Add a Node over [radio], on one Site of [sites]. Once a Node is assigned, [lots] and
     * [devices] read their lists again, so the Lot's tile is no longer *no Node*. The success
     * outcome's Calibrate action opens [calibrate] on the Lot that got the Node.
     */
    public fun nodeSetup(
        api: EnrolmentApi,
        lotsApi: LotsApi,
        radio: SetupRadio,
        sites: SitesEngine,
        lots: LotsEngine,
        devices: DevicesEngine,
        scope: CoroutineScope,
        calibrate: CalibrateEngine? = null,
    ): NodeSetupEngine =
        NodeSetupEngine(
            radio = radio,
            api = api,
            lots = lotsApi,
            sites = sites.state,
            scope = scope,
            onAssigned = {
                lots.load()
                devices.load()
            },
            onCalibrate = { lotId, lotName -> calibrate?.open(lotId, lotName) },
        )

    /** The API client, authorised by [signIn]; a 401 ends the session. */
    public fun api(
        config: CoreConfig,
        httpEngine: HttpClientEngine,
        signIn: SignInEngine,
    ): ColdframeApi =
        ColdframeApi(
            http = ColdframeApi.httpClient(httpEngine),
            serverUrl = config.serverUrl,
            accessToken = { signIn.accessToken() },
            onUnauthorized = { signIn.signOutExpired() },
        )
}
