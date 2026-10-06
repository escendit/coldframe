package com.escendit.coldframe.android

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.LotFixtures.plainSpaces
import com.escendit.coldframe.android.ui.sites.OverviewCopy
import com.escendit.coldframe.android.ui.sites.TileBorder
import com.escendit.coldframe.android.ui.sites.TileFill
import com.escendit.coldframe.android.ui.sites.carbonIcon
import com.escendit.coldframe.android.ui.sites.shape
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotTile
import com.escendit.coldframe.core.lots.LotTileVariant
import com.escendit.coldframe.core.lots.LotsOverview
import com.escendit.coldframe.core.lots.StaleAge
import com.escendit.coldframe.core.lots.StaleReason
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import java.time.ZoneOffset
import java.util.Locale
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull

/**
 * The catalogue strings of the Site overview (Story 4.7): the core's structured model in, the
 * words of `strings.xml` out. Nothing here decides a status, an order or a duration.
 */
@RunWith(AndroidJUnit4::class)
class OverviewCopyTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val copy = OverviewCopy(context.resources, LotFixtures.now, ZoneOffset.UTC, Locale.UK)

    private fun overview(
        lots: List<LotSummary>,
        stale: Boolean = false,
    ): LotsOverview =
        LotsOverview.of(
            LotFixtures.ready(lots, staleReason = if (stale) StaleReason.Unreachable else null),
            LotFixtures.now.toEpochMilli(),
        )

    private fun tile(
        lot: LotSummary,
        stale: Boolean = false,
    ): LotTile = overview(listOf(lot), stale).tiles.single()

    private fun shown(
        lot: LotSummary,
        stale: Boolean = false,
    ): List<String?> = tile(lot, stale).let { listOf(copy.label(it), copy.value(it), copy.foot(it)) }

    private fun spoken(
        lot: LotSummary,
        stale: Boolean = false,
    ): String = copy.spoken(tile(lot, stale))

    @Test
    fun `UX-DR18 UX-DR77 every status shows its label, value and foot from the Server's fields`() {
        assertEquals(listOf("Needs water", "~20", "07:02 · low 30%"), shown(LotFixtures.needsWater))
        assertEquals(listOf("OK", "~35", "07:01 · low 25%"), shown(LotFixtures.ok))
        assertEquals(listOf("Silent · unknown", "6 h", "was ~40% at 01:04"), shown(LotFixtures.unknownNode))
        assertEquals(listOf("Hub silent · unknown", "30 min", "last Reading 06:30"), shown(LotFixtures.unknownHub))
        assertEquals(listOf("Silent · unknown", "2 d", "no Readings yet"), shown(LotFixtures.unknownNoReading))
        assertEquals(
            listOf("Needs Calibration", "raw", "no % until calibrated"),
            shown(LotFixtures.needsCalibration),
        )
        assertEquals(listOf("Paused", "—", "until 1 Nov"), shown(LotFixtures.paused))
        assertEquals(listOf("Paused by Site", "—", "paused"), shown(LotFixtures.pausedBySite))
        assertEquals(listOf("no Node", "+", "add a Node"), shown(LotFixtures.noNode))
    }

    @Test
    fun `UX-DR18 an OK Lot without a percentage shows no big value, only the parts present`() {
        assertEquals(listOf("OK", null, null), shown(LotFixtures.okBare))
        assertEquals(
            listOf("OK", null, "07:01"),
            shown(LotFixtures.ok.copy(moisturePercent = null, lowThresholdPercent = null)),
        )
        assertEquals(listOf("OK", "~35", "low 25%"), shown(LotFixtures.ok.copy(lastReadingAtEpochMs = null)))
        assertEquals("Basil, OK", spoken(LotFixtures.okBare))
    }

    @Test
    fun `UX-DR128 soil moisture is a tilde and the nearest 5, and an uncalibrated Lot never gets a percentage`() {
        assertEquals("~20", copy.value(tile(LotFixtures.needsWater.copy(moisturePercent = 22.4))))
        assertEquals("~25", copy.value(tile(LotFixtures.needsWater.copy(moisturePercent = 22.5))))
        assertEquals("~100", copy.value(tile(LotFixtures.ok.copy(moisturePercent = 99.0))))
        val uncalibrated = LotFixtures.needsCalibration.copy(moisturePercent = 37.0, lowThresholdPercent = 30.0)
        assertEquals(listOf("Needs Calibration", "raw", "no % until calibrated"), shown(uncalibrated))
        assertFalse(spoken(uncalibrated).contains("37"))
    }

    @Test
    fun `UX-DR98 UX-DR77 every status has its complete spoken label`() {
        assertEquals(
            "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02",
            spoken(LotFixtures.needsWater),
        )
        assertEquals("Lettuce, OK, about 35 percent, low 25 percent", spoken(LotFixtures.ok))
        assertEquals(
            "Beans, unknown, Node silent for 6 hours, last about 40 percent at 01:04",
            spoken(LotFixtures.unknownNode),
        )
        assertEquals("Herbs, unknown, Hub silent for 30 minutes, last Reading 06:30", spoken(LotFixtures.unknownHub))
        assertEquals("Chillies, unknown, Node silent for 2 days, no Readings yet", spoken(LotFixtures.unknownNoReading))
        assertEquals(
            "Peppers, needs Calibration, no percentage until calibrated",
            spoken(LotFixtures.needsCalibration),
        )
        assertEquals("Squash, paused until 1 November", spoken(LotFixtures.paused))
        assertEquals("Kale, paused with the Site", spoken(LotFixtures.pausedBySite))
        assertEquals("Carrots, paused", spoken(LotFixtures.paused.copy(name = "Carrots", pausedUntilEpochMs = null)))
        assertEquals("Carrots, no Node, add a Node", spoken(LotFixtures.noNode))
    }

    @Test
    fun `UX-DR98 a silence of one unit is spoken in the singular`() {
        val hour =
            LotFixtures.unknownNode.copy(
                lastReadingAtEpochMs = Instant.parse("2026-10-06T06:00:00Z").toEpochMilli(),
            )
        assertEquals("Beans, unknown, Node silent for 1 hour, last about 40 percent at 06:00", spoken(hour))
    }

    @Test
    fun `UX-DR19 UX-DR98 a stale tile says Was and its status, no value, and as of the last good read`() {
        assertEquals(listOf("Was needs water", null, "as of 04:52"), shown(LotFixtures.needsWater, stale = true))
        assertEquals(listOf("Was OK", null, "as of 04:52"), shown(LotFixtures.ok, stale = true))
        assertEquals(listOf("Was silent · unknown", null, "as of 04:52"), shown(LotFixtures.unknownNode, stale = true))
        assertEquals(
            listOf("Was Hub silent · unknown", null, "as of 04:52"),
            shown(LotFixtures.unknownHub, stale = true),
        )
        assertEquals(
            listOf("Was needs Calibration", null, "as of 04:52"),
            shown(LotFixtures.needsCalibration, stale = true),
        )
        assertEquals(listOf("Was paused", null, "as of 04:52"), shown(LotFixtures.paused, stale = true))
        assertEquals(listOf("Was paused by Site", null, "as of 04:52"), shown(LotFixtures.pausedBySite, stale = true))
        assertEquals(listOf("Was no Node", null, "as of 04:52"), shown(LotFixtures.noNode, stale = true))

        assertEquals("Tomatoes, was needs water, not live, as of 04:52", spoken(LotFixtures.needsWater, stale = true))
        assertEquals("Lettuce, was OK, not live, as of 04:52", spoken(LotFixtures.ok, stale = true))
        assertEquals("Beans, was unknown, not live, as of 04:52", spoken(LotFixtures.unknownNode, stale = true))
        assertEquals(
            "Peppers, was needs Calibration, not live, as of 04:52",
            spoken(LotFixtures.needsCalibration, stale = true),
        )
        assertEquals("Kale, was paused, not live, as of 04:52", spoken(LotFixtures.pausedBySite, stale = true))
        assertEquals("Carrots, was no Node, not live, as of 04:52", spoken(LotFixtures.noNode, stale = true))
    }

    @Test
    fun `UX-DR129 the headline is the first sentence that applies`() {
        fun headline(vararg lots: LotSummary): String = copy.headline(overview(lots.toList()).headline)

        assertEquals("No Readings yet", headline())
        assertEquals("No Readings yet", headline(LotFixtures.noNode))
        assertEquals("Tomatoes needs water", headline(LotFixtures.needsWater, LotFixtures.unknownNode))
        assertEquals(
            "2 Lots need water",
            headline(LotFixtures.needsWater, LotFixtures.needsWater.copy(id = "lot-2", name = "Leeks")),
        )
        assertEquals("1 Lot can't be read", headline(LotFixtures.needsCalibration, LotFixtures.ok))
        assertEquals("2 Lots can't be read", headline(LotFixtures.needsCalibration, LotFixtures.unknownHub))
        assertEquals("Nothing needs water", headline(LotFixtures.ok, LotFixtures.paused, LotFixtures.noNode))
        assertEquals("Paused", headline(LotFixtures.pausedBySite, LotFixtures.noNode))
        val until = LotFixtures.pausedBySite.copy(pausedUntilEpochMs = LotFixtures.paused.pausedUntilEpochMs)
        assertEquals("Paused until 1 Nov", headline(until, until.copy(id = "lot-2", name = "Leeks")))
    }

    @Test
    fun `UX-DR129 UX-DR21 the subline counts the statuses in the Server's order, without needs water`() {
        fun subline(vararg lots: LotSummary): String? = copy.subline(overview(lots.toList()))

        assertEquals("Nothing is measuring, so there's no status to show.", subline())
        assertEquals("Nothing is measuring, so there's no status to show.", subline(LotFixtures.noNode))
        assertEquals(
            "1 needs Calibration · 3 unknown · 2 OK · 2 paused · 1 without Node",
            copy.subline(overview(LotFixtures.everyVariant)),
        )
        assertEquals(
            "2 need Calibration",
            subline(LotFixtures.needsCalibration, LotFixtures.needsCalibration.copy(id = "lot-2")),
        )
        assertNull(subline(LotFixtures.needsWater))
    }

    @Test
    fun `UX-DR24 the stale header names the Site, the age of the data and when it is from`() {
        assertEquals("Home garden · can't reach your Server", copy.staleTitle("Home garden"))
        assertEquals("2 h 12 min old", copy.staleAge(StaleAge(days = 0, hours = 2, minutes = 12)))
        assertEquals("12 min old", copy.staleAge(StaleAge(days = 0, hours = 0, minutes = 12)))
        assertEquals("2 h old", copy.staleAge(StaleAge(days = 0, hours = 2, minutes = 0)))
        assertEquals("3 d old", copy.staleAge(StaleAge(days = 3, hours = 4, minutes = 5)))
        assertEquals(
            "Last data 04:52. You may be away from home, or the Server is down. Nothing below is live.",
            copy.staleDetail(LotFixtures.fetchedAt.toEpochMilli()),
        )
    }

    @Test
    fun `UX-DR106 the stale announcements are the catalogue's sentences`() {
        assertEquals(
            "Can't reach your Server. Showing data from 04:52.",
            copy.enteredStale(LotFixtures.fetchedAt.toEpochMilli()),
        )
        assertEquals("Live again.", copy.leftStale())
        val us = OverviewCopy(context.resources, LotFixtures.now, ZoneOffset.UTC, Locale.US)
        assertEquals(
            "Can't reach your Server. Showing data from 4:52 AM.",
            us.enteredStale(LotFixtures.fetchedAt.toEpochMilli()).plainSpaces(),
        )
    }

    @Test
    fun `UX-DR99 every variant keeps its own Carbon icon and its own shape with colour and text removed`() {
        val icons = LotTileVariant.entries.associateWith { it.carbonIcon().carbonName }
        assertEquals(
            mapOf(
                LotTileVariant.NeedsWater to "rain-drop",
                LotTileVariant.Ok to "checkmark--outline",
                LotTileVariant.Unknown to "help",
                LotTileVariant.NeedsCalibration to "tools",
                LotTileVariant.Paused to "pause--outline",
                LotTileVariant.NoNode to "add",
                LotTileVariant.Stale to "cloud--offline",
            ),
            icons,
        )
        val shapes = LotTileVariant.entries.associateWith { it.shape() }
        assertEquals(LotTileVariant.entries.size, shapes.values.toSet().size)
        assertEquals(
            TileFill.Solid to TileBorder.None,
            shapes.getValue(LotTileVariant.NeedsWater).let {
                it.fill to
                    it.border
            },
        )
        assertEquals(TileFill.Soil to TileBorder.Solid, shapes.getValue(LotTileVariant.Ok).let { it.fill to it.border })
        assertEquals(
            TileFill.Hatch to TileBorder.Dashed,
            shapes.getValue(LotTileVariant.Unknown).let {
                it.fill to
                    it.border
            },
        )
        assertEquals(1, shapes.getValue(LotTileVariant.Unknown).borderWidthDp)
        assertEquals(
            TileFill.Hatch to TileBorder.Dashed,
            shapes.getValue(LotTileVariant.NeedsCalibration).let { it.fill to it.border },
        )
        assertEquals(2, shapes.getValue(LotTileVariant.NeedsCalibration).borderWidthDp)
        assertEquals(
            TileFill.Flat to TileBorder.Solid,
            shapes.getValue(LotTileVariant.Paused).let {
                it.fill to
                    it.border
            },
        )
        assertEquals(2, shapes.getValue(LotTileVariant.Paused).borderWidthDp)
        assertEquals(
            TileFill.Empty to TileBorder.Dotted,
            shapes.getValue(LotTileVariant.NoNode).let {
                it.fill to
                    it.border
            },
        )
        assertEquals(
            TileFill.Empty to TileBorder.Solid,
            shapes.getValue(LotTileVariant.Stale).let {
                it.fill to
                    it.border
            },
        )
        assertEquals(1, shapes.getValue(LotTileVariant.Stale).borderWidthDp)
    }

    @Test
    fun `UX-DR77 a status this client does not know reads as unknown, never as fine`() {
        val odd =
            LotSummary(
                "lot-odd",
                "Leeks",
                LotStatus.fromServer("tooWet"),
                statusSinceEpochMs = LotFixtures.now.toEpochMilli(),
            )
        assertEquals("Silent · unknown", copy.label(tile(odd)))
        assertEquals("Leeks, unknown, Node silent for 0 minutes, no Readings yet", spoken(odd))
    }
}
