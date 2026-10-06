package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesNotice

/**
 * A Lot's status as the Server computed it (AD-14). The client never computes or re-sorts it;
 * the list arrives in the Server's order.
 */
public enum class LotStatus {
    NeedsWater,
    NeedsCalibration,
    Unknown,
    Ok,
    Paused,
    NoNode,
    ;

    /** The contract value: `NeedsWater` → `needsWater`. */
    public val key: String get() = name.replaceFirstChar { it.lowercase() }

    public companion object {
        /** The Server's value; anything unknown reads as [Unknown], never as fine. */
        public fun fromServer(value: String): LotStatus = entries.firstOrNull { it.key == value } ?: Unknown
    }
}

/** Why a Lot is `unknown`, as the Server said: its Node is silent, or the Hub in front of it. */
public enum class LotUnknownCause {
    Node,
    Hub,
    ;

    /** The contract value: `node`, `hub`. */
    public val key: String get() = name.lowercase()

    public companion object {
        public fun fromServer(value: String?): LotUnknownCause? = entries.firstOrNull { it.key == value }
    }
}

/** What paused a Lot's Node, as the Server said: the Device itself, or its Site. */
public enum class LotPauseSource {
    Device,
    Site,
    ;

    /** The contract value: `device`, `site`. */
    public val key: String get() = name.lowercase()

    public companion object {
        public fun fromServer(value: String): LotPauseSource? = entries.firstOrNull { it.key == value }
    }
}

/**
 * A live Lot of the current Site, as the Server reported it. Every field is the Server's; times
 * are epoch milliseconds. [unknownCause] comes only with [LotStatus.Unknown], [pausedBy] and
 * [pausedUntilEpochMs] only with [LotStatus.Paused]. [moisturePercent] and
 * [lowThresholdPercent] are unrounded; [SoilMoisture] formats them.
 */
public data class LotSummary(
    val id: String,
    val name: String,
    val status: LotStatus,
    val statusSinceEpochMs: Long? = null,
    val lastReadingAtEpochMs: Long? = null,
    val unknownCause: LotUnknownCause? = null,
    val pausedBy: List<LotPauseSource> = emptyList(),
    val pausedUntilEpochMs: Long? = null,
    val moisturePercent: Double? = null,
    val lowThresholdPercent: Double? = null,
)

/**
 * Why the shown Lots are not live (transport only, AD-14). There is no age threshold: data is
 * stale because the Server did not answer, never because it is old.
 */
public enum class StaleReason {
    /** Last-good Lots from this device, shown until the first refresh for the Site lands. */
    Cached,

    /** A refresh and its one retry failed. */
    Unreachable,
}

/** What the shells announce politely (UX-DR106). Nothing else about the Lots is announced. */
public sealed interface LotsEvent {
    public val siteId: String

    /**
     * A refresh and its retry failed: "Can't reach your Server. Showing data from ‹t›.", with
     * [fetchedAtEpochMs] the time of the last successful refresh. Sent once per entry.
     */
    public data class EnteredStale(
        override val siteId: String,
        val fetchedAtEpochMs: Long,
    ) : LotsEvent

    /** The first successful refresh after [EnteredStale]: "Live again." Never sent for a first load. */
    public data class LeftStale(
        override val siteId: String,
    ) : LotsEvent
}

/**
 * What a Role may do in Site settings (UX-DR74, UX-DR84): only an Owner renames the Site;
 * Owners and Administrators create, rename and remove Lots; a Member sees everything read-only
 * with one notice. Controls a Role cannot use are hidden, not disabled.
 */
public data class SiteSettings(
    val canRenameSite: Boolean,
    val canEditLots: Boolean,
    val readOnlyNotice: Boolean,
) {
    public companion object {
        public fun of(role: SiteRole): SiteSettings =
            SiteSettings(
                canRenameSite = role == SiteRole.Owner,
                canEditLots = role >= SiteRole.Administrator,
                readOnlyNotice = role == SiteRole.Member,
            )
    }
}

/** The Site name field in Site settings. */
public data class SiteNameForm(
    val draft: String,
    val error: NameError?,
    val working: Boolean,
)

/**
 * The Create Lot field. [idempotencyKey] belongs to one create attempt: it is kept after a 503
 * or a network failure and replaced after a 422 or a success.
 */
public data class CreateLotForm(
    val name: String,
    val error: NameError?,
    val working: Boolean,
    val idempotencyKey: String,
)

/** The Rename Lot dialog for [lotId]. */
public data class RenameLotForm(
    val lotId: String,
    val draft: String,
    val error: NameError?,
    val working: Boolean,
)

/** The Remove Lot confirmation, naming [lotName]. */
public data class RemoveLotConfirmation(
    val lotId: String,
    val lotName: String,
    val working: Boolean,
)

/** Why a Site settings change did not happen. */
public enum class LotsNoticeKind {
    /** 403: "You can't change this on {site}. Ask an Owner or Administrator." */
    Forbidden,

    /** 409: "Move or unassign the Node on {lot} first." */
    LotClaimed,

    /** 404: the Lot was removed meanwhile or belongs to another Site. */
    LotNotFound,

    /** 503 on Rename Site: Keycloak did not answer, nothing changed. */
    RenameSiteUnavailable,

    /** 422 on Create Lot: nothing was created; the next attempt uses a new key. */
    KeyReused,
    Unreachable,
    Certificate,
    Unexpected,
}

/** An Inline notice in Site settings; [subject] is the Site name for [LotsNoticeKind.Forbidden], the Lot name for [LotsNoticeKind.LotClaimed]. */
public data class LotsNotice(
    val kind: LotsNoticeKind,
    val subject: String? = null,
)

/** The Lots of the current Site, and Site settings. Follows the current Site of the Sites engine. */
public sealed interface LotsState {
    /** Signed out, or no current Site. */
    public data object Idle : LotsState

    /**
     * The first read for a Site with no last good Lots on this device: outline skeleton tiles
     * and "Loading ‹Site›" (UX-DR80).
     */
    public data class Loading(
        val site: SiteSummary,
    ) : LotsState

    /**
     * The Lots could not be read and there are none to show; [notice] uses the Sites load copy
     * (Unreachable, Certificate, …).
     */
    public data class Failed(
        val site: SiteSummary,
        val notice: SitesNotice,
    ) : LotsState

    /**
     * [lots] are in the Server's order and never re-sorted (UX-DR20). [renaming] and [removing]
     * are the open dialogs; [notice] is the last change that did not happen.
     *
     * [fetchedAtEpochMs] is the time of the last successful refresh ("as of", "Last data").
     * [staleReason] is set while the Lots are not live; [refreshing] while a read is on its way
     * over the shown Lots.
     */
    public data class Ready(
        val site: SiteSummary,
        val lots: List<LotSummary>,
        val siteName: SiteNameForm,
        val create: CreateLotForm,
        val renaming: RenameLotForm?,
        val removing: RemoveLotConfirmation?,
        val notice: LotsNotice?,
        val fetchedAtEpochMs: Long = 0,
        val staleReason: StaleReason? = null,
        val refreshing: Boolean = false,
    ) : LotsState {
        val settings: SiteSettings get() = SiteSettings.of(site.role)
        val siteId: String get() = site.id

        /** Stale mode: the stale header replaces the summary and every tile is a stale tile. */
        val stale: Boolean get() = staleReason != null
    }
}

/** Whether the overview is in stale mode. Only [LotsState.Ready] can be. */
public val LotsState.stale: Boolean
    get() = (this as? LotsState.Ready)?.stale == true

/** The Site the Lots belong to, or `null` while [LotsState.Idle]. */
public val LotsState.site: SiteSummary?
    get() =
        when (this) {
            LotsState.Idle -> null
            is LotsState.Loading -> site
            is LotsState.Failed -> site
            is LotsState.Ready -> site
        }
