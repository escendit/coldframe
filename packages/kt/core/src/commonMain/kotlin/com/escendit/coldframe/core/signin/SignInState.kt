package com.escendit.coldframe.core.signin

/** What went wrong on the way to Keycloak, in the terms of the UX-DR92 notices. */
public enum class Failure {
    /** The Server gave no response: DNS, refused, timeout, or off the home network. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** The issuer, discovery, the browser flow or the code exchange failed. */
    Keycloak,
}

/** The one action a notice may carry. */
public enum class NoticeAction {
    TryAgain,
    SignIn,
}

/**
 * An Inline notice on the Sign-in card (UX-DR92, UX-DR93). Each carries its action, or none where
 * nothing can be done in the app. Shells map it to the catalogue copy and never branch on errors.
 */
public enum class Notice(
    public val action: NoticeAction?,
) {
    Unreachable(NoticeAction.TryAgain),
    Certificate(null),
    Keycloak(NoticeAction.TryAgain),
    SignedOut(NoticeAction.SignIn),
}

/** The notice a failure shows. */
public fun Failure.toNotice(): Notice =
    when (this) {
        Failure.Unreachable -> Notice.Unreachable
        Failure.Certificate -> Notice.Certificate
        Failure.Keycloak -> Notice.Keycloak
    }

/** The session as the shells see it. */
public sealed interface SignInState {
    /** Stored tokens are being read on start; shells show only the background. */
    public data object Restoring : SignInState

    /** The Sign-in surface, with a notice or none. */
    public data class SignedOut(
        val notice: Notice?,
    ) : SignInState

    /** SIGN IN was pressed; the button shows its working label and ignores presses. */
    public data object Working : SignInState

    /** Tokens are in the store. [displayName] comes from the ID token and may be absent. */
    public data class SignedIn(
        val displayName: String?,
    ) : SignInState
}
