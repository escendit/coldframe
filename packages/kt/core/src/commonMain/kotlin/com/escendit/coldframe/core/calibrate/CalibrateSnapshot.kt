package com.escendit.coldframe.core.calibrate

/**
 * [CalibrateState] flattened for Swift: enum values cross as catalogue key suffixes (`dry`,
 * `indistinct`, `freshReading`), times as Unix milliseconds, and an optional number as a decimal
 * string that is empty when absent. No token or URL crosses.
 *
 * [surface] is `idle`, `loading`, `failed`, `paused` or `ready`; [notice] the [CalibrateNotice] key
 * with [noticeTryAgain] (a failed open, or a step that did not happen). The rest is empty unless
 * `ready`:
 * - [step] (`dry`, `wet`, `confirm`), [stepNumber] 1 to 3, [working], [canRecord].
 * - The waiting panel: [lastRawValue] and [lastReadingAt] (empty without a Reading), [hasFresh].
 * - Recent Readings, newest first, in parallel lists: [readingSeqs], [readingRawValues], [readingAts],
 *   with [pickedSeq] (empty when none is picked).
 * - The recorded points [dryRaw] and [wetRaw] (empty when absent), [percent] (empty until the Server
 *   stored a calibrated Reading) and [waitingForPercent].
 * - The announcement ([announcementId] 0 for none), made once per id: [announcementKind],
 *   [announcementRaw], [announcementAt], [announcementPercent], [announcementLot], [announcementStep].
 * - For `paused`: [pausedBySite] and [offersResume] (Admin+ on the Device's own Pause).
 */
public data class CalibrateSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val lotId: String?,
    val lotName: String?,
    val step: String?,
    val stepNumber: Int,
    val working: Boolean,
    val canRecord: Boolean,
    val hasFresh: Boolean,
    val lastRawValue: String,
    val lastReadingAt: String,
    val readingSeqs: List<Long>,
    val readingRawValues: List<Long>,
    val readingAts: List<String>,
    val pickedSeq: String,
    val dryRaw: String,
    val wetRaw: String,
    val percent: String,
    val waitingForPercent: Boolean,
    /** The confirmation offers "Set Thresholds" for the same Lot (Story 5.4). */
    val offersThresholds: Boolean,
    val announcementId: Int,
    val announcementKind: String?,
    val announcementStep: String?,
    val announcementRaw: String,
    val announcementAt: String,
    val announcementPercent: String,
    val announcementLot: String?,
    val pausedBySite: Boolean,
    val offersResume: Boolean,
)

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

private fun Number?.text(): String = this?.toString().orEmpty()

/** The snapshot of [state]. */
public fun snapshotOf(state: CalibrateState): CalibrateSnapshot {
    val ready = state as? CalibrateState.Ready
    val paused = state as? CalibrateState.Paused
    val notice =
        when (state) {
            is CalibrateState.Failed -> state.notice
            is CalibrateState.Ready -> state.notice
            else -> null
        }
    val announcement = ready?.announcement
    return CalibrateSnapshot(
        surface =
            when (state) {
                CalibrateState.Idle -> "idle"
                is CalibrateState.Loading -> "loading"
                is CalibrateState.Failed -> "failed"
                is CalibrateState.Paused -> "paused"
                is CalibrateState.Ready -> "ready"
            },
        notice = notice?.key(),
        noticeTryAgain = notice?.tryAgain == true,
        siteId = state.site?.id,
        lotId =
            when (state) {
                CalibrateState.Idle -> null
                is CalibrateState.Loading -> state.lotId
                is CalibrateState.Failed -> state.lotId
                is CalibrateState.Paused -> state.lotId
                is CalibrateState.Ready -> state.lotId
            },
        lotName =
            when (state) {
                CalibrateState.Idle -> null
                is CalibrateState.Loading -> state.lotName
                is CalibrateState.Failed -> state.lotName
                is CalibrateState.Paused -> state.lotName
                is CalibrateState.Ready -> state.lotName
            },
        step = ready?.step?.key(),
        stepNumber = ready?.step?.number ?: 0,
        working = ready?.working == true,
        canRecord = ready?.canRecord == true,
        hasFresh = ready?.fresh != null,
        lastRawValue = ready?.lastReading?.rawValue.text(),
        lastReadingAt = ready?.lastReading?.measuredAtEpochMs.text(),
        readingSeqs = ready?.readings?.map { it.readingSeq }.orEmpty(),
        readingRawValues = ready?.readings?.map { it.rawValue }.orEmpty(),
        readingAts = ready?.readings?.map { it.measuredAtEpochMs.text() }.orEmpty(),
        pickedSeq = ready?.candidate?.readingSeq.text(),
        dryRaw = ready?.dryRaw.text(),
        wetRaw = ready?.wetRaw.text(),
        percent = ready?.percent.text(),
        waitingForPercent = ready?.waitingForPercent == true,
        offersThresholds = ready?.offersThresholds == true,
        announcementId = announcement?.id ?: 0,
        announcementKind = announcement?.kind?.key(),
        announcementStep = announcement?.step?.key(),
        announcementRaw = announcement?.rawValue.text(),
        announcementAt = announcement?.atEpochMs.text(),
        announcementPercent = announcement?.percent.text(),
        announcementLot = announcement?.lotName,
        pausedBySite = paused?.bySite == true,
        offersResume = paused?.offersResume == true,
    )
}
