package com.escendit.coldframe.core.signin

import io.ktor.client.HttpClient
import io.ktor.client.request.accept
import io.ktor.client.request.prepareGet
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.isSuccess
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** Upper bound for each probe, so SIGN IN never hangs on an unreachable host. */
public val PROBE_TIMEOUT: Duration = 5.seconds

/** The checks SIGN IN runs before handing off to Keycloak (mirrors the web `probe.ts`). */
public interface SignInProbe {
    /** Server first, then the issuer; the first failure decides the notice, `null` when both answer. */
    public suspend fun probeSignIn(): Failure?

    /**
     * After a failed browser flow or code exchange, checks the issuer again to tell an untrusted
     * certificate from any other Keycloak failure.
     */
    public suspend fun diagnose(): Failure
}

/** [SignInProbe] over Ktor. The client must not follow redirects: any response counts. */
public class HttpProbe(
    private val http: HttpClient,
    private val config: CoreConfig,
    private val timeout: Duration = PROBE_TIMEOUT,
    private val certificateError: (Throwable) -> Boolean = ::isCertificateError,
) : SignInProbe {
    override suspend fun probeSignIn(): Failure? = probeServer() ?: probeIssuer()

    override suspend fun diagnose(): Failure {
        val issuer = probeIssuer()
        return if (issuer == Failure.Certificate) Failure.Certificate else Failure.Keycloak
    }

    /** Refused, DNS, timeout → [Failure.Unreachable]; TLS → [Failure.Certificate]. */
    public suspend fun probeServer(): Failure? =
        when (val result = attempt { http.prepareGet(serverHealthUrl(config.serverUrl)).execute { it.status } }) {
            is Attempt.Answered -> null
            is Attempt.Failed -> if (certificateError(result.error)) Failure.Certificate else Failure.Unreachable
            Attempt.TimedOut -> Failure.Unreachable
        }

    /** Non-2xx, no `authorization_endpoint`, timeout or any failure but TLS → [Failure.Keycloak]. */
    public suspend fun probeIssuer(): Failure? {
        val result =
            attempt {
                http.prepareGet(config.discoveryUri) { accept(ContentType.Application.Json) }.execute { response ->
                    response.status.isSuccess() && hasAuthorizationEndpoint(response.bodyAsText())
                }
            }
        return when (result) {
            is Attempt.Answered -> if (result.value) null else Failure.Keycloak
            is Attempt.Failed -> if (certificateError(result.error)) Failure.Certificate else Failure.Keycloak
            Attempt.TimedOut -> Failure.Keycloak
        }
    }

    private fun hasAuthorizationEndpoint(body: String): Boolean =
        try {
            (Json.parseToJsonElement(body) as? JsonObject)?.containsKey("authorization_endpoint") == true
        } catch (_: IllegalArgumentException) {
            false
        }

    private sealed interface Attempt<out T> {
        data class Answered<T>(
            val value: T,
        ) : Attempt<T>

        data class Failed(
            val error: Throwable,
        ) : Attempt<Nothing>

        data object TimedOut : Attempt<Nothing>
    }

    private suspend fun <T> attempt(block: suspend () -> T): Attempt<T> =
        withTimeoutOrNull(timeout) {
            try {
                Attempt.Answered(block())
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught") error: Throwable,
            ) {
                Attempt.Failed(error)
            }
        } ?: Attempt.TimedOut
}
