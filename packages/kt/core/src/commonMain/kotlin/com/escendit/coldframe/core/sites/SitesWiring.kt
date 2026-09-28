package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ColdframeApi
import com.escendit.coldframe.core.signin.CoreConfig
import com.escendit.coldframe.core.signin.SignInEngine
import com.russhwolf.settings.Settings
import io.ktor.client.engine.HttpClientEngine
import kotlinx.coroutines.CoroutineScope

/** Builds the Sites engine over the Server API, with tokens from the sign-in engine. */
public object SitesWiring {
    public fun engine(
        config: CoreConfig,
        httpEngine: HttpClientEngine,
        signIn: SignInEngine,
        settings: Settings,
        scope: CoroutineScope,
    ): SitesEngine =
        SitesEngine(
            api = api(config, httpEngine, signIn),
            choices = DeviceChoices(settings),
            scope = scope,
            signIn = signIn.state,
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
