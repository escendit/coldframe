package com.escendit.coldframe.core.api

import com.escendit.coldframe.core.devices.DevicesApi
import com.escendit.coldframe.core.lots.LotDetailApi
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.setup.EnrolmentApi
import com.escendit.coldframe.core.signin.isCertificateError
import com.escendit.coldframe.core.sites.SitesApi
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.HttpClientEngine
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.HttpRequestBuilder
import io.ktor.client.request.bearerAuth
import io.ktor.client.request.delete
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.parameter
import io.ktor.client.request.patch
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.http.encodeURLPathPart
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * The Server's REST API for the mobile apps. Only the core calls it; the shells never see a URL
 * or a token (AD-14).
 *
 * [accessToken] returns the session's access token, refreshed when expired, or `null` when signed
 * out. [onUnauthorized] runs on a 401 and ends the session with the SignedOut notice.
 */
public class ColdframeApi(
    private val http: HttpClient,
    serverUrl: String,
    private val accessToken: suspend () -> String?,
    private val onUnauthorized: suspend () -> Unit,
    private val certificateError: (Throwable) -> Boolean = ::isCertificateError,
) : SitesApi,
    LotsApi,
    LotDetailApi,
    DevicesApi,
    EnrolmentApi {
    private val base = serverUrl.trimEnd('/')

    /** `GET /sites` (`listSites`). */
    override suspend fun listSites(): ApiResult<SiteListDto> =
        call({ http.get("$base/sites") { it() } }) { it.body<SiteListDto>() }

    /** `POST /sites` (`createSite`) with the attempt's [idempotencyKey]. */
    override suspend fun createSite(
        name: String,
        idempotencyKey: String,
    ): ApiResult<SiteDto> =
        call({
            http.post("$base/sites") {
                it()
                header(IDEMPOTENCY_KEY, idempotencyKey)
                contentType(ContentType.Application.Json)
                setBody(CreateSiteRequestDto(name))
            }
        }) { it.body<SiteDto>() }

    /** `PATCH /sites/{siteId}` (`renameSite`, Owner). */
    override suspend fun renameSite(
        siteId: String,
        name: String,
    ): ApiResult<SiteDto> =
        call({
            http.patch("$base/sites/${siteId.encoded()}") {
                it()
                contentType(ContentType.Application.Json)
                setBody(RenameRequestDto(name))
            }
        }) { it.body<SiteDto>() }

    /** `GET /sites/{siteId}/lots` (`listLots`): live Lots in the Server's order. */
    override suspend fun listLots(siteId: String): ApiResult<LotListDto> =
        call({ http.get(lots(siteId)) { it() } }) { it.body<LotListDto>() }

    /** `POST /sites/{siteId}/lots` (`createLot`) with the attempt's [idempotencyKey]. */
    override suspend fun createLot(
        siteId: String,
        name: String,
        idempotencyKey: String,
    ): ApiResult<LotDto> =
        call({
            http.post(lots(siteId)) {
                it()
                header(IDEMPOTENCY_KEY, idempotencyKey)
                contentType(ContentType.Application.Json)
                setBody(CreateLotRequestDto(name))
            }
        }) { it.body<LotDto>() }

    /** `GET /sites/{siteId}/lots/{lotId}` (`getLot`): with a Node, its `node` and `sensors` too. */
    override suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto> = call({ http.get("${lots(siteId)}/${lotId.encoded()}") { it() } }) { it.body<LotDto>() }

    /** `GET /sites/{siteId}/lots/{lotId}/history` (`getLotHistory`): one page of daily history. */
    override suspend fun getLotHistory(
        siteId: String,
        lotId: String,
        quantity: String,
        cursor: String?,
    ): ApiResult<LotHistoryDto> =
        call({
            http.get("${lots(siteId)}/${lotId.encoded()}/history") {
                it()
                parameter("quantity", quantity)
                if (cursor != null) parameter("cursor", cursor)
            }
        }) { it.body<LotHistoryDto>() }

    /** `PATCH /sites/{siteId}/lots/{lotId}` (`renameLot`). */
    override suspend fun renameLot(
        siteId: String,
        lotId: String,
        name: String,
    ): ApiResult<LotDto> =
        call({
            http.patch("${lots(siteId)}/${lotId.encoded()}") {
                it()
                contentType(ContentType.Application.Json)
                setBody(RenameRequestDto(name))
            }
        }) { it.body<LotDto>() }

    /** `DELETE /sites/{siteId}/lots/{lotId}` (`removeLot`): 204 without a body. */
    override suspend fun removeLot(
        siteId: String,
        lotId: String,
    ): ApiResult<Unit> = call({ http.delete("${lots(siteId)}/${lotId.encoded()}") { it() } }) { }

    /** `GET /enrolment-key` (`getEnrolmentKey`): the Server's X25519 enrolment key and fingerprint. */
    override suspend fun enrolmentKey(): ApiResult<EnrolmentKeyDto> =
        call({ http.get("$base/enrolment-key") { it() } }) { it.body<EnrolmentKeyDto>() }

    /** `GET /sites/{siteId}/devices` (`listDevices`, Member): every enrolled Device, with the Server's `online`. */
    override suspend fun listDevices(siteId: String): ApiResult<DeviceListDto> =
        call({ http.get("$base/sites/${siteId.encoded()}/devices") { it() } }) { it.body<DeviceListDto>() }

    /** `POST /sites/{siteId}/devices` (`enrolDevice`, Admin+) with the attempt's [idempotencyKey]. */
    override suspend fun enrolDevice(
        siteId: String,
        request: EnrolDeviceRequestDto,
        idempotencyKey: String,
    ): ApiResult<DeviceDto> =
        call({
            http.post("$base/sites/${siteId.encoded()}/devices") {
                it()
                header(IDEMPOTENCY_KEY, idempotencyKey)
                contentType(ContentType.Application.Json)
                setBody(request)
            }
        }) { it.body<DeviceDto>() }

    /** `POST /sites/{siteId}/devices/{deviceId}/move` (`moveDevice`, Admin+): a Lot that has a Node is 409 `lot-claimed`. */
    override suspend fun moveDevice(
        siteId: String,
        deviceId: String,
        lotId: String,
    ): ApiResult<DeviceDto> =
        call({
            http.post("$base/sites/${siteId.encoded()}/devices/${deviceId.encoded()}/move") {
                it()
                contentType(ContentType.Application.Json)
                setBody(MoveDeviceRequestDto(lotId))
            }
        }) { it.body<DeviceDto>() }

    /** `POST /sites/{siteId}/devices/{deviceId}/unassign` (`unassignDevice`, Admin+). */
    override suspend fun unassignDevice(
        siteId: String,
        deviceId: String,
    ): ApiResult<DeviceDto> =
        call({ http.post("$base/sites/${siteId.encoded()}/devices/${deviceId.encoded()}/unassign") { it() } }) {
            it.body<DeviceDto>()
        }

    private fun lots(siteId: String): String = "$base/sites/${siteId.encoded()}/lots"

    private fun String.encoded(): String = encodeURLPathPart()

    private suspend fun <T> call(
        send: suspend (HttpRequestBuilder.() -> Unit) -> HttpResponse,
        read: suspend (HttpResponse) -> T,
    ): ApiResult<T> {
        val token = accessToken() ?: return ApiResult.Failed(ApiFailure.Unauthorized)
        val response =
            try {
                send { bearerAuth(token) }
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught") error: Throwable,
            ) {
                return ApiResult.Failed(if (certificateError(error)) ApiFailure.Certificate else ApiFailure.Unreachable)
            }
        if (response.status.isSuccess()) {
            return try {
                ApiResult.Ok(read(response))
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught", "SwallowedException") unreadable: Throwable,
            ) {
                ApiResult.Failed(ApiFailure.Unexpected)
            }
        }
        return ApiResult.Failed(failureOf(response))
    }

    private suspend fun failureOf(response: HttpResponse): ApiFailure =
        when (response.status) {
            HttpStatusCode.Unauthorized -> {
                onUnauthorized()
                ApiFailure.Unauthorized
            }

            HttpStatusCode.BadRequest -> {
                if (problemType(response) == PROBLEM_VALIDATION) ApiFailure.Validation else ApiFailure.Unexpected
            }

            HttpStatusCode.Forbidden -> {
                ApiFailure.Forbidden
            }

            HttpStatusCode.NotFound -> {
                ApiFailure.NotFound
            }

            HttpStatusCode.Conflict -> {
                when (problemType(response)) {
                    PROBLEM_DEVICE_ON_ANOTHER_SITE -> ApiFailure.DeviceOnAnotherSite
                    PROBLEM_DEVICE_ASSIGNED -> ApiFailure.DeviceAssigned
                    else -> ApiFailure.LotClaimed
                }
            }

            HttpStatusCode.UnprocessableEntity -> {
                ApiFailure.KeyReused
            }

            HttpStatusCode.ServiceUnavailable -> {
                ApiFailure.IdentityProviderUnavailable
            }

            else -> {
                ApiFailure.Unexpected
            }
        }

    private suspend fun problemType(response: HttpResponse): String? =
        try {
            JSON.decodeFromString(ProblemDto.serializer(), response.bodyAsText()).type
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (
            @Suppress("SwallowedException") unreadable: SerializationException,
        ) {
            null
        } catch (
            @Suppress("SwallowedException") unreadable: IllegalArgumentException,
        ) {
            null
        }

    public companion object {
        /** Bound for every Server call, as for the OIDC calls. */
        public val TIMEOUT: Duration = 30.seconds

        public const val IDEMPOTENCY_KEY: String = "Idempotency-Key"
        public const val PROBLEM_VALIDATION: String = "urn:coldframe:problem:validation"
        public const val PROBLEM_DEVICE_ON_ANOTHER_SITE: String = "urn:coldframe:problem:device-on-another-site"
        public const val PROBLEM_DEVICE_ASSIGNED: String = "urn:coldframe:problem:device-assigned"

        private val JSON =
            Json {
                ignoreUnknownKeys = true
                explicitNulls = false
            }

        /** The API's HTTP client: JSON, 30 s bound, non-2xx answers are values. */
        public fun httpClient(engine: HttpClientEngine): HttpClient =
            HttpClient(engine) {
                expectSuccess = false
                install(HttpTimeout) {
                    requestTimeoutMillis = TIMEOUT.inWholeMilliseconds
                    connectTimeoutMillis = TIMEOUT.inWholeMilliseconds
                }
                install(ContentNegotiation) { json(JSON) }
            }
    }
}
