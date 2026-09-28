package com.escendit.coldframe.core.signin

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.sites.SitesWiring
import io.ktor.client.engine.mock.respond
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.headersOf
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** The API built by SitesWiring takes its token from the sign-in engine and signs out on a 401. */
class SitesWiringTest {
    @Test
    fun uxDr93AServer401ThroughTheWiredApiSignsOutAndClearsTheVault() =
        runTest {
            val vault = FakeVault(tokens = tokens())
            val engine =
                SignInEngine(
                    probe = FakeProbe(),
                    authFlow = FakeAuthFlow(),
                    vault = vault,
                    identity = FakeIdentity(),
                    scope = this,
                    nowEpochSeconds = { NOW },
                    certificateError = ::fakeCertificateError,
                )
            engine.resume()
            advanceUntilIdle()
            var authorization: String? = null
            val http =
                mockEngine { request ->
                    authorization = request.headers[HttpHeaders.Authorization]
                    respond(
                        """{"type":"urn:coldframe:problem:unauthorized","title":"t","status":401}""",
                        HttpStatusCode.Unauthorized,
                        headersOf(HttpHeaders.ContentType, "application/problem+json"),
                    )
                }

            val result = SitesWiring.api(testConfig, http, engine).listSites()

            assertEquals(ApiResult.Failed(ApiFailure.Unauthorized), result)
            assertEquals("Bearer access-1", authorization)
            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
            assertNull(vault.tokens)
        }
}
