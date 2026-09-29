package com.escendit.coldframe.core.signin

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import org.publicvalue.multiplatform.oidc.OpenIdConnectException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class SignInEngineTest {
    private val log = mutableListOf<String>()
    private val probe = FakeProbe()
    private val authFlow = FakeAuthFlow()
    private val vault = FakeVault(log = log)
    private val identity = FakeIdentity(log = log)

    private fun TestScope.engine(): SignInEngine =
        SignInEngine(
            probe = probe,
            authFlow = authFlow,
            vault = vault,
            identity = identity,
            scope = this,
            nowEpochSeconds = { NOW },
            certificateError = ::fakeCertificateError,
        )

    private fun TestScope.signedOut(): SignInEngine =
        engine().also {
            it.resume()
            advanceUntilIdle()
        }

    @Test
    fun uxDr60AColdStartWithAnEmptyStoreShowsTheSignInSurfaceWithoutANotice() =
        runTest {
            val engine = engine()
            assertEquals(SignInState.Restoring, engine.state.value)

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertEquals(0, authFlow.signIns)
        }

    @Test
    fun uxDr60SignInProbesThenAuthenticatesAndKeepsTheTokensInTheVault() =
        runTest {
            val engine = signedOut()

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            assertEquals("access-1", vault.tokens?.access_token)
            assertEquals(1, probe.probes)
            assertEquals(1, authFlow.signIns)
        }

    @Test
    fun uxDr34WorkingIgnoresASecondPress() =
        runTest {
            val engine = signedOut()
            authFlow.gate = CompletableDeferred()

            engine.signIn()
            engine.signIn()
            advanceUntilIdle()
            assertEquals(SignInState.Working, engine.state.value)
            engine.signIn()
            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.Working, engine.state.value)
            authFlow.gate?.complete(Unit)
            advanceUntilIdle()
            assertEquals(1, authFlow.signIns)
            assertEquals(1, probe.probes)
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
        }

    @Test
    fun uxDr92AnUnreachableServerShowsTryAgainAndStartsNoBrowser() =
        runTest {
            val engine = signedOut()
            probe.signIn = Failure.Unreachable

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Unreachable), engine.state.value)
            assertEquals(NoticeAction.TryAgain, Notice.Unreachable.action)
            assertEquals(0, authFlow.signIns)
        }

    @Test
    fun uxDr92AnUntrustedCertificateOnAProbeShowsTheCertificateNoticeWithNoAction() =
        runTest {
            val engine = signedOut()
            probe.signIn = Failure.Certificate

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Certificate), engine.state.value)
            assertNull(Notice.Certificate.action)
            assertEquals(0, authFlow.signIns)
        }

    @Test
    fun uxDr92AFailedExchangeReprobesTheIssuerSoACertificateFailureStillShows() =
        runTest {
            val engine = signedOut()
            authFlow.result = { throw OpenIdConnectException.TechnicalFailure("exchange failed", null) }
            probe.diagnosis = Failure.Certificate

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Certificate), engine.state.value)
            assertEquals(1, probe.diagnoses)
        }

    @Test
    fun uxDr92ATlsFailureInsideTheFlowIsTheCertificateNotice() =
        runTest {
            val engine = signedOut()
            authFlow.result = { throw OpenIdConnectException.TechnicalFailure("tls", FakeCertificateException()) }

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Certificate), engine.state.value)
        }

    @Test
    fun uxDr92AKeycloakErrorShowsTryAgainAndTryAgainRetriesDiscovery() =
        runTest {
            val engine = signedOut()
            probe.signIn = Failure.Keycloak

            engine.signIn()
            advanceUntilIdle()
            assertEquals(SignInState.SignedOut(Notice.Keycloak), engine.state.value)
            assertEquals(NoticeAction.TryAgain, Notice.Keycloak.action)

            probe.signIn = null
            engine.signIn()
            advanceUntilIdle()
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            assertEquals(2, probe.probes)
        }

    @Test
    fun uxDr92AnyOtherOidcFailureIsTheKeycloakNotice() =
        runTest {
            val failures =
                listOf(
                    OpenIdConnectException.AuthenticationFailure("access_denied"),
                    OpenIdConnectException.DiscoveryFailure("no document", null),
                    OpenIdConnectException.TechnicalFailure("boom", null),
                )
            for (failure in failures) {
                val engine = signedOut()
                authFlow.result = { throw failure }

                engine.signIn()
                advanceUntilIdle()

                assertEquals(SignInState.SignedOut(Notice.Keycloak), engine.state.value, failure.toString())
            }
        }

    @Test
    fun uxDr92CancellingReturnsToSignInWithoutANotice() =
        runTest {
            val engine = signedOut()
            authFlow.result = { throw OpenIdConnectException.AuthenticationCancelled() }

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertEquals(0, probe.diagnoses)
            assertNull(vault.tokens)
        }

    @Test
    fun aRestartWithAValidAccessTokenIsSignedInWithoutABrowser() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 100, expiresIn = 300)
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            assertEquals(0, authFlow.signIns)
            assertEquals(0, identity.refreshes)
        }

    @Test
    fun anExpiredAccessTokenIsRefreshedAndTheNewTokensKeepTheSession() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 1000, expiresIn = 300)
            identity.outcome = { RefreshOutcome.Refreshed(tokens(access = "access-2", name = "Simon")) }
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedIn("Simon"), engine.state.value)
            assertEquals(1, identity.refreshes)
        }

    @Test
    fun aTokenInsideTheExpiryToleranceIsRefreshed() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 250, expiresIn = 300)
            identity.outcome = { RefreshOutcome.Refreshed(tokens(access = "access-2")) }
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(1, identity.refreshes)
        }

    @Test
    fun uxDr93ARejectedRefreshClearsTheStoreAndShowsTheSignedOutNotice() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 1000)
            identity.outcome = { RefreshOutcome.Rejected }
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
            assertEquals(NoticeAction.SignIn, Notice.SignedOut.action)
            assertNull(vault.tokens)
        }

    @Test
    fun uxDr93ASessionThatEndsWhileInTheForegroundShowsTheSignedOutNotice() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 100)
            val engine = engine()
            engine.resume()
            advanceUntilIdle()
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)

            vault.tokens = tokens(receivedAt = NOW - 1000)
            identity.outcome = { RefreshOutcome.Rejected }
            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
        }

    @Test
    fun uxDr93SignInFromTheSignedOutNoticeStartsANewSignIn() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 1000)
            identity.outcome = { RefreshOutcome.Rejected }
            val engine = engine()
            engine.resume()
            advanceUntilIdle()

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
        }

    @Test
    fun aTransientRefreshFailureKeepsTheSessionAndRetriesOnTheNextForeground() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW - 1000)
            identity.outcome = { RefreshOutcome.Transient }
            val engine = engine()

            engine.resume()
            advanceUntilIdle()
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            assertEquals("access-1", vault.tokens?.access_token)

            engine.resume()
            advanceUntilIdle()
            assertEquals(2, identity.refreshes)
        }

    @Test
    fun uxDr113SignOutClearsTheStoreBeforeEndingTheSessionAndShowsNoNotice() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW)
            val engine = engine()
            engine.resume()
            advanceUntilIdle()

            engine.signOut()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertNull(vault.tokens)
            assertEquals(listOf("clear", "endSession"), log)
        }

    @Test
    fun uxDr113AFailedEndSessionIsIgnored() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW)
            identity.endSessionFails = true
            val engine = engine()
            engine.resume()
            advanceUntilIdle()

            engine.signOut()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertNull(vault.tokens)
        }

    @Test
    fun aSignInInterruptedByProcessDeathContinuesOnTheNextStart() =
        runTest {
            authFlow.canContinueResult = true
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
            assertEquals(1, authFlow.continues)
            assertEquals(0, authFlow.signIns)
            assertEquals("access-1", vault.tokens?.access_token)
        }

    @Test
    fun anUnreadableStoreOnStartIsTreatedAsNoTokens() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW)
            vault.readFails = true
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertEquals(listOf("clear"), log)
        }

    @Test
    fun anUnreadableStoreThatCannotBeClearedStillShowsSignIn() =
        runTest {
            vault.readFails = true
            vault.clearFails = true
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
        }

    @Test
    fun aFailingContinueCheckOnStartIsTreatedAsNothingToContinue() =
        runTest {
            authFlow.canContinueFails = true
            val engine = engine()

            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
            assertEquals(0, authFlow.continues)
        }

    @Test
    fun uxDr92TokensThatCannotBeSavedShowTheKeycloakNotice() =
        runTest {
            val engine = signedOut()
            vault.saveFails = true

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Keycloak), engine.state.value)
        }

    @Test
    fun uxDr92AFailedDiagnosisIsTheKeycloakNotice() =
        runTest {
            val engine = signedOut()
            authFlow.result = { throw OpenIdConnectException.TechnicalFailure("exchange failed", null) }
            probe.diagnoseFails = true

            engine.signIn()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.Keycloak), engine.state.value)
        }

    @Test
    fun uxDr113SignOutWithAStoreThatFailsStillShowsSignIn() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW)
            val engine = engine()
            engine.resume()
            advanceUntilIdle()
            vault.readFails = true
            vault.clearFails = true

            engine.signOut()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
        }

    @Test
    fun uxDr34SignInPressedWhileResumeReadsTheStoreIsNotUndoneByResume() =
        runTest {
            val engine = signedOut()
            vault.readGate = CompletableDeferred()
            authFlow.gate = CompletableDeferred()

            engine.resume()
            advanceUntilIdle()
            engine.signIn()
            vault.readGate?.complete(Unit)
            advanceUntilIdle()

            assertEquals(SignInState.Working, engine.state.value)
            engine.signIn()
            advanceUntilIdle()
            authFlow.gate?.complete(Unit)
            advanceUntilIdle()
            assertEquals(1, authFlow.signIns)
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)
        }

    @Test
    fun uxDr93AStoreEmptiedWhileSignedInShowsTheSignedOutNotice() =
        runTest {
            vault.tokens = tokens(receivedAt = NOW)
            val engine = engine()
            engine.resume()
            advanceUntilIdle()
            assertEquals(SignInState.SignedIn("Simon Novak"), engine.state.value)

            vault.tokens = null
            engine.resume()
            advanceUntilIdle()

            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
        }

    @Test
    fun theDisplayNameFallsBackToThePreferredUsernameAndIsAbsentWithoutAnIdToken() {
        val preferred = tokens().copy(id_token = idToken("""{"sub":"u1","preferred_username":"simon"}"""))

        assertEquals("simon", SignInEngine.displayNameOf(preferred))
        assertNull(SignInEngine.displayNameOf(tokens(name = null)))
        assertNull(SignInEngine.displayNameOf(tokens().copy(id_token = "not-a-jwt")))
    }
}
