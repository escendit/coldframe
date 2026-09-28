package com.escendit.coldframe.core.signin

import io.ktor.client.engine.mock.MockEngine
import io.ktor.client.engine.mock.MockEngineConfig
import io.ktor.client.engine.mock.MockRequestHandleScope
import io.ktor.client.engine.mock.MockRequestHandler
import io.ktor.client.engine.mock.respond
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.client.request.forms.FormDataContent
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.Url
import io.ktor.http.headersOf
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import org.publicvalue.multiplatform.oidc.OpenIdConnectClient
import org.publicvalue.multiplatform.oidc.OpenIdConnectException
import org.publicvalue.multiplatform.oidc.flows.CodeAuthFlow
import org.publicvalue.multiplatform.oidc.flows.CodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.flows.EndSessionFlow
import org.publicvalue.multiplatform.oidc.flows.PreferencesCodeAuthFlow
import org.publicvalue.multiplatform.oidc.preferences.Preferences
import org.publicvalue.multiplatform.oidc.preferences.setResponseUri
import org.publicvalue.multiplatform.oidc.tokenstore.SettingsStore
import org.publicvalue.multiplatform.oidc.types.AuthCodeRequest
import org.publicvalue.multiplatform.oidc.types.remote.AccessTokenResponse
import kotlin.experimental.ExperimentalObjCRefinement
import kotlin.io.encoding.Base64
import kotlin.native.HiddenFromObjC

val testConfig =
    CoreConfig(
        serverUrl = "https://server.example",
        keycloakIssuer = "https://id.example/realms/coldframe",
        clientId = "coldframe-mobile",
    )

/** Stands in for a TLS failure; the probes and the engine get a classifier that knows it. */
class FakeCertificateException : Exception("certificate")

fun fakeCertificateError(error: Throwable): Boolean =
    generateSequence(error) {
        it.cause
    }.any { it is FakeCertificateException }

/** A JWS without a signature; the library parses claims and never verifies here. */
fun idToken(claims: String): String {
    val base64 = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT)
    return base64.encode("""{"alg":"none"}""".encodeToByteArray()) + "." + base64.encode(claims.encodeToByteArray()) +
        "."
}

fun tokens(
    access: String = "access-1",
    refresh: String? = "refresh-1",
    expiresIn: Int? = 300,
    receivedAt: Long = NOW,
    name: String? = "Simon Novak",
): AccessTokenResponse =
    AccessTokenResponse(
        access_token = access,
        token_type = "Bearer",
        expires_in = expiresIn,
        refresh_token = refresh,
        id_token = name?.let { idToken("""{"sub":"u1","name":"$it"}""") },
        received_at = receivedAt,
    )

const val NOW: Long = 1_800_000_000L

class FakeProbe(
    var signIn: Failure? = null,
    var diagnosis: Failure = Failure.Keycloak,
) : SignInProbe {
    var probes = 0
    var diagnoses = 0

    override suspend fun probeSignIn(): Failure? {
        probes++
        return signIn
    }

    var diagnoseFails = false

    override suspend fun diagnose(): Failure {
        diagnoses++
        if (diagnoseFails) throw IllegalStateException("diagnosis failed")
        return diagnosis
    }
}

class FakeAuthFlow : AuthFlow {
    var result: () -> AccessTokenResponse = { tokens() }
    var gate: CompletableDeferred<Unit>? = null
    var canContinueResult = false
    var signIns = 0
    var continues = 0

    override suspend fun signIn(): AccessTokenResponse {
        signIns++
        gate?.await()
        return result()
    }

    var canContinueFails = false

    override suspend fun canContinue(): Boolean {
        if (canContinueFails) throw IllegalStateException("preferences unreadable")
        return canContinueResult
    }

    override suspend fun continueSignIn(): AccessTokenResponse {
        continues++
        canContinueResult = false
        return result()
    }
}

class FakeVault(
    var tokens: AccessTokenResponse? = null,
    private val log: MutableList<String> = mutableListOf(),
) : TokenVault {
    var readFails = false
    var saveFails = false
    var clearFails = false
    var readGate: CompletableDeferred<Unit>? = null

    override suspend fun read(): AccessTokenResponse? {
        readGate?.await()
        if (readFails) throw IllegalStateException("store unreadable")
        return tokens
    }

    override suspend fun save(tokens: AccessTokenResponse) {
        log += "save"
        if (saveFails) throw IllegalStateException("store unwritable")
        this.tokens = tokens
    }

    override suspend fun clear() {
        log += "clear"
        if (clearFails) throw IllegalStateException("store unwritable")
        tokens = null
    }
}

class FakeIdentity(
    var outcome: (AccessTokenResponse) -> RefreshOutcome = { RefreshOutcome.Transient },
    private val log: MutableList<String> = mutableListOf(),
) : IdentityProvider {
    var refreshes = 0
    var endSessionFails = false

    override suspend fun refresh(tokens: AccessTokenResponse): RefreshOutcome {
        refreshes++
        return outcome(tokens)
    }

    override suspend fun endSession(idToken: String) {
        log += "endSession"
        if (endSessionFails) throw OpenIdConnectException.TechnicalFailure("offline", null)
    }
}

/** An in-memory `SettingsStore` behind the library's `SettingsTokenStore`. */
@OptIn(ExperimentalObjCRefinement::class)
@HiddenFromObjC
class MapSettingsStore : SettingsStore {
    val values = mutableMapOf<String, String>()

    override suspend fun get(key: String): String? = values[key]

    override suspend fun put(
        key: String,
        value: String,
    ) {
        values[key] = value
    }

    override suspend fun remove(key: String) {
        values.remove(key)
    }

    override suspend fun clear() {
        values.clear()
    }
}

/** In-memory preferences that survive a simulated process death. */
class MapPreferences : Preferences {
    private val values = mutableMapOf<String, String>()

    override suspend fun get(key: String): String? = values[key]

    override suspend fun put(
        key: String,
        value: String,
    ) {
        values[key] = value
    }

    override suspend fun remove(key: String) {
        values.remove(key)
    }

    override suspend fun clear() {
        values.clear()
    }
}

/** What the fake browser saw, so the token endpoint can echo the nonce. */
class BrowserLog {
    var nonce: String? = null
    var authorizationUrl: Url? = null
    var cancel = false
    var dies = false
}

/** Signals that the process "died" while the browser was open. */
class ProcessDeath : Exception("process died")

/**
 * The browser: records the authorization URL and redirects back with a code, or cancels, or
 * "dies" after the redirect arrived and before the app exchanged the code.
 */
class FakeBrowserFlow(
    client: OpenIdConnectClient,
    preferences: Preferences,
    private val log: BrowserLog,
) : PreferencesCodeAuthFlow(client, preferences) {
    override suspend fun startLoginFlow(request: AuthCodeRequest) {
        log.authorizationUrl = request.url
        log.nonce = request.nonce
        if (log.cancel) throw OpenIdConnectException.AuthenticationCancelled()
        preferences.setResponseUri(Url("${CoreConfig.REDIRECT_URI}?code=the-code&state=${request.state}"))
        if (log.dies) throw ProcessDeath()
    }
}

class FakeBrowserFactory(
    private val preferences: Preferences,
    private val log: BrowserLog,
) : CodeAuthFlowFactory {
    override fun createAuthFlow(client: OpenIdConnectClient): CodeAuthFlow = FakeBrowserFlow(client, preferences, log)

    override fun createEndSessionFlow(client: OpenIdConnectClient): EndSessionFlow =
        error("Sign-out never opens a browser.")
}

/** A MockEngine on the test scheduler, so virtual time never outruns a response. */
fun TestScope.mockEngine(handler: MockRequestHandler): MockEngine =
    MockEngine(
        MockEngineConfig().apply {
            dispatcher = StandardTestDispatcher(testScheduler)
            addHandler(handler)
        },
    )

/** A Keycloak and Server over MockEngine, with switches for each failure. */
class FakeKeycloak(
    scope: TestScope,
) {
    val requests = mutableListOf<HttpRequestData>()
    val browser = BrowserLog()
    var serverDown = false
    var refreshStatus = HttpStatusCode.OK
    var refreshError = """{"error":"invalid_grant","error_description":"Token is not active"}"""
    var refreshThrows = false
    var exchangeStatus = HttpStatusCode.OK
    var issued = 0

    private val json = headersOf(HttpHeaders.ContentType, "application/json")

    val engine =
        scope.mockEngine { request ->
            requests += request
            handle(request)
        }

    private fun MockRequestHandleScope.handle(request: HttpRequestData): HttpResponseData {
        val path = request.url.encodedPath
        return when {
            path == "/.well-known/healthz" -> {
                if (serverDown) {
                    throw IllegalStateException(
                        "connection refused",
                    )
                } else {
                    respond("", HttpStatusCode.NoContent)
                }
            }

            path.endsWith("/.well-known/openid-configuration") -> {
                respond(DISCOVERY, HttpStatusCode.OK, json)
            }

            path.endsWith("/protocol/openid-connect/token") -> {
                token(request)
            }

            path.endsWith("/protocol/openid-connect/logout") -> {
                respond("", HttpStatusCode.NoContent)
            }

            else -> {
                respond("", HttpStatusCode.NotFound)
            }
        }
    }

    private fun MockRequestHandleScope.token(request: HttpRequestData): HttpResponseData {
        val form = (request.body as FormDataContent).formData
        return when (form["grant_type"]) {
            "authorization_code" -> {
                if (exchangeStatus == HttpStatusCode.OK) {
                    respond(issue(nonce = browser.nonce), HttpStatusCode.OK, json)
                } else {
                    respond("""{"error":"invalid_grant"}""", exchangeStatus, json)
                }
            }

            "refresh_token" -> {
                if (refreshThrows) throw IllegalStateException("no response")
                if (refreshStatus == HttpStatusCode.OK) {
                    respond(issue(nonce = null), HttpStatusCode.OK, json)
                } else {
                    respond(refreshError, refreshStatus, json)
                }
            }

            else -> {
                respond("", HttpStatusCode.BadRequest)
            }
        }
    }

    private fun issue(nonce: String?): String {
        issued++
        val claims =
            if (nonce ==
                null
            ) {
                """{"sub":"u1","name":"Simon Novak"}"""
            } else {
                """{"sub":"u1","name":"Simon Novak","nonce":"$nonce"}"""
            }
        return """{"access_token":"access-$issued","token_type":"Bearer","expires_in":300,""" +
            """"refresh_token":"refresh-$issued","id_token":"${idToken(claims)}"}"""
    }

    fun requestsTo(suffix: String): List<HttpRequestData> = requests.filter { it.url.encodedPath.endsWith(suffix) }

    companion object {
        const val ISSUER = "https://id.example/realms/coldframe"
        val DISCOVERY =
            """{"issuer":"$ISSUER","authorization_endpoint":"$ISSUER/protocol/openid-connect/auth",""" +
                """"token_endpoint":"$ISSUER/protocol/openid-connect/token",""" +
                """"end_session_endpoint":"$ISSUER/protocol/openid-connect/logout"}"""
    }
}
