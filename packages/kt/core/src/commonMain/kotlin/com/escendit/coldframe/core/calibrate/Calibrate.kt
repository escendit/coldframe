package com.escendit.coldframe.core.calibrate

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.CalibrationDto
import com.escendit.coldframe.core.api.CalibrationStateDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.sites.SiteSummary

/** The Calibrate calls; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface CalibrateApi {
    /** `getLot`: the Lot with its Sensors (`sensorId`, `calibratable`) and its status. */
    public suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto>

    /** `getSensorCalibration` (Admin+): the kept point and the recent stored Readings. */
    public suspend fun getSensorCalibration(
        siteId: String,
        sensorId: String,
    ): ApiResult<CalibrationStateDto>

    /** `calibrateSensor` (Admin+): a dry and/or a wet point, each a stored Reading's `reading_seq`. */
    public suspend fun calibrateSensor(
        siteId: String,
        sensorId: String,
        dryReadingSeq: Long?,
        wetReadingSeq: Long?,
    ): ApiResult<CalibrationDto>
}

/** The steps of Calibrate: the probe in dry soil, then in water, then the confirmation. */
public enum class CalibrateStep(
    public val number: Int,
) {
    Dry(1),
    Wet(2),
    Confirm(3),
}

/** A recent stored Reading of the Sensor: [readingSeq] is what a point names, [rawValue] what it measured. */
public data class CalibrationReading(
    val readingSeq: Long,
    val rawValue: Long,
    val measuredAtEpochMs: Long?,
)

/** Why a Calibrate step did not happen or the flow could not open. The copy says what happened, what did not change and what to do next. */
public enum class CalibrateNotice(
    public val tryAgain: Boolean,
) {
    /** 400: the dry and wet raw values are too close together. Nothing changed; take the wet point in water. */
    Indistinct(false),

    /** 503 `calibration-not-delivered`: the point is kept here, but the save was not confirmed. Retry. */
    NotDelivered(true),

    /** 403: the Role is too low; nothing changed. */
    Forbidden(false),

    /** 404: the Lot or Sensor is gone. */
    NotFound(false),

    /** The Lot has no Sensor that can be calibrated (yet): its Node has not sent a Reading. */
    NoSensor(true),
    Unreachable(true),
    Certificate(false),
    Unexpected(true),
}

/** What the shells announce (UX-DR105): never the waiting text, only these. */
public enum class CalibrateAnnouncementKind {
    /** Polite: "New Reading 07:17, raw 612. Record dry is available." */
    FreshReading,

    /** Polite: "Tomatoes reads about 40 percent." */
    FirstPercent,
}

/** One announcement, made once per [id]. */
public data class CalibrateAnnouncement(
    val id: Int,
    val kind: CalibrateAnnouncementKind,
    val step: CalibrateStep,
    val rawValue: Long?,
    val atEpochMs: Long?,
    val lotName: String,
    val percent: Int?,
)

/** Calibrate of one Lot's soil Sensor. */
public sealed interface CalibrateState {
    /** Not open. */
    public data object Idle : CalibrateState

    /** The Lot and its Sensor are being read. */
    public data class Loading(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
    ) : CalibrateState

    /** The flow could not open; [notice] says why. */
    public data class Failed(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
        val notice: CalibrateNotice,
    ) : CalibrateState

    /**
     * The Lot's Node is paused: nothing is waited for. Readings resume after the Pause ends.
     * [bySite] is true when the Site's Pause is in force. [offersResume] is true only for an
     * Administrator or Owner when the Pause is the Device's own.
     */
    public data class Paused(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
        val bySite: Boolean,
        val offersResume: Boolean,
    ) : CalibrateState

    /**
     * The two-step flow. [stepStartedAtEpochMs] is when the current step started (the injected clock);
     * a Reading counts as fresh only when it was taken after that. [readings] are the recent stored
     * Readings, newest first; [fresh] is the newest fresh one; [picked] the `reading_seq` the person chose
     * from the list. [dryRaw] and [wetRaw] are the recorded points. [percent] appears on the confirmation
     * only once the Server stored a calibrated Reading; until then [waitingForPercent] is true.
     */
    public data class Ready(
        val site: SiteSummary,
        val lotId: String,
        val lotName: String,
        val sensorId: String,
        val step: CalibrateStep,
        val stepStartedAtEpochMs: Long,
        val readings: List<CalibrationReading>,
        val fresh: CalibrationReading?,
        val picked: Long?,
        val dryRaw: Long?,
        val wetRaw: Long?,
        /** The `reading_seq` of the dry point recorded in this flow; that Reading never counts as fresh for wet. */
        val recordedSeq: Long?,
        val working: Boolean,
        val notice: CalibrateNotice?,
        val percent: Int?,
        val announcement: CalibrateAnnouncement?,
    ) : CalibrateState {
        /** The newest stored Reading: the waiting panel's "last raw value and time". */
        val lastReading: CalibrationReading? get() = readings.firstOrNull()

        /** The Reading "Record dry" / "Record wet" would record: the one picked, else the fresh one. */
        val candidate: CalibrationReading?
            get() = readings.firstOrNull { it.readingSeq == picked } ?: fresh

        /** Whether the step's record button is enabled. */
        val canRecord: Boolean get() = step != CalibrateStep.Confirm && !working && candidate != null

        /** The confirmation shows "% appears with the next Reading" until [percent] is known. */
        val waitingForPercent: Boolean get() = step == CalibrateStep.Confirm && percent == null
    }
}

/** The Site of the flow, or `null` while [CalibrateState.Idle]. */
public val CalibrateState.site: SiteSummary?
    get() =
        when (this) {
            CalibrateState.Idle -> null
            is CalibrateState.Loading -> site
            is CalibrateState.Failed -> site
            is CalibrateState.Paused -> site
            is CalibrateState.Ready -> site
        }
