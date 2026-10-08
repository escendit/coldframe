package com.escendit.coldframe.core.thresholds

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.NodeStatusDto
import com.escendit.coldframe.core.api.SensorReadingDto
import com.escendit.coldframe.core.api.SensorThresholdsDto
import com.escendit.coldframe.core.api.SetSensorThresholdsRequestDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.ThresholdSideDto
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class FakeThresholdsApi : ThresholdsApi {
    var lot: LotDto? = null
    val thresholds = mutableMapOf<String, SensorThresholdsDto>()
    var readFailure: ApiFailure? = null
    var writeFailure: ApiFailure? = null
    val puts = mutableListOf<Pair<String, SetSensorThresholdsRequestDto>>()

    override suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto> = lot?.let { ApiResult.Ok(it) } ?: ApiResult.Failed(ApiFailure.NotFound)

    override suspend fun getSensorThresholds(
        siteId: String,
        sensorId: String,
    ): ApiResult<SensorThresholdsDto> {
        readFailure?.let { return ApiResult.Failed(it) }
        return thresholds[sensorId]?.let { ApiResult.Ok(it) } ?: ApiResult.Failed(ApiFailure.NotFound)
    }

    override suspend fun setSensorThresholds(
        siteId: String,
        sensorId: String,
        request: SetSensorThresholdsRequestDto,
    ): ApiResult<SensorThresholdsDto> {
        puts += sensorId to request
        writeFailure?.let { return ApiResult.Failed(it) }
        val before = thresholds.getValue(sensorId)

        fun apply(
            current: ThresholdSideDto,
            side: ThresholdSideDto?,
        ) = when (side?.kind) {
            "override" -> side
            "cleared" -> ThresholdSideDto("cleared")
            else -> current
        }
        val after = before.copy(low = apply(before.low, request.low), high = apply(before.high, request.high))
        thresholds[sensorId] = after
        return ApiResult.Ok(after)
    }
}

/** Story 5.4: the Thresholds engine: columns, drafts, validation gating Save, roles and failures. */
class ThresholdsEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeThresholdsApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var savedCount = 0

    private val soil = "11111111-1111-7111-8111-111111111111"
    private val air = "22222222-2222-7222-8222-222222222222"

    private fun side(
        kind: String,
        value: Double? = null,
    ) = ThresholdSideDto(kind, value)

    private fun seed(
        soilLow: ThresholdSideDto = side("default", 30.0),
        soilHigh: ThresholdSideDto = side("default"),
    ) {
        api.lot =
            LotDto(
                id = "t",
                name = "Tomatoes",
                status = "ok",
                node = NodeStatusDto("7c19aaaaaaaaaaaa"),
                sensors =
                    listOf(
                        SensorReadingDto("soil_moisture", 40.0, "%", "2026-10-06T07:00:00.000Z", soil, true),
                        SensorReadingDto("air_temperature", 14.4, "°C", "2026-10-06T07:00:00.000Z", air, false),
                    ),
            )
        api.thresholds[soil] = SensorThresholdsDto("%", soilLow, soilHigh)
        api.thresholds[air] = SensorThresholdsDto("°C", side("cleared"), side("cleared"), proposedLow = 7.0)
    }

    private fun TestScope.start(role: String = "Owner"): ThresholdsEngine {
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
        return ThresholdsEngine(api, sites, backgroundScope, onSaved = { savedCount++ }).also { runCurrent() }
    }

    private fun TestScope.opened(
        role: String = "Owner",
        focus: String? = null,
    ): ThresholdsEngine {
        seed()
        val engine = start(role)
        engine.open("t", "Tomatoes", focus)
        runCurrent()
        return engine
    }

    private fun ThresholdsEngine.ready() = assertIs<ThresholdsState.Ready>(state.value)

    private fun ThresholdsEngine.column(id: String) = ready().columns.single { it.sensorId == id }

    @Test
    fun uxDr45EachSensorGetsAThresholdColumnWithTheServersSidesAndTheCurrentReading() =
        runTest {
            val engine = opened(focus = air)

            val columns = engine.ready().columns
            assertEquals(
                listOf(SensorQuantity.SoilMoisture, SensorQuantity.AirTemperature),
                columns.map { it.quantity },
            )
            val soilColumn = columns[0]
            assertEquals(30.0, soilColumn.low)
            assertNull(soilColumn.high)
            assertEquals(40.0, soilColumn.current)
            assertEquals(5.0, soilColumn.step)
            assertEquals(0.0 to 100.0, soilColumn.trackMin to soilColumn.trackMax)
            assertTrue(soilColumn.draggable)
            assertTrue(soilColumn.alerting)
            val airColumn = columns[1]
            assertFalse(airColumn.alerting)
            assertEquals(7.0, airColumn.proposedLow)
            assertNull(airColumn.step)
            assertFalse(airColumn.draggable)
            assertEquals(air, engine.ready().focusSensorId)
            assertFalse(engine.ready().dirty)
        }

    @Test
    fun uxDr45ALowMovesInFivePercentSteps() =
        runTest {
            val engine = opened()

            engine.setLow(soil, 27.0)
            assertEquals(25.0, engine.column(soil).low)
            engine.setLow(soil, 28.0)
            assertEquals(30.0, engine.column(soil).low)
            engine.setLow(soil, 140.0)
            assertEquals(100.0, engine.column(soil).low)
            engine.setLowText(soil, "42,4")
            assertEquals(40.0, engine.column(soil).low)
            engine.setLowText(soil, "not a number")
            assertEquals(40.0, engine.column(soil).low)
        }

    @Test
    fun uxDr91LowMustStayBelowHighGatesSaveAndTheServerStaysTheOnlyValidator() =
        runTest {
            val engine = opened()

            engine.setLow(soil, 70.0)
            engine.addHigh(soil)
            engine.setHigh(soil, 60.0)

            assertTrue(engine.column(soil).lowMustStayBelowHigh)
            assertFalse(engine.ready().canSave)
            engine.save()
            runCurrent()
            assertEquals(emptyList(), api.puts)

            engine.setHigh(soil, 80.0)
            assertFalse(engine.column(soil).lowMustStayBelowHigh)
            assertTrue(engine.ready().canSave)
            engine.setHigh(soil, 70.0)
            assertTrue(engine.column(soil).lowMustStayBelowHigh)
        }

    @Test
    fun uxDr91AHighNeedsALow() =
        runTest {
            val engine = opened()

            engine.addHigh(soil)
            engine.turnOffAlerts(soil)
            assertFalse(engine.column(soil).alerting)
            assertNull(engine.column(soil).high)

            engine.turnOnAlerts(soil)
            engine.addHigh(soil)
            assertTrue(engine.column(soil).high != null)
        }

    @Test
    fun setLowOnlySendsTheSideThatChanged() =
        runTest {
            val engine = opened()

            engine.setLow(soil, 25.0)
            assertTrue(engine.ready().canSave)
            engine.save()
            runCurrent()

            assertEquals(
                listOf(soil to SetSensorThresholdsRequestDto(low = ThresholdSideDto("override", 25.0))),
                api.puts,
            )
            val ready = engine.ready()
            assertTrue(ready.saved)
            assertFalse(ready.working)
            assertEquals(1, savedCount)
            assertFalse(ready.dirty)
        }

    @Test
    fun addAndClearHighSendOverrideThenCleared() =
        runTest {
            val engine = opened()

            engine.addHigh(soil)
            assertEquals(70.0, engine.column(soil).high)
            engine.save()
            runCurrent()
            assertEquals(
                ThresholdSideDto("override", 70.0),
                api.puts
                    .single()
                    .second.high,
            )
            assertNull(
                api.puts
                    .single()
                    .second.low,
            )

            val again = opened()
            again.setHigh(soil, 70.0)
            again.clearHigh(soil)
            assertNull(again.column(soil).high)
        }

    @Test
    fun clearingAnExistingHighIsSentAsCleared() =
        runTest {
            seed(soilHigh = side("override", 70.0))
            val engine = start()
            engine.open("t", "Tomatoes")
            runCurrent()

            engine.clearHigh(soil)
            engine.save()
            runCurrent()

            assertEquals(SetSensorThresholdsRequestDto(high = ThresholdSideDto("cleared")), api.puts.single().second)
        }

    @Test
    fun aSensorWithoutADefaultOffersTheServersProposedLowWhenAlertsTurnOnAndNeverAHigh() =
        runTest {
            val engine = opened()

            assertTrue(engine.column(air).offersProposal)
            engine.turnOnAlerts(air)

            assertEquals(7.0, engine.column(air).low)
            assertNull(engine.column(air).high)
            assertFalse(engine.column(air).offersProposal)
        }

    @Test
    fun turningAlertsBackOnRestoresTheLowTheSensorHad() =
        runTest {
            val engine = opened()

            engine.turnOffAlerts(soil)
            assertTrue(engine.ready().canSave)
            engine.turnOnAlerts(soil)

            assertEquals(30.0, engine.column(soil).low)
            assertFalse(engine.ready().dirty)
        }

    @Test
    fun uxDr84AMemberSeesTheColumnsReadOnlyAndCanNotChangeOrSaveAnything() =
        runTest {
            val engine = opened(role = "Member")

            val ready = engine.ready()
            assertFalse(ready.canEdit)
            assertEquals(2, ready.columns.size)

            engine.setLow(soil, 10.0)
            engine.addHigh(soil)
            engine.turnOffAlerts(soil)
            engine.save()
            runCurrent()

            assertEquals(30.0, engine.column(soil).low)
            assertFalse(engine.ready().canSave)
            assertEquals(emptyList(), api.puts)
        }

    @Test
    fun uxDr69CancelSendsNothingAndLeaves() =
        runTest {
            val engine = opened()

            engine.setLow(soil, 10.0)
            engine.close()
            runCurrent()

            assertEquals(ThresholdsState.Idle, engine.state.value)
            assertEquals(emptyList(), api.puts)
            assertEquals(0, savedCount)
        }

    @Test
    fun aServer400KeepsTheEditsAndSaysNothingChanged() =
        runTest {
            val engine = opened()
            engine.setLow(soil, 25.0)
            api.writeFailure = ApiFailure.Validation

            engine.save()
            runCurrent()

            val ready = engine.ready()
            assertEquals(ThresholdsNotice.Invalid, ready.notice)
            assertEquals(25.0, engine.column(soil).low)
            assertFalse(ready.saved)
            assertFalse(ready.working)
            assertTrue(ready.dirty)
        }

    @Test
    fun uxDr84AForbiddenRaceIsTheForbiddenNoticeAndKeepsTheEdits() =
        runTest {
            val engine = opened()
            engine.setLow(soil, 25.0)
            api.writeFailure = ApiFailure.Forbidden

            engine.save()
            runCurrent()

            assertEquals(ThresholdsNotice.Forbidden, engine.ready().notice)
            assertEquals(25.0, engine.column(soil).low)
        }

    @Test
    fun notDeliveredKeepsTheEditsAndARetryWorks() =
        runTest {
            val engine = opened()
            engine.setLow(soil, 25.0)
            api.writeFailure = ApiFailure.IdentityProviderUnavailable
            engine.save()
            runCurrent()
            assertEquals(ThresholdsNotice.NotSaved, engine.ready().notice)
            assertTrue(ThresholdsNotice.NotSaved.tryAgain)
            assertEquals(25.0, engine.column(soil).low)

            api.writeFailure = null
            engine.save()
            runCurrent()

            assertTrue(engine.ready().saved)
            assertEquals(
                25.0,
                api.thresholds
                    .getValue(soil)
                    .low.value,
            )
            assertEquals(1, savedCount)
        }

    @Test
    fun unreachableAndFailedOpensAreNamedAndOnlyTheRetryableCanRetry() =
        runTest {
            seed()
            val engine = start()
            api.readFailure = ApiFailure.Unreachable
            engine.open("t", "Tomatoes")
            runCurrent()
            assertEquals(ThresholdsNotice.Unreachable, assertIs<ThresholdsState.Failed>(engine.state.value).notice)

            api.readFailure = null
            engine.retry()
            runCurrent()
            assertIs<ThresholdsState.Ready>(engine.state.value)

            api.lot = null
            engine.open("t", "Tomatoes")
            runCurrent()
            assertEquals(ThresholdsNotice.NotFound, assertIs<ThresholdsState.Failed>(engine.state.value).notice)
            engine.retry()
            runCurrent()
            assertIs<ThresholdsState.Failed>(engine.state.value)
        }

    @Test
    fun aLotWithoutASensorHasNothingToSet() =
        runTest {
            seed()
            api.lot = api.lot!!.copy(sensors = emptyList())
            val engine = start()

            engine.open("t", "Tomatoes")
            runCurrent()

            assertEquals(ThresholdsNotice.NoSensor, assertIs<ThresholdsState.Failed>(engine.state.value).notice)
        }

    @Test
    fun theSnapshotFlattensTheColumnsForSwift() =
        runTest {
            val engine = opened(focus = soil)
            engine.setLow(soil, 25.0)

            val snapshot = snapshotOf(engine.state.value)

            assertEquals("ready", snapshot.surface)
            assertEquals(listOf(soil, air), snapshot.sensorIds)
            assertEquals(listOf("soilMoisture", "airTemperature"), snapshot.quantities)
            assertEquals(listOf("percent", "celsius"), snapshot.units)
            assertEquals(listOf("25", ""), snapshot.lows)
            assertEquals(listOf("30", ""), snapshot.originalLows)
            assertEquals(listOf("", "7"), snapshot.proposedLows)
            assertEquals(listOf("40", "14.4"), snapshot.currents)
            assertEquals(listOf("5", ""), snapshot.steps)
            assertEquals(listOf(true, false), snapshot.draggables)
            assertTrue(snapshot.canEdit)
            assertTrue(snapshot.canSave)
            assertEquals(soil, snapshot.focusSensorId)
            assertEquals("idle", snapshotOf(ThresholdsState.Idle).surface)
        }

    @Test
    fun theUnitOfAColumnIsTheServersAndFallsBackToTheReadings() {
        assertEquals(SensorUnit.Percent, SensorUnit.fromServer("%"))
    }
}
