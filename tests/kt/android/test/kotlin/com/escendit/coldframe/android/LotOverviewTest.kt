package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PixelMap
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeDown
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.LotFixtures.plainSpaces
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.sites.LOT_TILE_SKELETON_TAG
import com.escendit.coldframe.android.ui.sites.LotTileContent
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.OverviewCopy
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotTile
import com.escendit.coldframe.core.lots.LotsEvent
import com.escendit.coldframe.core.lots.LotsOverview
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.designtokens.ColorTokens
import com.escendit.coldframe.designtokens.ThemedColor
import kotlinx.coroutines.flow.MutableSharedFlow
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Instant
import java.time.ZoneOffset
import java.util.Locale
import java.util.TimeZone
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The Site overview with Lots of every status, stale mode, the skeleton and refresh (Story 4.7). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class LotOverviewTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private val opened = mutableListOf<String?>()
    private val events = MutableSharedFlow<LotsEvent>(extraBufferCapacity = 8)
    private var foregrounds = 0
    private var clock: Instant = LotFixtures.now
    private var lots by mutableStateOf<LotsState>(LotsState.Idle)

    private val defaultZone = TimeZone.getDefault()

    // Reading times are told in the phone's zone: fixed, so the labels hold anywhere.
    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private fun show(
        initial: LotsState,
        role: SiteRole = SiteRole.Owner,
        fontScale: Float = 1f,
    ) {
        lots = initial
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(homeSite(role)),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lots,
                    lotsActions = LotsActions(load = { calls += "load" }, refresh = { calls += "refresh" }),
                    now = { clock },
                    nodeSetupActions = NodeSetupActions(open = { opened += it }),
                    lotsEvents = events,
                    onForeground = { foregrounds++ },
                )
            }
        }
        compose.waitForIdle()
    }

    private val SemanticsNode.description: String?
        get() =
            config
                .getOrNull(SemanticsProperties.ContentDescription)
                ?.joinToString(" ")
                ?.plainSpaces()

    /** The Lot tiles: one node each, found by a spoken label that starts with a Lot's name. */
    private fun tiles(of: List<LotSummary> = LotFixtures.everyVariant): List<SemanticsNode> =
        compose
            .allNodes()
            .filter { node -> of.any { node.description?.startsWith("${it.name}, ") == true } }
            .sortedWith(compareBy({ it.positionInRoot.y }, { it.positionInRoot.x }))

    private fun pixelsOf(spoken: String): PixelMap =
        compose
            .onNodeWithContentDescription(spoken, substring = true)
            .performScrollTo()
            .captureToImage()
            .toPixelMap()

    private fun PixelMap.colours(): Set<Color> {
        val seen = mutableSetOf<Color>()
        for (x in 0 until width) for (y in 0 until height) seen += this[x, y]
        return seen
    }

    private fun light(token: ThemedColor) = Color(token.light.toInt())

    // Tiles

    @Test
    fun `UX-DR20 UX-DR98 one accessibility element per Lot, in the Server's order, with the complete spoken label`() {
        show(LotFixtures.ready())

        val spoken = tiles().map { it.description }
        assertEquals(
            listOf(
                "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 7:02 AM",
                "Peppers, needs Calibration, no percentage until calibrated",
                "Beans, unknown, Node silent for 6 hours, last about 40 percent at 1:04 AM",
                "Herbs, unknown, Hub silent for 30 minutes, last Reading 6:30 AM",
                "Chillies, unknown, Node silent for 2 days, no Readings yet",
                "Lettuce, OK, about 35 percent, low 25 percent",
                "Basil, OK",
                "Squash, paused until November 1",
                "Kale, paused with the Site",
                "Carrots, no Node, add a Node",
            ),
            spoken,
        )
        // Merged for TalkBack: nothing inside a tile is an element of its own, and no tile text
        // is read beside the spoken label.
        val merged = compose.onAllNodes(hasContentDescription(", ", substring = true)).fetchSemanticsNodes()
        val mergedTiles =
            merged.filter { node ->
                LotFixtures.everyVariant.any {
                    node.description?.startsWith(it.name) ==
                        true
                }
            }
        assertEquals(10, mergedTiles.size)
        for (tile in mergedTiles) {
            assertTrue(tile.children.isEmpty(), tile.description.orEmpty())
            assertNull(tile.config.getOrNull(SemanticsProperties.Text), tile.description)
        }
    }

    @Test
    fun `UX-DR20 UX-DR77 a reversed list is drawn reversed, the shell never re-sorts`() {
        show(LotFixtures.ready(LotFixtures.everyStatus.reversed()))

        assertEquals(
            listOf("Carrots", "Squash", "Lettuce", "Beans", "Peppers", "Tomatoes"),
            tiles().map { it.description.orEmpty().substringBefore(",") },
        )
    }

    @Test
    fun `UX-DR20 only a no-Node tile is a tap target, for Administrators and Owners`() {
        show(LotFixtures.ready())

        val tappable = tiles().filter { it.isClickable }
        assertEquals(listOf("Carrots, no Node, add a Node"), tappable.map { it.description })
        compose.onNodeWithContentDescription("Carrots, no Node, add a Node").performScrollTo().performClick()
        assertEquals(listOf<String?>("lot-carrots"), opened)
    }

    @Test
    fun `UX-DR20 a Member has no tile to tap`() {
        show(LotFixtures.ready(role = SiteRole.Member), role = SiteRole.Member)

        assertEquals(10, tiles().size)
        assertTrue(tiles().none { it.isClickable })
    }

    @Test
    fun `UX-DR17 UX-DR107 two columns of tiles that are at least square at the default font scale`() {
        show(LotFixtures.ready())

        val all = tiles()
        val gutter = with(compose.density) { 16.dp.toPx() }
        val gap = with(compose.density) { 8.dp.toPx() }
        val width =
            compose
                .onRoot()
                .fetchSemanticsNode()
                .size.width
        val side = (width - 2 * gutter - gap) / 2
        assertEquals(all[0].positionInRoot.y, all[1].positionInRoot.y)
        assertEquals(gutter, all[0].positionInRoot.x, 1f)
        assertEquals(gutter + side + gap, all[1].positionInRoot.x, 1f)
        assertTrue(all[2].positionInRoot.y > all[0].positionInRoot.y)
        for (tile in all) {
            assertEquals(side, tile.size.width.toFloat(), 1f)
            assertTrue(tile.size.height >= tile.size.width - 1, "${tile.description} is at least square")
        }
    }

    @Test
    fun `UX-DR97 UX-DR107 one column from font scale 1_5, and the tiles keep their square`() {
        show(LotFixtures.ready(), fontScale = 1.5f)

        val all = tiles()
        val gutter = with(compose.density) { 16.dp.toPx() }
        val width =
            compose
                .onRoot()
                .fetchSemanticsNode()
                .size.width
        assertEquals(10, all.size)
        assertEquals(1, all.map { it.positionInRoot.x }.toSet().size)
        assertEquals(10, all.map { it.positionInRoot.y }.toSet().size)
        for (tile in all) {
            assertEquals(width - 2 * gutter, tile.size.width.toFloat(), 1f)
            assertTrue(tile.size.height >= tile.size.width - 1, "${tile.description} never shrinks")
        }
    }

    private fun showContent(
        lot: LotSummary,
        oneColumn: Boolean,
        fontScale: Float = 1f,
        stale: Boolean = false,
    ) {
        val ready = LotFixtures.ready(listOf(lot), staleReason = if (stale) StaleReason.Unreachable else null)
        val tile = LotsOverview.of(ready, LotFixtures.now.toEpochMilli()).tiles.single()
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = false) {
                    Box(
                        Modifier.width(if (oneColumn) 320.dp else 160.dp).height(if (oneColumn) 600.dp else 400.dp),
                        propagateMinConstraints = true,
                    ) {
                        LotTileContent(
                            tile = tile,
                            copy = OverviewCopy(compose.activity.resources, LotFixtures.now, ZoneOffset.UTC, Locale.UK),
                            oneColumn = oneColumn,
                        )
                    }
                }
            }
        }
        compose.waitForIdle()
    }

    private fun gapBetweenLabelAndValue(): Float {
        val label = compose.onNodeWithText("NEEDS WATER").fetchSemanticsNode()
        val value = compose.onNodeWithText("~20").fetchSemanticsNode()
        return value.positionInRoot.y - (label.positionInRoot.y + label.size.height)
    }

    @Test
    fun `UX-DR17 in two columns the name and status label sit at the top and the value over the foot at the bottom`() {
        showContent(LotFixtures.needsWater, oneColumn = false)

        val name = compose.onNodeWithText("Tomatoes").fetchSemanticsNode()
        val label = compose.onNodeWithText("NEEDS WATER").fetchSemanticsNode()
        val value = compose.onNodeWithText("~20").fetchSemanticsNode()
        val foot = compose.onNodeWithText("07:02 · low 30%").fetchSemanticsNode()
        assertTrue(name.positionInRoot.y < label.positionInRoot.y)
        assertTrue(value.positionInRoot.y < foot.positionInRoot.y)
        // The tile is 400 dp high: the value is pushed to the bottom edge, far from the label.
        assertTrue(gapBetweenLabelAndValue() > with(compose.density) { 200.dp.toPx() })
        val bottom = with(compose.density) { 400.dp.toPx() }
        assertTrue(bottom - (foot.positionInRoot.y + foot.size.height) <= with(compose.density) { 14.dp.toPx() })
    }

    @Test
    fun `UX-DR17 UX-DR97 in one column the value sits directly under the status label`() {
        showContent(LotFixtures.needsWater, oneColumn = true, fontScale = 2f)

        val gap = gapBetweenLabelAndValue()
        assertTrue(gap >= 0f && gap <= with(compose.density) { 24.dp.toPx() }, "gap $gap")
    }

    @Test
    fun `UX-DR17 UX-DR97 at font scale 2 no tile clips its name, label, value or foot`() {
        var shown by mutableStateOf<LotTile?>(null)
        compose.setContent {
            AtFontScale(2f) {
                ColdframeTheme(isDark = false) {
                    // The width of a one-column tile on a 411 dp phone.
                    Box(Modifier.width(379.dp)) {
                        shown?.let {
                            LotTileContent(
                                tile = it,
                                copy =
                                    OverviewCopy(
                                        compose.activity.resources,
                                        LotFixtures.now,
                                        ZoneOffset.UTC,
                                        Locale.UK,
                                    ),
                                oneColumn = true,
                            )
                        }
                    }
                }
            }
        }
        for (lot in LotFixtures.everyVariant) {
            for (stale in listOf(false, true)) {
                val long = lot.copy(name = "${lot.name} by the greenhouse door")
                val ready = LotFixtures.ready(listOf(long), staleReason = if (stale) StaleReason.Unreachable else null)
                shown = LotsOverview.of(ready, LotFixtures.now.toEpochMilli()).tiles.single()
                compose.assertNothingOverflows("${lot.name}, stale $stale")
            }
        }
    }

    @Test
    fun `UX-DR18 every status is drawn with its own fill and border tokens`() {
        show(LotFixtures.ready())

        // Needs water: solid, no border, with the darker level under the Reading's percentage.
        val water = pixelsOf("Tomatoes, needs water")
        assertEquals(light(ColorTokens.statusWaterFill), water[0, 0])
        assertEquals(light(ColorTokens.statusWaterLevel), water[4, water.height - 4])
        assertTrue(light(ColorTokens.statusWaterInk) in water.colours())

        // OK: soil fill under a 1 dp border, the level rising from the bottom edge.
        val ok = pixelsOf("Lettuce, OK")
        assertEquals(light(ColorTokens.statusOkBorder), ok[0, ok.height / 2])
        assertEquals(light(ColorTokens.statusOkFill), ok[4, 4])
        assertEquals(light(ColorTokens.statusOkLevel), ok[4, ok.height - 4])
        assertTrue(light(ColorTokens.statusLevelEdge) in ok.colours())
        assertTrue(light(ColorTokens.statusLowMarker) in ok.colours())

        // OK without a percentage: no level.
        val bare = pixelsOf("Basil, OK")
        assertEquals(light(ColorTokens.statusOkFill), bare[4, bare.height - 4])
        assertFalse(light(ColorTokens.statusLevelEdge) in (0 until bare.height).map { bare[4, it] })

        // Unknown and needs Calibration: hatch lines on the hatch ground.
        for (hatched in listOf("Beans, unknown", "Peppers, needs Calibration")) {
            val colours = pixelsOf(hatched).colours()
            assertTrue(light(ColorTokens.statusHatchGround) in colours, hatched)
            assertTrue(light(ColorTokens.statusHatchLine) in colours, hatched)
        }
        assertTrue(light(ColorTokens.statusCalibrationBorder) in pixelsOf("Peppers, needs Calibration").colours())
        assertFalse(light(ColorTokens.statusCalibrationBorder) in pixelsOf("Beans, unknown").colours())

        // Paused: flat fill under a 2 dp border.
        val paused = pixelsOf("Squash, paused")
        assertEquals(light(ColorTokens.statusPausedBorder), paused[0, 0])
        assertEquals(light(ColorTokens.statusPausedBorder), paused[1, paused.height / 2])
        assertEquals(light(ColorTokens.statusPausedFill), paused[4, 4])

        // No Node: empty.
        val noNode = pixelsOf("Carrots, no Node")
        assertEquals(light(ColorTokens.background), noNode[4, 4])
    }

    @Test
    fun `UX-DR19 a stale tile is stripped to a solid outline, whatever its status was`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))

        for (lot in LotFixtures.everyVariant) {
            val pixels = pixelsOf("${lot.name}, was")
            val colours = pixels.colours()
            assertEquals(light(ColorTokens.staleBorder), pixels[0, pixels.height / 2], lot.name)
            assertEquals(light(ColorTokens.background), pixels[4, 4], lot.name)
            assertEquals(light(ColorTokens.background), pixels[4, pixels.height - 4], lot.name)
            for (fill in listOf(
                ColorTokens.statusWaterFill,
                ColorTokens.statusWaterLevel,
                ColorTokens.statusPausedFill,
                ColorTokens.statusPausedBorder,
                ColorTokens.statusCalibrationBorder,
            )) {
                assertFalse(light(fill) in colours, "${lot.name} keeps no fill or status colour")
            }
        }
    }

    // The header

    @Test
    fun `UX-DR21 UX-DR129 the headline is a heading and the counts follow it`() {
        show(LotFixtures.ready())

        compose.onNode(isHeading().and(hasText("Tomatoes needs water"))).assertExists()
        compose.onNodeWithText("1 needs Calibration · 3 unknown · 2 OK · 2 paused · 1 without Node").assertExists()
        compose.onAllNodesWithText("No Readings yet").assertCountEquals(0)
    }

    @Test
    fun `UX-DR129 a Site paused as a whole has its headline in paused ink`() {
        val until = LotFixtures.pausedBySite.copy(pausedUntilEpochMs = LotFixtures.paused.pausedUntilEpochMs)
        show(LotFixtures.ready(listOf(until, LotFixtures.noNode)))

        val headline = compose.onNode(isHeading().and(hasText("Paused until Nov 1"))).fetchSemanticsNode()
        assertEquals(
            light(ColorTokens.statusPausedInk),
            headline
                .textLayouts()
                .single()
                .layoutInput.style.color,
        )

        lots = LotFixtures.ready(listOf(LotFixtures.ok))
        compose.waitForIdle()
        val calm = compose.onNode(isHeading().and(hasText("Nothing needs water"))).fetchSemanticsNode()
        assertEquals(
            light(ColorTokens.textPrimary),
            calm
                .textLayouts()
                .single()
                .layoutInput.style.color,
        )
    }

    @Test
    fun `UX-DR129 a Site whose Lots have no Node keeps No Readings yet and its detail line`() {
        show(LotFixtures.ready(listOf(LotFixtures.noNode)))

        compose.onNode(isHeading().and(hasText("No Readings yet"))).assertExists()
        compose.onNodeWithText("Nothing is measuring, so there's no status to show.").assertExists()
    }

    // Cold start

    @Test
    fun `UX-DR80 UX-DR19 a cold start without stored Lots says Loading over outline tiles that are not focusable`() {
        show(LotsState.Loading(homeSite()))

        compose.onNode(isHeading().and(hasText("Loading Home garden"))).assertExists()
        compose.onAllNodesWithText("No Readings yet").assertCountEquals(0)
        val skeletons =
            compose
                .onAllNodes(
                    hasTestTag(LOT_TILE_SKELETON_TAG),
                    useUnmergedTree = true,
                ).fetchSemanticsNodes()
        assertEquals(4, skeletons.size)
        for (skeleton in skeletons) {
            assertNull(skeleton.config.getOrNull(SemanticsProperties.ContentDescription))
            assertNull(skeleton.config.getOrNull(SemanticsProperties.Text))
            assertNull(skeleton.config.getOrNull(SemanticsProperties.Focused))
            assertFalse(skeleton.isClickable)
            assertTrue(skeleton.children.isEmpty())
        }
        val pixels =
            compose
                .onAllNodes(hasTestTag(LOT_TILE_SKELETON_TAG), useUnmergedTree = true)[0]
                .performScrollTo()
                .captureToImage()
                .toPixelMap()
        assertEquals(setOf(light(ColorTokens.borderSubtle), light(ColorTokens.background)), pixels.colours())
    }

    @Test
    fun `UX-DR80 UX-DR79 stored Lots show in stale mode until the first read lands`() {
        show(LotFixtures.ready(staleReason = StaleReason.Cached, refreshing = true))

        compose.onNodeWithText("Home garden · can't reach your Server").assertExists()
        assertEquals(10, tiles().size)
        assertTrue(tiles().all { it.description.orEmpty().endsWith("not live, as of 4:52 AM") })

        lots = LotFixtures.ready()
        compose.waitForIdle()
        compose.onNode(isHeading().and(hasText("Tomatoes needs water"))).assertExists()
        compose.onAllNodesWithText("Home garden · can't reach your Server").assertCountEquals(0)
    }

    @Test
    fun `UX-DR80 without stored Lots and without the Server the load notice shows, and Try again loads`() {
        show(LotsState.Failed(homeSite(), SitesNotice.Unreachable))

        compose
            .onNodeWithText("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.")
            .assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertEquals(listOf("load"), calls)
    }

    // Stale mode

    @Test
    fun `UX-DR79 UX-DR24 in stale mode the stale header replaces the summary and every tile is stale`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))

        compose.onNodeWithText("Home garden · can't reach your Server").assertExists()
        compose.onNode(isHeading().and(hasText("2 h 12 min old"))).assertExists()
        val detail =
            compose.allNodes().mapNotNull { node ->
                node.config
                    .getOrNull(SemanticsProperties.Text)
                    ?.joinToString("") { it.text }
                    ?.plainSpaces()
            }
        assertTrue(
            "Last data 4:52 AM. You may be away from home, or the Server is down. Nothing below is live." in detail,
        )
        compose.onAllNodesWithText("Tomatoes needs water").assertCountEquals(0)
        compose.onAllNodesWithText("1 needs Calibration", substring = true).assertCountEquals(0)

        assertEquals(
            listOf(
                "Tomatoes, was needs water, not live, as of 4:52 AM",
                "Peppers, was needs Calibration, not live, as of 4:52 AM",
                "Beans, was unknown, not live, as of 4:52 AM",
                "Herbs, was unknown, not live, as of 4:52 AM",
                "Chillies, was unknown, not live, as of 4:52 AM",
                "Lettuce, was OK, not live, as of 4:52 AM",
                "Basil, was OK, not live, as of 4:52 AM",
                "Squash, was paused, not live, as of 4:52 AM",
                "Kale, was paused, not live, as of 4:52 AM",
                "Carrots, was no Node, not live, as of 4:52 AM",
            ),
            tiles().map { it.description },
        )
        // A stale no-Node tile does not start Add a Node.
        assertTrue(tiles().none { it.isClickable })
    }

    @Test
    fun `UX-DR79 the first successful refresh restores the live view`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))
        compose.onNode(isHeading().and(hasText("2 h 12 min old"))).assertExists()

        lots = LotFixtures.ready()
        compose.waitForIdle()

        compose.onAllNodesWithText("min old", substring = true).assertCountEquals(0)
        compose.onNode(isHeading().and(hasText("Tomatoes needs water"))).assertExists()
        assertEquals(
            "Tomatoes, needs water, about 20 percent, low 30 percent, Reading 7:02 AM",
            tiles().first().description,
        )
    }

    @Test
    fun `UX-DR22 UX-DR79 in stale mode the Site menu items are disabled with Needs your Server`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))

        compose.onNodeWithContentDescription("Site menu for Home garden").performClick()

        compose.onNode(hasText("Site settings").and(hasClickAction())).assertIsNotEnabled()
        compose.onNodeWithText("Needs your Server").assertExists()
    }

    @Test
    fun `UX-DR22 while live the Site menu opens Site settings and says nothing about the Server`() {
        show(LotFixtures.ready())

        compose.onNodeWithContentDescription("Site menu for Home garden").performClick()

        compose.onAllNodesWithText("Needs your Server").assertCountEquals(0)
        compose.onNode(hasText("Site settings").and(hasClickAction())).assertIsEnabled().performClick()
        compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
    }

    private fun liveRegions(): List<SemanticsNode> =
        compose.allNodes().filter { it.config.getOrNull(SemanticsProperties.LiveRegion) != null }

    @Test
    fun `UX-DR24 UX-DR106 the age ticks every minute and a tick is never announced`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))
        compose.onNode(isHeading().and(hasText("2 h 12 min old"))).assertExists()
        val before = liveRegions().map { it.description }

        clock = LotFixtures.now.plusSeconds(60)
        compose.mainClock.advanceTimeBy(60_000)
        compose.waitForIdle()

        compose.onNode(isHeading().and(hasText("2 h 13 min old"))).assertExists()
        // The age is plain text outside every live region, and no region changed with the tick.
        val age = compose.onNode(hasText("2 h 13 min old")).fetchSemanticsNode()
        assertNull(age.config.getOrNull(SemanticsProperties.LiveRegion))
        assertEquals(before, liveRegions().map { it.description })
        assertTrue(liveRegions().none { it.spokenLabel().contains("old") })
    }

    @Test
    fun `UX-DR77 the minute tick moves the silence of an unknown Lot from the Server's timestamps`() {
        show(LotFixtures.ready(listOf(LotFixtures.unknownHub)))
        assertEquals("Herbs, unknown, Hub silent for 30 minutes, last Reading 6:30 AM", tiles().single().description)

        clock = LotFixtures.now.plusSeconds(120)
        compose.mainClock.advanceTimeBy(60_000)
        compose.waitForIdle()

        assertEquals("Herbs, unknown, Hub silent for 32 minutes, last Reading 6:30 AM", tiles().single().description)
    }

    @Test
    fun `UX-DR106 entering and leaving stale mode are announced politely, once each`() {
        show(LotFixtures.ready())
        val region = { liveRegions().single() }
        assertEquals(LiveRegionMode.Polite, region().config[SemanticsProperties.LiveRegion])
        assertEquals("", region().description)

        compose.runOnIdle { events.tryEmit(LotsEvent.EnteredStale("site-home", LotFixtures.fetchedAt.toEpochMilli())) }
        compose.waitForIdle()
        assertEquals("Can't reach your Server. Showing data from 4:52 AM.", region().description)

        compose.runOnIdle { events.tryEmit(LotsEvent.LeftStale("site-home")) }
        compose.waitForIdle()
        assertEquals("Live again.", region().description)
    }

    @Test
    fun `UX-DR106 an event of another Site is not announced`() {
        show(LotFixtures.ready())

        compose.runOnIdle { events.tryEmit(LotsEvent.LeftStale("site-allotment")) }
        compose.waitForIdle()

        assertEquals("", liveRegions().single().description)
    }

    // Refresh

    private fun pullDown() {
        compose.onNode(hasScrollAction()).performTouchInput { swipeDown(startY = top + 10f, endY = bottom - 10f) }
        compose.waitForIdle()
    }

    @Test
    fun `UX-DR112 pulling the overview down refreshes the Lots`() {
        show(LotFixtures.ready())

        pullDown()

        assertEquals(listOf("refresh"), calls)
    }

    @Test
    fun `UX-DR112 pulling refreshes in stale mode and after a failed load too`() {
        show(LotFixtures.ready(staleReason = StaleReason.Unreachable))
        pullDown()
        assertEquals(listOf("refresh"), calls)

        lots = LotsState.Failed(homeSite(), SitesNotice.Unreachable)
        compose.waitForIdle()
        pullDown()
        assertEquals(listOf("refresh", "refresh"), calls)
    }

    @Test
    fun `UX-DR112 the app coming back to the foreground reloads`() {
        show(LotFixtures.ready())
        val atStart = foregrounds
        assertEquals(1, atStart)

        compose.activityRule.scenario.moveToState(Lifecycle.State.CREATED)
        compose.activityRule.scenario.moveToState(Lifecycle.State.RESUMED)
        compose.waitForIdle()

        assertEquals(atStart + 1, foregrounds)
    }

    @Test
    fun `UX-DR112 MainActivity resumes the session and refreshes the Lots on every start`() {
        val source =
            Repo.file("apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/MainActivity.kt").readText()
        assertTrue(
            Regex("""onForeground\s*=\s*\{[^}]*engine\.resume\(\)[^}]*lots\.refresh\(\)""").containsMatchIn(source),
        )
        assertTrue(source.contains("lotsEvents = lots.events"))
    }
}
