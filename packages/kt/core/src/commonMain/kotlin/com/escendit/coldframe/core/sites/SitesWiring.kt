package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ColdframeApi
import com.escendit.coldframe.core.devices.DevicesApi
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.setup.EnrolmentApi
import com.escendit.coldframe.core.setup.HubSetupEngine
import com.escendit.coldframe.core.setup.NodeSetupEngine
import com.escendit.coldframe.core.setup.SetupRadio
import com.escendit.coldframe.core.signin.CoreConfig
import com.escendit.coldframe.core.signin.SignInEngine
import com.russhwolf.settings.Settings
import io.ktor.client.engine.HttpClientEngine
import kotlinx.coroutines.CoroutineScope

/** Builds the Sites, Lots and Devices engines over the Server API, with tokens from the sign-in engine. */
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

    /** The Lots engine, following the current Site of [sites]. */
    public fun lots(
        api: LotsApi,
        sites: SitesEngine,
        scope: CoroutineScope,
    ): LotsEngine = LotsEngine(api = api, sites = sites, scope = scope)

    /** The Devices engine, following the current Site of [sites]. */
    public fun devices(
        api: DevicesApi,
        sites: SitesEngine,
        scope: CoroutineScope,
    ): DevicesEngine = DevicesEngine(api = api, sites = sites, scope = scope)

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
     * [devices] read their lists again, so the Lot's tile is no longer *no Node*.
     */
    public fun nodeSetup(
        api: EnrolmentApi,
        lotsApi: LotsApi,
        radio: SetupRadio,
        sites: SitesEngine,
        lots: LotsEngine,
        devices: DevicesEngine,
        scope: CoroutineScope,
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
