package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.calibrate.CalibrateActions
import com.escendit.coldframe.android.ui.calibrate.CalibrateScreen
import com.escendit.coldframe.android.ui.sites.HISTORY_CHART_TAG
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotDetailCopy
import com.escendit.coldframe.android.ui.sites.LotDetailScreen
import com.escendit.coldframe.android.ui.sites.THRESHOLDS_ACTION_TAG
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.android.ui.thresholds.THRESHOLDS_SAVE_TAG
import com.escendit.coldframe.android.ui.thresholds.ThresholdsActions
import com.escendit.coldframe.android.ui.thresholds.ThresholdsScreen
import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.lots.HistoryChart
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotHistory
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorReading
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.lots.SoilThresholds
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.thresholds.ThresholdColumn
import com.escendit.coldframe.core.thresholds.ThresholdsNotice
import com.escendit.coldframe.core.thresholds.ThresholdsState
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Instant
import java.time.ZoneOffset
import java.util.TimeZone
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Thresholds states as the core hands them to the shell (Story 5.4), told against [LotFixtures.now]. */
object ThresholdsStates {
    private fun at(text: String): Long = Instant.parse(text).toEpochMilli()

    const val SOIL = "sensor-soil"
    const val AIR = "sensor-air"

    val soil =
        ThresholdColumn(
            sensorId = SOIL,
            quantity = SensorQuantity.SoilMoisture,
            unit = SensorUnit.Percent,
            low = 30.0,
            high = null,
            originalLow = 30.0,
            originalHigh = null,
            proposedLow = null,
            current = 40.0,
        )

    /** A watched temperature Sensor with no default: alerts are off and the Server proposes a low. */
    val air =
        ThresholdColumn(
            sensorId = AIR,
            quantity = SensorQuantity.AirTemperature,
            unit = SensorUnit.Celsius,
            low = null,
            high = null,
            originalLow = null,
            originalHigh = null,
            proposedLow = 7.0,
            current = 14.4,
        )

    fun ready(
        role: SiteRole = SiteRole.Owner,
        columns: List<ThresholdColumn> = listOf(soil, air),
        notice: ThresholdsNotice? = null,
        working: Boolean = false,
        focus: String? = null,
    ) = ThresholdsState.Ready(
        site = homeSite(role),
        lotId = "lot-tomatoes",
        lotName = "Tomatoes",
        canEdit = role >= SiteRole.Administrator,
        columns = columns,
        working = working,
        notice = notice,
        saved = false,
        focusSensorId = focus,
    )

    val editor = ready()
    val member = ready(role = SiteRole.Member)
    val withHigh = ready(columns = listOf(soil.copy(high = 70.0, originalHigh = 70.0), air))

    /** Low 70 over high 60: "Low must stay below high." and Save disabled. */
    val lowAboveHigh = ready(columns = listOf(soil.copy(low = 70.0, high = 60.0, originalHigh = 60.0), air))
    val changed = ready(columns = listOf(soil.copy(low = 25.0), air))
    val notSaved = ready(columns = listOf(soil.copy(low = 25.0), air), notice = ThresholdsNotice.NotSaved)
    val invalid = ready(columns = listOf(soil.copy(low = 25.0), air), notice = ThresholdsNotice.Invalid)
    val forbidden = ready(columns = listOf(soil.copy(low = 25.0), air), notice = ThresholdsNotice.Forbidden)
    val failed = ThresholdsState.Failed(homeSite(), "lot-tomatoes", "Tomatoes", ThresholdsNotice.Unreachable)

    /** Thirty days of calibrated soil moisture in %, one day below 30 %. */
    val days: List<LotHistoryDayDto> =
        (0 until 30)
            .filter { it % 7 != 3 }
            .map { index ->
                val day =
                    java.time.LocalDate
                        .of(2026, 9, 7)
                        .plusDays(index.toLong())
                        .toString()
                val low = if (index == 12) 20.0 else 35.0 + (index * 5 % 30)
                LotHistoryDayDto(day, low, low + 15.0, 96)
            }

    val sensors =
        listOf(
            SensorReading(
                SensorQuantity.SoilMoisture,
                40.0,
                SensorUnit.Percent,
                at("2026-10-06T07:02:00Z"),
                sensorId = SOIL,
                calibratable = true,
            ),
            SensorReading(
                SensorQuantity.AirTemperature,
                14.4,
                SensorUnit.Celsius,
                at("2026-10-06T07:02:00Z"),
                sensorId = AIR,
                calibratable = false,
            ),
        )

    /** A calibrated, in-range Lot: ~40 % and OK, with the band 30 % to 70 %. */
    fun detail(
        role: SiteRole = SiteRole.Owner,
        thresholds: SoilThresholds? = SoilThresholds(SOIL, 30, 70),
    ): LotDetailState.Ready =
        LotDetailFixtures
            .ready(
                lot = LotDetailFixtures.ok.copy(sensors = sensors),
                role = role,
                history =
                    mapOf(
                        SensorQuantity.SoilMoisture to
                            LotHistory(SensorQuantity.SoilMoisture, SensorUnit.Percent, days),
                    ),
            ).copy(soilThresholds = thresholds)
}

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ThresholdsScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var state: ThresholdsState by mutableStateOf(ThresholdsStates.editor)
    private val defaultZone = TimeZone.getDefault()

    private val actions =
        ThresholdsActions(
            close = { calls += "close" },
            retry = { calls += "retry" },
            setLowText = { id, text -> calls += "low $id $text" },
            setHighText = { id, text -> calls += "high $id $text" },
            addHigh = { calls += "addHigh $it" },
            clearHigh = { calls += "clearHigh $it" },
            turnOnAlerts = { calls += "on $it" },
            turnOffAlerts = { calls += "off $it" },
            save = { calls += "save" },
        )

    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private fun show(next: ThresholdsState) {
        state = next
        compose.setContent {
            ColdframeTheme(isDark = false) {
                ThresholdsScreen(state = state, actions = actions, now = { LotFixtures.now }, zone = ZoneOffset.UTC)
            }
        }
        compose.waitForIdle()
    }

    @Test
    fun `UX-DR45 every Sensor gets a Threshold column with its low, no-high marker and current Reading`() {
        show(ThresholdsStates.editor)
        compose.onNodeWithText("Thresholds for Tomatoes").assertExists()
        compose.onNodeWithTag("threshold-column-${ThresholdsStates.SOIL}").assertExists()
        compose.onNodeWithTag("threshold-column-${ThresholdsStates.AIR}").assertExists()
        compose.onNodeWithText("Now 40 %").assertExists()
        compose.onNodeWithText("Now 14.4 °C").assertExists()
        compose.onNodeWithText("no high").assertExists()
        compose.onNodeWithText("Alerts are off").assertExists()
        compose.onNode(hasContentDescription("Low for Soil moisture"), useUnmergedTree = true).assertExists()
    }

    @Test
    fun `UX-DR45 Add high, Clear high and the alerts toggle forward to the core`() {
        show(ThresholdsStates.editor)
        compose.onNodeWithText("Add high", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("Turn on Alerts", ignoreCase = true).performScrollTo().performClick()
        compose.onNodeWithText("Turn off Alerts", ignoreCase = true).performScrollTo().performClick()
        assertEquals(
            listOf("addHigh ${ThresholdsStates.SOIL}", "on ${ThresholdsStates.AIR}", "off ${ThresholdsStates.SOIL}"),
            calls,
        )
        calls.clear()
        state = ThresholdsStates.withHigh
        compose.waitForIdle()
        compose.onAllNodesWithText("Add high", ignoreCase = true).assertCountEquals(0)
        compose.onNodeWithText("Clear high", ignoreCase = true).performScrollTo().performClick()
        assertEquals(listOf("clearHigh ${ThresholdsStates.SOIL}"), calls)
    }

    @Test
    fun `UX-DR45 UX-DR91 Low must stay below high shows inline and Save is disabled while invalid`() {
        show(ThresholdsStates.lowAboveHigh)
        compose.onNodeWithText("Low must stay below high.").assertExists()
        compose.onNodeWithTag(THRESHOLDS_SAVE_TAG).assertIsNotEnabled()
        state = ThresholdsStates.changed
        compose.waitForIdle()
        compose.onAllNodesWithText("Low must stay below high.").assertCountEquals(0)
        compose.onNodeWithTag(THRESHOLDS_SAVE_TAG).assertIsEnabled()
        compose.onNodeWithTag(THRESHOLDS_SAVE_TAG).performClick()
        assertEquals(listOf("save"), calls)
    }

    @Test
    fun `UX-DR69 the modal has Cancel and Save, and Cancel sends nothing`() {
        show(ThresholdsStates.changed)
        compose.onNodeWithText("Cancel", ignoreCase = true).performClick()
        assertEquals(listOf("close"), calls)
        compose.onNodeWithText("Save", ignoreCase = true).assertExists()
    }

    @Test
    fun `UX-DR84 a Member sees the Thresholds read-only with no edit control, hidden not disabled`() {
        show(ThresholdsStates.member)
        compose.onNodeWithText("Only an Owner or Administrator can change Thresholds.").assertExists()
        compose.onNodeWithText("Low for Soil moisture: 30 %").assertExists()
        for (control in listOf("Save", "Add high", "Clear high", "Turn on Alerts", "Turn off Alerts", "Cancel")) {
            compose.onAllNodesWithText(control, ignoreCase = true).assertCountEquals(0)
        }
        compose.onNodeWithText("Back", ignoreCase = true).performClick()
        assertEquals(listOf("close"), calls)
    }

    @Test
    fun `UX-DR91 a Server 400 says what happened, what did not change and what to do next`() {
        show(ThresholdsStates.invalid)
        compose.onNodeWithText("did not accept these Thresholds", substring = true).assertExists()
        compose.onNodeWithText("Nothing was changed", substring = true).assertExists()
        compose.onNodeWithText("Check that Low is below High", substring = true).assertExists()
    }

    @Test
    fun `UX-DR91 UX-DR84 a 403 race names the Site and who can change it`() {
        show(ThresholdsStates.forbidden)
        compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.").assertExists()
    }

    @Test
    fun `UX-DR91 a save that was not delivered keeps the edits and offers Try again`() {
        show(ThresholdsStates.notSaved)
        compose.onNodeWithText("Not saved.", substring = true).assertExists()
        compose.onNodeWithText("Try again", ignoreCase = true).performClick()
        assertEquals(listOf("save"), calls)
    }

    @Test
    fun `UX-DR91 a Thresholds screen that could not open offers Try again`() {
        show(ThresholdsStates.failed)
        compose.onNodeWithText("Try again", ignoreCase = true).performClick()
        assertEquals(listOf("retry"), calls)
    }

    // Entry points

    private fun showDetail(
        role: SiteRole,
        state: LotDetailState = ThresholdsStates.detail(role),
    ) {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                LotDetailScreen(
                    state = state,
                    actions = LotDetailActions.None,
                    now = { LotFixtures.now },
                    zone = ZoneOffset.UTC,
                    onThresholds = { id, name, sensor -> calls += "thresholds $id $name ${sensor.orEmpty()}" },
                )
            }
        }
        compose.waitForIdle()
    }

    @Test
    fun `UX-DR45 Lot detail offers Set Thresholds to an Administrator and opens it for the Lot`() {
        showDetail(SiteRole.Administrator)
        compose.onNodeWithTag(THRESHOLDS_ACTION_TAG).performClick()
        assertEquals(listOf("thresholds lot-tomatoes Tomatoes "), calls)
        compose.onNodeWithText("Set Thresholds", ignoreCase = true).assertExists()
    }

    @Test
    fun `UX-DR45 a Sensor cell opens that Sensor's column`() {
        showDetail(SiteRole.Owner)
        compose.onNodeWithText("14 °C").performScrollTo().performClick()
        assertEquals(listOf("thresholds lot-tomatoes Tomatoes ${ThresholdsStates.AIR}"), calls)
    }

    @Test
    fun `UX-DR84 a Member gets View Thresholds on Lot detail, not Set`() {
        showDetail(SiteRole.Member)
        compose.onNodeWithTag(THRESHOLDS_ACTION_TAG).assertExists()
        compose.onNodeWithText("View Thresholds", ignoreCase = true).assertExists()
        compose.onAllNodesWithText("Set Thresholds", ignoreCase = true).assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Lot detail whose Sensors carry no id offers no Thresholds at all`() {
        showDetail(SiteRole.Owner, LotDetailFixtures.ready(LotDetailFixtures.ok))
        compose.onAllNodes(hasTestTag(THRESHOLDS_ACTION_TAG)).assertCountEquals(0)
    }

    @Test
    fun `UX-DR5 UX-DR32 UX-DR33 the chart has a legend and a summary that names the below-low days`() {
        showDetail(SiteRole.Owner)
        compose.onNodeWithText("solid bar = below 30 %").performScrollTo().assertExists()
        val summary =
            compose
                .onNodeWithTag(HISTORY_CHART_TAG)
                .fetchSemanticsNode()
                .config
                .getOrNull(SemanticsProperties.ContentDescription)
                .orEmpty()
                .joinToString(" ")
        assertTrue(summary.contains("Below 30 % on Sep 19."), summary)
    }

    @Test
    fun `UX-DR5 UX-DR33 without a band there is no legend and the summary says nothing about Thresholds`() {
        val raw = LotDetailFixtures.ready(LotDetailFixtures.needsCalibration)
        showDetail(SiteRole.Owner, raw)
        compose.onAllNodesWithText("solid bar", substring = true).assertCountEquals(0)
    }

    @Test
    fun `UX-DR5 the summary says never below when no day is under the low Threshold`() {
        val none = ThresholdsStates.detail(thresholds = SoilThresholds(ThresholdsStates.SOIL, 10, null))
        showDetail(SiteRole.Owner, none)
        compose.onNodeWithText("solid bar = below 10 %").performScrollTo().assertExists()
        val summary =
            compose
                .onNodeWithTag(HISTORY_CHART_TAG)
                .fetchSemanticsNode()
                .config[SemanticsProperties.ContentDescription]
                .joinToString(" ")
        assertTrue(summary.contains("Never below 10 %."), summary)
    }

    @Test
    fun `UX-DR5 the chart band is only built for soil moisture in percent`() {
        val chart =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Percent,
                ThresholdsStates.days,
                LotFixtures.now.toEpochMilli(),
                30,
                70,
            )
        assertEquals(1, chart.bars.count { it.belowLow })
        val copy = LotDetailCopy(compose.activity.resources, LotFixtures.now, ZoneOffset.UTC, java.util.Locale.ENGLISH)
        assertEquals("solid bar = below 30 %", copy.chartLegend(chart))
    }

    @Test
    fun `UX-DR66 UX-DR45 the Calibrate confirmation leads on to Thresholds for the same Lot`() {
        var received = ""
        compose.setContent {
            ColdframeTheme(isDark = false) {
                CalibrateScreen(
                    state = CalibrateStates.confirmPending,
                    actions = CalibrateActions(setThresholds = { id, name -> received = "$id $name" }),
                    now = { LotFixtures.now },
                    zone = ZoneOffset.UTC,
                )
            }
        }
        compose.waitForIdle()
        compose.onNodeWithText("Set Thresholds", ignoreCase = true).performClick()
        assertEquals("lot-tomatoes Tomatoes", received)
    }
}
