package com.escendit.coldframe.core.signin

import io.ktor.client.HttpClient
import io.ktor.client.HttpClientConfig
import io.ktor.client.engine.HttpClientEngine
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.http.ContentType
import io.ktor.http.ContentTypeMatcher
import io.ktor.serialization.kotlinx.KotlinxSerializationConverter
import kotlinx.coroutines.CoroutineScope
import kotlinx.serialization.json.Json
import org.publicvalue.multiplatform.oidc.DefaultOpenIdConnectClient
import org.publicvalue.multiplatform.oidc.OpenIdConnectClient
import org.publicvalue.multiplatform.oidc.OpenIdConnectClientConfig
import org.publicvalue.multiplatform.oidc.flows.CodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.tokenstore.TokenStore
import org.publicvalue.multiplatform.oidc.types.CodeChallengeMethod
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** Builds the Ktor clients, the library client and the engine for one platform. */
public object SignInClients {
    /** The HTTP client of the probes: no redirects, because any response counts. */
    public fun probeHttpClient(engine: HttpClientEngine): HttpClient =
        HttpClient(engine) {
            followRedirects = false
            expectSuccess = false
            timeouts(PROBE_TIMEOUT)
        }

    /**
     * The HTTP client of the library: JSON for every content type, as the library's default
     * client does for identity providers that send the wrong one.
     */
    public fun oidcHttpClient(engine: HttpClientEngine): HttpClient =
        HttpClient(engine) {
            timeouts(OIDC_TIMEOUT)
            install(ContentNegotiation) {
                register(
                    ContentType.Application.Json,
                    KotlinxSerializationConverter(
                        Json {
                            explicitNulls = false
                            ignoreUnknownKeys = true
                        },
                    ),
                    object : ContentTypeMatcher {
                        override fun contains(contentType: ContentType): Boolean = true
                    },
                ) {}
            }
        }

    /** Authorization Code + PKCE (S256) for the public mobile client. */
    public fun oidcClient(
        config: CoreConfig,
        httpClient: HttpClient,
    ): OpenIdConnectClient =
        DefaultOpenIdConnectClient(
            httpClient = httpClient,
            config =
                OpenIdConnectClientConfig(discoveryUri = config.discoveryUri).apply {
                    clientId = config.clientId
                    scope = "openid profile"
                    codeChallengeMethod = CodeChallengeMethod.S256
                    redirectUri = config.redirectUri
                    postLogoutRedirectUri = config.postLogoutRedirectUri
                },
        )

    /** The engine over the real library pieces. */
    public fun engine(
        config: CoreConfig,
        httpEngine: HttpClientEngine,
        store: TokenStore,
        authFlowFactory: () -> CodeAuthFlowFactory,
        scope: CoroutineScope,
    ): SignInEngine {
        val client = oidcClient(config, oidcHttpClient(httpEngine))
        return SignInEngine(
            probe = HttpProbe(probeHttpClient(httpEngine), config),
            authFlow = LibraryAuthFlow(client, authFlowFactory),
            vault = StoreTokenVault(store),
            identity = OidcIdentityProvider(client, store),
            scope = scope,
        )
    }

    /**
     * Bound for discovery, the code exchange, refresh and end-session. Only the two probes are
     * held to [PROBE_TIMEOUT]; a slow exchange after a successful browser login must not fail.
     */
    public val OIDC_TIMEOUT: Duration = 30.seconds

    private fun HttpClientConfig<*>.timeouts(bound: Duration) {
        install(HttpTimeout) {
            requestTimeoutMillis = bound.inWholeMilliseconds
            connectTimeoutMillis = bound.inWholeMilliseconds
        }
    }
}
