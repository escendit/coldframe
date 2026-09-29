package com.escendit.coldframe.core.signin

/** How deep a `cause` chain is followed before giving up. */
internal const val MAX_CAUSE_DEPTH: Int = 10

/**
 * True when [error], or any error in its `cause` chain, is a TLS or certificate verification
 * failure. A certificate failure is never retried insecurely and wins over every other failure.
 */
public expect fun isCertificateError(error: Throwable): Boolean

/** Walks [error] and its causes, at most [MAX_CAUSE_DEPTH] deep. */
internal inline fun anyCause(
    error: Throwable,
    predicate: (Throwable) -> Boolean,
): Boolean {
    var current: Throwable? = error
    var depth = 0
    while (current != null && depth < MAX_CAUSE_DEPTH) {
        if (predicate(current)) return true
        current = current.cause
        depth++
    }
    return false
}
