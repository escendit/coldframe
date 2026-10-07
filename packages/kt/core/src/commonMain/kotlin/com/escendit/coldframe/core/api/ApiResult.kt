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

    /** 409 `device-on-another-site`: the Device is enrolled on another Site; nothing changed. */
    DeviceOnAnotherSite,

    /** 409 `device-assigned`: the Node is assigned to another Lot; nothing changed. */
    DeviceAssigned,

    /** 503 `calibration-not-delivered`: the Calibration is saved but the Node's Device has not acknowledged it yet. */
    CalibrationNotDelivered,

    /** 401, or no session: the engine signs out with the SignedOut notice. */
    Unauthorized,

    /** No response: DNS, refused, timeout, or off the home network. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** Any other answer, including a 5xx other than 503. */
    Unexpected,
    ;

    /**
     * Whether a read that failed this way may be answered from the last good data in stale mode:
     * no answer, a 5xx, or an answer that is not the expected one. A 403 or 404 is an answer about
     * the caller, a 401 ends the session, and a certificate failure is never served stale.
     */
    public val transport: Boolean
        get() = this == Unreachable || this == Unexpected || this == IdentityProviderUnavailable
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
