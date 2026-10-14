package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DevicePauseDto
import com.escendit.coldframe.core.api.SitePauseDto
import com.escendit.coldframe.core.devices.DeviceName
import com.escendit.coldframe.core.lots.LotPauseSource

/*
 * Pause and Resume as both shells draw them (Story 8.2): the Pause sheet, a Resume under way and
 * the last change that did not happen. Whether anything is paused is the Server's answer only
 * (`Site.paused`, `Lot.pausedBy`, `DeviceListItem.pausedBy`); nothing here computes or keeps it.
 */

/** The four Pause calls; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface PauseApi {
    /** Pauses the Site (Admin+). [until] is the picked calendar date, `yyyy-MM-dd`, or `null` for no end. */
    public suspend fun pauseSite(
        siteId: String,
        until: String?,
    ): ApiResult<SitePauseDto>

    /** Resumes the Site (Admin+). */
    public suspend fun resumeSite(siteId: String): ApiResult<SitePauseDto>

    /** Pauses one Device, a Hub or a Node (Admin+); [until] as for [pauseSite]. */
    public suspend fun pauseDevice(
        siteId: String,
        deviceId: String,
        until: String?,
    ): ApiResult<DevicePauseDto>

    /** Lifts the Device's own Pause (Admin+); it stays paused while its Site is. */
    public suspend fun resumeDevice(
        siteId: String,
        deviceId: String,
    ): ApiResult<DevicePauseDto>
}

/** What a Pause or a Resume is about: one Device, or the whole Site. */
public enum class PauseScope {
    Device,
    Site,
    ;

    /** The catalogue key suffix and the contract's source name: `device`, `site`. */
    public val key: String get() = name.lowercase()

    public companion object {
        public fun fromKey(key: String): PauseScope? = entries.firstOrNull { it.key == key }
    }
}

/** Why a Pause or a Resume did not happen. Nothing changed with any of them. */
public enum class PauseNotice(
    /** Whether sending the same request again can help. */
    public val tryAgain: Boolean,
) {
    /** 400 `validation` on a Pause that carried a date: it is today or earlier. The sheet marks its date field: "Choose a date after today." */
    DateRefused(false),

    /** 403: the Role is too low (it may have changed meanwhile): "You can't change this on ‹Site›. Ask an Owner or Administrator." */
    Forbidden(false),

    /** 404 on a Device: the Device is gone. */
    NotFound(false),

    /** 404 on the Site: "‹Site› is no longer one of your Sites. Nothing was changed." The Sites are read again. */
    SiteGone(false),

    /** No answer. */
    Unreachable(true),

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate(false),

    /** Any other answer. */
    Unexpected(true),
}

/**
 * The Pause sheet (UX-DR46). Opened from the Site menu it has no Device ([deviceId] is `null`) and
 * Whole Site is its only scope; opened from Lot detail or a Devices row it offers This Device,
 * selected, and Whole Site. [until] is the calendar date the User picked, `yyyy-MM-dd`, or `null`:
 * "Leave empty to pause until someone resumes." While [working] the request is on its way. A
 * [notice] keeps the sheet open with what was entered.
 */
public data class PauseForm(
    val siteName: String,
    val deviceId: String?,
    val scope: PauseScope,
    val until: String? = null,
    val working: Boolean = false,
    val notice: PauseNotice? = null,
) {
    /** The segments of the scope choice, in order. A scope that cannot be chosen is not listed. */
    val scopes: List<PauseScope>
        get() = if (deviceId == null) listOf(PauseScope.Site) else listOf(PauseScope.Device, PauseScope.Site)

    /** "7C19": the Device as the primary button names it; `null` from the Site menu. */
    val deviceName: String? get() = deviceId?.let(DeviceName::short)

    /** What the primary button names: "Pause ‹7C19›" for This Device, "Pause ‹Home garden›" for Whole Site. */
    val target: String get() = if (scope == PauseScope.Device) deviceName ?: siteName else siteName

    /** The Server refused the date: the field is marked and says "Choose a date after today." */
    val dateRefused: Boolean get() = notice == PauseNotice.DateRefused

    /** [until] as numbers, or `null` without a date. */
    val untilDate: PauseDate? get() = until?.let(PauseDate::parse)
}

/** A calendar date as the User picked it. No time zone is involved: the Server resolves the date. */
public data class PauseDate(
    val year: Int,
    val month: Int,
    val day: Int,
) {
    /** `yyyy-MM-dd`, as the contract's `SetPauseRequest.until`. */
    val iso: String
        get() = "${year.toString().padStart(
            4,
            '0',
        )}-${month.toString().padStart(2, '0')}-${day.toString().padStart(2, '0')}"

    public companion object {
        private const val LENGTH = 10
        private const val MONTHS = 12
        private const val MAX_DAY = 31

        /** A date of the calendar, or `null`: month 1 to 12, a day that month has. */
        public fun of(
            year: Int,
            month: Int,
            day: Int,
        ): PauseDate? {
            if (year !in 1..9999 || month !in 1..MONTHS || day !in 1..MAX_DAY) return null
            return PauseDate(year, month, day).takeIf { day <= daysIn(year, month) }
        }

        /** Reads `yyyy-MM-dd`, digits and the two hyphens only; anything else is `null`. */
        public fun parse(text: String): PauseDate? {
            if (text.length != LENGTH || text[4] != '-' || text[7] != '-') return null
            if (text.count { it in '0'..'9' } != LENGTH - 2) return null
            val year = text.substring(0, 4).toIntOrNull() ?: return null
            val month = text.substring(5, 7).toIntOrNull() ?: return null
            val day = text.substring(8, 10).toIntOrNull() ?: return null
            return of(year, month, day)
        }

        private fun daysIn(
            year: Int,
            month: Int,
        ): Int =
            when (month) {
                2 -> if (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) 29 else 28
                4, 6, 9, 11 -> 30
                else -> MAX_DAY
            }
    }
}

/** What a one-tap Resume is about: the Site, or the Device [deviceId]. */
public data class PauseTarget(
    val scope: PauseScope,
    val deviceId: String? = null,
)

/**
 * The last Resume that did not happen, for the surface it was asked on. [siteName] is the Site of
 * the [PauseNotice.Forbidden] copy. A Pause that did not happen stays in its sheet ([PauseForm.notice]).
 */
public data class PauseFailure(
    val target: PauseTarget,
    val notice: PauseNotice,
    val siteName: String,
)

/**
 * Pause on one surface (the overview, Lot detail or Devices): the open [sheet], the Resume that is
 * [resuming], and the last Resume that did not happen.
 */
public data class PauseState(
    val sheet: PauseForm? = null,
    val resuming: PauseTarget? = null,
    val failure: PauseFailure? = null,
) {
    /** A request is on its way: one change at a time. */
    val working: Boolean get() = resuming != null || sheet?.working == true

    /** The Device [deviceId] is being paused or resumed. */
    public fun workingOn(deviceId: String): Boolean =
        resuming?.deviceId == deviceId ||
            (sheet?.working == true && sheet.scope == PauseScope.Device && sheet.deviceId == deviceId)

    /** The Site is being paused or resumed. */
    val workingOnSite: Boolean
        get() = resuming?.scope == PauseScope.Site || (sheet?.working == true && sheet.scope == PauseScope.Site)
}

/**
 * What a Device's row or a Lot's detail says and offers about Pause (UX-DR70), from the Server's
 * `pausedBy` and `pausedUntil` alone. The controls are for Administrators and Owners and do not
 * exist for a Member: hidden, never disabled.
 */
public data class DevicePause(
    val pausedBy: List<LotPauseSource> = emptyList(),
    val untilEpochMs: Long? = null,
) {
    /** The Server says the Device is paused. */
    val paused: Boolean get() = pausedBy.isNotEmpty()

    /** The Device has its own Pause. */
    val byDevice: Boolean get() = LotPauseSource.Device in pausedBy

    /** Its Site is paused: the label reads "Paused by Site". */
    val bySite: Boolean get() = LotPauseSource.Site in pausedBy

    /** Pause shows: the Device has no Pause of its own yet. It stays available while only the Site is paused. */
    public fun canPause(role: SiteRole): Boolean = allowed(role) && !byDevice

    /** Resume shows: only the Device's own Pause can be lifted here. */
    public fun canResume(role: SiteRole): Boolean = allowed(role) && byDevice

    /**
     * "Resume the Site to resume this Node" (or "… this Hub"): paused by the Site alone, there is no
     * Resume for the Device, and the Site's Pause is lifted from the Site menu. A Device that is also
     * paused by itself shows Resume instead.
     */
    public fun resumeSiteHint(role: SiteRole): Boolean = allowed(role) && bySite && !byDevice

    public companion object {
        /** Not paused. */
        public val None: DevicePause = DevicePause()

        /** Pause and Resume are for Administrators and Owners (UX-DR84). */
        public fun allowed(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /** The Server's members; a source this client does not know is left out. */
        public fun of(
            pausedBy: List<String>?,
            untilEpochMs: Long?,
        ): DevicePause {
            val sources = pausedBy.orEmpty().mapNotNull(LotPauseSource::fromServer)
            return DevicePause(sources, untilEpochMs.takeIf { sources.isNotEmpty() })
        }
    }
}

/**
 * [PauseState] flattened for Swift, the same on the overview, Lot detail and Devices. Enum values
 * cross as catalogue key suffixes; an absent value is an empty string or 0.
 *
 * - The sheet: [sheetOpen]; [sheetScopes] are its segments in order (`device`, `site`; only `site`
 *   from the Site menu) and [sheetScope] the selected one. [sheetTarget] is what the primary button
 *   names ("Pause ‹7C19›", "Pause ‹Home garden›"), with [sheetDeviceId], [sheetDeviceName] ("7C19")
 *   and [sheetSiteName]. [sheetUntil] is the picked date as `yyyy-MM-dd` and [sheetUntilYear] /
 *   [sheetUntilMonth] / [sheetUntilDay] the same as numbers (all empty or 0 without a date).
 *   [sheetWorking] while the request runs. [sheetDateRefused]: the date field is marked, "Choose a
 *   date after today." [sheetNotice] is any other reason the Pause did not happen (`forbidden`,
 *   `notFound`, `siteGone`, `unreachable`, `certificate`, `unexpected`) with [sheetNoticeTryAgain]; the sheet
 *   stays open with what was entered.
 * - Resume: [resumingScope] (`device`, `site`) with [resumingDeviceId] while a Resume runs.
 *   [failureNotice] is the last Resume that did not happen (the keys above), for [failureScope] and
 *   [failureDeviceId], with [failureTryAgain] and [failureSiteName] for the `forbidden` copy.
 */
public data class PauseSnapshot(
    val sheetOpen: Boolean,
    val sheetScopes: List<String>,
    val sheetScope: String,
    val sheetTarget: String,
    val sheetDeviceId: String,
    val sheetDeviceName: String,
    val sheetSiteName: String,
    val sheetUntil: String,
    val sheetUntilYear: Int,
    val sheetUntilMonth: Int,
    val sheetUntilDay: Int,
    val sheetWorking: Boolean,
    val sheetDateRefused: Boolean,
    val sheetNotice: String,
    val sheetNoticeTryAgain: Boolean,
    val resumingScope: String,
    val resumingDeviceId: String,
    val failureScope: String,
    val failureDeviceId: String,
    val failureNotice: String,
    val failureTryAgain: Boolean,
    val failureSiteName: String,
)

/** The snapshot of [state]. */
public fun snapshotOf(state: PauseState): PauseSnapshot {
    val sheet = state.sheet
    val date = sheet?.untilDate
    // A refused date marks its field; it is not a second notice.
    val sheetNotice = sheet?.notice?.takeIf { it != PauseNotice.DateRefused }
    return PauseSnapshot(
        sheetOpen = sheet != null,
        sheetScopes = sheet?.scopes?.map { it.key }.orEmpty(),
        sheetScope = sheet?.scope?.key.orEmpty(),
        sheetTarget = sheet?.target.orEmpty(),
        sheetDeviceId = sheet?.deviceId.orEmpty(),
        sheetDeviceName = sheet?.deviceName.orEmpty(),
        sheetSiteName = sheet?.siteName.orEmpty(),
        sheetUntil = date?.iso.orEmpty(),
        sheetUntilYear = date?.year ?: 0,
        sheetUntilMonth = date?.month ?: 0,
        sheetUntilDay = date?.day ?: 0,
        sheetWorking = sheet?.working == true,
        sheetDateRefused = sheet?.dateRefused == true,
        sheetNotice = sheetNotice?.key().orEmpty(),
        sheetNoticeTryAgain = sheetNotice?.tryAgain == true,
        resumingScope =
            state.resuming
                ?.scope
                ?.key
                .orEmpty(),
        resumingDeviceId = state.resuming?.deviceId.orEmpty(),
        failureScope =
            state.failure
                ?.target
                ?.scope
                ?.key
                .orEmpty(),
        failureDeviceId =
            state.failure
                ?.target
                ?.deviceId
                .orEmpty(),
        failureNotice =
            state.failure
                ?.notice
                ?.key()
                .orEmpty(),
        failureTryAgain = state.failure?.notice?.tryAgain == true,
        failureSiteName = state.failure?.siteName.orEmpty(),
    )
}
