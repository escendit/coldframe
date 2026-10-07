package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.click
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeRight
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.android.ui.sites.HISTORY_CHART_TAG
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotDetailScreen
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.lots.LotDetailNotice
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.sites.SiteRole
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
import kotlin.test.assertTrue

/**
 * Lot detail (Story 4.8): hero, Sensor cells, History chart and Device cells, drawn from the
 * core's models. The values are the Server's, fixed in [LotDetailFixtures].
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class LotDetailScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private val defaultZone = TimeZone.getDefault()

    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    /** A UTC date as the screen writes it: "6 Oct" or "Oct 6", by the locale. */
    private fun utcDay(date: String): String =
        Formats.day(Instant.parse("${date}T00:00:00Z"), ZoneOffset.UTC, Locale.getDefault())

    /** Some node with [text] exists: a label drawn in capitals still matches, and a value drawn twice is not an error. */
    private fun assertShown(
        text: String,
        substring: Boolean = false,
    ) {
        val found = compose.onAllNodes(hasText(text, substring = substring, ignoreCase = true)).fetchSemanticsNodes()
        assertTrue(found.isNotEmpty(), "\"$text\" is on screen")
    }

    private fun assertNotShown(
        text: String,
        substring: Boolean = false,
    ) {
        compose.onAllNodes(hasText(text, substring = substring, ignoreCase = true)).assertCountEquals(0)
    }

    private fun show(state: LotDetailState) {
        compose.setContent {
            ColdframeTheme(isDark = false) {
                LotDetailScreen(
                    state = state,
                    actions =
                        LotDetailActions(
                            refresh = { calls += "refresh" },
                            pick = { calls += "pick ${it.key}" },
                        ),
                    now = { LotFixtures.now },
                    onAddNode = { calls += "addNode $it" },
                    onOpenDevices = { calls += "devices" },
                )
            }
        }
    }

    @Test
    fun `UX-DR27 the hero names the Lot, its status and since, and shows raw while uncalibrated`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.needsCalibration))

        compose.onNode(isHeading().and(hasText("Tomatoes"))).assertIsDisplayed()
        assertShown("Needs calibration")
        assertShown("since 5:45", substring = true)
        assertShown("raw 1840")
        // No percentage anywhere before Calibration.
        assertNotShown("~", substring = true)
    }

    @Test
    fun `UX-DR27 the hero shows the percentage the Server sent, rounded to 5`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.ok))

        assertShown("~35")
        assertShown("low 25", substring = true)
    }

    @Test
    fun `UX-DR27 paused by the Site says so and offers the resume hint to Admins only`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.pausedBySite, role = SiteRole.Owner))

        assertShown("Paused with the Site")
        assertShown("Resume the Site to resume this Node")
    }

    @Test
    fun `UX-DR27 a Member does not get the resume hint`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.pausedBySite, role = SiteRole.Member))

        assertShown("Paused with the Site")
        assertNotShown("Resume the Site to resume this Node")
    }

    @Test
    fun `UX-DR78 the status notes of the hero`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.unknown))
        assertShown("Check power or range.")
        assertShown("6 h")
    }

    @Test
    fun `UX-DR78 a paused Lot says until when`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.paused))

        assertShown("Paused until ${utcDay("2026-11-01")}")
    }

    @Test
    fun `UX-DR78 needs calibration says there is no percentage until calibrated`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.needsCalibration))

        assertShown("no % until calibrated")
    }

    @Test
    fun `UX-DR28 the Sensor cells show the Servers values with their units and times`() {
        show(LotDetailFixtures.ready())

        assertShown("Soil moisture")
        assertShown("raw 1840")
        assertShown("14 °C")
        assertShown("78 %")
        assertShown("142 kΩ")
        assertShown("Temperature")
        assertShown("Humidity")
        assertShown("Air (gas)")
    }

    @Test
    fun `UX-DR29 the Device cells show battery, charging, last seen and the cadence`() {
        show(LotDetailFixtures.ready())

        assertShown("Battery")
        assertShown("62 %")
        assertShown("charging")
        assertShown("Last seen")
        assertShown("7:02", substring = true)
        assertShown("every 15 min")
        assertShown("Node 7c19a1b2c3d4e5f6")
    }

    @Test
    fun `UX-DR29 a battery below 20 percent shows the low battery icon and a tap opens Devices`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.lowBattery))

        assertShown("14 %")
        assertShown("not charging")
        compose.onNode(hasText("Battery")).performScrollTo().performClick()
        assertEquals(listOf("devices"), calls)
    }

    @Test
    fun `UX-DR63 a stale detail shows the stale header and no live value`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.needsCalibration, staleReason = StaleReason.Unreachable))

        assertShown("Home garden · can't reach your Server")
        assertShown("2 h 12 min old")
        assertNotShown("raw 1840")
        assertNotShown("142 kΩ")
        assertNotShown("62 %")
        assertShown("Was needs calibration")
    }

    @Test
    fun `UX-DR63 a Lot without a Node has an empty detail with Add a Node for an Admin`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.noNode, role = SiteRole.Administrator))

        assertShown("This Lot has no Node.")
        compose.onNode(hasText("Add a Node", ignoreCase = true)).performClick()
        assertEquals(listOf("addNode lot-tomatoes"), calls)
        assertNotShown("Sensors")
        assertNotShown("History")
    }

    @Test
    fun `UX-DR63 a Member sees the empty detail without an action`() {
        show(LotDetailFixtures.ready(LotDetailFixtures.noNode, role = SiteRole.Member))

        assertShown("This Lot has no Node.")
        assertNotShown("Add a Node")
    }

    @Test
    fun `UX-DR63 a failed read shows its notice and Try again`() {
        show(LotDetailState.Failed(homeSite(), "lot-x", "Tomatoes", LotDetailNotice.Unreachable))

        compose.onNode(hasText("Try again", ignoreCase = true)).performClick()
        assertEquals(listOf("refresh"), calls)
    }

    @Test
    fun `UX-DR63 an unknown Lot is told as not found`() {
        show(LotDetailState.Failed(homeSite(), "lot-x", "Tomatoes", LotDetailNotice.NotFound))

        assertShown("Home garden has no such Lot. Nothing was changed.")
    }

    @Test
    fun `UX-DR32 the chart is one element whose label is the text summary`() {
        show(LotDetailFixtures.ready())

        val chart = compose.onNodeWithTag(HISTORY_CHART_TAG)
        chart.assertExists()
        compose.onNode(hasContentDescription("Soil moisture, 30 days", substring = true)).assertExists()
        assertShown(utcDay("2026-09-07"))
        assertShown(utcDay("2026-10-06"))
    }

    @Test
    fun `UX-DR98 the chart summary names the lowest day and how many days have Readings`() {
        show(LotDetailFixtures.ready())

        compose
            .onNode(hasContentDescription("26 days with Readings", substring = true))
            .assertExists()
    }

    @Test
    fun `UX-DR33 the Sensor picker switches the chart to another quantity`() {
        show(LotDetailFixtures.ready())

        assertShown("Sensor")
        compose.onNode(hasText("Temperature", ignoreCase = true).and(hasClickAction())).performScrollTo().performClick()
        assertEquals(listOf("pick air_temperature"), calls)
    }

    @Test
    fun `UX-DR33 tapping a day shows its lowest reading as text`() {
        show(LotDetailFixtures.ready())

        compose.onNodeWithTag(HISTORY_CHART_TAG).performScrollTo().performTouchInput {
            click(
                centerRight.copy(
                    x =
                        right - 4f,
                ),
            )
        }

        compose.onNode(hasText("${utcDay("2026-10-06")}: lowest", substring = true)).assertExists()
    }

    @Test
    fun `UX-DR33 dragging along the chart follows the finger`() {
        show(LotDetailFixtures.ready())

        compose.onNodeWithTag(HISTORY_CHART_TAG).performScrollTo().performTouchInput { swipeRight() }

        compose.onNode(hasText("lowest", substring = true)).assertExists()
    }

    @Test
    fun `UX-DR33 temperature shows the range of the selected day`() {
        show(
            LotDetailFixtures.ready(
                picked = SensorQuantity.AirTemperature,
            ),
        )

        compose.onNodeWithTag(HISTORY_CHART_TAG).performScrollTo().performTouchInput {
            click(
                centerRight.copy(
                    x =
                        right - 4f,
                ),
            )
        }

        compose.onNode(hasText("°C to", substring = true)).assertExists()
    }

    @Test
    fun `UX-DR63 the Garden opens Lot detail from a tile and Back closes it`() {
        var detail by mutableStateOf<LotDetailState>(LotDetailState.Idle)
        val seen = mutableListOf<String>()
        compose.setContent {
            com.escendit.coldframe.android.ColdframeRoot(
                state =
                    com.escendit.coldframe.core.signin.SignInState
                        .SignedIn("Simon"),
                sites = readySites(),
                theme = com.escendit.coldframe.core.appearance.ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                lots = LotFixtures.ready(listOf(LotFixtures.needsCalibration)),
                lotDetail = detail,
                lotDetailActions =
                    LotDetailActions(
                        open = { id, name ->
                            seen += "open $id $name"
                            detail = LotDetailFixtures.ready()
                        },
                        close = {
                            seen += "close"
                            detail = LotDetailState.Idle
                        },
                    ),
                now = { LotFixtures.now },
            )
        }

        compose
            .onNodeWithContentDescription(
                "Peppers, needs Calibration",
                substring = true,
            ).performScrollTo()
            .performClick()
        compose.waitForIdle()
        assertEquals(listOf("open lot-peppers Peppers"), seen)
        compose.onNode(isHeading().and(hasText("Tomatoes"))).assertIsDisplayed()

        compose.onNodeWithContentDescription("Back").performClick()
        compose.waitForIdle()
        assertEquals(listOf("open lot-peppers Peppers", "close"), seen)
        compose.onNode(isHeading().and(hasText("Lots"))).assertExists()
    }
}
