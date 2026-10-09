package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.api.AlertDto
import com.escendit.coldframe.core.api.AlertListDto
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.CompletableDeferred
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

/** An in-memory Server for the Alerts list, per Site, paged [pageSize] at a time with the index as cursor. */
class FakeAlertsApi : AlertsApi {
    val alerts = mutableMapOf<String, List<AlertDto>>()
    val calls = mutableListOf<String>()

    /** When set, the pages by cursor (`-` for the first), each naming the next cursor; [alerts] is not read. */
    var scripted: Map<String, Pair<List<AlertDto>, String?>>? = null
    var failure: ApiFailure? = null
    var gate: CompletableDeferred<Unit>? = null
    var pageSize = 50

    /** When set, every page says more follow: a Server that never ends. */
    var endless = false

    override suspend fun listAlerts(
        siteId: String,
        cursor: String?,
    ): ApiResult<AlertListDto> {
        calls += "list $siteId ${cursor ?: "-"}"
        scripted?.let { pages ->
            val (page, next) = pages.getValue(cursor ?: "-")
            return ApiResult.Ok(AlertListDto(page, page.count { it.closedAt == null }, next))
        }
        val all = alerts[siteId].orEmpty()
        val from = cursor?.toInt() ?: 0
        val page = all.drop(from).take(pageSize)
        val next = (from + pageSize).takeIf { endless || it < all.size }?.toString()
        val answer =
            failure?.let { ApiResult.Failed(it) }
                ?: ApiResult.Ok(AlertListDto(page, all.count { it.closedAt == null }, next))
        gate?.await()
        return answer
    }
}

class AlertsEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeAlertsApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)

    private val opened = "2026-10-09T05:45:00.000Z"
    private val openedMs = 1_791_524_700_000L
    private val closed = "2026-10-09T06:40:00.000Z"
    private val closedMs = 1_791_528_000_000L

    private fun alert(
        id: String,
        kind: String = "threshold",
        side: String? = "low",
        quantity: String = "soil_moisture",
        lotName: String = "Tomatoes",
        closedAt: String? = null,
    ) = AlertDto(
        id = id,
        kind = kind,
        quantity = quantity,
        lotId = "lot-$id",
        lotName = lotName,
        deviceId = "7c19000000000001",
        openedAt = opened,
        side = side,
        closedAt = closedAt,
        reason = closedAt?.let { "recovered" },
    )

    private fun TestScope.engines(vararg sites: SiteDto): Pair<SitesEngine, AlertsEngine> {
        sitesApi.sites += sites
        val sitesEngine =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(MapSettings()),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "site-key" },
                zones = { emptyList() },
            )
        val alerts = AlertsEngine(api, sitesEngine, backgroundScope)
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return sitesEngine to alerts
    }

    private fun TestScope.alertsFor(): AlertsEngine = engines(SiteDto("a", "Home", "Member")).second

    private fun AlertsEngine.ready(): AlertsState.Ready = assertIs<AlertsState.Ready>(state.value)

    @Test
    fun uxDr98TheAlertsLoadWhenASiteBecomesCurrentWithoutOpeningTheSurface() =
        runTest {
            api.alerts["a"] = listOf(alert("1"), alert("2", closedAt = closed))

            val alerts = alertsFor()

            assertEquals(listOf("list a -"), api.calls)
            assertEquals(1, alerts.ready().openCount)
            assertEquals(1, alerts.state.value.openCount)
            assertEquals(
                AlertSummary(
                    id = "1",
                    kind = "threshold",
                    side = AlertSide.Low,
                    quantity = SensorQuantity.SoilMoisture,
                    lotId = "lot-1",
                    lotName = "Tomatoes",
                    deviceId = "7c19000000000001",
                    openedAtEpochMs = openedMs,
                    closedAtEpochMs = null,
                ),
                alerts.ready().alerts.first(),
            )
            assertEquals(
                closedMs,
                alerts
                    .ready()
                    .alerts
                    .last()
                    .closedAtEpochMs,
            )
        }

    @Test
    fun uxDr25OpenAlertsGroupAsThresholdThenHealthThenClosedInTheServersOrder() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("h1", kind = "silent", side = null),
                    alert("t1"),
                    alert("h2", kind = "battery", side = null),
                    alert("t2", side = "high"),
                    alert("c1", closedAt = closed),
                    alert("c2", kind = "silent", side = null, closedAt = closed),
                )

            val ready = alertsFor().ready()

            assertEquals(listOf("t1", "t2"), ready.threshold.map { it.id })
            assertEquals(listOf("h1", "h2"), ready.health.map { it.id })
            assertEquals(listOf("c1", "c2"), ready.closed.map { it.id })
            assertEquals(4, ready.openCount)
        }

    @Test
    fun uxDr26OnlyAnOpenLowSideSoilMoistureThresholdAlertIsNeedsWater() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("water"),
                    alert("wet", side = "high"),
                    alert("cold", quantity = "air_temperature"),
                    alert("humid", side = "high", quantity = "relative_humidity"),
                    alert("was", closedAt = closed),
                )

            val rows = alertsFor().ready().alerts.associateBy { it.id }

            assertEquals(AlertVariant.NeedsWater, rows.getValue("water").variant)
            assertEquals(AlertCondition.NeedsWater, rows.getValue("water").condition)
            assertEquals(AlertIcon.RainDrop, rows.getValue("water").icon)
            assertEquals(AlertEyebrow.NeedsWater, rows.getValue("water").eyebrow)

            assertEquals(AlertVariant.Threshold, rows.getValue("wet").variant)
            assertEquals(AlertCondition.TooWet, rows.getValue("wet").condition)
            assertEquals(AlertIcon.ArrowUp, rows.getValue("wet").icon)
            assertEquals(AlertEyebrow.AboveHigh, rows.getValue("wet").eyebrow)

            assertEquals(AlertVariant.Threshold, rows.getValue("cold").variant)
            assertEquals(AlertCondition.TooLow, rows.getValue("cold").condition)
            assertEquals(AlertIcon.ArrowDown, rows.getValue("cold").icon)
            assertEquals(AlertEyebrow.BelowLow, rows.getValue("cold").eyebrow)

            assertEquals(AlertCondition.TooHigh, rows.getValue("humid").condition)

            // Closed is never orange, and still says what it was.
            assertEquals(AlertVariant.Closed, rows.getValue("was").variant)
            assertEquals(AlertCondition.NeedsWater, rows.getValue("was").condition)
            assertEquals(AlertGroup.Closed, rows.getValue("was").group)
        }

    @Test
    fun uxDr26HealthAlertsHaveTheirIconAndAnUnknownKindIsAHealthAlertWithHelp() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("s", kind = "silent", side = null),
                    alert("b", kind = "battery", side = null),
                    alert("u", kind = "uncalibrated", side = null),
                    alert("x", kind = "flooded", side = null),
                    alert("q", quantity = "wind_speed"),
                    alert("n", side = null),
                )

            val rows = alertsFor().ready().alerts.associateBy { it.id }

            assertEquals(
                listOf(
                    AlertIcon.Help,
                    AlertIcon.BatteryLow,
                    AlertIcon.Tools,
                    AlertIcon.Help,
                    AlertIcon.Help,
                    AlertIcon.Help,
                ),
                listOf("s", "b", "u", "x", "q", "n").map { rows.getValue(it).icon },
            )
            assertEquals(
                listOf(
                    AlertCondition.Silent,
                    AlertCondition.Battery,
                    AlertCondition.Uncalibrated,
                    AlertCondition.Unknown,
                    AlertCondition.Unknown,
                    AlertCondition.Unknown,
                ),
                listOf("s", "b", "u", "x", "q", "n").map { rows.getValue(it).condition },
            )
            for (row in rows.values) {
                assertEquals(AlertVariant.Health, row.variant, row.id)
                assertEquals(AlertGroup.Health, row.group, row.id)
                assertEquals(AlertEyebrow.Health, row.eyebrow, row.id)
            }
        }

    @Test
    fun uxDr25ThresholdAndUncalibratedRowsOpenTheLotSilentAndBatteryRowsOpenDevices() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("t"),
                    alert("u", kind = "uncalibrated", side = null),
                    alert("s", kind = "silent", side = null),
                    alert("b", kind = "battery", side = null),
                    alert("x", kind = "flooded", side = null),
                )

            val rows = alertsFor().ready().alerts.associateBy { it.id }

            assertEquals(AlertTarget.Lot, rows.getValue("t").target)
            assertEquals(AlertTarget.Lot, rows.getValue("u").target)
            assertEquals(AlertTarget.Devices, rows.getValue("s").target)
            assertEquals(AlertTarget.Devices, rows.getValue("b").target)
            assertEquals(AlertTarget.Devices, rows.getValue("x").target)
        }

    @Test
    fun everyPageIsFollowedToTheEndAndNoAlertIsListedTwice() =
        runTest {
            api.pageSize = 2
            api.alerts["a"] =
                listOf(alert("1"), alert("2"), alert("3"), alert("4", closedAt = closed), alert("5", closedAt = closed))

            val ready = alertsFor().ready()

            assertEquals(listOf("list a -", "list a 2", "list a 4"), api.calls)
            assertEquals(listOf("1", "2", "3", "4", "5"), ready.alerts.map { it.id })
            assertEquals(3, ready.openCount)
        }

    @Test
    fun anAlertServedOnTwoPagesIsListedOnceWhereItFirstCame() =
        runTest {
            api.scripted =
                mapOf(
                    "-" to (listOf(alert("1"), alert("2")) to "p2"),
                    "p2" to (listOf(alert("2"), alert("3")) to null),
                )

            val ready = alertsFor().ready()

            assertEquals(listOf("list a -", "list a p2"), api.calls)
            assertEquals(listOf("1", "2", "3"), ready.alerts.map { it.id })
        }

    @Test
    fun pagingStopsAtTheFixedPageCap() =
        runTest {
            api.pageSize = 1
            api.endless = true
            api.alerts["a"] = listOf(alert("1"))

            val ready = alertsFor().ready()

            assertEquals(AlertsEngine.MAX_PAGES, api.calls.size)
            assertEquals(listOf("1"), ready.alerts.map { it.id })
        }

    @Test
    fun uxDr82ASiteWithoutAlertsIsReadyAndEmpty() =
        runTest {
            val ready = alertsFor().ready()

            assertTrue(ready.alerts.isEmpty())
            assertEquals(0, ready.openCount)
        }

    @Test
    fun uxDr64AFailedLoadShowsANoticeWithTryAgainNoRowsAndNoCount() =
        runTest {
            api.failure = ApiFailure.Unreachable

            val alerts = alertsFor()

            val failed = assertIs<AlertsState.Failed>(alerts.state.value)
            assertEquals(AlertsNotice.Unreachable, failed.notice)
            assertTrue(failed.notice.tryAgain)
            assertEquals(0, alerts.state.value.openCount)

            api.failure = null
            api.alerts["a"] = listOf(alert("1"))
            alerts.load()
            assertIs<AlertsState.Loading>(alerts.state.value)
            runCurrent()

            assertEquals(1, alerts.ready().openCount)
        }

    @Test
    fun aFailureOnALaterPageShowsTheNoticeAndNoRows() =
        runTest {
            api.pageSize = 1
            api.alerts["a"] = listOf(alert("1"), alert("2"))
            val alerts = alertsFor()
            assertEquals(2, alerts.ready().alerts.size)

            api.gate = CompletableDeferred()
            alerts.refresh()
            runCurrent()
            api.failure = ApiFailure.Unreachable
            api.gate!!.complete(Unit)
            runCurrent()

            assertIs<AlertsState.Failed>(alerts.state.value)
        }

    @Test
    fun aCertificateFailureHasNoTryAgain() =
        runTest {
            api.failure = ApiFailure.Certificate

            val failed = assertIs<AlertsState.Failed>(alertsFor().state.value)

            assertEquals(AlertsNotice.Certificate, failed.notice)
            assertFalse(failed.notice.tryAgain)
        }

    @Test
    fun uxDr64A401LeavesTheSurfaceIdle() =
        runTest {
            api.failure = ApiFailure.Unauthorized

            assertEquals(AlertsState.Idle, alertsFor().state.value)
        }

    @Test
    fun uxDr98ARefreshKeepsTheRowsAndTheCountWhileItRuns() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            val alerts = alertsFor()
            api.gate = CompletableDeferred()
            api.alerts["a"] = listOf(alert("1"), alert("2"))

            alerts.refresh()
            runCurrent()

            assertTrue(alerts.ready().refreshing)
            assertEquals(1, alerts.ready().openCount)

            api.gate!!.complete(Unit)
            runCurrent()

            assertFalse(alerts.ready().refreshing)
            assertEquals(2, alerts.ready().openCount)
        }

    @Test
    fun aRefreshThatFailsDropsTheRowsThereIsNoStaleMode() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            val alerts = alertsFor()
            api.failure = ApiFailure.Unreachable

            alerts.refresh()
            runCurrent()

            assertIs<AlertsState.Failed>(alerts.state.value)
            assertEquals(0, alerts.state.value.openCount)
        }

    @Test
    fun onlyTheNewestReadIsApplied() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            val alerts = alertsFor()
            val first = CompletableDeferred<Unit>()
            api.gate = first
            alerts.refresh()
            runCurrent()
            api.gate = null
            api.alerts["a"] = listOf(alert("1"), alert("2"))
            alerts.refresh()
            runCurrent()
            assertEquals(2, alerts.ready().openCount)

            first.complete(Unit)
            runCurrent()

            assertEquals(2, alerts.ready().openCount)
            assertFalse(alerts.ready().refreshing)
        }

    @Test
    fun anotherCurrentSiteLoadsItsOwnAlertsAndDropsTheOthers() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            api.alerts["b"] = listOf(alert("2"), alert("3"))
            val (sites, alerts) = engines(SiteDto("a", "Home", "Owner"), SiteDto("b", "Allotment", "Member"))
            assertEquals(1, alerts.ready().openCount)

            sites.select("b")
            runCurrent()

            assertEquals("b", alerts.ready().site.id)
            assertEquals(listOf("2", "3"), alerts.ready().alerts.map { it.id })
        }

    @Test
    fun signingOutLeavesTheSurfaceIdle() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            val alerts = alertsFor()

            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            assertEquals(AlertsState.Idle, alerts.state.value)
            assertNull(alerts.state.value.site)
        }

    @Test
    fun anOpenedTimeThatIsNotAnInstantDropsOnlyThatAlert() =
        runTest {
            api.alerts["a"] = listOf(alert("1").copy(openedAt = "yesterday"), alert("2"))

            assertEquals(
                listOf("2"),
                alertsFor().ready().alerts.map { it.id },
            )
        }

    @Test
    fun aClosedTimeThatIsNotAnInstantDropsThatAlertInsteadOfShowingItOpen() =
        runTest {
            api.alerts["a"] = listOf(alert("1", closedAt = "later"), alert("2"), alert("3", closedAt = closed))

            val ready = alertsFor().ready()

            assertEquals(listOf("2", "3"), ready.alerts.map { it.id })
            assertEquals(listOf("2"), ready.threshold.map { it.id })
        }

    @Test
    fun aRenameOfTheCurrentSiteKeepsTheAlertsWithoutReadingTheListAgain() =
        runTest {
            api.alerts["a"] = listOf(alert("1"), alert("2", closedAt = closed))
            val (sites, alerts) = engines(SiteDto("a", "Home", "Owner"))
            val before = alerts.ready().alerts

            sites.renameSite("Home garden")
            runCurrent()

            assertEquals("Home garden", alerts.ready().site.name)
            assertEquals(before, alerts.ready().alerts)
            assertEquals(1, alerts.ready().openCount)
            assertEquals(listOf("list a -"), api.calls)
        }

    @Test
    fun theSnapshotCarriesTheAlertsAsParallelListsForSwift() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("t"),
                    alert("s", kind = "silent", side = null, lotName = "Beans"),
                    alert("c", side = "high", quantity = "air_temperature", closedAt = closed),
                )

            val snapshot = snapshotOf(alertsFor().state.value)

            assertEquals(
                AlertsSnapshot(
                    surface = "ready",
                    notice = null,
                    noticeTryAgain = false,
                    siteId = "a",
                    siteName = "Home",
                    openCount = 2,
                    refreshing = false,
                    alertIds = listOf("t", "s", "c"),
                    alertGroups = listOf("threshold", "health", "closed"),
                    alertVariants = listOf("needsWater", "health", "closed"),
                    alertConditions = listOf("needsWater", "silent", "tooHigh"),
                    alertEyebrows = listOf("needsWater", "health", "aboveHigh"),
                    alertIcons = listOf("rain-drop", "help", "arrow--up"),
                    alertQuantities = listOf("soil_moisture", "soil_moisture", "air_temperature"),
                    alertLotIds = listOf("lot-t", "lot-s", "lot-c"),
                    alertLotNames = listOf("Tomatoes", "Beans", "Tomatoes"),
                    alertDeviceIds = List(3) { "7c19000000000001" },
                    alertOpenedAt = List(3) { openedMs.toString() },
                    alertClosedAt = listOf("", "", closedMs.toString()),
                    alertTargets = listOf("lot", "devices", "lot"),
                ),
                snapshot,
            )
        }

    @Test
    fun theSnapshotOfAFailedLoadHasTheNoticeNoRowsAndNoCount() =
        runTest {
            api.failure = ApiFailure.Unreachable

            val snapshot = snapshotOf(alertsFor().state.value)

            assertEquals("failed", snapshot.surface)
            assertEquals("unreachable", snapshot.notice)
            assertTrue(snapshot.noticeTryAgain)
            assertEquals(0, snapshot.openCount)
            assertTrue(snapshot.alertIds.isEmpty())
            assertEquals("idle", snapshotOf(AlertsState.Idle).surface)
        }
}
