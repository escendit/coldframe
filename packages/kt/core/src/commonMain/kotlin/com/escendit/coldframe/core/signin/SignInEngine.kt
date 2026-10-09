package com.escendit.coldframe.core.signin

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import org.publicvalue.multiplatform.oidc.OpenIdConnectException
import org.publicvalue.multiplatform.oidc.types.parseJwt
import org.publicvalue.multiplatform.oidc.types.remote.AccessTokenResponse
import kotlin.time.Clock
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * Sign-in and session, the only place that knows about probes, OIDC and tokens (AD-14).
 *
 * The shells observe [state], call [signIn] for SIGN IN and Try again, [resume] on start and on
 * every return to the foreground, and [signOut] after the user confirms.
 */
public class SignInEngine(
    private val probe: SignInProbe,
    private val authFlow: AuthFlow,
    private val vault: TokenVault,
    private val identity: IdentityProvider,
    private val scope: CoroutineScope,
    private val nowEpochSeconds: () -> Long = { Clock.System.now().epochSeconds },
    private val endSessionTimeout: Duration = PROBE_TIMEOUT,
    private val certificateError: (Throwable) -> Boolean = ::isCertificateError,
) {
    private val mutableState = MutableStateFlow<SignInState>(SignInState.Restoring)

    /** One observable state for the shells. */
    public val state: StateFlow<SignInState> = mutableState.asStateFlow()

    /** Serialises resume, sign-in and sign-out so they never interleave. */
    private val mutex = Mutex()

    private val signingOut = mutableListOf<suspend () -> Unit>()

    /**
     * Runs [action] at the start of every deliberate [signOut], while the session is still valid, so it can still
     * call the Server as this User: the push engine removes this device's registration with it (Story 6.5). It is
     * best effort: each action is held to the end-session bound, and one that fails or runs out of time does not
     * keep the user signed in. A session the Server ended (a 401) runs none.
     */
    internal fun onSigningOut(action: suspend () -> Unit) {
        signingOut += action
    }

    /**
     * SIGN IN or Try again. Ignored unless the Sign-in surface shows: a second press while
     * [SignInState.Working] does nothing.
     */
    public fun signIn() {
        val current = mutableState.value
        if (current !is SignInState.SignedOut || !mutableState.compareAndSet(current, SignInState.Working)) return
        scope.launch { mutex.withLock { runSignIn() } }
    }

    /** On start and on return to the foreground. Does nothing while a sign-in runs. */
    public fun resume() {
        if (mutableState.value == SignInState.Working) return
        scope.launch {
            mutex.withLock {
                if (mutableState.value != SignInState.Working) runResume()
            }
        }
    }

    /**
     * Deliberate sign-out, after the native confirmation: runs what [onSigningOut] registered while the session is
     * still valid, then clears the store, then ends the Keycloak session best effort. Shows the Sign-in surface
     * without a notice.
     */
    public fun signOut() {
        scope.launch {
            // Outside the lock: these actions call the Server, and the API takes the lock for its access token.
            if (mutableState.value is SignInState.SignedIn) {
                for (action in signingOut.toList()) attempt { withTimeoutOrNull(endSessionTimeout) { action() } }
            }
            mutex.withLock {
                val tokens = attempt { vault.read() }
                attempt { vault.clear() }
                mutableState.value = SignInState.SignedOut(null)
                tokens?.id_token?.let { endSessionBestEffort(it) }
            }
        }
    }

    /**
     * The access token for a Server call, for the core's API client only: the stored token,
     * refreshed first when it has expired. `null` when signed out, or when Keycloak refused the
     * refresh (which signs out with the SignedOut notice).
     */
    internal suspend fun accessToken(): String? =
        mutex.withLock {
            if (mutableState.value !is SignInState.SignedIn) return@withLock null
            val tokens = attempt { vault.read() } ?: return@withLock null
            if (!isExpired(tokens)) return@withLock tokens.access_token
            when (val outcome = identity.refresh(tokens)) {
                is RefreshOutcome.Refreshed -> {
                    outcome.tokens.access_token
                }

                RefreshOutcome.Rejected -> {
                    attempt { vault.clear() }
                    mutableState.value = SignInState.SignedOut(Notice.SignedOut)
                    null
                }

                // Keycloak did not answer; the Server decides whether the old token still counts.
                RefreshOutcome.Transient -> {
                    tokens.access_token
                }
            }
        }

    /** The Server answered 401: the session is over. Shows the SignedOut notice. */
    internal suspend fun signOutExpired() {
        mutex.withLock {
            if (mutableState.value !is SignInState.SignedIn) return@withLock
            attempt { vault.clear() }
            mutableState.value = SignInState.SignedOut(Notice.SignedOut)
        }
    }

    private suspend fun runSignIn() {
        mutableState.value = SignInState.Working
        val failure = probe.probeSignIn()
        if (failure != null) {
            mutableState.value = SignInState.SignedOut(failure.toNotice())
            return
        }
        completeSignIn { authFlow.signIn() }
    }

    private suspend fun runResume() {
        if (attempt { authFlow.canContinue() } == true) {
            // The process died while the browser was open; the redirect has arrived since.
            mutableState.value = SignInState.Working
            completeSignIn { authFlow.continueSignIn() }
            return
        }
        val tokens =
            try {
                vault.read()
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught", "SwallowedException") unreadable: Throwable,
            ) {
                // A corrupt or undecryptable store counts as no tokens.
                attempt { vault.clear() }
                showUnlessSigningIn(SignInState.SignedOut(null))
                return
            }
        if (tokens == null) {
            showUnlessSigningIn(
                when (val current = mutableState.value) {
                    is SignInState.SignedIn -> SignInState.SignedOut(Notice.SignedOut)
                    is SignInState.SignedOut -> current
                    else -> SignInState.SignedOut(null)
                },
            )
            return
        }
        if (!isExpired(tokens)) {
            showUnlessSigningIn(SignInState.SignedIn(displayNameOf(tokens)))
            return
        }
        showUnlessSigningIn(
            when (val outcome = identity.refresh(tokens)) {
                is RefreshOutcome.Refreshed -> {
                    SignInState.SignedIn(displayNameOf(outcome.tokens))
                }

                RefreshOutcome.Rejected -> {
                    attempt { vault.clear() }
                    SignInState.SignedOut(Notice.SignedOut)
                }

                RefreshOutcome.Transient -> {
                    SignInState.SignedIn(displayNameOf(tokens))
                }
            },
        )
    }

    /**
     * Resume never overwrites [SignInState.Working] it did not set: SIGN IN may have been pressed
     * while resume was reading the store, and its sign-in runs next.
     */
    private fun showUnlessSigningIn(next: SignInState) {
        if (mutableState.value != SignInState.Working) mutableState.value = next
    }

    /** Runs [block]; a failure other than cancellation yields `null`. */
    private suspend fun <T> attempt(block: suspend () -> T): T? =
        try {
            block()
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (
            @Suppress("TooGenericExceptionCaught", "SwallowedException") ignored: Throwable,
        ) {
            null
        }

    private suspend fun completeSignIn(flow: suspend () -> AccessTokenResponse) {
        val tokens =
            try {
                flow()
            } catch (cancelled: OpenIdConnectException.AuthenticationCancelled) {
                mutableState.value = SignInState.SignedOut(null)
                return
            } catch (cancellation: CancellationException) {
                mutableState.value = SignInState.SignedOut(null)
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught") error: Throwable,
            ) {
                val diagnosis = if (certificateError(error)) Failure.Certificate else attempt { probe.diagnose() }
                val failure = diagnosis ?: Failure.Keycloak
                mutableState.value = SignInState.SignedOut(failure.toNotice())
                return
            }
        if (attempt { vault.save(tokens) } == null) {
            // The tokens could not be stored, so there is no session.
            mutableState.value = SignInState.SignedOut(Notice.Keycloak)
            return
        }
        mutableState.value = SignInState.SignedIn(displayNameOf(tokens))
    }

    private suspend fun endSessionBestEffort(idToken: String) {
        try {
            withTimeoutOrNull(endSessionTimeout) { identity.endSession(idToken) }
        } catch (cancellation: CancellationException) {
            throw cancellation
        } catch (
            @Suppress("TooGenericExceptionCaught", "SwallowedException") ignored: Throwable,
        ) {
            // Best effort: the local session is already gone.
        }
    }

    private fun isExpired(tokens: AccessTokenResponse): Boolean {
        val expiresIn = tokens.expires_in ?: return false
        return tokens.received_at + expiresIn <= nowEpochSeconds() + EXPIRY_TOLERANCE.inWholeSeconds
    }

    public companion object {
        /** Tokens with less than this left count as expired, as in the library's policy. */
        public val EXPIRY_TOLERANCE: Duration = 60.seconds

        /** The ID token's `name`, else `preferred_username`; `null` without an ID token. */
        public fun displayNameOf(tokens: AccessTokenResponse): String? {
            val idToken = tokens.id_token ?: return null
            val claims =
                try {
                    idToken.parseJwt().payload.additionalClaims
                } catch (_: OpenIdConnectException) {
                    return null
                }
            return (claims["name"] as? String) ?: (claims["preferred_username"] as? String)
        }
    }
}
