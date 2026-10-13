package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.api.LotHistoryDto
import com.escendit.coldframe.core.api.NodeStatusDto
import com.escendit.coldframe.core.api.SensorReadingDto
import com.escendit.coldframe.core.api.SensorThresholdsDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.ThresholdSideDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakePauseApi
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.PauseState
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class FakeLotDetailApi : LotDetailApi {
    var lot: LotDto? = null
    var lotFailures = ArrayDeque<ApiFailure>()
    var historyFailure: ApiFailure? = null
    val pages = mutableMapOf<String?, LotHistoryDto>()
    val calls = mutableListOf<String>()
    var thresholds: ApiResult<SensorThresholdsDto> = ApiResult.Failed(ApiFailure.NotFound)

    override suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto> {
        calls += "lot $lotId"
        lotFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        return lot?.let { ApiResult.Ok(it) } ?: ApiResult.Failed(ApiFailure.NotFound)
    }

    override suspend fun getLotHistory(
        siteId: String,
        lotId: String,
        quantity: String,
        cursor: String?,
    ): ApiResult<LotHistoryDto> {
        calls += "history $quantity ${cursor.orEmpty()}"
        historyFailure?.let { return ApiResult.Failed(it) }
        return pages[cursor]?.let { ApiResult.Ok(it.copy(quantity = quantity)) }
            ?: ApiResult.Failed(ApiFailure.NotFound)
    }

    override suspend fun getSensorThresholds(
        siteId: String,
        sensorId: String,
    ): ApiResult<SensorThresholdsDto> {
        calls += "thresholds $sensorId"
        return thresholds
    }
}

/** Story 4.8: Lot detail's engine: one read, one retry, stale mode and paged history. */
class LotDetailEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeLotDetailApi()
    private val settings = MapSettings()
    private var clock = 1_791_270_120_000L
    private val events = mutableListOf<LotsEvent>()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)

    private val detail =
        LotDto(
            id = "t",
            name = "Tomatoes",
            status = "needsCalibration",
            statusSince = "2026-10-06T07:02:00.000Z",
            node = NodeStatusDto("7c19aaaaaaaaaaaa", 62, "charging", "2026-10-06T07:00:00.000Z"),
            sensors =
                listOf(
                    SensorReadingDto("soil_moisture", 1840.0, "raw", "2026-10-06T07:00:00.000Z"),
                    SensorReadingDto("air_temperature", 14.4, "°C", "2026-10-06T07:00:00.000Z"),
                    SensorReadingDto("quantity_from_the_future", 1.0, "raw", "2026-10-06T07:00:00.000Z"),
                ),
        )

    private fun TestScope.start(): LotDetailEngine = engineOver(startSites())

    private val pauseApi = FakePauseApi(sitesApi)

    private fun TestScope.engineOver(sitesEngine: SitesEngine): LotDetailEngine {
        val engine = LotDetailEngine(api, sitesEngine, settings, backgroundScope, now = { clock }, pauseApi = pauseApi)
        backgroundScope.launch { engine.events.collect { events += it } }
        runCurrent()
        return engine
    }

    private fun TestScope.startSites(): SitesEngine {
        sitesApi.sites += SiteDto("a", "Home", "Owner")
        val sitesEngine =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(settings),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "k" },
                zones = { emptyList() },
            )
        runCurrent()
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return sitesEngine
    }

    private fun LotDetailEngine.ready() = assertIs<LotDetailState.Ready>(state.value)

    @Test
    fun uxDr63OpeningALotReadsItAndTheHistoryOfTheDefaultQuantity() =
        runTest {
            api.lot = detail
            api.pages[null] =
                LotHistoryDto("soil_moisture", "raw", listOf(LotHistoryDayDto("2026-10-06", 1790.0, 2050.0, 30)))
            val engine = start()

            engine.open("t", "Tomatoes")
            assertIs<LotDetailState.Loading>(engine.state.value)
            runCurrent()

            val ready = engine.ready()
            assertNull(ready.staleReason)
            assertEquals(SensorQuantity.SoilMoisture, ready.picked)
            assertEquals(
                listOf(SensorQuantity.SoilMoisture, SensorQuantity.AirTemperature),
                ready.lot.sensors.map { it.quantity },
            )
            assertEquals(62, ready.lot.node?.batteryPercent)
            assertEquals(
                1,
                ready.history
                    .getValue(SensorQuantity.SoilMoisture)
                    .days.size,
            )
        }

    @Test
    fun uxDr33PickingAQuantityReadsItsHistoryOnce() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "°C", listOf(LotHistoryDayDto("2026-10-06", 9.0, 15.0, 30)))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            engine.pick(SensorQuantity.AirTemperature)
            runCurrent()
            engine.pick(SensorQuantity.SoilMoisture)
            engine.pick(SensorQuantity.AirTemperature)
            runCurrent()

            assertEquals(1, api.calls.count { it == "history air_temperature " })
            assertEquals(SensorQuantity.AirTemperature, engine.ready().picked)
            assertEquals(
                SensorUnit.Celsius,
                engine
                    .ready()
                    .history
                    .getValue(SensorQuantity.AirTemperature)
                    .unit,
            )
            // A quantity the Lot does not have is ignored.
            engine.pick(SensorQuantity.GasResistance)
            assertEquals(SensorQuantity.AirTemperature, engine.ready().picked)
        }

    @Test
    fun uxDr33HistoryPagesAreJoinedByTheirCursor() =
        runTest {
            api.lot = detail
            api.pages[null] =
                LotHistoryDto("s", "raw", listOf(LotHistoryDayDto("2026-10-05", 1.0, 2.0, 1)), nextCursor = "c2")
            api.pages["c2"] = LotHistoryDto("s", "raw", listOf(LotHistoryDayDto("2026-10-06", 3.0, 4.0, 1)))
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            assertEquals(
                listOf(
                    "2026-10-05",
                    "2026-10-06",
                ),
                engine.ready().history.getValue(SensorQuantity.SoilMoisture).days.map {
                    it.day
                },
            )
        }

    @Test
    fun uxDr79ARefreshThatFailsTwiceKeepsTheDetailStaleAndTheNextSuccessIsLive() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("s", "raw", emptyList())
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            val before = engine.ready().fetchedAtEpochMs

            clock += 600_000
            api.lotFailures += listOf(ApiFailure.Unreachable, ApiFailure.Unreachable)
            engine.refresh()
            runCurrent()

            val stale = engine.ready()
            assertEquals(StaleReason.Unreachable, stale.staleReason)
            assertEquals(before, stale.fetchedAtEpochMs)
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", before)), events)

            engine.refresh()
            runCurrent()

            assertNull(engine.ready().staleReason)
            assertEquals(LotsEvent.LeftStale("a"), events.last())
        }

    @Test
    fun uxDr80AColdStartShowsTheLastGoodDetailStaleUntilTheReadLands() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("s", "raw", listOf(LotHistoryDayDto("2026-10-06", 1.0, 2.0, 1)))
            val sitesEngine = startSites()
            engineOver(sitesEngine).apply {
                open("t", "Tomatoes")
                runCurrent()
            }
            api.lotFailures += listOf(ApiFailure.Unreachable, ApiFailure.Unreachable)
            val second = engineOver(sitesEngine)

            second.open("t", "Tomatoes")
            runCurrent()

            val ready = second.ready()
            assertEquals(StaleReason.Unreachable, ready.staleReason)
            assertEquals(
                1,
                ready.history
                    .getValue(SensorQuantity.SoilMoisture)
                    .days.size,
            )
        }

    @Test
    fun aMissingLotIsNotFoundAndATransportFailureWithNothingToShowIsUnreachable() =
        runTest {
            val engine = start()

            engine.open("gone", "Gone")
            runCurrent()
            assertEquals(LotDetailNotice.NotFound, assertIs<LotDetailState.Failed>(engine.state.value).notice)

            api.lot = detail
            api.lotFailures += listOf(ApiFailure.Unreachable, ApiFailure.Unreachable)
            engine.open("other", "Other")
            runCurrent()
            assertEquals(LotDetailNotice.Unreachable, assertIs<LotDetailState.Failed>(engine.state.value).notice)
            assertTrue(LotDetailNotice.Unreachable.tryAgain)
        }

    @Test
    fun closingGoesIdleAndSnapshotsCrossAsKeys() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("s", "raw", listOf(LotHistoryDayDto("2026-10-06", 1790.0, 2050.0, 30)))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            val snapshot = snapshotOf(engine.state.value, clock)

            assertEquals("ready", snapshot.surface)
            assertEquals("needsCalibration", snapshot.heroStatus)
            assertEquals("raw", snapshot.heroValue)
            assertEquals("1840", snapshot.heroRawNumber)
            assertEquals(listOf("soilMoisture", "airTemperature"), snapshot.sensorQuantities)
            assertEquals(listOf("1840", "14"), snapshot.sensorNumbers)
            assertEquals(listOf("raw", "celsius"), snapshot.sensorUnits)
            assertEquals("charging", snapshot.deviceCharging)
            assertEquals(30, snapshot.barDays.size)
            assertEquals(1, snapshot.chartDaysWithReadings)

            engine.close()
            assertEquals("idle", snapshotOf(engine.state.value, clock).surface)
        }

    private fun camel(entry: Enum<*>) = entry.name.replaceFirstChar { it.lowercase() }

    @Test
    fun uxDr33ARefreshKeepsOnlyThePickedQuantitysHistorySoTheNextPickReadsAgain() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", listOf(LotHistoryDayDto("2026-10-06", 1.0, 2.0, 1)))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            engine.pick(SensorQuantity.AirTemperature)
            runCurrent()
            assertEquals(setOf(SensorQuantity.SoilMoisture, SensorQuantity.AirTemperature), engine.ready().history.keys)

            engine.refresh()
            runCurrent()

            assertEquals(setOf(SensorQuantity.AirTemperature), engine.ready().history.keys)
            engine.pick(SensorQuantity.SoilMoisture)
            runCurrent()
            assertEquals(2, api.calls.count { it == "history soil_moisture " })
        }

    @Test
    fun uxDr33AFailedHistoryReadIsUnavailableKeepsTheOthersAndALaterPickClearsIt() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", listOf(LotHistoryDayDto("2026-10-06", 1.0, 2.0, 1)))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            api.historyFailure = ApiFailure.Unreachable
            engine.pick(SensorQuantity.AirTemperature)
            runCurrent()

            assertTrue(engine.ready().historyUnavailable)
            assertEquals(setOf(SensorQuantity.SoilMoisture), engine.ready().history.keys)

            api.historyFailure = null
            engine.pick(SensorQuantity.AirTemperature)
            runCurrent()

            assertEquals(false, engine.ready().historyUnavailable)
            assertTrue(SensorQuantity.AirTemperature in engine.ready().history.keys)
        }

    private fun lastGoodKeys() = settings.keys.filter { it.startsWith("lotDetail.lastGood.") }

    @Test
    fun signingOutLeavesLotDetailIdleWithNothingKept() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", emptyList())
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            assertTrue(lastGoodKeys().isNotEmpty())

            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            assertEquals(LotDetailState.Idle, engine.state.value)
            assertTrue(lastGoodKeys().isEmpty())
        }

    @Test
    fun a401OnARefreshGoesIdleAndForgetsTheSession() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", emptyList())
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            api.lotFailures += ApiFailure.Unauthorized
            engine.refresh()
            runCurrent()

            assertEquals(LotDetailState.Idle, engine.state.value)
            assertTrue(lastGoodKeys().isEmpty())
        }

    @Test
    fun a404OnARefreshOfACachedLotIsNotFoundAndDropsItsCache() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", emptyList())
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            assertTrue(lastGoodKeys().isNotEmpty())

            api.lot = null
            engine.refresh()
            runCurrent()

            assertEquals(LotDetailNotice.NotFound, assertIs<LotDetailState.Failed>(engine.state.value).notice)
            assertTrue(lastGoodKeys().isEmpty())
        }

    @Test
    fun switchingSiteResetsLotDetailToIdle() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", emptyList())
            sitesApi.sites += SiteDto("b", "Allotment", "Member")
            val sitesEngine = startSites()
            val engine = engineOver(sitesEngine)
            engine.open("t", "Tomatoes")
            runCurrent()
            assertIs<LotDetailState.Ready>(engine.state.value)

            sitesEngine.select("a")
            runCurrent()

            assertEquals(LotDetailState.Idle, engine.state.value)
        }

    @Test
    fun uxDr98TheSnapshotVocabularyIsTheOneTheSwiftSideReads() {
        assertEquals(
            listOf(
                "none",
                "noPercentUntilCalibrated",
                "checkPowerOrRange",
                "hubSilent",
                "pausedUntil",
                "paused",
                "pausedWithSite",
            ),
            HeroNote.entries.map(::camel),
        )
        assertEquals(listOf("raw", "celsius", "percent", "kiloOhm"), SensorUnit.entries.map(::camel))
        assertEquals(
            listOf("soilMoisture", "airTemperature", "relativeHumidity", "gasResistance"),
            SensorQuantity.entries.map(::camel),
        )
        assertEquals(listOf("charging", "notCharging"), ChargeState.entries.map(::camel))
        assertEquals(
            listOf("notFound", "forbidden", "unreachable", "certificate", "unexpected"),
            LotDetailNotice.entries.map(::camel),
        )
    }

    @Test
    fun uxDr98AStaleAndAFailedSnapshotCrossAsKeys() =
        runTest {
            api.lot = detail
            api.pages[null] = LotHistoryDto("x", "raw", emptyList())
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            api.lotFailures += listOf(ApiFailure.Unreachable, ApiFailure.Unreachable)
            engine.refresh()
            runCurrent()

            val stale = snapshotOf(engine.state.value, clock)
            assertEquals("ready", stale.surface)
            assertEquals("unreachable", stale.staleReason)

            api.lot = null
            engine.refresh()
            runCurrent()

            val failed = snapshotOf(engine.state.value, clock)
            assertEquals("failed", failed.surface)
            assertEquals("notFound", failed.notice)
        }

    private val soilDetail =
        detail.copy(
            sensors =
                listOf(
                    SensorReadingDto(
                        "soil_moisture",
                        40.0,
                        "%",
                        "2026-10-06T07:00:00.000Z",
                        "11111111-1111-7111-8111-111111111111",
                        true,
                    ),
                ),
        )

    @Test
    fun uxDr5OpeningALotReadsTheSoilSensorsThresholdsForTheChartBand() =
        runTest {
            api.lot = soilDetail
            api.pages[null] = LotHistoryDto("s", "%", listOf(LotHistoryDayDto("2026-10-06", 20.0, 50.0, 30)))
            api.thresholds =
                ApiResult.Ok(
                    SensorThresholdsDto("%", ThresholdSideDto("override", 30.0), ThresholdSideDto("override", 70.0)),
                )
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            assertEquals(
                SoilThresholds("11111111-1111-7111-8111-111111111111", 30, 70),
                engine.ready().soilThresholds,
            )
            assertTrue(api.calls.any { it.startsWith("thresholds ") })
        }

    @Test
    fun uxDr5AFailedThresholdsReadLeavesTheDetailLiveWithoutAHighLine() =
        runTest {
            api.lot = soilDetail
            api.pages[null] = LotHistoryDto("s", "%", listOf(LotHistoryDayDto("2026-10-06", 20.0, 50.0, 30)))
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            assertNull(engine.ready().soilThresholds)
            assertNull(engine.ready().staleReason)
        }

    @Test
    fun uxDr5ARefreshReadsTheThresholdsAgainSoASavedChangeMovesTheBand() =
        runTest {
            api.lot = soilDetail
            api.pages[null] = LotHistoryDto("s", "%", listOf(LotHistoryDayDto("2026-10-06", 20.0, 50.0, 30)))
            api.thresholds =
                ApiResult.Ok(SensorThresholdsDto("%", ThresholdSideDto("override", 30.0), ThresholdSideDto("cleared")))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            assertNull(engine.ready().soilThresholds?.highPercent)

            api.thresholds =
                ApiResult.Ok(
                    SensorThresholdsDto("%", ThresholdSideDto("override", 25.0), ThresholdSideDto("override", 80.0)),
                )
            engine.refresh()
            runCurrent()

            assertEquals(25, engine.ready().soilThresholds?.lowPercent)
            assertEquals(80, engine.ready().soilThresholds?.highPercent)
        }

    @Test
    fun uxDr5AFailedRereadAfterASaveDropsTheOldBandInsteadOfDrawingIt() =
        runTest {
            api.lot = soilDetail
            api.pages[null] = LotHistoryDto("s", "%", listOf(LotHistoryDayDto("2026-10-06", 20.0, 50.0, 30)))
            api.thresholds =
                ApiResult.Ok(
                    SensorThresholdsDto("%", ThresholdSideDto("override", 30.0), ThresholdSideDto("override", 70.0)),
                )
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            assertEquals(70, engine.ready().soilThresholds?.highPercent)

            // The high was cleared and saved, but the read that follows fails: the old 70 must not stay on the chart.
            api.thresholds = ApiResult.Failed(ApiFailure.Unreachable)
            engine.refresh()
            runCurrent()

            assertNull(engine.ready().soilThresholds)
            assertNull(engine.ready().staleReason)
        }

    // Pause and Resume from Lot detail (Story 8.2)

    private val node = "7c19aaaaaaaaaaaa"

    /** The Server's bookkeeping of a Device pause: the Lot reads `paused` by the Device, or live again. */
    private fun followThePause() {
        pauseApi.onDevice = { _, paused ->
            api.lot =
                if (paused) {
                    detail.copy(
                        status = "paused",
                        pausedBy = listOf("device"),
                        pausedUntil = "2026-11-01T00:00:00.000Z",
                    )
                } else {
                    detail
                }
        }
    }

    @Test
    fun uxDr70PausingTheNodeFromLotDetailSendsThePauseAndReadsTheLotAgain() =
        runTest {
            api.lot = detail
            followThePause()
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            assertTrue(LotDetail.of(engine.ready(), clock).canPause)
            val reads = api.calls.count { it == "lot t" }

            engine.pause.openForDevice(node)
            engine.pause.setUntil("2026-11-01")
            engine.pause.save()
            runCurrent()

            assertEquals(listOf("pauseDevice a $node 2026-11-01"), pauseApi.calls)
            assertEquals(PauseState(), engine.pause.state.value)
            assertEquals(reads + 1, api.calls.count { it == "lot t" })
            // What the hero shows is the Server's answer, not a guess.
            val ready = engine.ready()
            assertEquals(LotStatus.Paused, ready.lot.status)
            assertEquals(listOf(LotPauseSource.Device), ready.lot.pausedBy)
            val shown = LotDetail.of(ready, clock)
            assertTrue(shown.canResume)
            assertEquals(HeroNote.PausedUntil, shown.hero.note)
        }

    @Test
    fun uxDr70ResumeFromLotDetailIsOneTapAndTheLotIsLiveAgain() =
        runTest {
            api.lot = detail.copy(status = "paused", pausedBy = listOf("device"))
            followThePause()
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            engine.pause.resumeDevice(node)
            runCurrent()

            assertEquals(listOf("resumeDevice a $node"), pauseApi.calls)
            assertNull(engine.pause.state.value.sheet)
            assertEquals(LotStatus.NeedsCalibration, engine.ready().lot.status)
            assertTrue(LotDetail.of(engine.ready(), clock).canPause)
        }

    @Test
    fun uxDr46ARefusedPauseLeavesLotDetailAsItWasAndTheSheetOpen() =
        runTest {
            api.lot = detail
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()
            val before = engine.ready()
            val reads = api.calls.size

            engine.pause.openForDevice(node)
            engine.pause.setUntil("2020-01-01")
            pauseApi.failure = ApiFailure.Validation
            engine.pause.save()
            runCurrent()

            assertTrue(
                engine.pause.state.value.sheet!!
                    .dateRefused,
            )
            assertEquals(before, engine.ready())
            assertEquals(reads, api.calls.size)

            pauseApi.failure = ApiFailure.Forbidden
            engine.pause.save()
            runCurrent()
            assertEquals(
                PauseNotice.Forbidden,
                engine.pause.state.value.sheet!!
                    .notice,
            )
            assertEquals(before, engine.ready())
        }

    // The sheet and a failed Resume belong to the Lot they were asked on: the next Lot starts clean, so its
    // Try again can never resume another Lot's Node.
    @Test
    fun uxDr70OpeningOrClosingALotDropsThePauseSheetAndAFailedResumeOfTheLotBefore() =
        runTest {
            api.lot = detail
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            pauseApi.failure = ApiFailure.Unreachable
            engine.pause.resumeDevice(node)
            runCurrent()
            assertEquals(
                node,
                engine.pause.state.value.failure
                    ?.target
                    ?.deviceId,
            )

            engine.open("b", "Beans")
            runCurrent()

            assertEquals(PauseState(), engine.pause.state.value)
            engine.pause.retry()
            runCurrent()
            assertEquals(listOf("resumeDevice a $node"), pauseApi.calls)

            // An open sheet does not outlive its Lot either, whether the Lot is left or another is opened.
            engine.pause.openForDevice(node)
            assertNotNull(engine.pause.state.value.sheet)
            engine.close()
            assertEquals(PauseState(), engine.pause.state.value)

            engine.open("t", "Tomatoes")
            runCurrent()
            engine.pause.openForDevice(node)
            engine.open("b", "Beans")
            assertEquals(PauseState(), engine.pause.state.value)
        }

    @Test
    fun uxDr70WholeSiteFromLotDetailPausesTheSiteAndThePauseBelongsToThisSurface() =
        runTest {
            api.lot = detail
            val sitesEngine = startSites()
            val engine = engineOver(sitesEngine)
            val other =
                LotDetailEngine(api, sitesEngine, settings, backgroundScope, now = { clock }, pauseApi = pauseApi)
            engine.open("t", "Tomatoes")
            runCurrent()

            engine.pause.openForDevice(node)
            engine.pause.setScope(PauseScope.Site)
            // The sheet is this surface's: another surface's Pause stays closed.
            assertNull(other.pause.state.value.sheet)
            engine.pause.save()
            runCurrent()

            assertEquals(listOf("pauseSite a open"), pauseApi.calls)
            assertTrue((sitesEngine.state.value as com.escendit.coldframe.core.sites.SitesState.Ready).current.paused)
        }

    @Test
    fun uxDr70APauseSomewhereElseInTheAppReadsTheOpenLotAgainAndAClosedOneNotAtAll() =
        runTest {
            api.lot = detail
            val sitesEngine = startSites()
            val engine = engineOver(sitesEngine)

            sitesEngine.pauseChanged()
            runCurrent()
            assertTrue(api.calls.isEmpty())

            engine.open("t", "Tomatoes")
            runCurrent()
            val reads = api.calls.count { it == "lot t" }
            sitesEngine.pauseChanged()
            runCurrent()
            assertEquals(reads + 1, api.calls.count { it == "lot t" })
        }
}
