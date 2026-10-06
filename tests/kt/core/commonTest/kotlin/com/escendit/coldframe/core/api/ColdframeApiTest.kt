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
    fun a401OnASiteSettingsCallEndsTheSession() =
        runTest {
            answer = { respond(problem("unauthorized"), HttpStatusCode.Unauthorized, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().renameSite("s", "Home garden"))
            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().listLots("s"))
            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().createLot("s", "Beans", "k"))
            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().renameLot("s", "l", "Peppers"))
            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), api().removeLot("s", "l"))
            assertEquals(5, unauthorized)
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

    private fun HttpRequestData.text(): String = (body as OutgoingContent.ByteArrayContent).bytes().decodeToString()

    @Test
    fun uxDr74RenameSiteIsAPatchOfTheSiteWithTheName() =
        runTest {
            answer = { respond("""{"id":"a","name":"Home garden","role":"Owner"}""", HttpStatusCode.OK, json) }

            val result = api().renameSite("a", "Home garden")

            assertEquals(ApiResult.Ok(SiteDto("a", "Home garden", "Owner")), result)
            val request = requests.single()
            assertEquals(HttpMethod.Patch, request.method)
            assertEquals("https://server.example/sites/a", request.url.toString())
            assertEquals("Bearer access-1", request.headers[HttpHeaders.Authorization])
            assertEquals("""{"name":"Home garden"}""", request.text())
        }

    @Test
    fun uxDr20ListLotsKeepsTheServerOrderAndStatus() =
        runTest {
            answer = {
                respond(
                    """{"lots":[{"id":"l2","name":"Beans","status":"unknown"},{"id":"l1","name":"Tomatoes","status":"noNode"}]}""",
                    HttpStatusCode.OK,
                    json,
                )
            }

            val result = api().listLots("a")

            assertEquals(
                ApiResult.Ok(LotListDto(listOf(LotDto("l2", "Beans", "unknown"), LotDto("l1", "Tomatoes", "noNode")))),
                result,
            )
            assertEquals(HttpMethod.Get, requests.single().method)
            assertEquals("https://server.example/sites/a/lots", requests.single().url.toString())
        }

    @Test
    fun uxDr65ListDevicesReadsEveryDeviceWithTheServersOnlineFlag() =
        runTest {
            answer = {
                respond(
                    """{"devices":[""" +
                        """{"id":"1b00aa11bb22cc33","kind":"hub",""" +
                        """"lastSeenAt":"2026-10-06T07:02:00.000Z","online":true},""" +
                        """{"id":"3f2a9c0d1e4b5a67","kind":"hub","online":false},""" +
                        """{"id":"7c19000000000001","kind":"node","lotId":"l1","online":false,"later":1}]}""",
                    HttpStatusCode.OK,
                    json,
                )
            }

            val result = api().listDevices("a")

            assertEquals(
                ApiResult.Ok(
                    DeviceListDto(
                        listOf(
                            DeviceListItemDto("1b00aa11bb22cc33", "hub", true, lastSeenAt = "2026-10-06T07:02:00.000Z"),
                            DeviceListItemDto("3f2a9c0d1e4b5a67", "hub", false),
                            DeviceListItemDto("7c19000000000001", "node", false, lotId = "l1"),
                        ),
                    ),
                ),
                result,
            )
            val request = requests.single()
            assertEquals(HttpMethod.Get, request.method)
            assertEquals("https://server.example/sites/a/devices", request.url.toString())
            assertEquals("Bearer access-1", request.headers[HttpHeaders.Authorization])
        }

    @Test
    fun uxDr84ListDevicesWithoutARoleIsForbiddenAndAnUnknownSiteIsNotFound() =
        runTest {
            answer = { respond(problem("forbidden"), HttpStatusCode.Forbidden, problem) }
            assertEquals(ApiResult.Failed(ApiFailure.Forbidden), api().listDevices("a"))

            answer = { respond(problem("site-not-found"), HttpStatusCode.NotFound, problem) }
            assertEquals(ApiResult.Failed(ApiFailure.NotFound), api().listDevices("a"))
        }

    @Test
    fun uxDr74CreateLotSendsTheIdempotencyKeyAndTheName() =
        runTest {
            answer = { respond("""{"id":"l1","name":"Tomatoes","status":"noNode"}""", HttpStatusCode.Created, json) }

            val result = api().createLot("a", "Tomatoes", "key-1")

            assertEquals(ApiResult.Ok(LotDto("l1", "Tomatoes", "noNode")), result)
            val request = requests.single()
            assertEquals(HttpMethod.Post, request.method)
            assertEquals("https://server.example/sites/a/lots", request.url.toString())
            assertEquals("key-1", request.headers[ColdframeApi.IDEMPOTENCY_KEY])
            assertEquals("""{"name":"Tomatoes"}""", request.text())
        }

    @Test
    fun uxDr74RenameLotIsAPatchOfTheLot() =
        runTest {
            answer = { respond("""{"id":"l1","name":"Peppers","status":"noNode"}""", HttpStatusCode.OK, json) }

            val result = api().renameLot("a", "l1", "Peppers")

            assertEquals(ApiResult.Ok(LotDto("l1", "Peppers", "noNode")), result)
            val request = requests.single()
            assertEquals(HttpMethod.Patch, request.method)
            assertEquals("https://server.example/sites/a/lots/l1", request.url.toString())
            assertEquals("""{"name":"Peppers"}""", request.text())
        }

    @Test
    fun uxDr74RemoveLotIsADeleteAnswered204() =
        runTest {
            answer = { respond("", HttpStatusCode.NoContent) }

            val result = api().removeLot("a", "l1")

            assertEquals(ApiResult.Ok(Unit), result)
            assertEquals(HttpMethod.Delete, requests.single().method)
            assertEquals("https://server.example/sites/a/lots/l1", requests.single().url.toString())
        }

    @Test
    fun uxDr84A403IsForbidden() =
        runTest {
            answer = { respond(problem("forbidden"), HttpStatusCode.Forbidden, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.Forbidden), api().renameSite("a", "Home garden"))
        }

    @Test
    fun a404IsNotFound() =
        runTest {
            answer = { respond(problem("lot-not-found"), HttpStatusCode.NotFound, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.NotFound), api().renameLot("a", "l1", "Peppers"))
        }

    @Test
    fun a409IsLotClaimed() =
        runTest {
            answer = { respond(problem("lot-claimed"), HttpStatusCode.Conflict, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.LotClaimed), api().removeLot("a", "l1"))
        }

    @Test
    fun aRenameSite503IsIdentityProviderUnavailable() =
        runTest {
            answer = { respond(problem("identity-provider-unavailable"), HttpStatusCode.ServiceUnavailable, problem) }

            assertEquals(ApiResult.Failed(ApiFailure.IdentityProviderUnavailable), api().renameSite("a", "Home garden"))
        }

    @Test
    fun uxDr66EnrolmentKeyIsAGetOfTheKeyAndFingerprint() =
        runTest {
            answer =
                {
                    respond(
                        """{"publicKey":"${"A".repeat(43)}","fingerprint":"${"0".repeat(64)}"}""",
                        HttpStatusCode.OK,
                        json,
                    )
                }

            val result = api().enrolmentKey()

            assertEquals(ApiResult.Ok(EnrolmentKeyDto("A".repeat(43), "0".repeat(64))), result)
            assertEquals(HttpMethod.Get, requests.single().method)
            assertEquals("https://server.example/enrolment-key", requests.single().url.toString())
        }

    @Test
    fun uxDr66EnrolDeviceRelaysTheSealedKeyWithTheIdempotencyKey() =
        runTest {
            answer =
                { respond("""{"id":"3f2a9c01b2d4e6f8","kind":"hub","siteId":"a"}""", HttpStatusCode.Created, json) }
            val request = EnrolDeviceRequestDto("3f2a9c01b2d4e6f8", "hub", "e".repeat(43), "c".repeat(64))

            val result = api().enrolDevice("a", request, "key-1")

            assertEquals(ApiResult.Ok(DeviceDto("3f2a9c01b2d4e6f8", "hub", "a")), result)
            val sent = requests.single()
            assertEquals(HttpMethod.Post, sent.method)
            assertEquals("https://server.example/sites/a/devices", sent.url.toString())
            assertEquals("key-1", sent.headers[ColdframeApi.IDEMPOTENCY_KEY])
            assertEquals(
                """{"deviceId":"3f2a9c01b2d4e6f8","kind":"hub","enc":"${"e".repeat(
                    43,
                )}","ciphertext":"${"c".repeat(64)}"}""",
                sent.text(),
            )
        }

    @Test
    fun uxDr95EnrolmentProblemsMapToTheirFailures() =
        runTest {
            val request = EnrolDeviceRequestDto("3f2a9c01b2d4e6f8", "hub", "e", "c")
            val cases =
                listOf(
                    Triple(HttpStatusCode.Conflict, "device-on-another-site", ApiFailure.DeviceOnAnotherSite),
                    Triple(HttpStatusCode.Forbidden, "forbidden", ApiFailure.Forbidden),
                    Triple(HttpStatusCode.NotFound, "site-not-found", ApiFailure.NotFound),
                    Triple(HttpStatusCode.UnprocessableEntity, "idempotency-key-reused", ApiFailure.KeyReused),
                    Triple(HttpStatusCode.InternalServerError, "internal", ApiFailure.Unexpected),
                )
            for ((status, type, failure) in cases) {
                answer = { respond(problem(type), status, problem) }
                assertEquals(ApiResult.Failed(failure), api().enrolDevice("a", request, "key-1"), type)
            }
        }

    @Test
    fun uxDr67EnrolANodeSendsTheLotAndReadsItBack() =
        runTest {
            answer =
                {
                    respond(
                        """{"id":"7c19aa01b2d4e6f8","kind":"node","siteId":"a","lotId":"lot-1"}""",
                        HttpStatusCode.Created,
                        json,
                    )
                }
            val request = EnrolDeviceRequestDto("7c19aa01b2d4e6f8", "node", "e", "c", lotId = "lot-1")

            val result = api().enrolDevice("a", request, "key-1")

            assertEquals(ApiResult.Ok(DeviceDto("7c19aa01b2d4e6f8", "node", "a", "lot-1")), result)
            assertEquals(
                """{"deviceId":"7c19aa01b2d4e6f8","kind":"node","enc":"e","ciphertext":"c","lotId":"lot-1"}""",
                requests.single().text(),
            )
        }

    @Test
    fun uxDr94ANodeAssignedToAnotherLotIsNotReadAsALotClaimed() =
        runTest {
            val request = EnrolDeviceRequestDto("7c19aa01b2d4e6f8", "node", "e", "c", lotId = "lot-1")
            val cases =
                listOf(
                    "device-assigned" to ApiFailure.DeviceAssigned,
                    "lot-claimed" to ApiFailure.LotClaimed,
                    "device-on-another-site" to ApiFailure.DeviceOnAnotherSite,
                )
            for ((type, failure) in cases) {
                answer = { respond(problem(type), HttpStatusCode.Conflict, problem) }
                assertEquals(ApiResult.Failed(failure), api().enrolDevice("a", request, "key-1"), type)
            }
        }
}
