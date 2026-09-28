package com.escendit.coldframe.core.api

import com.escendit.coldframe.core.signin.FakeCertificateException
import com.escendit.coldframe.core.signin.fakeCertificateError
import com.escendit.coldframe.core.signin.mockEngine
import io.ktor.client.engine.mock.MockRequestHandleScope
import io.ktor.client.engine.mock.respond
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.content.OutgoingContent
import io.ktor.http.headersOf
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals

class ColdframeApiTest {
    private val requests = mutableListOf<HttpRequestData>()
    private var token: String? = "access-1"
    private var unauthorized = 0
    private var answer: MockRequestHandleScope.(
        HttpRequestData,
    ) -> HttpResponseData = { respond("", HttpStatusCode.NotFound) }

    private val json = headersOf(HttpHeaders.ContentType, "application/json")
    private val problem = headersOf(HttpHeaders.ContentType, "application/problem+json")

    private fun TestScope.api(): ColdframeApi =
        ColdframeApi(
            http =
                ColdframeApi.httpClient(
                    mockEngine { request ->
                        requests += request
                        answer(request)
                    },
                ),
            serverUrl = "https://server.example/",
            accessToken = { token },
            onUnauthorized = { unauthorized++ },
            certificateError = ::fakeCertificateError,
        )

    private fun problem(type: String): String = """{"type":"urn:coldframe:problem:$type","title":"t","status":400}"""

    @Test
    fun listSitesSendsTheBearerTokenAndReadsTheSitesInTheServerOrder() =
        runTest {
            answer = {
                respond(
                    """{"sites":[{"id":"b","name":"Allotment","role":"Member"},{"id":"a","name":"Home","role":"Owner"}]}""",
                    HttpStatusCode.OK,
                    json,
                )
            }

            val result = api().listSites()

            assertEquals(
                ApiResult.Ok(SiteListDto(listOf(SiteDto("b", "Allotment", "Member"), SiteDto("a", "Home", "Owner")))),
                result,
            )
            val request = requests.single()
            assertEquals(HttpMethod.Get, request.method)
            assertEquals("https://server.example/sites", request.url.toString())
            assertEquals("Bearer access-1", request.headers[HttpHeaders.Authorization])
        }

    @Test
    fun uxDr61CreateSiteSendsTheIdempotencyKeyAndOnlyTheName() =
        runTest {
            answer = { respond("""{"id":"a","name":"Home","role":"Owner"}""", HttpStatusCode.Created, json) }

            val result = api().createSite("Home", "key-1")

            assertEquals(ApiResult.Ok(SiteDto("a", "Home", "Owner")), result)
            val request = requests.single()
            assertEquals(HttpMethod.Post, request.method)
            assertEquals("https://server.example/sites", request.url.toString())
            assertEquals("key-1", request.headers[ColdframeApi.IDEMPOTENCY_KEY])
            assertEquals("Bearer access-1", request.headers[HttpHeaders.Authorization])
            assertEquals(
                """{"name":"Home"}""",
                (request.body as OutgoingContent.ByteArrayContent).bytes().decodeToString(),
            )
        }

    @Test
    fun aValidationProblemIsValidation() =
        runTest {
            answer = { respond(problem("validation"), HttpStatusCode.BadRequest, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.Validation), api().createSite(" ", "k"))
        }

    @Test
    fun anotherBadRequestIsUnexpected() =
        runTest {
            answer = { respond(problem("idempotency-key-missing"), HttpStatusCode.BadRequest, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.Unexpected), api().createSite("Home", "k"))
        }

    @Test
    fun aBadRequestWithoutProblemDetailsIsUnexpected() =
        runTest {
            answer = { respond("nope", HttpStatusCode.BadRequest) }

            assertEquals(ApiResult.Failed(ApiFailure.Unexpected), api().createSite("Home", "k"))
        }

    @Test
    fun a422IsKeyReused() =
        runTest {
            answer = { respond(problem("idempotency-key-reused"), HttpStatusCode.UnprocessableEntity, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.KeyReused), api().createSite("Home", "k"))
        }

    @Test
    fun a503IsIdentityProviderUnavailable() =
        runTest {
            answer = { respond(problem("identity-provider-unavailable"), HttpStatusCode.ServiceUnavailable, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.IdentityProviderUnavailable), api().createSite("Home", "k"))
        }

    @Test
    fun a401EndsTheSession() =
        runTest {
            answer = { respond(problem("unauthorized"), HttpStatusCode.Unauthorized, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().listSites())
            assertEquals(1, unauthorized)
        }

    @Test
    fun noSessionSendsNothing() =
        runTest {
            token = null

            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().listSites())
            assertEquals(0, requests.size)
            assertEquals(0, unauthorized)
        }

    @Test
    fun noResponseIsUnreachable() =
        runTest {
            answer = { throw IllegalStateException("connection refused") }

            assertEquals(ApiResult.Failed(ApiFailure.Unreachable), api().listSites())
        }

    @Test
    fun aCertificateFailureIsCertificate() =
        runTest {
            answer = { throw IllegalStateException("tls", FakeCertificateException()) }

            assertEquals(ApiResult.Failed(ApiFailure.Certificate), api().createSite("Home", "k"))
        }

    @Test
    fun anotherStatusIsUnexpected() =
        runTest {
            answer = { respond("", HttpStatusCode.InternalServerError) }

            assertEquals(ApiResult.Failed(ApiFailure.Unexpected), api().listSites())
        }

    @Test
    fun anUnreadableSuccessIsUnexpected() =
        runTest {
            answer = { respond("""{"sites":"no"}""", HttpStatusCode.OK, json) }

            assertEquals(ApiResult.Failed(ApiFailure.Unexpected), api().listSites())
        }
}
