package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.devices.DevicesState

/**
 * [LotsState] flattened for Swift. Enum values cross as catalogue key suffixes (`owner`,
 * `lotClaimed`, `tooLong`, …), the Lots as parallel lists in the Server's order, and statuses
 * as contract values (`noNode`). No token, URL or Idempotency-Key crosses this boundary.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`. [notice] is the load failure (the Sites
 * load keys); [actionNotice] is the last change that did not happen, with [actionNoticeSubject]
 * the Site name for `forbidden` and the Lot name for `lotClaimed`.
 *
 * The overview fields are [LotsOverview] flattened, built for the `now` the snapshot was taken
 * at; they are empty unless `ready`. Times are epoch milliseconds. An optional number crosses as
 * a decimal string that is empty when absent (`"1791270120000"`, `"20"`, `""`).
 *
 * - Stale mode: [stale], [staleReason] (`cached`, `unreachable` or `null`), [refreshing],
 *   [fetchedAtEpochMs] (the last successful refresh: "as of", "Last data"; 0 unless `ready`) and
 *   the age [staleAgeDays] / [staleAgeHours] / [staleAgeMinutes] (all 0 unless stale).
 * - Headline: [headline] is `noReadings`, `needsWater`, `cantBeRead`, `paused` or
 *   `nothingNeedsWater` (`null` unless `ready`), with [headlineCount], [headlineLotName] (one Lot
 *   needs water), [headlinePausedUntil] (every Site-paused Lot shares this end) and
 *   [headlinePausedInk].
 * - Counts subline: [countStatuses] (contract values, in order) with [countValues].
 * - Tiles, one entry per Lot in every `lot…` list: [lotVariants] (`needsWater`, `ok`, `unknown`,
 *   `needsCalibration`, `paused`, `noNode`, `stale`), [lotLabels] (`needsWater`, `ok`, `silent`,
 *   `hubSilent`, `needsCalibration`, `paused`, `pausedBySite`, `noNode`), [lotValues] (`soil`,
 *   `duration`, `raw`, `dash`, `plus`, `none`), [lotFoots] (`reading`, `wasPercentAt`,
 *   `lastReading`, `noReadingsYet`, `noPercentUntilCalibrated`, `pausedUntil`, `paused`,
 *   `addNode`, `asOf`, `none`), [lotSpokens] (`needsWater`, `ok`, `nodeSilent`, `hubSilent`,
 *   `needsCalibration`, `pausedUntil`, `pausedWithSite`, `paused`, `noNode`, `stale`) and their
 *   parts [lotSoilPercents], [lotLowPercents], [lotReadingAts], [lotDurationSinces],
 *   [lotDurationValues] with [lotDurationUnits] (`minutes`, `hours`, `days`, or empty),
 *   [lotPausedBySite], [lotPausedUntils] and [lotOpensAddNode]. A stale tile's "as of" is
 *   [fetchedAtEpochMs].
 * - Site menu: [menuItems] (`siteSettings`, …) and [menuEnabled], false in stale mode ("Needs
 *   your Server"). Use these instead of the Sites snapshot's menu while `ready`.
 */
public data class LotsSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val siteName: String?,
    val role: String?,
    val canRenameSite: Boolean,
    val canEditLots: Boolean,
    val readOnlyNotice: Boolean,
    val siteNameDraft: String,
    val siteNameError: String?,
    val siteRenameWorking: Boolean,
    val lotIds: List<String>,
    val lotNames: List<String>,
    val lotStatuses: List<String>,
    val newLotName: String,
    val newLotNameError: String?,
    val createWorking: Boolean,
    val renamingLotId: String?,
    val renameDraft: String,
    val renameError: String?,
    val renameWorking: Boolean,
    val removingLotId: String?,
    val removingLotName: String?,
    val removeWorking: Boolean,
    val actionNotice: String?,
    val actionNoticeSubject: String?,
    /** Whether a *no Node* tile starts Add a Node: Administrators and Owners only. */
    val canAddNode: Boolean,
    val stale: Boolean,
    val staleReason: String?,
    val refreshing: Boolean,
    val fetchedAtEpochMs: Long,
    val staleAgeDays: Int,
    val staleAgeHours: Int,
    val staleAgeMinutes: Int,
    val headline: String?,
    val headlineCount: Int,
    val headlineLotName: String?,
    val headlinePausedUntil: String,
    val headlinePausedInk: Boolean,
    val countStatuses: List<String>,
    val countValues: List<Int>,
    val lotVariants: List<String>,
    val lotLabels: List<String>,
    val lotValues: List<String>,
    val lotFoots: List<String>,
    val lotSpokens: List<String>,
    val lotSoilPercents: List<String>,
    val lotLowPercents: List<String>,
    val lotReadingAts: List<String>,
    val lotDurationSinces: List<String>,
    val lotDurationValues: List<String>,
    val lotDurationUnits: List<String>,
    val lotPausedBySite: List<Boolean>,
    val lotPausedUntils: List<String>,
    val lotOpensAddNode: List<Boolean>,
    val menuItems: List<String>,
    val menuEnabled: Boolean,
)

/**
 * A [LotsEvent] for Swift: [kind] is `enteredStale` ("Can't reach your Server. Showing data from
 * ‹t›.", with [fetchedAtEpochMs]) or `leftStale` ("Live again.", [fetchedAtEpochMs] 0).
 */
public data class LotsEventSnapshot(
    val kind: String,
    val siteId: String,
    val fetchedAtEpochMs: Long,
)

/** The snapshot of [event]. */
public fun snapshotOf(event: LotsEvent): LotsEventSnapshot =
    when (event) {
        is LotsEvent.EnteredStale -> LotsEventSnapshot("enteredStale", event.siteId, event.fetchedAtEpochMs)
        is LotsEvent.LeftStale -> LotsEventSnapshot("leftStale", event.siteId, 0)
    }

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

private fun Number?.text(): String = this?.toString().orEmpty()

/** The snapshot of [state], with ages and durations measured at [nowEpochMs]. */
public fun snapshotOf(
    state: LotsState,
    nowEpochMs: Long,
): LotsSnapshot {
    val surface =
        when (state) {
            LotsState.Idle -> "idle"
            is LotsState.Loading -> "loading"
            is LotsState.Failed -> "failed"
            is LotsState.Ready -> "ready"
        }
    val site = state.site
    val ready = state as? LotsState.Ready
    val settings = ready?.settings
    val failed = state as? LotsState.Failed
    val overview = ready?.let { LotsOverview.of(it, nowEpochMs) }
    val tiles = overview?.tiles.orEmpty()
    val menu = state.siteMenu()
    return LotsSnapshot(
        surface = surface,
        notice = failed?.notice?.key(),
        noticeTryAgain = failed?.notice?.tryAgain == true,
        siteId = site?.id,
        siteName = site?.name,
        role = site?.role?.key(),
        canRenameSite = settings?.canRenameSite == true,
        canEditLots = settings?.canEditLots == true,
        readOnlyNotice = settings?.readOnlyNotice == true,
        siteNameDraft = ready?.siteName?.draft.orEmpty(),
        siteNameError = ready?.siteName?.error?.key(),
        siteRenameWorking = ready?.siteName?.working == true,
        lotIds = ready?.lots?.map { it.id }.orEmpty(),
        lotNames = ready?.lots?.map { it.name }.orEmpty(),
        lotStatuses = ready?.lots?.map { it.status.key }.orEmpty(),
        newLotName = ready?.create?.name.orEmpty(),
        newLotNameError = ready?.create?.error?.key(),
        createWorking = ready?.create?.working == true,
        renamingLotId = ready?.renaming?.lotId,
        renameDraft = ready?.renaming?.draft.orEmpty(),
        renameError = ready?.renaming?.error?.key(),
        renameWorking = ready?.renaming?.working == true,
        removingLotId = ready?.removing?.lotId,
        removingLotName = ready?.removing?.lotName,
        removeWorking = ready?.removing?.working == true,
        actionNotice = ready?.notice?.kind?.key(),
        actionNoticeSubject = ready?.notice?.subject,
        canAddNode = site?.let { DevicesState.canAddNode(it.role) } == true,
        stale = overview?.stale == true,
        staleReason = ready?.staleReason?.key(),
        refreshing = overview?.refreshing == true,
        fetchedAtEpochMs = overview?.fetchedAtEpochMs ?: 0,
        staleAgeDays = overview?.staleAge?.days ?: 0,
        staleAgeHours = overview?.staleAge?.hours ?: 0,
        staleAgeMinutes = overview?.staleAge?.minutes ?: 0,
        headline = overview?.headline?.kind?.key(),
        headlineCount = overview?.headline?.count ?: 0,
        headlineLotName = overview?.headline?.lotName,
        headlinePausedUntil = overview?.headline?.pausedUntilEpochMs.text(),
        headlinePausedInk = overview?.headline?.pausedInk == true,
        countStatuses = overview?.counts?.map { it.status.key }.orEmpty(),
        countValues = overview?.counts?.map { it.count }.orEmpty(),
        lotVariants = tiles.map { it.variant.key() },
        lotLabels = tiles.map { it.label.key() },
        lotValues = tiles.map { it.value.key() },
        lotFoots = tiles.map { it.foot.key() },
        lotSpokens = tiles.map { it.spoken.key() },
        lotSoilPercents = tiles.map { it.soilPercent.text() },
        lotLowPercents = tiles.map { it.lowPercent.text() },
        lotReadingAts = tiles.map { it.readingAtEpochMs.text() },
        lotDurationSinces = tiles.map { it.durationSinceEpochMs.text() },
        lotDurationValues = tiles.map { it.duration?.value.text() },
        lotDurationUnits =
            tiles.map {
                it.duration
                    ?.unit
                    ?.key()
                    .orEmpty()
            },
        lotPausedBySite = tiles.map { it.pausedBySite },
        lotPausedUntils = tiles.map { it.pausedUntilEpochMs.text() },
        lotOpensAddNode = tiles.map { it.opensAddNode },
        menuItems = menu.map { it.action.key() },
        menuEnabled = menu.all { it.enabled },
    )
}
