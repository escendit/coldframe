package com.escendit.coldframe.core.sites

/** A Role on one Site. Declared low to high, so `role >= SiteRole.Administrator` reads as "Admin+". */
public enum class SiteRole {
    Member,
    Administrator,
    Owner,
    ;

    public companion object {
        /** The Server's role string; anything unknown grants the least (Member). */
        public fun fromServer(value: String): SiteRole = entries.firstOrNull { it.name == value } ?: Member
    }
}

/** A Site the user holds a Membership on, with the Role the Server reported. */
public data class SiteSummary(
    val id: String,
    val name: String,
    val role: SiteRole,
)

/** The one-line reason under the Site name field. */
public enum class NameError {
    /** Empty after trimming. */
    Blank,

    /** Longer than [CreateSiteForm.MAX_NAME_LENGTH] after trimming. */
    TooLong,
}

/**
 * An Inline notice on the Sites surfaces. [Unreachable] and [Certificate] reuse the sign-in copy;
 * only [Certificate] has no action, because it is never retried insecurely.
 */
public enum class SitesNotice(
    public val tryAgain: Boolean,
) {
    Unreachable(true),
    Certificate(false),

    /** 503: the Site was not created; Try again reuses the same Idempotency-Key. */
    IdentityProviderUnavailable(true),

    /** 422: the Site was not created; the next attempt uses a new key. */
    KeyReused(true),

    /** Any other answer; nothing was changed. */
    Unexpected(true),
}

/**
 * The time-zone confirm panel of Create Site (UX-DR61, UX-DR48). [detected] comes from the OS;
 * [chosen] is the zone the User confirmed or picked, read from the Server once it has it, and
 * never overwritten by detection. [changing] shows the searchable list. The zone is the User's
 * (AD-11): it goes to `PATCH /me/notification-settings`, never with `POST /sites`.
 */
public data class TimeZoneProposal(
    val detected: String,
    val chosen: String?,
    val changing: Boolean,
) {
    /** The zone the panel names: the user's choice, else the detected one. */
    val shown: String get() = chosen ?: detected

    val confirmed: Boolean get() = chosen != null
}

/**
 * The Create Site form. [idempotencyKey] is made when the form opens and reused for every retry
 * of the same submission; it changes only after a 422 or a success. [cancellable] is false on
 * first sign-in with no Membership, true from "New Site".
 */
public data class CreateSiteForm(
    val name: String,
    val nameError: NameError?,
    val working: Boolean,
    val notice: SitesNotice?,
    val idempotencyKey: String,
    val timeZone: TimeZoneProposal,
    val cancellable: Boolean,
) {
    public companion object {
        public const val MAX_NAME_LENGTH: Int = 100

        /** The client check; `null` means the name may be sent. */
        public fun validate(name: String): NameError? {
            val trimmed = name.trim()
            return when {
                trimmed.isEmpty() -> NameError.Blank
                trimmed.length > MAX_NAME_LENGTH -> NameError.TooLong
                else -> null
            }
        }
    }
}

/** What the Sites surfaces show while signed in. */
public sealed interface SitesState {
    /** Signed out: nothing to show. */
    public data object Idle : SitesState

    /** The Sites are being read; shells show only the background. */
    public data object Loading : SitesState

    /** The Sites could not be read; the notice carries Try again (except Certificate). */
    public data class Failed(
        val notice: SitesNotice,
    ) : SitesState

    /** No Membership: Create Site replaces the tab shell. */
    public data class NeedsSite(
        val form: CreateSiteForm,
    ) : SitesState

    /**
     * The tab shell on [current]. [sites] are in the Server's order and never re-sorted.
     * [creating] is the Create Site form opened from "New Site", shown over the shell.
     *
     * [fromCache] is true while [sites] are the last good list kept on this device: on a cold
     * start until the first read lands, and for as long as the Server does not answer. The Sites
     * surfaces have no stale mode of their own; the overview's is [com.escendit.coldframe.core.lots.LotsState.stale].
     */
    public data class Ready(
        val sites: List<SiteSummary>,
        val current: SiteSummary,
        val creating: CreateSiteForm?,
        val fromCache: Boolean = false,
    ) : SitesState
}
