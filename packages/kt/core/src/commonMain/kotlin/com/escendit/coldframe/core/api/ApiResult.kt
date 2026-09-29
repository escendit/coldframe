package com.escendit.coldframe.core.api

/** Why a Server call did not succeed, in the terms the surfaces need. */
public enum class ApiFailure {
    /** 400 `validation`: a field was refused. */
    Validation,

    /** 503 `identity-provider-unavailable`: nothing was created or renamed; retry with the same key. */
    IdentityProviderUnavailable,

    /** 422 `idempotency-key-reused`: nothing was created; the next attempt needs a new key. */
    KeyReused,

    /** 403 `forbidden`: the caller's Role on the Site is missing or too low; nothing changed. */
    Forbidden,

    /** 404 `site-not-found` or `lot-not-found`: nothing changed. */
    NotFound,

    /** 409 `lot-claimed`: a Node is assigned to the Lot; nothing changed. */
    LotClaimed,

    /** 401, or no session: the engine signs out with the SignedOut notice. */
    Unauthorized,

    /** No response: DNS, refused, timeout, or off the home network. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** Any other answer. */
    Unexpected,
}

/** A Server call's outcome as a value; the API never throws for HTTP or I/O failures. */
public sealed interface ApiResult<out T> {
    public data class Ok<T>(
        val value: T,
    ) : ApiResult<T>

    public data class Failed(
        val failure: ApiFailure,
    ) : ApiResult<Nothing>
}
