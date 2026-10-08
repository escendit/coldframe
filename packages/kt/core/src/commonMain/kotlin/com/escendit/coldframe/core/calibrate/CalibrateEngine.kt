package com.escendit.coldframe.core.calibrate

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.CalibrationReadingDto
import com.escendit.coldframe.core.api.CalibrationStateDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorReading
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.lots.SoilMoisture
import com.escendit.coldframe.core.lots.toSensorReading
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Calibrate from the app (Story 5.2, UX-DR66): dry, then wet, on one Lot's soil Sensor. The
 * Server keeps the dry point, so leaving and coming back resumes at wet. Each step waits for a
 * Reading taken after the step started by reading the Server's recent Readings every
 * [pollMs] while the flow is open (an explicit exception to "no polling"); the wait stops on
 * [close], on a Pause and on a failed open. Reference points are stored Readings named by their
 * `reading_seq`, sent over REST; there is no Bluetooth. Rules, roles and states live here and
 * the shells draw them (AD-14): a Role below Administrator never opens the flow.
 */
public class CalibrateEngine(
    private val api: CalibrateApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
    private val pollMs: Long = POLL_MS,
) {
    private val mutableState = MutableStateFlow<CalibrateState>(CalibrateState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<CalibrateState> = mutableState.asStateFlow()

    private var generation = 0
    private var announcements = 0
    private var poller: Job? = null

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
        poller?.cancel()
        poller = null
        mutableState.value = CalibrateState.Idle
    }

    /** Opens Calibrate for [lotId], shown under [name] until the Server answers. */
    public fun open(
        lotId: String,
        name: String,
    ) {
        val site = (sites.state.value as? SitesState.Ready)?.current ?: return
        reset()
        if (site.role < SiteRole.Administrator) {
            mutableState.value = CalibrateState.Failed(site, lotId, name, CalibrateNotice.Forbidden)
            return
        }
        mutableState.value = CalibrateState.Loading(site, lotId, name)
        val started = generation
        scope.launch { load(started, site.id, lotId, name) }
    }

    /** Leaves Calibrate: the wait stops, the Server keeps a recorded dry point. */
    public fun close() {
        reset()
    }

    /** Try again after a failed open. */
    public fun retry() {
        val failed = mutableState.value as? CalibrateState.Failed ?: return
        if (!failed.notice.tryAgain) return
        open(failed.lotId, failed.lotName)
    }

    /** Picks a stored Reading from the Recent Readings list instead of waiting; picking it again clears it. */
    public fun pick(readingSeq: Long) {
        val ready = mutableState.value as? CalibrateState.Ready ?: return
        if (ready.step == CalibrateStep.Confirm || ready.working) return
        if (ready.readings.none { it.readingSeq == readingSeq }) return
        mutableState.value = ready.copy(picked = readingSeq.takeIf { it != ready.picked }, notice = null)
    }

    /** Records the candidate Reading as the dry or the wet point of the current step. */
    public fun record() {
        val ready = mutableState.value as? CalibrateState.Ready ?: return
        val reading = ready.candidate ?: return
        if (!ready.canRecord) return
        val started = generation
        mutableState.value = ready.copy(working = true, notice = null)
        scope.launch {
            val dry = reading.readingSeq.takeIf { ready.step == CalibrateStep.Dry }
            val wet = reading.readingSeq.takeIf { ready.step == CalibrateStep.Wet }
            val result = api.calibrateSensor(ready.site.id, ready.sensorId, dry, wet)
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> recorded(ready, reading, result.value.calibrated, result.value.pendingDry?.rawValue)
                is ApiResult.Failed -> failedRecord(ready, result.failure)
            }
        }
    }

    private fun recorded(
        before: CalibrateState.Ready,
        reading: CalibrationReading,
        calibrated: Boolean,
        pendingDryRaw: Long?,
    ) {
        val current = mutableState.value as? CalibrateState.Ready ?: return
        if (before.step == CalibrateStep.Dry && !calibrated) {
            // The dry point is kept by the Server; the Sensor stays uncalibrated. Wet starts now.
            mutableState.value =
                current.copy(
                    step = CalibrateStep.Wet,
                    afterSeq = newestSeq(current.readings, reading.readingSeq),
                    afterMeasuredAtEpochMs = newestMeasuredAt(current.readings),
                    fresh = null,
                    picked = null,
                    dryRaw = pendingDryRaw ?: reading.rawValue,
                    recordedSeq = reading.readingSeq,
                    working = false,
                    notice = null,
                )
        } else {
            val dry = if (before.step == CalibrateStep.Wet) before.dryRaw else reading.rawValue
            val wet = if (before.step == CalibrateStep.Wet) reading.rawValue else before.wetRaw
            mutableState.value =
                current.copy(
                    step = CalibrateStep.Confirm,
                    afterSeq = newestSeq(current.readings, reading.readingSeq),
                    afterMeasuredAtEpochMs = newestMeasuredAt(current.readings),
                    fresh = null,
                    picked = null,
                    dryRaw = dry,
                    wetRaw = wet,
                    recordedSeq = null,
                    working = false,
                    notice = null,
                    percent = null,
                )
        }
        startPolling()
    }

    private fun failedRecord(
        before: CalibrateState.Ready,
        failure: ApiFailure,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            reset()
            sites.forget()
            return
        }
        val current = mutableState.value as? CalibrateState.Ready ?: return
        // Nothing advances; the chosen Reading stays chosen so a retry records the same point.
        mutableState.value =
            current.copy(
                working = false,
                notice = noticeOf(failure),
                picked = current.candidate?.readingSeq,
            )
    }

    private suspend fun load(
        started: Int,
        siteId: String,
        lotId: String,
        name: String,
    ) {
        val site = (mutableState.value as? CalibrateState.Loading)?.site ?: return
        var lotResult = api.getLot(siteId, lotId)
        if (started != generation) return
        if (lotResult is ApiResult.Failed && lotResult.failure.transport) {
            lotResult = api.getLot(siteId, lotId)
            if (started != generation) return
        }
        val lotDto =
            when (lotResult) {
                is ApiResult.Ok -> lotResult.value
                is ApiResult.Failed -> return failedOpen(site, lotId, name, lotResult.failure)
            }
        val lotName = lotDto.name
        if (lotDto.status == LotStatus.Paused.key) {
            val by = lotDto.pausedBy.orEmpty().mapNotNull(LotPauseSource::fromServer)
            mutableState.value =
                CalibrateState.Paused(
                    site = site,
                    lotId = lotId,
                    lotName = lotName,
                    bySite = LotPauseSource.Site in by,
                    offersResume = by == listOf(LotPauseSource.Device) && site.role >= SiteRole.Administrator,
                )
            return
        }
        val sensor = soilSensor(lotDto)
        val sensorId = sensor?.sensorId
        if (sensorId == null) {
            mutableState.value = CalibrateState.Failed(site, lotId, lotName, CalibrateNotice.NoSensor)
            return
        }
        val stateResult = api.getSensorCalibration(siteId, sensorId)
        if (started != generation) return
        val dto =
            when (stateResult) {
                is ApiResult.Ok -> stateResult.value
                is ApiResult.Failed -> return failedOpen(site, lotId, lotName, stateResult.failure)
            }
        // A kept dry point resumes at wet.
        val resumeAtWet = dto.pendingDry != null && dto.pendingWet == null
        mutableState.value =
            CalibrateState.Ready(
                site = site,
                lotId = lotId,
                lotName = lotName,
                sensorId = sensorId,
                step = if (resumeAtWet) CalibrateStep.Wet else CalibrateStep.Dry,
                afterSeq = newestSeq(dto.readings.map { it.toReading() }),
                afterMeasuredAtEpochMs = newestMeasuredAt(dto.readings.map { it.toReading() }),
                readings = dto.readings.map { it.toReading() },
                fresh = null,
                picked = null,
                dryRaw = dto.pendingDry?.rawValue,
                wetRaw = dto.pendingWet?.rawValue,
                recordedSeq = null,
                working = false,
                notice = null,
                percent = null,
                announcement = null,
            )
        startPolling()
    }

    private fun soilSensor(lot: LotDto): SensorReading? =
        lot.sensors
            .orEmpty()
            .mapNotNull { it.toSensorReading() }
            .firstOrNull { it.quantity == SensorQuantity.SoilMoisture && it.calibratable && it.sensorId != null }

    private fun failedOpen(
        site: SiteSummary,
        lotId: String,
        name: String,
        failure: ApiFailure,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            reset()
            sites.forget()
            return
        }
        mutableState.value = CalibrateState.Failed(site, lotId, name, noticeOf(failure))
    }

    // The wait: re-read while a step waits for a Reading, and on the confirmation for the first %.

    private fun startPolling() {
        poller?.cancel()
        val started = generation
        poller =
            scope.launch {
                while (started == generation) {
                    delay(pollMs)
                    if (started != generation) return@launch
                    tick(started)
                }
            }
    }

    private suspend fun tick(started: Int) {
        val ready = mutableState.value as? CalibrateState.Ready ?: return
        if (ready.working) return
        if (ready.step == CalibrateStep.Confirm) {
            watchForPercent(started, ready)
        } else {
            watchForReading(started, ready)
        }
    }

    private suspend fun watchForReading(
        started: Int,
        ready: CalibrateState.Ready,
    ) {
        val result = api.getSensorCalibration(ready.site.id, ready.sensorId)
        if (started != generation) return
        val dto: CalibrationStateDto = (result as? ApiResult.Ok)?.value ?: return unauthorizedOrKeep(result)
        val current = mutableState.value as? CalibrateState.Ready ?: return
        if (current.step != ready.step || current.working) return
        val readings = dto.readings.map { it.toReading() }
        val fresh =
            readings.firstOrNull {
                it.readingSeq > current.afterSeq && it.readingSeq != current.recordedSeq
            }
        val isNew = fresh != null && fresh.readingSeq != current.fresh?.readingSeq
        mutableState.value =
            current.copy(
                readings = readings,
                fresh = fresh,
                announcement = if (isNew) announce(current, fresh) else current.announcement,
            )
    }

    /** The newest `reading_seq` among [readings] and [also]: where a step's freshness starts (-1 when none). */
    private fun newestSeq(
        readings: List<CalibrationReading>,
        also: Long = -1,
    ): Long = maxOf(also, readings.maxOfOrNull { it.readingSeq } ?: -1)

    /** The newest Reading time among [readings] (0 when none): where the first percentage's freshness starts. */
    private fun newestMeasuredAt(readings: List<CalibrationReading>): Long =
        readings.maxOfOrNull { it.measuredAtEpochMs ?: 0 } ?: 0

    private fun announce(
        ready: CalibrateState.Ready,
        fresh: CalibrationReading?,
    ): CalibrateAnnouncement =
        CalibrateAnnouncement(
            id = ++announcements,
            kind = CalibrateAnnouncementKind.FreshReading,
            step = ready.step,
            rawValue = fresh?.rawValue,
            atEpochMs = fresh?.measuredAtEpochMs,
            lotName = ready.lotName,
            percent = null,
        )

    private suspend fun watchForPercent(
        started: Int,
        ready: CalibrateState.Ready,
    ) {
        val result = api.getLot(ready.site.id, ready.lotId)
        if (started != generation) return
        val lot: LotDto = (result as? ApiResult.Ok)?.value ?: return unauthorizedOrKeep(result)
        val soil =
            lot.sensors
                .orEmpty()
                .mapNotNull { it.toSensorReading() }
                .firstOrNull { it.sensorId == ready.sensorId && it.unit == SensorUnit.Percent }
        val at = soil?.measuredAtEpochMs
        val current = mutableState.value as? CalibrateState.Ready ?: return
        // A percentage counts only when it was stored after the Readings seen when the Calibration was saved.
        if (soil == null || at == null || at <= current.afterMeasuredAtEpochMs || current.percent != null) return
        val percent = SoilMoisture.rounded(soil.value)
        mutableState.value =
            current.copy(
                percent = percent,
                announcement =
                    CalibrateAnnouncement(
                        id = ++announcements,
                        kind = CalibrateAnnouncementKind.FirstPercent,
                        step = CalibrateStep.Confirm,
                        rawValue = null,
                        atEpochMs = at,
                        lotName = current.lotName,
                        percent = percent,
                    ),
            )
        poller?.cancel()
    }

    private fun unauthorizedOrKeep(result: ApiResult<*>) {
        if ((result as? ApiResult.Failed)?.failure == ApiFailure.Unauthorized) {
            reset()
            sites.forget()
        }
    }

    private fun CalibrationReadingDto.toReading(): CalibrationReading =
        CalibrationReading(readingSeq, rawValue, DevicesEngine.epochMsOf(measuredAt))

    private fun noticeOf(failure: ApiFailure): CalibrateNotice =
        when (failure) {
            ApiFailure.Validation -> CalibrateNotice.Indistinct
            ApiFailure.CalibrationNotDelivered -> CalibrateNotice.NotDelivered
            ApiFailure.Forbidden -> CalibrateNotice.Forbidden
            ApiFailure.NotFound -> CalibrateNotice.NotFound
            ApiFailure.Unreachable -> CalibrateNotice.Unreachable
            ApiFailure.Certificate -> CalibrateNotice.Certificate
            else -> CalibrateNotice.Unexpected
        }

    public companion object {
        /** How often a waiting step re-reads the Server. */
        public const val POLL_MS: Long = 3_000
    }
}
