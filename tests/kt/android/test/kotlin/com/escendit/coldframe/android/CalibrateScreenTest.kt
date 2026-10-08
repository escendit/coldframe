package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.calibrate.CalibrateActions
import com.escendit.coldframe.android.ui.calibrate.CalibrateScreen
import com.escendit.coldframe.android.ui.setup.AddNodeFlow
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotDetailScreen
import com.escendit.coldframe.android.ui.sites.LotTiles
import com.escendit.coldframe.android.ui.sites.rememberOverviewCopy
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.calibrate.CalibrateAnnouncement
import com.escendit.coldframe.core.calibrate.CalibrateAnnouncementKind
import com.escendit.coldframe.core.calibrate.CalibrateNotice
import com.escendit.coldframe.core.calibrate.CalibrateState
import com.escendit.coldframe.core.calibrate.CalibrateStep
import com.escendit.coldframe.core.calibrate.CalibrationReading
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotTile
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorReading
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.setup.NodeOutcome
import com.escendit.coldframe.core.setup.NodeOutcomeAction
import com.escendit.coldframe.core.setup.NodeOutcomeKind
import com.escendit.coldframe.core.sites.SiteRole
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

/** Calibrate states as the core hands them to the shell (Story 5.2), told against [LotFixtures.now]. */
object CalibrateStates {
    private fun at(text: String): Long = Instant.parse(text).toEpochMilli()

    val readings =
        listOf(
            CalibrationReading(42, 1840, at("2026-10-06T07:02:00Z")),
            CalibrationReading(41, 1790, at("2026-10-06T06:47:00Z")),
            CalibrationReading(40, 1825, at("2026-10-06T06:32:00Z")),
        )

    val dry =
        CalibrateState.Ready(
            site = homeSite(),
            lotId = "lot-tomatoes",
            lotName = "Tomatoes",
            sensorId = "sensor-soil",
            step = CalibrateStep.Dry,
            afterSeq = readings.maxOfOrNull { it.readingSeq } ?: -1,
            afterMeasuredAtEpochMs = at("2026-10-06T07:03:00Z"),
            readings = readings,
            fresh = null,
            picked = null,
            dryRaw = null,
            wetRaw = null,
            recordedSeq = null,
            working = false,
            notice = null,
            percent = null,
            announcement = null,
        )

    private val freshReading = CalibrationReading(43, 612, at("2026-10-06T07:17:00Z"))

    val dryFresh =
        dry.copy(
            readings = listOf(freshReading) + readings,
            fresh = freshReading,
            announcement =
                CalibrateAnnouncement(
                    1,
                    CalibrateAnnouncementKind.FreshReading,
                    CalibrateStep.Dry,
                    612,
                    freshReading.measuredAtEpochMs,
                    "Tomatoes",
                    null,
                ),
        )

    /** Left after the dry point and returned to: the Server kept 1840. */
    val wetResumed = dry.copy(step = CalibrateStep.Wet, dryRaw = 1840)

    val wetFresh = wetResumed.copy(readings = listOf(freshReading) + readings, fresh = freshReading)

    val confirmPending = dry.copy(step = CalibrateStep.Confirm, dryRaw = 1840, wetRaw = 612)

    val confirmPercent =
        confirmPending.copy(
            percent = 40,
            announcement =
                CalibrateAnnouncement(
                    2,
                    CalibrateAnnouncementKind.FirstPercent,
                    CalibrateStep.Confirm,
                    null,
                    null,
                    "Tomatoes",
                    40,
                ),
        )

    val paused = CalibrateState.Paused(homeSite(), "lot-tomatoes", "Tomatoes", bySite = false, offersResume = true)
    val pausedBySite =
        CalibrateState.Paused(
            homeSite(),
            "lot-tomatoes",
            "Tomatoes",
            bySite = true,
            offersResume = false,
        )
    val indistinct = wetFresh.copy(notice = CalibrateNotice.Indistinct)
    val notDelivered = wetFresh.copy(notice = CalibrateNotice.NotDelivered, picked = 43)
    val noSensor = CalibrateState.Failed(homeSite(), "lot-tomatoes", "Tomatoes", CalibrateNotice.NoSensor)
}

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class CalibrateScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var state: CalibrateState by mutableStateOf(CalibrateStates.dry)
    private val defaultZone = TimeZone.getDefault()

    private val actions =
        CalibrateActions(
            close = { calls += "close" },
            retry = { calls += "retry" },
            pick = { calls += "pick $it" },
            record = { calls += "record" },
        )

    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private fun show(next: CalibrateState) {
        state = next
        compose.setContent {
            ColdframeTheme(isDark = false) {
                CalibrateScreen(state = state, actions = actions, now = { LotFixtures.now }, zone = ZoneOffset.UTC)
            }
        }
        compose.waitForIdle()
    }

    private fun liveRegionOf(description: String): LiveRegionMode? =
        compose
            .onNode(hasContentDescription(description), useUnmergedTree = true)
            .fetchSemanticsNode()
            .config
            .getOrNull(SemanticsProperties.LiveRegion)

    @Test
    fun `UX-DR66 step 1 waits for the next Reading with the last raw value and the hint`() {
        show(CalibrateStates.dry)
        compose.onNodeWithText("Step 1 of 2: dry").assertExists()
        compose.onNodeWithText("Put the probe in dry soil.").assertExists()
        compose.onNodeWithText("Waiting for the next Reading").assertExists()
        compose.onNodeWithText("Last Reading: raw 1840", substring = true).assertExists()
        compose.onNodeWithText("Short-press the Node's setup button to send a Reading now.").assertExists()
    }

    @Test
    fun `UX-DR66 Record dry stays disabled until a Reading taken after the step started arrives`() {
        show(CalibrateStates.dry)
        compose.onNodeWithText("Record dry", ignoreCase = true).assertIsNotEnabled()
        state = CalibrateStates.dryFresh
        compose.waitForIdle()
        compose.onNodeWithText("Record dry", ignoreCase = true).assertIsEnabled()
        compose.onNodeWithText("New Reading", substring = true).assertExists()
        compose.onNodeWithText("Record dry", ignoreCase = true).performClick()
        assertEquals(listOf("record"), calls)
    }

    @Test
    fun `UX-DR105 a fresh Reading is announced politely`() {
        show(CalibrateStates.dryFresh)
        val region =
            compose
                .onNode(hasContentDescription("Record dry is available.", substring = true), useUnmergedTree = true)
                .fetchSemanticsNode()
                .config
        assertEquals(LiveRegionMode.Polite, region.getOrNull(SemanticsProperties.LiveRegion))
        assertTrue(region[SemanticsProperties.ContentDescription].first().contains("raw 612. Record dry is available."))
    }

    @Test
    fun `UX-DR105 the waiting text is never announced`() {
        show(CalibrateStates.dry)
        for (text in listOf(
            "Waiting for the next Reading",
            "Short-press the Node's setup button to send a Reading now.",
        )) {
            val node = compose.onNodeWithText(text).fetchSemanticsNode()
            assertEquals(null, node.config.getOrNull(SemanticsProperties.LiveRegion), text)
        }
        // Nothing is announced while no Reading is fresh: the one region exists and is empty.
        compose
            .onNode(
                hasContentDescription("Record dry is available.", substring = true),
                useUnmergedTree = true,
            ).assertDoesNotExist()
    }

    @Test
    fun `UX-DR66 a Recent Reading can be picked instead of waiting`() {
        show(CalibrateStates.dry)
        compose.onNodeWithText("Recent Readings").assertExists()
        compose.onNodeWithText("raw 1790", substring = true).performScrollTo().performClick()
        assertEquals(listOf("pick 41"), calls)
        state = CalibrateStates.dry.copy(picked = 41)
        compose.waitForIdle()
        compose.onNodeWithText("Record dry", ignoreCase = true).assertIsEnabled()
    }

    @Test
    fun `UX-DR66 leaving after the dry point and returning resumes at step 2 wet`() {
        show(CalibrateStates.wetResumed)
        compose.onNodeWithText("Step 2 of 2: wet").assertExists()
        compose.onNodeWithText("Put the probe in water.").assertExists()
        compose.onNodeWithText("Record wet", ignoreCase = true).assertIsNotEnabled()
        state = CalibrateStates.wetFresh
        compose.waitForIdle()
        compose.onNodeWithText("Record wet", ignoreCase = true).assertIsEnabled()
    }

    @Test
    fun `UX-DR66 the confirmation shows both raw values and updates in place to the percentage`() {
        show(CalibrateStates.confirmPending)
        compose.onNodeWithText("Calibration saved").assertExists()
        compose.onNodeWithText("Dry raw 1840, wet raw 612.").assertExists()
        compose.onNodeWithText("% appears with the next Reading").assertExists()
        compose.onAllNodesWithText("Tomatoes reads", substring = true).assertCountEquals(0)
        state = CalibrateStates.confirmPercent
        compose.waitForIdle()
        compose.onNodeWithText("Tomatoes reads ~40 %").assertExists()
        compose.onAllNodesWithText("% appears with the next Reading").assertCountEquals(0)
        assertEquals(
            LiveRegionMode.Polite,
            liveRegionOf("Tomatoes reads about 40 percent."),
        )
    }

    @Test
    fun `UX-DR66 a paused Node is explained and nothing is waited for`() {
        show(CalibrateStates.paused)
        compose.onNodeWithText("This Node is paused", substring = true).assertExists()
        compose.onAllNodesWithText("Waiting for the next Reading").assertCountEquals(0)
        compose.onAllNodesWithText("Record dry", ignoreCase = true).assertCountEquals(0)
        state = CalibrateStates.pausedBySite
        compose.waitForIdle()
        compose.onNodeWithText("The Site is paused", substring = true).assertExists()
    }

    @Test
    fun `UX-DR66 indistinct points say what happened, what did not change and what to do`() {
        show(CalibrateStates.indistinct)
        compose.onNodeWithText("too close together", substring = true).assertExists()
        compose.onNodeWithText("Nothing was saved", substring = true).assertExists()
        compose.onNodeWithText("Record wet", ignoreCase = true).assertIsEnabled()
    }

    @Test
    fun `UX-DR66 a save the Node has not confirmed keeps the point and can be retried`() {
        show(CalibrateStates.notDelivered)
        compose.onNodeWithText("has not confirmed it yet", substring = true).assertExists()
        compose.onNodeWithText("Record wet", ignoreCase = true).performClick()
        assertEquals(listOf("record"), calls)
    }

    @Test
    fun `UX-DR66 a Lot without a calibratable Sensor offers Try again`() {
        show(CalibrateStates.noSensor)
        compose.onNodeWithText("no Sensor that can be calibrated", substring = true).assertExists()
        compose.onNodeWithText("Try again", ignoreCase = true).performClick()
        assertEquals(listOf("retry"), calls)
    }

    @Test
    fun `UX-DR66 Back leaves the flow and the Server keeps the dry point`() {
        show(CalibrateStates.dry)
        compose.onNodeWithText("Back", ignoreCase = true).performClick()
        assertEquals(listOf("close"), calls)
    }

    // Entry points

    private fun tiles(canCalibrate: Boolean): List<LotTile> =
        listOf(
            LotTile.of(
                LotFixtures.needsCalibration,
                stale = false,
                fetchedAtEpochMs = LotFixtures.now.toEpochMilli(),
                nowEpochMs = LotFixtures.now.toEpochMilli(),
                canAddNode = false,
                canCalibrate = canCalibrate,
            ),
        )

    private fun showTiles(canCalibrate: Boolean) {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                Column {
                    LotTiles(
                        tiles(canCalibrate),
                        rememberOverviewCopy(LotFixtures.now, ZoneOffset.UTC),
                        onOpenLot = { id, _ -> calls += "open $id" },
                        onCalibrate = { id, name -> calls += "calibrate $id $name" },
                    )
                }
            }
        }
        compose.waitForIdle()
    }

    @Test
    fun `UX-DR66 the needs-Calibration tile has a Calibrate control that does not replace the tile's tap target`() {
        showTiles(canCalibrate = true)
        compose.onNodeWithText("Calibrate", ignoreCase = true).performClick()
        compose.onNode(hasContentDescription("no percentage until calibrated", substring = true)).performClick()
        assertEquals(
            listOf(
                "calibrate ${LotFixtures.needsCalibration.id} ${LotFixtures.needsCalibration.name}",
                "open ${LotFixtures.needsCalibration.id}",
            ),
            calls,
        )
    }

    @Test
    fun `UX-DR84 a Member sees no Calibrate on the tile, hidden not disabled`() {
        val member = LotTile.of(LotFixtures.needsCalibration, false, 0, 0, canAddNode = false, canCalibrate = false)
        assertEquals(false, member.opensCalibrate)
        showTiles(canCalibrate = false)
        compose.onAllNodesWithText("Calibrate", ignoreCase = true).assertCountEquals(0)
    }

    private val calibratableSoil =
        SensorReading(
            SensorQuantity.SoilMoisture,
            1840.0,
            SensorUnit.Raw,
            Instant.parse("2026-10-06T07:02:00Z").toEpochMilli(),
            sensorId = "sensor-soil",
            calibratable = true,
        )

    private fun showDetail(role: SiteRole) {
        val lot = LotDetailFixtures.needsCalibration.copy(sensors = listOf(calibratableSoil))
        compose.setContent {
            ColdframeTheme(isDark = false) {
                LotDetailScreen(
                    state = LotDetailFixtures.ready(lot, role = role),
                    actions = LotDetailActions.None,
                    now = { LotFixtures.now },
                    onCalibrate = { id, name -> calls += "calibrate $id $name" },
                )
            }
        }
        compose.waitForIdle()
    }

    @Test
    fun `UX-DR66 Lot detail offers Calibrate to an Administrator and starts the flow`() {
        showDetail(SiteRole.Administrator)
        compose.onNodeWithText("Calibrate", ignoreCase = true).performClick()
        assertEquals(listOf("calibrate lot-tomatoes Tomatoes"), calls)
    }

    @Test
    fun `UX-DR84 a Member sees no Calibrate on Lot detail`() {
        showDetail(SiteRole.Member)
        compose.onAllNodesWithText("Calibrate", ignoreCase = true).assertCountEquals(0)
    }

    @Test
    fun `UX-DR66 a Lot detail without a calibratable Sensor shows no Calibrate to an Owner`() {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                LotDetailScreen(
                    state = LotDetailFixtures.ready(LotDetailFixtures.needsCalibration),
                    actions = LotDetailActions.None,
                    now = { LotFixtures.now },
                )
            }
        }
        compose.onAllNodesWithText("Calibrate", ignoreCase = true).assertCountEquals(0)
    }

    @Test
    fun `UX-DR66 the Node-added outcome offers Calibrate as its second action`() {
        val outcomeCalls = mutableListOf<NodeOutcomeAction>()
        compose.setContent {
            ColdframeTheme(isDark = false) {
                AddNodeFlow(
                    state =
                        NodeStates.assigned.copy(
                            outcome = NodeOutcome(NodeOutcomeKind.Assigned, 5, calibrateLotId = "lot-t"),
                        ),
                    actions = NodeSetupActions(outcomeAction = { outcomeCalls += it }),
                )
            }
        }
        compose.onNodeWithText("Calibrate", ignoreCase = true).performClick()
        compose.onNodeWithText("Done", ignoreCase = true).performClick()
        assertEquals(listOf(NodeOutcomeAction.Calibrate, NodeOutcomeAction.Done), outcomeCalls)
    }

    @Test
    fun `UX-DR84 the Node-added outcome of a caller who may not calibrate has no Calibrate`() {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                AddNodeFlow(state = NodeStates.assigned, actions = NodeSetupActions.None)
            }
        }
        compose.onAllNodesWithText("Calibrate", ignoreCase = true).assertCountEquals(0)
        assertTrue(LotStatus.NeedsCalibration.key.isNotEmpty())
    }
}
