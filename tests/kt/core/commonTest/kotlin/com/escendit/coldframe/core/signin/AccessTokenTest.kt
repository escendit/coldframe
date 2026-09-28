package com.escendit.coldframe.core.signin

import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** The token access the API client uses (Story 1.8). */
class AccessTokenTest {
    private val log = mutableListOf<String>()
    private val vault = FakeVault(log = log)
    private val identity = FakeIdentity(log = log)

    private fun TestScope.signedIn(): SignInEngine =
        SignInEngine(
            probe = FakeProbe(),
            authFlow = FakeAuthFlow(),
            vault = vault,
            identity = identity,
            scope = this,
            nowEpochSeconds = { NOW },
            certificateError = ::fakeCertificateError,
        ).also {
            vault.tokens = tokens()
            it.resume()
            advanceUntilIdle()
        }

    @Test
    fun aValidTokenIsReturnedWithoutARefresh() =
        runTest {
            val engine = signedIn()

            assertEquals("access-1", engine.accessToken())
            assertEquals(0, identity.refreshes)
        }

    @Test
    fun anExpiredTokenIsRefreshedFirst() =
        runTest {
            val engine = signedIn()
            vault.tokens = tokens(receivedAt = NOW - 1_000)
            identity.outcome = { RefreshOutcome.Refreshed(tokens(access = "access-2")) }

            assertEquals("access-2", engine.accessToken())
            assertEquals(1, identity.refreshes)
        }

    @Test
    fun aRejectedRefreshSignsOutWithTheSignedOutNotice() =
        runTest {
            val engine = signedIn()
            vault.tokens = tokens(receivedAt = NOW - 1_000)
            identity.outcome = { RefreshOutcome.Rejected }

            assertNull(engine.accessToken())
            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
            assertNull(vault.tokens)
        }

    @Test
    fun aTransientRefreshFailureKeepsTheOldToken() =
        runTest {
            val engine = signedIn()
            vault.tokens = tokens(receivedAt = NOW - 1_000)

            assertEquals("access-1", engine.accessToken())
        }

    @Test
    fun signedOutHasNoToken() =
        runTest {
            val engine = signedIn()
            engine.signOut()
            advanceUntilIdle()

            assertNull(engine.accessToken())
        }

    @Test
    fun uxDr93AServer401SignsOutWithTheSignedOutNotice() =
        runTest {
            val engine = signedIn()

            engine.signOutExpired()

            assertEquals(SignInState.SignedOut(Notice.SignedOut), engine.state.value)
            assertNull(vault.tokens)
        }

    @Test
    fun a401AfterSignOutKeepsTheSurfaceWithoutANotice() =
        runTest {
            val engine = signedIn()
            engine.signOut()
            advanceUntilIdle()

            engine.signOutExpired()

            assertEquals(SignInState.SignedOut(null), engine.state.value)
        }
}
