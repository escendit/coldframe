package com.escendit.coldframe.core.signin

import io.ktor.http.HttpStatusCode
import kotlinx.coroutines.CancellationException
import org.publicvalue.multiplatform.oidc.OpenIdConnectClient
import org.publicvalue.multiplatform.oidc.OpenIdConnectException
import org.publicvalue.multiplatform.oidc.tokenstore.TokenRefreshHandler
import org.publicvalue.multiplatform.oidc.tokenstore.TokenStore
import org.publicvalue.multiplatform.oidc.types.remote.AccessTokenResponse

/** What a refresh attempt means for the session. */
public sealed interface RefreshOutcome {
    /** New tokens were saved. */
    public data class Refreshed(
        val tokens: AccessTokenResponse,
    ) : RefreshOutcome

    /** Keycloak refused the refresh token (400/401, e.g. `invalid_grant`): the session ended. */
    public data object Rejected : RefreshOutcome

    /** No response or a 5xx: the session may still be valid; retried on the next foreground. */
    public data object Transient : RefreshOutcome
}

/** Keycloak calls the session needs after sign-in. */
public interface IdentityProvider {
    /** Refreshes [tokens] and saves the result in the store. Never throws for HTTP or I/O failures. */
    public suspend fun refresh(tokens: AccessTokenResponse): RefreshOutcome

    /** RP-initiated logout without a browser. May throw; callers treat it as best effort. */
    public suspend fun endSession(idToken: String)
}

/** [IdentityProvider] over the library client, refreshing through [TokenRefreshHandler]. */
public class OidcIdentityProvider(
    private val client: OpenIdConnectClient,
    private val store: TokenStore,
) : IdentityProvider {
    private val refreshHandler = TokenRefreshHandler(store)

    override suspend fun refresh(tokens: AccessTokenResponse): RefreshOutcome =
        try {
            refreshHandler.refreshAndSaveToken(client, tokens.access_token)
            // The handler saved the new tokens; a store that cannot be read is retried later.
            store.getTokenResponse()?.let { RefreshOutcome.Refreshed(it) } ?: RefreshOutcome.Transient
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (error: OpenIdConnectException) {
            classify(error)
        }

    override suspend fun endSession(idToken: String) {
        if (client.config.endpoints?.endSessionEndpoint == null) {
            client.discover()
        }
        client.endSession(idToken)
    }

    public companion object {
        /** 400 and 401 from the token endpoint mean the refresh token is no longer accepted. */
        public fun classify(error: OpenIdConnectException): RefreshOutcome =
            when (error) {
                is OpenIdConnectException.UnsuccessfulTokenRequest -> {
                    if (error.statusCode == HttpStatusCode.BadRequest ||
                        error.statusCode == HttpStatusCode.Unauthorized
                    ) {
                        RefreshOutcome.Rejected
                    } else {
                        RefreshOutcome.Transient
                    }
                }

                // No refresh token, or it has expired by its own lifetime.
                is OpenIdConnectException.TokenExpired -> {
                    RefreshOutcome.Rejected
                }

                else -> {
                    RefreshOutcome.Transient
                }
            }
    }
}
