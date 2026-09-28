package com.escendit.coldframe.core.signin

/**
 * [SignInState] flattened for Swift: which surface shows, and the notice and action as their
 * catalogue key suffixes (`unreachable`, `tryAgain`, …). No token ever crosses this boundary.
 */
public data class SignInSnapshot(
    val restoring: Boolean,
    val working: Boolean,
    val signedIn: Boolean,
    val notice: String?,
    val action: String?,
    val displayName: String?,
)

/** The snapshot of [state]. */
public fun snapshotOf(state: SignInState): SignInSnapshot =
    when (state) {
        SignInState.Restoring -> {
            SignInSnapshot(true, false, false, null, null, null)
        }

        SignInState.Working -> {
            SignInSnapshot(false, true, false, null, null, null)
        }

        is SignInState.SignedIn -> {
            SignInSnapshot(false, false, true, null, null, state.displayName)
        }

        is SignInState.SignedOut -> {
            SignInSnapshot(
                restoring = false,
                working = false,
                signedIn = false,
                notice = state.notice?.name?.replaceFirstChar { it.lowercase() },
                action =
                    state.notice
                        ?.action
                        ?.name
                        ?.replaceFirstChar { it.lowercase() },
                displayName = null,
            )
        }
    }
