package com.escendit.coldframe.core.signin

import io.ktor.client.request.forms.FormDataContent
import io.ktor.http.HttpStatusCode
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import org.publicvalue.multiplatform.oidc.OpenIdConnectClient
import org.publicvalue.multiplatform.oidc.tokenstore.SettingsTokenStore
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.time.Clock

/** Waits until the engine shows a surface; the library hops threads while exchanging. */
private suspend fun SignInEngine.settle(): SignInState =
    state.first {
        it != SignInState.Working &&
            it != SignInState.Restoring
    }

/** The real library client, token store and refresh handler over a mocked network. */
class OidcFlowTest {
    private class World(
        scope: TestScope,
    ) {
        val keycloak = FakeKeycloak(scope)
        val settings = MapSettingsStore()
        val store = SettingsTokenStore(settings)
        val preferences = MapPreferences()

        fun client(): OpenIdConnectClient =
            SignInClients.oidcClient(testConfig, SignInClients.oidcHttpClient(keycloak.engine))

        fun engine(scope: TestScope): SignInEngine {
            val client = client()
            return SignInEngine(
                probe = HttpProbe(SignInClients.probeHttpClient(keycloak.engine), testConfig),
                authFlow = LibraryAuthFlow(client) { FakeBrowserFactory(preferences, keycloak.browser) },
                vault = StoreTokenVault(store),
                identity = OidcIdentityProvider(client, store),
                scope = scope,
            )
        }
    }

    @Test
    fun uxDr60DiscoveryThenAuthorizationCodeWithPkceS256ThenTheExchange() =
        runTest {
            val world = World(this)
            val engine = world.engine(this)
            engine.resume()
            engine.settle()

            engine.signIn()
            engine.settle()

            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            val url = requireNotNull(world.keycloak.browser.authorizationUrl)
            assertTrue(url.toString().startsWith("${FakeKeycloak.ISSUER}/protocol/openid-connect/auth"))
            assertEquals("coldframe-mobile", url.parameters["client_id"])
            assertEquals("code", url.parameters["response_type"])
            assertEquals("S256", url.parameters["code_challenge_method"])
            assertEquals("com.escendit.coldframe:/signin/callback", url.parameters["redirect_uri"])
            assertEquals("openid profile", url.parameters["scope"])

            val exchange = world.keycloak.requestsTo("/token").single()
            val form = (exchange.body as FormDataContent).formData
            assertEquals("authorization_code", form["grant_type"])
            assertEquals("the-code", form["code"])
            assertTrue(form["code_verifier"].orEmpty().length >= 43)
            assertNull(form["client_secret"])

            assertEquals("access-1", world.store.getAccessToken())
            assertEquals("refresh-1", world.store.getRefreshToken())
        }

    @Test
    fun uxDr92CancellingInTheBrowserStoresNothing() =
        runTest {
            val world = World(this)
            world.keycloak.browser.cancel = true
            val engine = world.engine(this)
            engine.resume()
            engine.settle()

            engine.signIn()
            engine.settle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertNull(world.store.getTokenResponse())
            assertTrue(world.keycloak.requestsTo("/token").isEmpty())
        }

    @Test
    fun uxDr92ARefusedCodeExchangeIsTheKeycloakNotice() =
        runTest {
            val world = World(this)
            world.keycloak.exchangeStatus = HttpStatusCode.BadRequest
            val engine = world.engine(this)
            engine.resume()
            engine.settle()

            engine.signIn()
            engine.settle()

            assertEquals(SignInState.SignedOut(Notice.Keycloak), engine.state.value)
            assertNull(world.store.getTokenResponse())
        }

    @Test
    fun uxDr92AnUnreachableServerNeverOpensTheBrowser() =
        runTest {
            val world = World(this)
            world.keycloak.serverDown = true
            val engine = world.engine(this)
            engine.resume()
            engine.settle()

            engine.signIn()
            engine.settle()

            assertEquals(SignInState.SignedOut(Notice.Unreachable), engine.state.value)
            assertNull(world.keycloak.browser.authorizationUrl)
        }

    @Test
    fun aSignInInterruptedByProcessDeathIsExchangedByContinueLoginOnTheNextStart() =
        runTest {
            val world = World(this)
            world.keycloak.browser.dies = true
            val first = world.engine(this)
            first.resume()
            first.settle()
            first.signIn()
            first.settle()
            assertTrue(world.keycloak.requestsTo("/token").isEmpty())

            // A new process: new client and engine, the same persisted preferences and store.
            world.keycloak.browser.dies = false
            val second = world.engine(this)
            second.resume()
            second.settle()

            assertEquals(SignInState.SignedIn("Simon Novak"), second.state.value)
            assertEquals("access-1", world.store.getAccessToken())
        }

    @Test
    fun aRefreshThatSucceedsSavesTheNewTokens() =
        runTest {
            val world = World(this)
            world.store.saveTokens(tokens(access = "old", refresh = "old-refresh"))

            val outcome =
                OidcIdentityProvider(
                    world.client(),
                    world.store,
                ).refresh(tokens(access = "old", refresh = "old-refresh"))

            val refreshed = assertIs<RefreshOutcome.Refreshed>(outcome)
            assertEquals("access-1", refreshed.tokens.access_token)
            assertEquals("access-1", world.store.getAccessToken())
            val form =
                (
                    world.keycloak
                        .requestsTo("/token")
                        .single()
                        .body as FormDataContent
                ).formData
            assertEquals("refresh_token", form["grant_type"])
            assertEquals("old-refresh", form["refresh_token"])
            assertEquals("coldframe-mobile", form["client_id"])
        }

    @Test
    fun uxDr93ARefreshRejectedWithInvalidGrantEndsTheSession() =
        runTest {
            val world = World(this)
            world.keycloak.refreshStatus = HttpStatusCode.BadRequest
            world.store.saveTokens(tokens(access = "old"))

            val outcome = OidcIdentityProvider(world.client(), world.store).refresh(tokens(access = "old"))

            assertEquals(RefreshOutcome.Rejected, outcome)
        }

    @Test
    fun uxDr93ARefreshRejectedWith401EndsTheSession() =
        runTest {
            val world = World(this)
            world.keycloak.refreshStatus = HttpStatusCode.Unauthorized
            world.keycloak.refreshError = """{"error":"invalid_client"}"""
            world.store.saveTokens(tokens(access = "old"))

            val outcome = OidcIdentityProvider(world.client(), world.store).refresh(tokens(access = "old"))

            assertEquals(RefreshOutcome.Rejected, outcome)
        }

    @Test
    fun aRefreshAnsweredWith503IsTransient() =
        runTest {
            val world = World(this)
            world.keycloak.refreshStatus = HttpStatusCode.ServiceUnavailable
            world.keycloak.refreshError = "unavailable"
            world.store.saveTokens(tokens(access = "old"))

            val outcome = OidcIdentityProvider(world.client(), world.store).refresh(tokens(access = "old"))

            assertEquals(RefreshOutcome.Transient, outcome)
            assertEquals("old", world.store.getAccessToken())
        }

    @Test
    fun aRefreshWithoutAResponseIsTransient() =
        runTest {
            val world = World(this)
            world.keycloak.refreshThrows = true
            world.store.saveTokens(tokens(access = "old"))

            val outcome = OidcIdentityProvider(world.client(), world.store).refresh(tokens(access = "old"))

            assertEquals(RefreshOutcome.Transient, outcome)
        }

    @Test
    fun aStoredSessionWithoutARefreshTokenIsRejected() =
        runTest {
            val world = World(this)
            world.store.saveTokens(tokens(access = "old", refresh = null))

            val outcome =
                OidcIdentityProvider(
                    world.client(),
                    world.store,
                ).refresh(tokens(access = "old", refresh = null))

            assertEquals(RefreshOutcome.Rejected, outcome)
        }

    @Test
    fun uxDr93AnExpiredSessionThatKeycloakRejectsClearsTheStoreThroughTheEngine() =
        runTest {
            val world = World(this)
            world.keycloak.refreshStatus = HttpStatusCode.BadRequest
            world.store.saveTokens(tokens(access = "old", receivedAt = 1, expiresIn = 300))
            val engine = world.engine(this)

            engine.resume()
            engine.settle()

            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
            assertNull(world.store.getTokenResponse())
        }

    @Test
    fun uxDr113SignOutEndsTheKeycloakSessionWithTheIdTokenAfterClearingTheStore() =
        runTest {
            val world = World(this)
            val engine = world.engine(this)
            engine.resume()
            engine.settle()
            engine.signIn()
            engine.settle()
            val idToken = world.store.getIdToken()

            engine.signOut()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertNull(world.store.getTokenResponse())
            val logout = world.keycloak.requestsTo("/logout").single()
            assertEquals(idToken, (logout.body as FormDataContent).formData["id_token_hint"])
        }

    @Test
    fun uxDr113SignOutAfterAColdStartDiscoversTheLogoutEndpointAndEndsTheSession() =
        runTest {
            val world = World(this)
            val stored = tokens(receivedAt = Clock.System.now().epochSeconds)
            world.store.saveTokens(stored)
            val engine = world.engine(this)
            engine.resume()
            engine.settle()
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)

            engine.signOut()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertNull(world.store.getTokenResponse())
            val logout = world.keycloak.requestsTo("/logout").single()
            assertEquals(stored.id_token, (logout.body as FormDataContent).formData["id_token_hint"])
        }
}
