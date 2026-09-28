package com.escendit.coldframe.core.api

import com.escendit.coldframe.core.signin.isCertificateError
import com.escendit.coldframe.core.sites.SitesApi
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.HttpClientEngine
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.HttpRequestBuilder
import io.ktor.client.request.bearerAuth
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
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
) : SitesApi {
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
