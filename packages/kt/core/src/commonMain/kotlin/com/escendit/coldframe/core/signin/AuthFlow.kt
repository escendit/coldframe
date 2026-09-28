package com.escendit.coldframe.core.signin

import org.publicvalue.multiplatform.oidc.OpenIdConnectClient
import org.publicvalue.multiplatform.oidc.flows.CodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.types.remote.AccessTokenResponse

/**
 * OIDC Authorization Code + PKCE in the system browser session. Throws
 * `OpenIdConnectException.AuthenticationCancelled` when the user cancels.
 */
public interface AuthFlow {
    /** Opens the browser, waits for the redirect and exchanges the code. */
    public suspend fun signIn(): AccessTokenResponse

    /** True when a sign-in started before the process died and its redirect has arrived. */
    public suspend fun canContinue(): Boolean

    /** Exchanges the code of a sign-in that started before the process died. */
    public suspend fun continueSignIn(): AccessTokenResponse
}

/**
 * [AuthFlow] over `kotlin-multiplatform-oidc`: `AndroidCodeAuthFlowFactory` (Custom Tabs) or
 * `IosCodeAuthFlowFactory` (`ASWebAuthenticationSession`). The factory is resolved on each call
 * because Android registers the activity after the core is created.
 */
public class LibraryAuthFlow(
    private val client: OpenIdConnectClient,
    private val factory: () -> CodeAuthFlowFactory,
) : AuthFlow {
    override suspend fun signIn(): AccessTokenResponse = factory().createAuthFlow(client).getAccessToken()

    override suspend fun canContinue(): Boolean = factory().createAuthFlow(client).canContinueLogin()

    override suspend fun continueSignIn(): AccessTokenResponse = factory().createAuthFlow(client).continueLogin()
}
