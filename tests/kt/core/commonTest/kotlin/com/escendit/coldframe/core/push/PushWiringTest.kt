package com.escendit.coldframe.core.push

import com.escendit.coldframe.core.signin.FakeAuthFlow
import com.escendit.coldframe.core.signin.FakeIdentity
import com.escendit.coldframe.core.signin.FakeProbe
import com.escendit.coldframe.core.signin.FakeVault
import com.escendit.coldframe.core.signin.NOW
import com.escendit.coldframe.core.signin.SignInEngine
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.signin.fakeCertificateError
import com.escendit.coldframe.core.signin.mockEngine
import com.escendit.coldframe.core.signin.testConfig
import com.escendit.coldframe.core.signin.tokens
import com.escendit.coldframe.core.sites.SitesWiring
import com.russhwolf.settings.MapSettings
import io.ktor.client.engine.mock.respond
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.headersOf
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** Story 6.5: the push engine as the apps wire it, over the real API client and sign-in engine. */
class PushWiringTest {
    private val log = mutableListOf<String>()
    private val vault = FakeVault(tokens = tokens(), log = log)
    private var removalFails = false
    private val settings = MapSettings()
    private lateinit var signIn: SignInEngine

    private fun TestScope.wired(): PushEngine {
        signIn =
            SignInEngine(
                probe = FakeProbe(),
                authFlow = FakeAuthFlow(),
                vault = vault,
                identity = FakeIdentity(log = log),
                scope = backgroundScope,
                nowEpochSeconds = { NOW },
                certificateError = ::fakeCertificateError,
            )
        val http =
            mockEngine { request ->
                val path = request.url.encodedPath
                when {
                    path.startsWith("/me/push-registrations/") -> {
                        log += "${request.method.value} $path ${request.headers[HttpHeaders.Authorization]}"
                        if (removalFails &&
                            request.method == HttpMethod.Delete
                        ) {
                            throw IllegalStateException("no response")
                        }
                        respond("", HttpStatusCode.NoContent)
                    }

                    path == "/sites" -> {
                        respond(
                            """{"sites":[]}""",
                            HttpStatusCode.OK,
                            headersOf(HttpHeaders.ContentType, "application/json"),
                        )
                    }

                    else -> {
                        respond("", HttpStatusCode.NotFound)
                    }
                }
            }
        val api = SitesWiring.api(testConfig, http, signIn)
        val sites = SitesWiring.engine(api, signIn, settings, backgroundScope)
        val lots = SitesWiring.lots(api, sites, settings, backgroundScope, cadence = api)
        val push = SitesWiring.push(api, sites, lots, signIn, settings, backgroundScope, PushPlatform.Fcm)
        signIn.resume()
        runCurrent()
        assertEquals(SignInState.SignedIn("Simon Novak"), signIn.state.value)
        return push
    }

    private fun registration(): String = "/me/push-registrations/${settings.getStringOrNull("push.installationId")}"

    @Test
    fun uxDr115SignOutRemovesTheRegistrationBeforeTheSessionEnds() =
        runTest {
            val push = wired()
            push.tokenReceived("token-1")
            runCurrent()

            signIn.signOut()
            runCurrent()

            assertEquals(
                listOf(
                    "PUT ${registration()} Bearer access-1",
                    "DELETE ${registration()} Bearer access-1",
                    "clear",
                    "endSession",
                ),
                log,
            )
            assertEquals(SignInState.SignedOut(null), signIn.state.value)
        }

    @Test
    fun uxDr115SignOutCompletesWhenTheRemovalFails() =
        runTest {
            val push = wired()
            push.tokenReceived("token-1")
            runCurrent()
            removalFails = true

            signIn.signOut()
            runCurrent()

            assertEquals("DELETE ${registration()} Bearer access-1", log[1])
            assertEquals(SignInState.SignedOut(null), signIn.state.value)
            assertNull(vault.tokens)
        }

    @Test
    fun uxDr115TheNextSignInOnThisPhoneRegistersTheSameInstallationAgain() =
        runTest {
            val push = wired()
            push.tokenReceived("token-1")
            runCurrent()
            signIn.signOut()
            runCurrent()
            log.clear()

            signIn.signIn()
            runCurrent()

            assertEquals(SignInState.SignedIn("Simon Novak"), signIn.state.value)
            assertEquals(listOf("save", "PUT ${registration()} Bearer access-1"), log)
        }
}
