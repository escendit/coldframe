package com.escendit.coldframe.core.signin

import io.ktor.client.engine.mock.respond
import io.ktor.client.request.HttpRequestData
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.headersOf
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.time.Duration.Companion.seconds

class ProbeTest {
    private val json = headersOf(HttpHeaders.ContentType, "application/json")

    private fun TestScope.probe(
        server: suspend (HttpRequestData) -> Any,
        issuer: suspend (HttpRequestData) -> Any = { FakeKeycloak.DISCOVERY to HttpStatusCode.OK },
        log: MutableList<String> = mutableListOf(),
    ): HttpProbe {
        val engine =
            mockEngine { request ->
                val path = request.url.encodedPath
                log += path
                val answer = if (path == "/.well-known/healthz") server(request) else issuer(request)
                @Suppress("UNCHECKED_CAST")
                    val (body, status) = answer as Pair<String, HttpStatusCode>
                respond(body, status, json)
            }
        return HttpProbe(SignInClients.probeHttpClient(engine), testConfig, certificateError = ::fakeCertificateError)
    }

    @Test
    fun uxDr60ProbesTheServerFirstThenTheIssuerAndPassesWhenBothAnswer() =
        runTest {
            val log = mutableListOf<String>()
            val result = probe(server = { "" to HttpStatusCode.NoContent }, log = log).probeSignIn()

            assertNull(result)
            assertEquals(listOf("/.well-known/healthz", "/realms/coldframe/.well-known/openid-configuration"), log)
        }

    @Test
    fun uxDr92AnyHttpResponseFromTheServerMeansReachable() =
        runTest {
            val result = probe(server = { "down for maintenance" to HttpStatusCode.ServiceUnavailable }).probeServer()

            assertNull(result)
        }

    @Test
    fun uxDr92NoResponseFromTheServerIsUnreachableAndTheIssuerIsNeverAsked() =
        runTest {
            val log = mutableListOf<String>()
            val result = probe(server = { throw IllegalStateException("connection refused") }, log = log).probeSignIn()

            assertEquals(Failure.Unreachable, result)
            assertEquals(listOf("/.well-known/healthz"), log)
        }

    @Test
    fun uxDr92TheServerProbeIsBoundedToFiveSeconds() =
        runTest {
            val result = probe(server = { awaitCancellation() }).probeSignIn()

            assertEquals(Failure.Unreachable, result)
            assertEquals(5.seconds.inWholeMilliseconds, currentTime)
            assertEquals(5.seconds, PROBE_TIMEOUT)
        }

    @Test
    fun uxDr92TheIssuerProbeIsBoundedToFiveSecondsAndShowsTheKeycloakNotice() =
        runTest {
            val result =
                probe(
                    server = { "" to HttpStatusCode.NoContent },
                    issuer = { awaitCancellation() },
                ).probeIssuer()

            assertEquals(Failure.Keycloak, result)
            assertEquals(5.seconds.inWholeMilliseconds, currentTime)
        }

    @Test
    fun uxDr92ACertificateFailureOnTheServerIsTheCertificateNotice() =
        runTest {
            val result =
                probe(
                    server = { throw IllegalStateException("tls", FakeCertificateException()) },
                ).probeSignIn()

            assertEquals(Failure.Certificate, result)
        }

    @Test
    fun uxDr92ACertificateFailureOnTheIssuerIsTheCertificateNotice() =
        runTest {
            val result =
                probe(
                    server = { "" to HttpStatusCode.NoContent },
                    issuer = { throw FakeCertificateException() },
                ).probeSignIn()

            assertEquals(Failure.Certificate, result)
        }

    @Test
    fun uxDr92AnIssuerThatAnswersNon2xxIsTheKeycloakNotice() =
        runTest {
            val result =
                probe(server = {
                    "" to HttpStatusCode.NoContent
                }, issuer = { "" to HttpStatusCode.NotFound }).probeSignIn()

            assertEquals(Failure.Keycloak, result)
        }

    @Test
    fun uxDr92ADiscoveryDocumentWithoutAnAuthorizationEndpointIsTheKeycloakNotice() =
        runTest {
            val withoutEndpoint =
                probe(server = { "" to HttpStatusCode.NoContent }, issuer = {
                    """{"issuer":"x"}""" to
                        HttpStatusCode.OK
                })
            val notJson = probe(server = { "" to HttpStatusCode.NoContent }, issuer = { "<html>" to HttpStatusCode.OK })

            assertEquals(Failure.Keycloak, withoutEndpoint.probeSignIn())
            assertEquals(Failure.Keycloak, notJson.probeSignIn())
        }

    @Test
    fun uxDr92AnIssuerWithoutAnyResponseIsTheKeycloakNotice() =
        runTest {
            val result =
                probe(
                    server = { "" to HttpStatusCode.NoContent },
                    issuer = { throw IllegalStateException("refused") },
                ).probeSignIn()

            assertEquals(Failure.Keycloak, result)
        }

    @Test
    fun uxDr92DiagnosingAFailedExchangeKeepsTheCertificateNoticeOnlyForATlsFailure() =
        runTest {
            val tls = probe(server = { "" to HttpStatusCode.NoContent }, issuer = { throw FakeCertificateException() })
            val broken =
                probe(server = { "" to HttpStatusCode.NoContent }, issuer = { "" to HttpStatusCode.BadGateway })
            val healthy = probe(server = { "" to HttpStatusCode.NoContent })

            assertEquals(Failure.Certificate, tls.diagnose())
            assertEquals(Failure.Keycloak, broken.diagnose())
            assertEquals(Failure.Keycloak, healthy.diagnose())
        }

    @Test
    fun aServerUrlThatIsNotAUrlIsUnreachable() =
        runTest {
            val engine = mockEngine { respond("", HttpStatusCode.NoContent) }
            val probe = HttpProbe(SignInClients.probeHttpClient(engine), testConfig.copy(serverUrl = "not a url"))

            assertEquals(Failure.Unreachable, probe.probeSignIn())
        }
}
