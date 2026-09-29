package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesNotice

/**
 * A Lot's status as the Server computed it (AD-14). The client never computes or re-sorts it;
 * the list arrives in the Server's order. In Story 1.9 only [NoNode] has its own tile.
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

/** A live Lot of the current Site, as the Server reported it. */
public data class LotSummary(
    val id: String,
    val name: String,
    val status: LotStatus,
)

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

    public data class Loading(
        val site: SiteSummary,
    ) : LotsState

    /** The Lots could not be read; [notice] uses the Sites load copy (Unreachable, Certificate, …). */
    public data class Failed(
        val site: SiteSummary,
        val notice: SitesNotice,
    ) : LotsState

    /**
     * [lots] are in the Server's order and never re-sorted (UX-DR20). [renaming] and [removing]
     * are the open dialogs; [notice] is the last change that did not happen.
     */
    public data class Ready(
        val site: SiteSummary,
        val lots: List<LotSummary>,
        val siteName: SiteNameForm,
        val create: CreateLotForm,
        val renaming: RenameLotForm?,
        val removing: RemoveLotConfirmation?,
        val notice: LotsNotice?,
    ) : LotsState {
        val settings: SiteSettings get() = SiteSettings.of(site.role)
        val siteId: String get() = site.id
    }
}
