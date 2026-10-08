package com.escendit.coldframe.core.thresholds

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.SensorThresholdsDto
import com.escendit.coldframe.core.api.SetSensorThresholdsRequestDto
import com.escendit.coldframe.core.api.ThresholdSideDto
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.lots.toSensorReading
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Set Thresholds from the app (Story 5.4, UX-DR45, UX-DR69, UX-DR84, UX-DR91): a Threshold column per Sensor of one
 * Lot, edited as a draft and saved with Save, dropped with Cancel ([close]). Only an Owner or Administrator edits;
 * a Member opens the same columns read-only. The Server stays the only validator (AD-14, AD-19): the checks here
 * only gate Save, and a 400 is shown with what did not change. A Sensor without a default low is offered the
 * Server's `proposedLow` when alerts are turned on, never a proposed high.
 */
public class ThresholdsEngine(
    private val api: ThresholdsApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
    private val onSaved: () -> Unit = {},
) {
    private val mutableState = MutableStateFlow<ThresholdsState>(ThresholdsState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<ThresholdsState> = mutableState.asStateFlow()

    private var generation = 0

    init {
        sites.onSessionEnded { reset() }
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current?.id }
                .distinctUntilChanged()
                .collect { reset() }
        }
    }

    private fun reset() {
        generation++
        mutableState.value = ThresholdsState.Idle
    }

    /** Opens the Thresholds of [lotId], shown under [name] until the Server answers; [sensorId] is the cell it came from. */
    public fun open(
        lotId: String,
        name: String,
        sensorId: String? = null,
    ) {
        val site = (sites.state.value as? SitesState.Ready)?.current ?: return
        reset()
        mutableState.value = ThresholdsState.Loading(site, lotId, name)
        val started = generation
        scope.launch { load(started, site.role, lotId, name, sensorId) }
    }

    /** Cancel: nothing is sent. */
    public fun close() {
        reset()
    }

    /** Try again after a failed open. */
    public fun retry() {
        val failed = mutableState.value as? ThresholdsState.Failed ?: return
        if (!failed.notice.tryAgain) return
        open(failed.lotId, failed.lotName)
    }

    /** Moves the low line of [sensorId]; a percentage snaps to its step within 0 to 100. */
    public fun setLow(
        sensorId: String,
        value: Double,
    ) {
        edit(sensorId) { it.copy(low = it.snap(value)) }
    }

    /** Moves the high line of [sensorId]. */
    public fun setHigh(
        sensorId: String,
        value: Double,
    ) {
        edit(sensorId) { it.copy(high = it.snap(value)) }
    }

    /** The low typed as text (`,` or `.`); text that is no number leaves it as it is. */
    public fun setLowText(
        sensorId: String,
        text: String,
    ) {
        parse(text)?.let { setLow(sensorId, it) }
    }

    /** The high typed as text; an empty text clears it, text that is no number leaves it as it is. */
    public fun setHighText(
        sensorId: String,
        text: String,
    ) {
        if (text.isBlank()) clearHigh(sensorId) else parse(text)?.let { setHigh(sensorId, it) }
    }

    /** "Add high": starts the high line above the low. */
    public fun addHigh(sensorId: String) {
        edit(sensorId) { column ->
            if (column.high != null) {
                column
            } else {
                val step = column.step ?: 1.0
                val base = column.low ?: column.current ?: column.trackMin
                val start = if (column.isPercent) maxOf(base + step, DEFAULT_HIGH_PERCENT) else base + step
                column.copy(high = column.snap(start))
            }
        }
    }

    /** Clears the high: no high never alerts. */
    public fun clearHigh(sensorId: String) {
        edit(sensorId) { it.copy(high = null) }
    }

    /** Turns alerts on: the low the Server has, else its proposal, else the middle of the track; never a high. */
    public fun turnOnAlerts(sensorId: String) {
        edit(sensorId) { column ->
            if (column.low != null) {
                column
            } else {
                column.copy(
                    low =
                        column.snap(
                            column.originalLow ?: column.proposedLow ?: (column.trackMin + column.trackMax) / 2,
                        ),
                )
            }
        }
    }

    /** Turns alerts off: no low, and so no high either. */
    public fun turnOffAlerts(sensorId: String) {
        edit(sensorId) { it.copy(low = null, high = null) }
    }

    /** Save: sends the sides that changed, one Sensor after the other; an error keeps every edit. */
    public fun save() {
        val ready = mutableState.value as? ThresholdsState.Ready ?: return
        if (!ready.canSave) return
        mutableState.value = ready.copy(working = true, notice = null)
        val started = generation
        scope.launch { saveAll(started, ready.site.id) }
    }

    private suspend fun saveAll(
        started: Int,
        siteId: String,
    ) {
        while (true) {
            val ready = mutableState.value as? ThresholdsState.Ready ?: return
            val column = ready.columns.firstOrNull { it.dirty }
            if (column == null) {
                mutableState.value = ready.copy(working = false, saved = true, notice = null)
                onSaved()
                return
            }
            val result = api.setSensorThresholds(siteId, column.sensorId, request(column))
            if (started != generation) return
            when (result) {
                is ApiResult.Ok -> {
                    val now = result.value
                    replace(column.sensorId) {
                        it.copy(
                            low = now.low.value,
                            high = now.high.value,
                            originalLow = now.low.value,
                            originalHigh = now.high.value,
                            proposedLow = now.proposedLow,
                        )
                    }
                }

                is ApiResult.Failed -> {
                    if (result.failure == ApiFailure.Unauthorized) {
                        reset()
                        sites.forget()
                        return
                    }
                    val current = mutableState.value as? ThresholdsState.Ready ?: return
                    // Nothing is lost: the drafts stay, and a retry sends what is still unsaved.
                    mutableState.value = current.copy(working = false, notice = saveNoticeOf(result.failure))
                    return
                }
            }
        }
    }

    private fun request(column: ThresholdColumn): SetSensorThresholdsRequestDto =
        SetSensorThresholdsRequestDto(
            low = side(column.low, column.originalLow),
            high = side(column.high, column.originalHigh),
        )

    private fun side(
        draft: Double?,
        original: Double?,
    ): ThresholdSideDto? =
        when {
            draft == original -> null
            draft == null -> ThresholdSideDto(CLEARED)
            else -> ThresholdSideDto(OVERRIDE, draft)
        }

    private fun edit(
        sensorId: String,
        change: (ThresholdColumn) -> ThresholdColumn,
    ) {
        val ready = mutableState.value as? ThresholdsState.Ready ?: return
        if (!ready.canEdit || ready.working || ready.saved) return
        mutableState.value =
            ready.copy(
                columns = ready.columns.map { if (it.sensorId == sensorId) change(it) else it },
                notice = null,
            )
    }

    private fun replace(
        sensorId: String,
        change: (ThresholdColumn) -> ThresholdColumn,
    ) {
        val ready = mutableState.value as? ThresholdsState.Ready ?: return
        mutableState.value = ready.copy(columns = ready.columns.map { if (it.sensorId == sensorId) change(it) else it })
    }

    private suspend fun load(
        started: Int,
        role: SiteRole,
        lotId: String,
        name: String,
        focus: String?,
    ) {
        val site = (mutableState.value as? ThresholdsState.Loading)?.site ?: return
        var lotResult = api.getLot(site.id, lotId)
        if (started != generation) return
        if (lotResult is ApiResult.Failed && lotResult.failure.transport) {
            lotResult = api.getLot(site.id, lotId)
            if (started != generation) return
        }
        val lot =
            when (lotResult) {
                is ApiResult.Ok -> lotResult.value
                is ApiResult.Failed -> return failedOpen(site, lotId, name, lotResult.failure)
            }
        val sensors =
            lot.sensors
                .orEmpty()
                .mapNotNull { it.toSensorReading() }
                .filter { it.sensorId != null }
        if (sensors.isEmpty()) {
            mutableState.value = ThresholdsState.Failed(site, lotId, lot.name, ThresholdsNotice.NoSensor)
            return
        }
        val columns = mutableListOf<ThresholdColumn>()
        for (sensor in sensors) {
            val result = api.getSensorThresholds(site.id, sensor.sensorId!!)
            if (started != generation) return
            val dto =
                when (result) {
                    is ApiResult.Ok -> result.value
                    is ApiResult.Failed -> return failedOpen(site, lotId, lot.name, result.failure)
                }
            columns += column(sensor.sensorId, sensor, dto)
        }
        mutableState.value =
            ThresholdsState.Ready(
                site = site,
                lotId = lotId,
                lotName = lot.name,
                canEdit = role >= SiteRole.Administrator,
                columns = columns,
                working = false,
                notice = null,
                saved = false,
                focusSensorId = focus,
            )
    }

    private fun column(
        sensorId: String,
        reading: com.escendit.coldframe.core.lots.SensorReading,
        dto: SensorThresholdsDto,
    ): ThresholdColumn {
        val unit = SensorUnit.fromServer(dto.unit) ?: reading.unit
        return ThresholdColumn(
            sensorId = sensorId,
            quantity = reading.quantity,
            unit = unit,
            low = dto.low.value,
            high = dto.high.value,
            originalLow = dto.low.value,
            originalHigh = dto.high.value,
            proposedLow = dto.proposedLow,
            current = reading.value.takeIf { reading.unit == unit },
        )
    }

    private fun failedOpen(
        site: com.escendit.coldframe.core.sites.SiteSummary,
        lotId: String,
        name: String,
        failure: ApiFailure,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            reset()
            sites.forget()
            return
        }
        mutableState.value = ThresholdsState.Failed(site, lotId, name, openNoticeOf(failure))
    }

    private fun openNoticeOf(failure: ApiFailure): ThresholdsNotice =
        when (failure) {
            ApiFailure.Forbidden -> ThresholdsNotice.Forbidden
            ApiFailure.NotFound -> ThresholdsNotice.NotFound
            ApiFailure.Unreachable -> ThresholdsNotice.Unreachable
            ApiFailure.Certificate -> ThresholdsNotice.Certificate
            else -> ThresholdsNotice.Unexpected
        }

    private fun saveNoticeOf(failure: ApiFailure): ThresholdsNotice =
        when (failure) {
            ApiFailure.Validation -> ThresholdsNotice.Invalid
            ApiFailure.Forbidden -> ThresholdsNotice.Forbidden
            ApiFailure.NotFound -> ThresholdsNotice.NotFound
            ApiFailure.Certificate -> ThresholdsNotice.Certificate
            else -> ThresholdsNotice.NotSaved
        }

    private fun parse(text: String): Double? =
        text
            .trim()
            .replace(',', '.')
            .toDoubleOrNull()
            ?.takeIf { it.isFinite() }

    private companion object {
        const val OVERRIDE = "override"
        const val CLEARED = "cleared"
        const val DEFAULT_HIGH_PERCENT = 70.0
    }
}
