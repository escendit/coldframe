package com.escendit.coldframe.core.thresholds

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.SensorThresholdsDto
import com.escendit.coldframe.core.api.SetSensorThresholdsRequestDto
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.sites.SiteSummary
import kotlin.math.roundToLong

/** The Thresholds calls; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface ThresholdsApi {
    /** `getLot`: the Lot with its Sensors (`sensorId`) and their newest Readings. */
    public suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto>

    /** `getSensorThresholds` (Member): the Thresholds in force and the Server's proposed low. */
    public suspend fun getSensorThresholds(
        siteId: String,
        sensorId: String,
    ): ApiResult<SensorThresholdsDto>

    /** `setSensorThresholds` (Admin+): the sides that changed; a side left out stays. */
    public suspend fun setSensorThresholds(
        siteId: String,
        sensorId: String,
        request: SetSensorThresholdsRequestDto,
    ): ApiResult<SensorThresholdsDto>
}

/**
 * The Threshold column of one Sensor (UX-DR45): a vertical track with the low line, the current Reading marker and
 * the dashed "no high" marker. [low] and [high] are the draft being edited, [originalLow] and [originalHigh] what the
 * Server has. A Sensor with no [low] is watched only (no alerts). Calibrated soil moves in 5 % steps within 0 to 100 %
 * ([step]); the Server stays the only validator, the checks here only gate Save.
 */
public data class ThresholdColumn(
    val sensorId: String,
    val quantity: SensorQuantity,
    val unit: SensorUnit,
    val low: Double?,
    val high: Double?,
    val originalLow: Double?,
    val originalHigh: Double?,
    /** The Server's suggestion for a Sensor with no default low; offered when alerts are turned on, never a high. */
    val proposedLow: Double?,
    /** The newest Reading in this column's unit, or `null` when it is not in it (raw soil, no Reading). */
    val current: Double?,
) {
    /** Calibrated soil and humidity are percentages on a 0 to 100 track. */
    val isPercent: Boolean get() = unit == SensorUnit.Percent

    /** 5 for calibrated soil, 1 for humidity, none for a unit with a free scale. */
    val step: Double?
        get() =
            when {
                isPercent && quantity == SensorQuantity.SoilMoisture -> SOIL_STEP
                isPercent -> 1.0
                else -> null
            }

    /** The track runs from [trackMin] to [trackMax]: 0 to 100 for a percentage, else the values it shows with room around. */
    val trackMin: Double get() = if (isPercent) 0.0 else span().first

    val trackMax: Double get() = if (isPercent) PERCENT_MAX else span().second

    /** Dragging needs a fixed range, so only a percentage can be dragged; the others are typed. */
    val draggable: Boolean get() = isPercent

    /** The column is watched only: no low, so nothing alerts. */
    val alerting: Boolean get() = low != null

    /** "Low must stay below high." appears inline and Save is disabled. */
    val lowMustStayBelowHigh: Boolean get() = low != null && high != null && low >= high

    /** A high without a low: low is required on an alerting Sensor. */
    val lowRequired: Boolean get() = high != null && low == null

    val valid: Boolean get() = !lowMustStayBelowHigh && !lowRequired

    val dirty: Boolean get() = low != originalLow || high != originalHigh

    /** Turning alerts on offers the Server's proposal when the Sensor has no default (UX: never a proposed high). */
    val offersProposal: Boolean get() = low == null && originalLow == null && proposedLow != null

    private fun span(): Pair<Double, Double> {
        val shown = listOfNotNull(low, high, originalLow, originalHigh, proposedLow, current)
        val min = shown.minOrNull() ?: 0.0
        val max = shown.maxOrNull() ?: 1.0
        val pad = maxOf((max - min) * PAD, 1.0)
        return (min - pad) to (max + pad)
    }

    internal fun snap(value: Double): Double {
        val ranged = if (isPercent) value.coerceIn(0.0, PERCENT_MAX) else value
        val unitStep = step ?: return (ranged * TENTHS).roundToLong() / TENTHS
        return (ranged / unitStep).roundToLong() * unitStep
    }

    internal companion object {
        const val SOIL_STEP: Double = 5.0
        const val PERCENT_MAX: Double = 100.0
        private const val PAD: Double = 0.25
        private const val TENTHS: Double = 10.0
    }
}

/** Why Thresholds could not open or were not saved. The copy says what happened, what did not change and what to do next. */
public enum class ThresholdsNotice(
    public val tryAgain: Boolean,
) {
    /** 400: the Server refused the values (low must stay below high, or out of range). Nothing changed; adjust and save again. */
    Invalid(false),

    /** 403: the Role is too low, or was changed meanwhile; nothing changed. */
    Forbidden(false),

    /** 404: the Lot or Sensor is gone. */
    NotFound(false),

    /** The Lot has no Sensor to set Thresholds on (yet): its Node has not declared one. */
    NoSensor(true),

    /** Not delivered: the edits are kept here and nothing was saved. Retry. */
    NotSaved(true),
    Unreachable(true),
    Certificate(false),
    Unexpected(true),
}

/** Thresholds of one Lot's Sensors. */
public sealed interface ThresholdsState {
    /** Not open. */
    public data object Idle : ThresholdsState

    public data class Loading(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
    ) : ThresholdsState

    public data class Failed(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
        val notice: ThresholdsNotice,
    ) : ThresholdsState

    /**
     * One [ThresholdColumn] per Sensor. [canEdit] is true for an Owner or Administrator; a Member sees the same
     * columns read-only and no edit control (UX-DR84). [working] while Save is in flight; [notice] says why a save did
     * not happen (the edits are kept). [saved] once everything was saved: the shells close and Lot detail reads again.
     * [focusSensorId] is the Sensor cell the screen was opened from.
     */
    public data class Ready(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
        val canEdit: Boolean,
        val columns: List<ThresholdColumn>,
        val working: Boolean,
        val notice: ThresholdsNotice?,
        val saved: Boolean,
        val focusSensorId: String?,
    ) : ThresholdsState {
        val dirty: Boolean get() = columns.any { it.dirty }

        /** Save is enabled: something changed, every column is valid and nothing is in flight. */
        val canSave: Boolean get() = canEdit && dirty && columns.all { it.valid } && !working && !saved
    }
}

/** The Site of the screen, or `null` while [ThresholdsState.Idle]. */
public val ThresholdsState.site: SiteSummary?
    get() =
        when (this) {
            ThresholdsState.Idle -> null
            is ThresholdsState.Loading -> site
            is ThresholdsState.Failed -> site
            is ThresholdsState.Ready -> site
        }
