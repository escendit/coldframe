package com.escendit.coldframe.core.calibrate

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.CalibrationDto
import com.escendit.coldframe.core.api.CalibrationReadingDto
import com.escendit.coldframe.core.api.CalibrationStateDto
import com.escendit.coldframe.core.api.CalibrationValueDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.NodeStatusDto
import com.escendit.coldframe.core.api.SensorReadingDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class FakeCalibrateApi : CalibrateApi {
    var lot: LotDto? = null
    var state = CalibrationStateDto(calibrated = false)
    var calibrateResult: ApiResult<CalibrationDto> = ApiResult.Ok(CalibrationDto(calibrated = false))
    var stateFailure: ApiFailure? = null
    val posts = mutableListOf<Pair<Long?, Long?>>()
    var stateReads = 0
    var lotReads = 0

    override suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto> {
        lotReads++
        return lot?.let { ApiResult.Ok(it) } ?: ApiResult.Failed(ApiFailure.NotFound)
    }

    override suspend fun getSensorCalibration(
        siteId: String,
        sensorId: String,
    ): ApiResult<CalibrationStateDto> {
        stateReads++
        stateFailure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(state)
    }

    override suspend fun calibrateSensor(
        siteId: String,
        sensorId: String,
        dryReadingSeq: Long?,
        wetReadingSeq: Long?,
    ): ApiResult<CalibrationDto> {
        posts += dryReadingSeq to wetReadingSeq
        return calibrateResult
    }
}

/** Story 5.2: Calibrate's engine: dry then wet, fresh Readings, resume, paused, roles, failures. */
class CalibrateEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeCalibrateApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var clock = 1_791_270_120_000L

    private val soil = "11111111-1111-7111-8111-111111111111"

    private fun lot(
        status: String = "needsCalibration",
        pausedBy: List<String>? = null,
        sensors: List<SensorReadingDto>? =
            listOf(SensorReadingDto("soil_moisture", 1840.0, "raw", "2026-10-06T07:00:00.000Z", soil, true)),
    ) = LotDto(
        id = "t",
        name = "Tomatoes",
        status = status,
        pausedBy = pausedBy,
        node = NodeStatusDto("7c19aaaaaaaaaaaa", 62, "charging", "2026-10-06T07:00:00.000Z"),
        sensors = sensors,
    )

    private fun reading(
        seq: Long,
        raw: Long,
        atMs: Long,
    ) = CalibrationReadingDto(
        seq,
        raw,
        kotlin.time.Instant
            .fromEpochMilliseconds(atMs)
            .toString(),
    )

    private fun TestScope.start(role: String = "Owner"): CalibrateEngine {
        sitesApi.sites += SiteDto("a", "Home", role)
        val sites =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(MapSettings()),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "k" },
                zones = { emptyList() },
            )
        runCurrent()
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return CalibrateEngine(api, sites, backgroundScope, pollMs = 1_000).also { runCurrent() }
    }

    private fun CalibrateEngine.ready() = assertIs<CalibrateState.Ready>(state.value)

    private fun TestScope.opened(): CalibrateEngine {
        api.lot = lot()
        val engine = start()
        engine.open("t", "Tomatoes")
        runCurrent()
        return engine
    }

    private fun TestScope.wait(
        engine: CalibrateEngine,
        ms: Long = 1_000,
    ) {
        clock += ms
        advanceTimeBy(ms)
        runCurrent()
    }

    @Test
    fun story52StartOpensTheDryStepWithTheLastReadingAndNothingFresh() =
        runTest {
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(7, 612, clock - 60_000)))
            val engine = opened()

            val ready = engine.ready()
            assertEquals(CalibrateStep.Dry, ready.step)
            assertEquals(612L, ready.lastReading?.rawValue)
            assertNull(ready.fresh)
            assertFalse(ready.canRecord)
            assertEquals(soil, ready.sensorId)
        }

    @Test
    fun story52AReadingStoredAfterTheStepStartedIsFreshWhateverThePhoneClockSays() =
        runTest {
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(7, 612, clock - 60_000)))
            val engine = opened()

            // The Node's time is far behind this phone's clock, but the Server stored the Reading later.
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    readings = listOf(reading(8, 640, clock - 3_600_000), reading(7, 612, clock - 60_000)),
                )
            wait(engine)

            assertEquals(8L, engine.ready().fresh?.readingSeq)
            assertTrue(engine.ready().canRecord)
        }

    @Test
    fun story52AReadingTakenAfterTheStepStartedEnablesRecordAndIsAnnouncedOnce() =
        runTest {
            val engine = opened()
            assertFalse(engine.ready().canRecord)
            assertNull(engine.ready().announcement)

            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(8, 612, clock + 500)))
            wait(engine)

            val ready = engine.ready()
            assertTrue(ready.canRecord)
            assertEquals(8L, ready.fresh?.readingSeq)
            val announcement = assertNotNull(ready.announcement)
            assertEquals(CalibrateAnnouncementKind.FreshReading, announcement.kind)
            assertEquals(612L, announcement.rawValue)
            assertEquals(CalibrateStep.Dry, announcement.step)

            // The same Reading again is not announced again.
            wait(engine)
            assertEquals(announcement.id, engine.ready().announcement?.id)
        }

    @Test
    fun story52AReadingFromBeforeTheStepNeverEnablesRecord() =
        runTest {
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(5, 700, clock - 1)))
            val engine = opened()

            wait(engine)

            assertFalse(engine.ready().canRecord)
            assertNull(engine.ready().announcement)
        }

    @Test
    fun story52PickingARecentReadingEnablesRecordWithoutWaiting() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    readings =
                        listOf(
                            reading(7, 612, clock - 60_000),
                            reading(
                                6,
                                640,
                                clock - 120_000,
                            ),
                        ),
                )
            val engine = opened()

            engine.pick(6)

            assertTrue(engine.ready().canRecord)
            assertEquals(640L, engine.ready().candidate?.rawValue)
            engine.pick(99)
            assertEquals(6L, engine.ready().picked)
        }

    @Test
    fun story52RecordDryPostsOnlyTheDryPointAndMovesToWet() =
        runTest {
            val engine = opened()
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(8, 3000, clock + 500)))
            wait(engine)
            api.calibrateResult =
                ApiResult.Ok(CalibrationDto(calibrated = false, pendingDry = CalibrationValueDto(3000)))

            engine.record()
            runCurrent()

            assertEquals(listOf<Pair<Long?, Long?>>(8L to null), api.posts)
            val ready = engine.ready()
            assertEquals(CalibrateStep.Wet, ready.step)
            assertEquals(3000L, ready.dryRaw)
            assertFalse(ready.canRecord)
        }

    @Test
    fun story52TheDryReadingIsNotFreshForTheWetStepButALaterOneIs() =
        runTest {
            val engine = opened()
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(8, 3000, clock + 500)))
            wait(engine)
            api.calibrateResult =
                ApiResult.Ok(CalibrationDto(calibrated = false, pendingDry = CalibrationValueDto(3000)))
            engine.record()
            runCurrent()
            // The dry Reading is taken later than the wet step started on the client clock (skew).
            api.state = CalibrationStateDto(calibrated = false, readings = listOf(reading(8, 3000, clock + 5_000)))
            wait(engine, 2_000)
            assertFalse(engine.ready().canRecord)

            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    readings =
                        listOf(
                            reading(9, 1200, clock + 9_000),
                            reading(
                                8,
                                3000,
                                clock + 5_000,
                            ),
                        ),
                )
            wait(engine, 10_000)

            assertEquals(9L, engine.ready().fresh?.readingSeq)
            assertTrue(engine.ready().canRecord)
        }

    @Test
    fun story52RecordWetConfirmsWithBothRawValuesAndNoPercentYet() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    pendingDry = CalibrationValueDto(3000),
                    readings =
                        listOf(
                            reading(
                                9,
                                1200,
                                clock - 1_000,
                            ),
                        ),
                )
            val engine = opened()
            engine.pick(9)
            api.calibrateResult =
                ApiResult.Ok(
                    CalibrationDto(
                        calibrated = true,
                        calibrationId = "c",
                        dry = CalibrationValueDto(3000),
                        wet = CalibrationValueDto(1200),
                    ),
                )

            engine.record()
            runCurrent()

            assertEquals(listOf<Pair<Long?, Long?>>(null to 9L), api.posts)
            val ready = engine.ready()
            assertEquals(CalibrateStep.Confirm, ready.step)
            assertEquals(3000L to 1200L, ready.dryRaw to ready.wetRaw)
            assertTrue(ready.waitingForPercent)
            assertNull(ready.percent)
            // Story 5.4: the confirmation leads on to Thresholds for the same Lot.
            assertTrue(ready.offersThresholds)
            assertTrue(snapshotOf(ready).offersThresholds)
        }

    @Test
    fun story54OnlyTheConfirmationOffersThresholds() =
        runTest {
            val engine = opened()

            assertFalse(engine.ready().offersThresholds)
        }

    @Test
    fun story52TheConfirmationUpdatesInPlaceWhenACalibratedReadingArrivesAndAnnouncesIt() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    pendingDry = CalibrationValueDto(3000),
                    readings =
                        listOf(
                            reading(
                                9,
                                1200,
                                clock - 1_000,
                            ),
                        ),
                )
            val engine = opened()
            engine.pick(9)
            api.calibrateResult =
                ApiResult.Ok(
                    CalibrationDto(calibrated = true, dry = CalibrationValueDto(3000), wet = CalibrationValueDto(1200)),
                )
            engine.record()
            runCurrent()

            // A raw Reading is not a percentage.
            wait(engine)
            assertNull(engine.ready().percent)

            // A percentage stored before the Calibration was saved does not count either.
            api.lot =
                lot(
                    "ok",
                    sensors =
                        listOf(
                            SensorReadingDto(
                                "soil_moisture",
                                35.0,
                                "%",
                                kotlin.time.Instant
                                    .fromEpochMilliseconds(clock - 5_000)
                                    .toString(),
                                soil,
                                true,
                            ),
                        ),
                )
            wait(engine)
            assertNull(engine.ready().percent)

            api.lot =
                lot(
                    "ok",
                    sensors =
                        listOf(
                            SensorReadingDto(
                                "soil_moisture",
                                42.0,
                                "%",
                                kotlin.time.Instant
                                    .fromEpochMilliseconds(clock + 500)
                                    .toString(),
                                soil,
                                true,
                            ),
                        ),
                )
            wait(engine)

            val ready = engine.ready()
            assertEquals(40, ready.percent)
            assertFalse(ready.waitingForPercent)
            assertEquals(CalibrateAnnouncementKind.FirstPercent, ready.announcement?.kind)
            assertEquals(40, ready.announcement?.percent)
        }

    @Test
    fun story52AKeptDryPointResumesAtWet() =
        runTest {
            api.state = CalibrationStateDto(calibrated = false, pendingDry = CalibrationValueDto(3000))
            val engine = opened()

            assertEquals(CalibrateStep.Wet, engine.ready().step)
            assertEquals(3000L, engine.ready().dryRaw)
        }

    @Test
    fun story52LeavingStopsTheWaitAndTheServerKeepsTheDryPoint() =
        runTest {
            val engine = opened()
            wait(engine)
            val reads = api.stateReads

            engine.close()
            wait(engine, 5_000)

            assertEquals(CalibrateState.Idle, engine.state.value)
            assertEquals(reads, api.stateReads)
        }

    @Test
    fun story52IndistinctPointsShowTheNoticeAndNothingAdvances() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    pendingDry = CalibrationValueDto(3000),
                    readings =
                        listOf(
                            reading(
                                9,
                                3005,
                                clock - 1_000,
                            ),
                        ),
                )
            val engine = opened()
            engine.pick(9)
            api.calibrateResult = ApiResult.Failed(ApiFailure.Validation)

            engine.record()
            runCurrent()

            val ready = engine.ready()
            assertEquals(CalibrateNotice.Indistinct, ready.notice)
            assertEquals(CalibrateStep.Wet, ready.step)
            assertFalse(ready.working)
            assertTrue(ready.canRecord)
        }

    @Test
    fun story52ANotDeliveredSaveKeepsThePointAndAllowsARetry() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    pendingDry = CalibrationValueDto(3000),
                    readings =
                        listOf(
                            reading(
                                9,
                                1200,
                                clock - 1_000,
                            ),
                        ),
                )
            val engine = opened()
            engine.pick(9)
            api.calibrateResult = ApiResult.Failed(ApiFailure.CalibrationNotDelivered)

            engine.record()
            runCurrent()

            assertEquals(CalibrateNotice.NotDelivered, engine.ready().notice)
            assertTrue(CalibrateNotice.NotDelivered.tryAgain)
            assertEquals(9L, engine.ready().picked)
            assertEquals(CalibrateStep.Wet, engine.ready().step)

            api.calibrateResult =
                ApiResult.Ok(
                    CalibrationDto(calibrated = true, dry = CalibrationValueDto(3000), wet = CalibrationValueDto(1200)),
                )
            engine.record()
            runCurrent()

            assertEquals(listOf<Pair<Long?, Long?>>(null to 9L, null to 9L), api.posts)
            assertEquals(CalibrateStep.Confirm, engine.ready().step)
        }

    @Test
    fun story52APausedLotExplainsAndNeverWaits() =
        runTest {
            api.lot = lot("paused", pausedBy = listOf("device"))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            val paused = assertIs<CalibrateState.Paused>(engine.state.value)
            assertFalse(paused.bySite)
            assertTrue(paused.offersResume)
            wait(engine, 5_000)
            assertEquals(0, api.stateReads)
        }

    @Test
    fun story52ResumeIsOfferedOnlyForTheDevicesOwnPauseAndToAdministrators() =
        runTest {
            api.lot = lot("paused", pausedBy = listOf("device", "site"))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            val both = assertIs<CalibrateState.Paused>(engine.state.value)
            assertTrue(both.bySite)
            assertFalse(both.offersResume)

            api.lot = lot("paused", pausedBy = listOf("site"))
            engine.open("t", "Tomatoes")
            runCurrent()
            assertFalse(assertIs<CalibrateState.Paused>(engine.state.value).offersResume)
        }

    @Test
    fun story52AMemberNeverOpensTheFlowAndTheServerIsNeverAsked() =
        runTest {
            api.lot = lot()
            val engine = start(role = "Member")

            engine.open("t", "Tomatoes")
            runCurrent()

            val failed = assertIs<CalibrateState.Failed>(engine.state.value)
            assertEquals(CalibrateNotice.Forbidden, failed.notice)
            assertEquals(0, api.lotReads)
        }

    @Test
    fun story52AnAdministratorMayOpenIt() =
        runTest {
            api.lot = lot()
            val engine = start(role = "Administrator")

            engine.open("t", "Tomatoes")
            runCurrent()

            assertIs<CalibrateState.Ready>(engine.state.value)
        }

    @Test
    fun story52ALotWithoutACalibratableSensorFailsWithNoSensorAndCanTryAgain() =
        runTest {
            api.lot =
                lot(
                    sensors =
                        listOf(
                            SensorReadingDto("soil_moisture", 1840.0, "raw", "2026-10-06T07:00:00.000Z", soil, false),
                        ),
                )
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            val failed = assertIs<CalibrateState.Failed>(engine.state.value)
            assertEquals(CalibrateNotice.NoSensor, failed.notice)

            api.lot = lot()
            engine.retry()
            runCurrent()
            assertIs<CalibrateState.Ready>(engine.state.value)
        }

    @Test
    fun story52AnUnreachableServerFailsTheOpenWithTryAgain() =
        runTest {
            api.lot = lot()
            api.stateFailure = ApiFailure.Unreachable
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            val failed = assertIs<CalibrateState.Failed>(engine.state.value)
            assertEquals(CalibrateNotice.Unreachable, failed.notice)
            assertTrue(failed.notice.tryAgain)
        }

    @Test
    fun story52ASnapshotFlattensTheReadyStateForSwift() =
        runTest {
            api.state =
                CalibrationStateDto(
                    calibrated = false,
                    pendingDry = CalibrationValueDto(3000),
                    readings =
                        listOf(
                            reading(
                                9,
                                1200,
                                clock - 1_000,
                            ),
                        ),
                )
            val engine = opened()
            engine.pick(9)

            val snapshot = snapshotOf(engine.state.value)

            assertEquals("ready", snapshot.surface)
            assertEquals("wet", snapshot.step)
            assertEquals(2, snapshot.stepNumber)
            assertEquals("3000", snapshot.dryRaw)
            assertEquals("", snapshot.wetRaw)
            assertEquals(listOf(9L), snapshot.readingSeqs)
            assertEquals("9", snapshot.pickedSeq)
            assertTrue(snapshot.canRecord)
            assertEquals("", snapshot.percent)
            assertEquals("idle", snapshotOf(CalibrateState.Idle).surface)
        }
}
