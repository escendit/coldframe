package com.escendit.coldframe.core.thresholds

/**
 * [ThresholdsState] flattened for Swift: enum values cross as catalogue key suffixes (`soilMoisture`, `percent`,
 * `invalid`), and an optional number as a decimal string that is empty when absent. No token or URL crosses.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`; [notice] the [ThresholdsNotice] key with [noticeTryAgain]
 * (a failed open, or a save that did not happen). The rest is empty unless `ready`:
 * - [canEdit] (Owner or Administrator; a Member sees no edit control), [working], [dirty], [canSave], [saved] and
 *   [focusSensorId] (the Sensor cell the screen was opened from, empty when none).
 * - One entry per Sensor in the parallel lists [sensorIds], [quantities], [units] (`celsius`, `percent`, `kiloOhm`,
 *   `raw`), [lows] and [highs] (the draft, empty for none), [originalLows], [originalHighs], [proposedLows],
 *   [currents] (the newest Reading in the column's unit), [steps] (empty for a free scale), [trackMins], [trackMaxs],
 *   [draggables], [alerting], [lowMustStayBelowHigh], [lowRequired], [offersProposal].
 */
public data class ThresholdsSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val lotId: String?,
    val lotName: String?,
    val canEdit: Boolean,
    val working: Boolean,
    val dirty: Boolean,
    val canSave: Boolean,
    val saved: Boolean,
    val focusSensorId: String,
    val sensorIds: List<String>,
    val quantities: List<String>,
    val units: List<String>,
    val lows: List<String>,
    val highs: List<String>,
    val originalLows: List<String>,
    val originalHighs: List<String>,
    val proposedLows: List<String>,
    val currents: List<String>,
    val steps: List<String>,
    val trackMins: List<Double>,
    val trackMaxs: List<Double>,
    val draggables: List<Boolean>,
    val alerting: List<Boolean>,
    val lowMustStayBelowHigh: List<Boolean>,
    val lowRequired: List<Boolean>,
    val offersProposal: List<Boolean>,
)

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

private fun Double?.text(): String =
    this
        ?.let {
            if (it % 1.0 ==
                0.0
            ) {
                it.toLong().toString()
            } else {
                it.toString()
            }
        }.orEmpty()

/** The snapshot of [state]. */
public fun snapshotOf(state: ThresholdsState): ThresholdsSnapshot {
    val ready = state as? ThresholdsState.Ready
    val columns = ready?.columns.orEmpty()
    val notice =
        when (state) {
            is ThresholdsState.Failed -> state.notice
            is ThresholdsState.Ready -> state.notice
            else -> null
        }
    return ThresholdsSnapshot(
        surface =
            when (state) {
                ThresholdsState.Idle -> "idle"
                is ThresholdsState.Loading -> "loading"
                is ThresholdsState.Failed -> "failed"
                is ThresholdsState.Ready -> "ready"
            },
        notice = notice?.key(),
        noticeTryAgain = notice?.tryAgain == true,
        siteId = state.site?.id,
        lotId =
            when (state) {
                ThresholdsState.Idle -> null
                is ThresholdsState.Loading -> state.lotId
                is ThresholdsState.Failed -> state.lotId
                is ThresholdsState.Ready -> state.lotId
            },
        lotName =
            when (state) {
                ThresholdsState.Idle -> null
                is ThresholdsState.Loading -> state.lotName
                is ThresholdsState.Failed -> state.lotName
                is ThresholdsState.Ready -> state.lotName
            },
        canEdit = ready?.canEdit == true,
        working = ready?.working == true,
        dirty = ready?.dirty == true,
        canSave = ready?.canSave == true,
        saved = ready?.saved == true,
        focusSensorId = ready?.focusSensorId.orEmpty(),
        sensorIds = columns.map { it.sensorId },
        quantities = columns.map { it.quantity.key() },
        units = columns.map { it.unit.key() },
        lows = columns.map { it.low.text() },
        highs = columns.map { it.high.text() },
        originalLows = columns.map { it.originalLow.text() },
        originalHighs = columns.map { it.originalHigh.text() },
        proposedLows = columns.map { it.proposedLow.text() },
        currents = columns.map { it.current.text() },
        steps = columns.map { it.step.text() },
        trackMins = columns.map { it.trackMin },
        trackMaxs = columns.map { it.trackMax },
        draggables = columns.map { it.draggable },
        alerting = columns.map { it.alerting },
        lowMustStayBelowHigh = columns.map { it.lowMustStayBelowHigh },
        lowRequired = columns.map { it.lowRequired },
        offersProposal = columns.map { it.offersProposal },
    )
}
