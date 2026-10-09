package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeDown
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.alerts.AlertRowBorder
import com.escendit.coldframe.android.ui.alerts.AlertRowFill
import com.escendit.coldframe.android.ui.alerts.AlertsActions
import com.escendit.coldframe.android.ui.alerts.AlertsCopy
import com.escendit.coldframe.android.ui.alerts.alertPaint
import com.escendit.coldframe.android.ui.alerts.carbonIcon
import com.escendit.coldframe.android.ui.alerts.rowShape
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.theme.ColdframeColors
import com.escendit.coldframe.core.alerts.AlertVariant
import com.escendit.coldframe.core.alerts.AlertsNotice
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.designtokens.CarbonIcon
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.ZoneOffset
import java.util.Locale
import java.util.TimeZone
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/** The Alerts tab (UX-DR25, UX-DR26, UX-DR64), Story 6.2: the Alerts of the current Site. */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AlertsScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var alerts by mutableStateOf<AlertsState>(AlertsState.Idle)

    private val defaultZone = TimeZone.getDefault()

    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private val copy =
        AlertsCopy(
            ApplicationProvider.getApplicationContext<android.content.Context>().resources,
            AlertFixtures.now,
            ZoneOffset.UTC,
            Locale.UK,
        )

    /** Signed in on "Home garden"; the Alerts tab is opened unless [open] is false. */
    private fun show(
        state: AlertsState,
        open: Boolean = true,
    ) {
        alerts = state
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                alerts = alerts,
                alertsActions = AlertsActions(load = { calls += "load" }, refresh = { calls += "refresh" }),
                lotDetailActions = LotDetailActions(open = { id, name -> calls += "openLot $id $name" }),
                now = { AlertFixtures.now },
            )
        }
        if (open) {
            alertsTab().performClick()
            compose.onNode(isHeading().and(hasText("Alerts"))).assertExists()
        }
    }

    private fun alertsTab() =
        compose.onNode(
            SemanticsMatcher
                .expectValue(SemanticsProperties.Role, Role.Tab)
                .and(SemanticsMatcher("names Alerts") { it.spokenLabel().contains("Alerts") }),
        )

    private fun selectedTab(): String =
        compose
            .allNodes()
            .single { it.role == Role.Tab && it.config.getOrNull(SemanticsProperties.Selected) == true }
            .spokenLabel()

    /** The spoken labels of the Alert rows, top to bottom. */
    private fun rows(): List<String> =
        compose
            .allNodes()
            .filter { it.role == Role.Button && it.isClickable }
            .sortedBy { it.positionInRoot.y }
            .mapNotNull { it.config.getOrNull(SemanticsProperties.ContentDescription)?.singleOrNull() }
            .map { it.plain() }

    /** Clock times carry a narrow no-break space in en-US; the tests compare with a plain space. */
    private fun String.plain(): String = replace('\u202F', ' ')

    /** The Alert row TalkBack reads as [label]. */
    private fun row(label: String) =
        compose.onNode(
            SemanticsMatcher("is the row \"$label\"") { node ->
                node.config
                    .getOrNull(SemanticsProperties.ContentDescription)
                    ?.singleOrNull()
                    ?.plain() == label
            },
        )

    private fun top(text: String): Float =
        compose
            .onNode(isHeading().and(hasText(text)))
            .fetchSemanticsNode()
            .positionInRoot.y

    @Test
    fun `UX-DR64 UX-DR26 open Alerts list as Threshold then Health newest first, then Closed`() {
        show(AlertFixtures.ready())

        assertTrue(top("Threshold Alerts") < top("Health Alerts"))
        assertTrue(top("Health Alerts") < top("Closed"))
        assertEquals(
            listOf(
                "Tomatoes needs water, since 5:45 AM",
                "Herbs too wet, since 4:10 AM",
                "Peppers temperature too low, since Thu",
                "Basil humidity too high, since Oct 1",
                "Node on Lot 'Beans' silent, since 1:05 AM",
                "Node battery low on Lot 'Salad', since Thu",
                "Soil Sensor on Lot 'Carrots' needs Calibration, since Wed",
                "Health Alert on Lot 'Leeks', since Tue",
                "Tomatoes needs water, since 5:45 AM, closed 6:40 AM",
                "Herbs too wet, since Wed, closed Wed",
            ),
            rows(),
        )
    }

    @Test
    fun `UX-DR98 pulling the Alerts down refreshes them`() {
        show(AlertFixtures.ready())
        calls.clear()

        compose.onNode(hasScrollAction()).performTouchInput { swipeDown(startY = top + 10f, endY = bottom - 10f) }
        compose.waitForIdle()

        assertEquals(listOf("refresh"), calls)
    }

    @Test
    fun `UX-DR98 an Alert row is one tap target whose label states the condition and when it started`() {
        show(AlertFixtures.ready(listOf(AlertFixtures.needsWater, AlertFixtures.closedWater)))

        row("Tomatoes needs water, since 5:45 AM").assertExists()
        row("Tomatoes needs water, since 5:45 AM, closed 6:40 AM").assertExists()
        // The row's own texts are not separate stops, and the row offers nothing but its tap.
        compose.onAllNodesWithText("Tomatoes needs water").assertCountEquals(0)
        assertEquals(2, compose.allNodes().count { it.role == Role.Button && it.isClickable })
    }

    @Test
    fun `UX-DR25 the eyebrow and title of every variant come from the catalogue in sentence case`() {
        assertEquals("Needs water · 05:45", copy.eyebrow(AlertFixtures.needsWater))
        assertEquals("Tomatoes needs water", copy.title(AlertFixtures.needsWater))
        assertEquals("Threshold Alert · above high · 04:10", copy.eyebrow(AlertFixtures.tooWet))
        assertEquals("Herbs too wet", copy.title(AlertFixtures.tooWet))
        assertEquals("Threshold Alert · below low · Thu", copy.eyebrow(AlertFixtures.tooCold))
        assertEquals("Peppers temperature too low", copy.title(AlertFixtures.tooCold))
        assertEquals("Basil humidity too high", copy.title(AlertFixtures.tooHumid))
        assertEquals("Health Alert", copy.eyebrow(AlertFixtures.silent))
        assertEquals("Node on Lot 'Beans' silent", copy.title(AlertFixtures.silent))
        assertEquals("Node battery low on Lot 'Salad'", copy.title(AlertFixtures.battery))
        assertEquals("Soil Sensor on Lot 'Carrots' needs Calibration", copy.title(AlertFixtures.uncalibrated))
        assertEquals("Health Alert on Lot 'Leeks'", copy.title(AlertFixtures.unknownKind))
        assertEquals("Needs water · closed 06:40", copy.eyebrow(AlertFixtures.closedWater))
        assertEquals("Threshold Alert · above high · closed Wed", copy.eyebrow(AlertFixtures.closedWet))
    }

    @Test
    fun `UX-DR25 UX-DR99 every variant has its own fill and outline, and its icon by cause`() {
        assertEquals(
            AlertRowFill.Solid to AlertRowBorder.None,
            AlertVariant.NeedsWater.rowShape().let {
                it.fill to
                    it.border
            },
        )
        assertEquals(
            Triple(AlertRowFill.Flat, AlertRowBorder.Solid, 2),
            AlertVariant.Threshold.rowShape().let { Triple(it.fill, it.border, it.borderWidthDp) },
        )
        assertEquals(
            Triple(AlertRowFill.Hatch, AlertRowBorder.Dashed, 1),
            AlertVariant.Health.rowShape().let { Triple(it.fill, it.border, it.borderWidthDp) },
        )
        assertEquals(
            Triple(AlertRowFill.Empty, AlertRowBorder.Solid, 1),
            AlertVariant.Closed.rowShape().let { Triple(it.fill, it.border, it.borderWidthDp) },
        )
        assertEquals(
            4,
            AlertVariant.entries
                .map { it.rowShape() }
                .toSet()
                .size,
        )
        assertEquals(
            listOf(
                CarbonIcon.RAIN_DROP,
                CarbonIcon.ARROW_UP,
                CarbonIcon.ARROW_DOWN,
                CarbonIcon.HELP,
                CarbonIcon.BATTERY_LOW,
                CarbonIcon.TOOLS,
                CarbonIcon.HELP,
            ),
            listOf(
                AlertFixtures.needsWater,
                AlertFixtures.tooWet,
                AlertFixtures.tooCold,
                AlertFixtures.silent,
                AlertFixtures.battery,
                AlertFixtures.uncalibrated,
                AlertFixtures.unknownKind,
            ).map { it.icon.carbonIcon() },
        )
    }

    @Test
    fun `UX-DR14 solid orange is the needs-water row only, in both themes`() {
        for (isDark in listOf(false, true)) {
            val colors = ColdframeColors.of(isDark)
            assertEquals(colors.statusWaterFill, colors.alertPaint(AlertVariant.NeedsWater).ground)
            assertEquals(colors.statusWaterInk, colors.alertPaint(AlertVariant.NeedsWater).ink)
            for (variant in AlertVariant.entries - AlertVariant.NeedsWater) {
                val paint = colors.alertPaint(variant)
                for (colour in listOf(paint.ground, paint.ink, paint.border, paint.hatchLine)) {
                    assertNotEquals(colors.statusWaterFill, colour, "$variant, dark $isDark")
                    assertNotEquals(colors.primary, colour, "$variant, dark $isDark")
                }
            }
            assertEquals(colors.layer01, colors.alertPaint(AlertVariant.Threshold).ground)
            assertEquals(colors.borderStrong, colors.alertPaint(AlertVariant.Threshold).border)
            assertEquals(colors.statusHatchGround, colors.alertPaint(AlertVariant.Health).ground)
            assertEquals(colors.statusUnknownBorder, colors.alertPaint(AlertVariant.Health).border)
            assertEquals(colors.borderSubtle, colors.alertPaint(AlertVariant.Closed).border)
            assertEquals(colors.textSecondary, colors.alertPaint(AlertVariant.Closed).ink)
        }
        // Too wet is a Threshold row, never the orange one.
        assertEquals(AlertVariant.Threshold, AlertFixtures.tooWet.variant)
        assertEquals(AlertVariant.Closed, AlertFixtures.closedWater.variant)
    }

    @Test
    fun `UX-DR26 a Threshold row opens Lot detail of its Lot on the Garden tab`() {
        show(AlertFixtures.ready())

        row("Herbs too wet, since 4:10 AM").performClick()

        assertEquals("openLot lot-wet Herbs", calls.last())
        assertEquals("Garden", selectedTab())
    }

    @Test
    fun `UX-DR26 an uncalibrated row opens Lot detail, silent and battery rows open Devices`() {
        show(AlertFixtures.ready(AlertFixtures.health))

        row("Soil Sensor on Lot 'Carrots' needs Calibration, since Wed").performClick()
        assertEquals("openLot lot-uncal Carrots", calls.last())
        assertEquals("Garden", selectedTab())

        alertsTab().performClick()
        row("Node on Lot 'Beans' silent, since 1:05 AM").performClick()
        assertEquals("Devices", selectedTab())

        alertsTab().performClick()
        row("Node battery low on Lot 'Salad', since Thu").performClick()
        assertEquals("Devices", selectedTab())
        assertEquals(1, calls.count { it.startsWith("openLot") })
    }

    @Test
    fun `UX-DR82 without Alerts the surface says No open Alerts and never All good`() {
        show(AlertFixtures.ready(emptyList()))

        compose.onNodeWithText("No open Alerts.").assertExists()
        compose.onAllNodes(isHeading().and(hasText("Closed"))).assertCountEquals(0)
        compose.onAllNodes(isHeading().and(hasText("Threshold Alerts"))).assertCountEquals(0)
        assertFalse(compose.allNodes().any { it.spokenLabel().contains("all good", ignoreCase = true) })
        assertTrue(rows().isEmpty())
    }

    @Test
    fun `UX-DR64 UX-DR82 with only closed Alerts No open Alerts is followed by Closed and its rows`() {
        show(AlertFixtures.ready(AlertFixtures.closed))

        val empty =
            compose
                .onNodeWithText("No open Alerts.")
                .fetchSemanticsNode()
                .positionInRoot.y
        assertTrue(empty < top("Closed"))
        assertEquals(2, rows().size)
    }

    @Test
    fun `UX-DR64 a failed load shows the notice with Try again, no rows and no count on the tab`() {
        show(AlertsState.Failed(homeSite(), AlertsNotice.Unreachable))

        compose.onNodeWithText("Can't reach your Server.").assertExists()
        assertTrue(rows().isEmpty())
        alertsTab().assertIsSelected()
        assertEquals("Alerts", selectedTab())
        calls.clear()
        compose.onNodeWithText("TRY AGAIN").performClick()
        assertEquals(listOf("load"), calls)
    }

    @Test
    fun `UX-DR64 a certificate failure offers no Try again`() {
        show(AlertsState.Failed(homeSite(), AlertsNotice.Certificate))

        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR64 entering the Alerts tab reads the Alerts again`() {
        show(AlertFixtures.ready(), open = false)
        assertFalse("load" in calls)

        alertsTab().performClick()
        compose.waitForIdle()

        assertEquals(listOf("load"), calls)
    }

    @Test
    fun `UX-DR57 UX-DR98 the Alerts tab carries the open count on every tab and speaks it`() {
        show(AlertFixtures.ready(List(5) { AlertFixtures.needsWater.copy(id = "a$it") }), open = false)

        assertEquals("Garden", selectedTab())
        val tab = alertsTab().fetchSemanticsNode()
        assertTrue(tab.spokenLabel().contains("Alerts · 5"), tab.spokenLabel())
        assertTrue(tab.spokenLabel().contains("Alerts, 5 open"), tab.spokenLabel())

        compose.onNodeWithText("Devices").performClick()
        assertTrue(alertsTab().fetchSemanticsNode().spokenLabel().contains("Alerts · 5"))
    }

    @Test
    fun `UX-DR57 the Alerts tab has no count without open Alerts, while loading and when idle`() {
        show(AlertFixtures.ready(AlertFixtures.closed), open = false)
        assertEquals("Alerts", alertsTab().fetchSemanticsNode().spokenLabel())

        alerts = AlertsState.Loading(homeSite())
        compose.waitForIdle()
        assertEquals("Alerts", alertsTab().fetchSemanticsNode().spokenLabel())

        alerts = AlertsState.Idle
        compose.waitForIdle()
        assertEquals("Alerts", alertsTab().fetchSemanticsNode().spokenLabel())
    }

    @Test
    fun `UX-DR96 UX-DR100 at font scale 2 the Alerts rows clip nothing and are at least 48 dp tall`() {
        alerts = AlertFixtures.ready()
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = ThemePreference.Light,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    alerts = alerts,
                    now = { AlertFixtures.now },
                )
            }
        }
        alertsTab().performClick()

        compose.assertNothingOverflows("Alerts")
        val density = compose.density.density
        val rowNodes = compose.allNodes().filter { it.role == Role.Button && it.isClickable }
        assertEquals(10, rowNodes.size)
        for (row in rowNodes) assertTrue(row.size.height / density >= 48f, row.spokenLabel())
        row("Herbs too wet, since Wed, closed Wed").performScrollTo().assertExists()
    }
}
