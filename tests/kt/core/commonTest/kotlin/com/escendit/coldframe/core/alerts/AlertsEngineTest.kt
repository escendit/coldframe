package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.api.AlertDto
import com.escendit.coldframe.core.api.AlertListDto
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.HealthAlertDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.LotDurationUnit
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

/** 2026-10-09T07:10:00Z: the `now` every title and snapshot of these tests is told against. No test reads a wall clock. */
private const val NOW_MS = 1_791_529_800_000L

private fun snapshotOf(state: AlertsState): AlertsSnapshot = snapshotOf(state, NOW_MS)

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

    /** The Site's Health Alerts: sent with the first page only, as the contract says; no entry sends none at all. */
    val health = mutableMapOf<String, List<HealthAlertDto>>()

    override suspend fun listAlerts(
        siteId: String,
        cursor: String?,
    ): ApiResult<AlertListDto> {
        calls += "list $siteId ${cursor ?: "-"}"
        scripted?.let { pages ->
            val (page, next) = pages.getValue(cursor ?: "-")
            return ApiResult.Ok(
                AlertListDto(page, page.count { it.closedAt == null }, next, health[siteId].takeIf { cursor == null }),
            )
        }
        val all = alerts[siteId].orEmpty()
        val from = cursor?.toInt() ?: 0
        val page = all.drop(from).take(pageSize)
        val next = (from + pageSize).takeIf { endless || it < all.size }?.toString()
        val answer =
            failure?.let { ApiResult.Failed(it) }
                ?: ApiResult.Ok(
                    AlertListDto(
                        page,
                        all.count { it.closedAt == null } + health[siteId].orEmpty().count { it.closedAt == null },
                        next,
                        health[siteId].takeIf { cursor == null },
                    ),
                )
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
        val alerts = AlertsEngine(api, sitesEngine, backgroundScope, now = { NOW_MS })
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
                    alertTitles = listOf("needsWater", "nodeOnLotSilent", "tooHigh"),
                    alertDeviceKinds = listOf("", "", ""),
                    alertDeviceNames = List(3) { "7C19" },
                    alertSilentSince = listOf("", "", ""),
                    alertDurationValues = listOf("", "", ""),
                    alertDurationUnits = listOf("", "", ""),
                    alertBatteryPercents = listOf("", "", ""),
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

    // Story 7.2: Health Alerts come from `healthAlerts`.

    private val nodeId = "9c0d1e4b5a673f2a"
    private val hubId = "3f2a9c0d1e4b5a67"

    private fun health(
        id: String,
        deviceId: String = nodeId,
        deviceKind: String? = "node",
        kind: String = "silent",
        lotName: String? = null,
        silentSince: String? = null,
        openedAt: String = opened,
        closedAt: String? = null,
        batteryPercent: Int? = null,
    ) = HealthAlertDto(
        id = id,
        kind = kind,
        deviceId = deviceId,
        openedAt = openedAt,
        deviceKind = deviceKind,
        lotId = lotName?.let { "lot-$id" },
        lotName = lotName,
        silentSince = silentSince,
        closedAt = closedAt,
        reason = closedAt?.let { "recovered" },
        batteryPercent = batteryPercent,
    )

    /** A Node on Beans last heard at 01:05 and a Hub last heard at 07:05, both told at 07:10. */
    private val silentNode = health("n", lotName = "Beans", silentSince = "2026-10-09T01:05:00.000Z")
    private val silentHub = health("h", hubId, "hub", silentSince = "2026-10-09T07:05:00.000Z")

    private fun AlertSummary.titleNow(): AlertTitle = title(NOW_MS)

    @Test
    fun uxDr25HealthAlertsComeFromHealthAlertsAndStandInTheHealthGroupAfterTheThresholdAlerts() =
        runTest {
            api.alerts["a"] = listOf(alert("t"), alert("c", closedAt = closed))
            api.health["a"] = listOf(silentNode, silentHub)

            val ready = alertsFor().ready()

            assertEquals(listOf("t", "n", "h", "c"), ready.alerts.map { it.id })
            assertEquals(listOf("t"), ready.threshold.map { it.id })
            assertEquals(listOf("n", "h"), ready.health.map { it.id })
            assertEquals(listOf("c"), ready.closed.map { it.id })
            // The Server's count covers both kinds.
            assertEquals(3, ready.openCount)
            val (node, hub) = ready.health
            assertEquals(AlertDeviceKind.Node, node.deviceKind)
            assertEquals("lot-n", node.lotId)
            assertEquals("Beans", node.lotName)
            assertEquals(1_791_507_900_000, node.silentSinceEpochMs)
            // A Hub's Alert has neither a Lot nor a quantity.
            assertEquals(AlertDeviceKind.Hub, hub.deviceKind)
            assertNull(hub.lotId)
            assertNull(hub.lotName)
            assertNull(hub.quantity)
            for (alert in ready.health) {
                assertEquals(AlertCondition.Silent, alert.condition)
                assertEquals(AlertVariant.Health, alert.variant)
                assertEquals(AlertEyebrow.Health, alert.eyebrow)
                assertEquals(AlertIcon.Help, alert.icon)
                assertEquals(AlertTarget.Devices, alert.target)
            }
        }

    @Test
    fun uxDr117TheSilentTitlesNameTheLotOrTheDeviceWithTheSilenceRoundedDown() =
        runTest {
            api.health["a"] =
                listOf(
                    silentNode,
                    silentHub,
                    health("u", "9c0d000000000000", silentSince = "2026-10-09T01:00:00.000Z"),
                )

            val (node, hub, unassigned) = alertsFor().ready().alerts

            // "Node on Lot 'Beans' silent for 6 h": 6 h 5 min, rounded down.
            assertEquals(
                AlertTitle(
                    AlertTitleKind.NodeOnLotSilent,
                    lotName = "Beans",
                    duration = LotDuration(6, LotDurationUnit.Hours),
                ),
                node.titleNow(),
            )
            // "Hub 3F2A silent for 5 min".
            assertEquals(
                AlertTitle(
                    AlertTitleKind.HubSilent,
                    deviceName = "3F2A",
                    duration = LotDuration(5, LotDurationUnit.Minutes),
                ),
                hub.titleNow(),
            )
            // A Node on no Lot: "Node 9C0D silent for 6 h".
            assertEquals(
                AlertTitle(
                    AlertTitleKind.NodeSilent,
                    deviceName = "9C0D",
                    duration = LotDuration(6, LotDurationUnit.Hours),
                ),
                unassigned.titleNow(),
            )
            // A day later the same Alerts say days.
            assertEquals(LotDuration(1, LotDurationUnit.Days), node.title(NOW_MS + 24 * 3_600_000L).duration)
        }

    @Test
    fun uxDr117AClosedRowAndAnOpenOneWithoutSilentSinceDropTheDuration() =
        runTest {
            api.health["a"] =
                listOf(
                    health("old", hubId, "hub"),
                    health("bad", lotName = "Beans", silentSince = "a while ago"),
                    health("was", hubId, "hub", silentSince = "2026-10-09T05:00:00.000Z", closedAt = closed),
                    health("gone", lotName = "Beans", silentSince = "2026-10-09T01:05:00.000Z", closedAt = closed),
                )

            val titles = alertsFor().ready().alerts.associate { it.id to it.titleNow() }

            // "Hub 3F2A silent" and "Node on Lot 'Beans' silent".
            assertEquals(AlertTitle(AlertTitleKind.HubSilent, deviceName = "3F2A"), titles["old"])
            assertEquals(AlertTitle(AlertTitleKind.NodeOnLotSilent, lotName = "Beans"), titles["bad"])
            assertEquals(AlertTitle(AlertTitleKind.HubSilent, deviceName = "3F2A"), titles["was"])
            assertEquals(AlertTitle(AlertTitleKind.NodeOnLotSilent, lotName = "Beans"), titles["gone"])
        }

    @Test
    fun uxDr25AHealthKindOrDeviceKindThisAppDoesNotKnowKeepsTheNeutralHealthRow() =
        runTest {
            api.health["a"] =
                listOf(
                    health("flood", kind = "flooded", lotName = "Leeks"),
                    health("flood-node", kind = "flooded"),
                    health("flood-hub", hubId, "hub", kind = "flooded"),
                    health("odd", deviceKind = "repeater"),
                    health("none", deviceKind = null),
                    // Never sent by the Server; if it were, it is not shown as a Threshold Alert.
                    health("thr", kind = "threshold", lotName = "Beans"),
                    health("unc", kind = "uncalibrated"),
                )

            val ready = alertsFor().ready()

            assertEquals(7, ready.health.size)
            assertTrue(ready.threshold.isEmpty())
            val titles = ready.alerts.associate { it.id to it.titleNow() }
            assertEquals(AlertTitle(AlertTitleKind.HealthOnLot, lotName = "Leeks"), titles["flood"])
            // Without a Lot the neutral row is "Health Alert", for a Node and for a Hub.
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["flood-node"])
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["flood-hub"])
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["odd"])
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["none"])
            assertEquals(AlertTitle(AlertTitleKind.HealthOnLot, lotName = "Beans"), titles["thr"])
            // A kind that names a Lot, on a Device without one, falls back to the neutral title too.
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["unc"])
            assertTrue(ready.alerts.all { it.target == AlertTarget.Devices })
            assertEquals(AlertCondition.Unknown, ready.alerts.first().condition)
        }

    @Test
    fun theThresholdTitlesStillNameTheLot() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("w"),
                    alert("h", side = "high", quantity = "air_temperature", lotName = "Peppers"),
                    alert("s", kind = "silent", side = null, lotName = "Beans"),
                )

            val titles = alertsFor().ready().alerts.map { it.titleNow() }

            assertEquals(
                listOf(
                    AlertTitle(AlertTitleKind.NeedsWater, lotName = "Tomatoes"),
                    AlertTitle(AlertTitleKind.TooHigh, lotName = "Peppers"),
                    // An Alert listed without a Device kind and with a Lot is a Node's, as before.
                    AlertTitle(AlertTitleKind.NodeOnLotSilent, lotName = "Beans"),
                ),
                titles,
            )
        }

    @Test
    fun uxDr25HealthAlertsAreNotPagedAndALaterPageWithoutThemKeepsThoseOfTheFirst() =
        runTest {
            api.pageSize = 1
            api.alerts["a"] = listOf(alert("1"), alert("2"), alert("3"))
            api.health["a"] = listOf(silentNode, silentHub)

            val ready = alertsFor().ready()

            assertEquals(listOf("list a -", "list a 1", "list a 2"), api.calls)
            assertEquals(listOf("1", "2", "3", "n", "h"), ready.alerts.map { it.id })
            assertEquals(5, ready.openCount)
        }

    @Test
    fun uxDr25AServerWithoutHealthAlertsListsTheThresholdAlertsAsBefore() =
        runTest {
            api.alerts["a"] = listOf(alert("1"), alert("2", closedAt = closed))

            val ready = alertsFor().ready()

            assertEquals(listOf("1", "2"), ready.alerts.map { it.id })
            assertTrue(ready.health.isEmpty())
        }

    @Test
    fun uxDr25ClosedAlertsOfBothListsKeepTheServersOrderAndAreInterleavedNewestFirst() =
        runTest {
            api.alerts["a"] =
                listOf(
                    alert("t-open"),
                    alert("t-0640", closedAt = "2026-10-09T06:40:00.000Z"),
                    alert("t-0500", closedAt = "2026-10-09T05:00:00.000Z"),
                )
            api.health["a"] =
                listOf(
                    health("h-open", hubId, "hub"),
                    health("h-0700", hubId, "hub", closedAt = "2026-10-09T07:00:00.000Z"),
                    health("h-0640", closedAt = "2026-10-09T06:40:00.000Z"),
                    health("h-0400", closedAt = "2026-10-09T04:00:00.000Z"),
                    // The same Alert listed twice is shown once.
                    health("h-open", hubId, "hub"),
                )

            val ready = alertsFor().ready()

            assertEquals(
                listOf("t-open", "h-open", "h-0700", "t-0640", "h-0640", "t-0500", "h-0400"),
                ready.alerts.map { it.id },
            )
            assertEquals(listOf("h-0700", "t-0640", "h-0640", "t-0500", "h-0400"), ready.closed.map { it.id })
        }

    @Test
    fun aHealthAlertWhoseTimesCannotBeReadIsLeftOutInsteadOfFailingTheList() =
        runTest {
            api.alerts["a"] = listOf(alert("1"))
            api.health["a"] =
                listOf(
                    health("bad-open", openedAt = "yesterday"),
                    health("bad-closed", closedAt = "later"),
                    silentHub,
                )

            assertEquals(
                listOf("1", "h"),
                alertsFor().ready().alerts.map { it.id },
            )
        }

    // Story 7.3: a low battery says the level it opened with.

    private val spareId = "7c19000000000004"

    @Test
    fun uxDr25ALowBatterySaysTheLevelItOpenedWithByItsLotOrByItsNodesShortName() =
        runTest {
            api.health["a"] =
                listOf(
                    health("lot", kind = "battery", lotName = "Lettuce", batteryPercent = 14),
                    health("spare", spareId, kind = "battery", batteryPercent = 14),
                    health("empty", kind = "battery", lotName = "Lettuce", batteryPercent = 0),
                )

            val ready = alertsFor().ready()

            assertEquals(listOf("lot", "spare", "empty"), ready.health.map { it.id })
            val (onLot, spare, empty) = ready.health
            // "Node battery 14 %, not charging — Lot 'Lettuce'" and "Node 7C19 battery 14 %, not charging".
            assertEquals(
                AlertTitle(AlertTitleKind.NodeOnLotBattery, lotName = "Lettuce", batteryPercent = 14),
                onLot.titleNow(),
            )
            assertEquals(
                AlertTitle(AlertTitleKind.NodeBattery, deviceName = "7C19", batteryPercent = 14),
                spare.titleNow(),
            )
            // An empty battery is a level.
            assertEquals(0, empty.titleNow().batteryPercent)
            for (alert in ready.health) {
                assertEquals(AlertCondition.Battery, alert.condition)
                assertEquals(AlertGroup.Health, alert.group)
                assertEquals(AlertVariant.Health, alert.variant)
                assertEquals(AlertIcon.BatteryLow, alert.icon)
                assertEquals(AlertEyebrow.Health, alert.eyebrow)
                // A low battery leads to Devices, with or without a Lot.
                assertEquals(AlertTarget.Devices, alert.target)
                assertEquals(AlertDeviceKind.Node, alert.deviceKind)
                assertNull(alert.silentSinceEpochMs)
            }
            assertEquals(14, onLot.batteryPercent)
        }

    @Test
    fun uxDr25AClosedLowBatteryAndOneWithoutALevelReadBatteryLow() =
        runTest {
            api.alerts["a"] = listOf(alert("old", kind = "battery", side = null, lotName = "Peppers"))
            api.health["a"] =
                listOf(
                    health("none", kind = "battery", lotName = "Lettuce"),
                    health("spare", spareId, kind = "battery"),
                    // A level the contract cannot describe is dropped; the Alert stays.
                    health("high", kind = "battery", lotName = "Lettuce", batteryPercent = 140),
                    health("neg", spareId, kind = "battery", batteryPercent = -1),
                    // Never sent by the Server: a Hub has no battery to report.
                    health("hub", hubId, "hub", kind = "battery", batteryPercent = 14),
                    health("was", kind = "battery", lotName = "Lettuce", batteryPercent = 14, closedAt = closed),
                    health("gone", spareId, kind = "battery", batteryPercent = 14, closedAt = closed),
                )

            val ready = alertsFor().ready()
            val titles = ready.alerts.associate { it.id to it.titleNow() }

            // "Node battery low on Lot 'Lettuce'" and "Node 7C19 battery low".
            assertEquals(AlertTitle(AlertTitleKind.Battery, lotName = "Lettuce"), titles["none"])
            assertEquals(AlertTitle(AlertTitleKind.NodeBatteryLow, deviceName = "7C19"), titles["spare"])
            assertEquals(AlertTitle(AlertTitleKind.Battery, lotName = "Lettuce"), titles["high"])
            assertEquals(AlertTitle(AlertTitleKind.NodeBatteryLow, deviceName = "7C19"), titles["neg"])
            assertEquals(AlertTitle(AlertTitleKind.Health), titles["hub"])
            // The level at opening is no longer true once the Alert closed.
            assertEquals(AlertTitle(AlertTitleKind.Battery, lotName = "Lettuce"), titles["was"])
            assertEquals(AlertTitle(AlertTitleKind.NodeBatteryLow, deviceName = "7C19"), titles["gone"])
            assertEquals(14, ready.alerts.single { it.id == "was" }.batteryPercent)
            assertNull(ready.alerts.single { it.id == "high" }.batteryPercent)
            // An Alert of the Threshold list that is a battery Alert reads as before.
            assertEquals(AlertTitle(AlertTitleKind.Battery, lotName = "Peppers"), titles["old"])
        }

    @Test
    fun uxDr26TheSnapshotCarriesTheLevelOfALowBatteryTitleForSwift() =
        runTest {
            api.health["a"] =
                listOf(
                    health("lot", kind = "battery", lotName = "Lettuce", batteryPercent = 14),
                    health("spare", spareId, kind = "battery", batteryPercent = 9),
                    health("none", kind = "battery", lotName = "Lettuce"),
                    silentNode,
                    health("was", kind = "battery", lotName = "Lettuce", batteryPercent = 14, closedAt = closed),
                    health("gone", spareId, kind = "battery", batteryPercent = 14, closedAt = closed),
                )

            val snapshot = snapshotOf(alertsFor().state.value)

            assertEquals(listOf("lot", "spare", "none", "n", "was", "gone"), snapshot.alertIds)
            assertEquals(
                listOf("nodeOnLotBattery", "nodeBattery", "battery", "nodeOnLotSilent", "battery", "nodeBatteryLow"),
                snapshot.alertTitles,
            )
            assertEquals(listOf("14", "9", "", "", "", ""), snapshot.alertBatteryPercents)
            assertEquals(listOf("9C0D", "7C19", "9C0D", "9C0D", "9C0D", "7C19"), snapshot.alertDeviceNames)
            assertEquals(
                listOf("battery", "battery", "battery", "silent", "battery", "battery"),
                snapshot.alertConditions,
            )
            assertEquals(
                listOf("battery--low", "battery--low", "battery--low", "help", "battery--low", "battery--low"),
                snapshot.alertIcons,
            )
            assertEquals(List(6) { "devices" }, snapshot.alertTargets)
            assertEquals(listOf("", "", "", "6", "", ""), snapshot.alertDurationValues)
        }

    // Story 7.4: the Server produces the uncalibrated kind, for a soil Sensor of a Node on a Lot.

    @Test
    fun uxDr25AnUncalibratedSensorNamesItsLotWithTheToolsIconAndOpensLotDetail() =
        runTest {
            api.alerts["a"] = listOf(alert("t"))
            api.health["a"] =
                listOf(
                    health("unc", kind = "uncalibrated", lotName = "Peppers"),
                    health("was", kind = "uncalibrated", lotName = "Herbs", closedAt = closed),
                )

            val ready = alertsFor().ready()

            // Open under Health after the Threshold Alerts, closed under Closed.
            assertEquals(listOf("t"), ready.threshold.map { it.id })
            assertEquals(listOf("unc"), ready.health.map { it.id })
            assertEquals(listOf("was"), ready.closed.map { it.id })
            val open = ready.health.single()
            // "Soil Sensor on Lot 'Peppers' needs Calibration".
            assertEquals(AlertTitle(AlertTitleKind.Uncalibrated, lotName = "Peppers"), open.titleNow())
            assertEquals(AlertCondition.Uncalibrated, open.condition)
            assertEquals(AlertGroup.Health, open.group)
            assertEquals(AlertVariant.Health, open.variant)
            assertEquals(AlertIcon.Tools, open.icon)
            assertEquals(AlertEyebrow.Health, open.eyebrow)
            // It leads to Lot detail of its Lot, where Calibrate is offered: never to Devices.
            assertEquals(AlertTarget.Lot, open.target)
            assertEquals("lot-unc", open.lotId)
            assertEquals(AlertDeviceKind.Node, open.deviceKind)
            assertNull(open.silentSinceEpochMs)
            assertNull(open.batteryPercent)
            // The closed row keeps its title, icon and target.
            val was = ready.closed.single()
            assertEquals(AlertTitle(AlertTitleKind.Uncalibrated, lotName = "Herbs"), was.titleNow())
            assertEquals(AlertCondition.Uncalibrated, was.condition)
            assertEquals(AlertGroup.Closed, was.group)
            assertEquals(AlertVariant.Closed, was.variant)
            assertEquals(AlertIcon.Tools, was.icon)
            assertEquals(AlertTarget.Lot, was.target)
        }

    @Test
    fun uxDr26TheSnapshotCarriesAnUncalibratedSensorForSwift() =
        runTest {
            api.health["a"] =
                listOf(
                    health("unc", kind = "uncalibrated", lotName = "Peppers"),
                    silentNode,
                    health("was", kind = "uncalibrated", lotName = "Herbs", closedAt = closed),
                )

            val snapshot = snapshotOf(alertsFor().state.value)

            assertEquals(listOf("unc", "n", "was"), snapshot.alertIds)
            assertEquals(listOf("health", "health", "closed"), snapshot.alertGroups)
            assertEquals(listOf("uncalibrated", "nodeOnLotSilent", "uncalibrated"), snapshot.alertTitles)
            assertEquals(listOf("uncalibrated", "silent", "uncalibrated"), snapshot.alertConditions)
            assertEquals(listOf("tools", "help", "tools"), snapshot.alertIcons)
            assertEquals(listOf("lot", "devices", "lot"), snapshot.alertTargets)
            assertEquals(listOf("lot-unc", "lot-n", "lot-was"), snapshot.alertLotIds)
            assertEquals(listOf("Peppers", "Beans", "Herbs"), snapshot.alertLotNames)
            assertEquals(listOf("node", "node", "node"), snapshot.alertDeviceKinds)
            // It has neither a duration nor a level.
            assertEquals(listOf("", "6", ""), snapshot.alertDurationValues)
            assertEquals(listOf("", "", ""), snapshot.alertBatteryPercents)
            assertEquals(2, snapshot.openCount)
        }

    @Test
    fun uxDr117TheSnapshotCarriesTheTitleInputsOfHealthAlertsForSwift() =
        runTest {
            api.alerts["a"] = listOf(alert("t"))
            api.health["a"] =
                listOf(
                    silentNode,
                    silentHub,
                    health("u", "9c0d000000000000", silentSince = "2026-10-09T01:00:00.000Z"),
                    health("was", hubId, "hub", silentSince = "2026-10-09T05:00:00.000Z", closedAt = closed),
                    health("flood", hubId, "hub", kind = "flooded", closedAt = "2026-10-09T06:00:00.000Z"),
                )

            val state = alertsFor().state.value
            val snapshot = snapshotOf(state)

            assertEquals(listOf("t", "n", "h", "u", "was", "flood"), snapshot.alertIds)
            assertEquals(listOf("threshold", "health", "health", "health", "closed", "closed"), snapshot.alertGroups)
            assertEquals(
                listOf("needsWater", "nodeOnLotSilent", "hubSilent", "nodeSilent", "hubSilent", "health"),
                snapshot.alertTitles,
            )
            assertEquals(listOf("lot-t", "lot-n", "", "", "", ""), snapshot.alertLotIds)
            assertEquals(listOf("Tomatoes", "Beans", "", "", "", ""), snapshot.alertLotNames)
            assertEquals(listOf("", "node", "hub", "node", "hub", "hub"), snapshot.alertDeviceKinds)
            assertEquals(listOf("7C19", "9C0D", "3F2A", "9C0D", "3F2A", "3F2A"), snapshot.alertDeviceNames)
            assertEquals(listOf("", "6", "5", "6", "", ""), snapshot.alertDurationValues)
            assertEquals(listOf("", "hours", "minutes", "hours", "", ""), snapshot.alertDurationUnits)
            assertEquals(
                listOf("", "1791507900000", "1791529500000", "1791507600000", "1791522000000", ""),
                snapshot.alertSilentSince,
            )
            assertEquals(listOf("soil_moisture", "", "", "", "", ""), snapshot.alertQuantities)
            assertEquals(listOf("lot", "devices", "devices", "devices", "devices", "devices"), snapshot.alertTargets)
            assertEquals(listOf("silent", "silent", "silent", "silent", "unknown"), snapshot.alertConditions.drop(1))
            assertEquals(4, snapshot.openCount)
            // The next minute tick moves the durations and nothing else.
            val later = snapshotOf(state, NOW_MS + 55 * 60_000L)
            assertEquals(listOf("", "7", "1", "7", "", ""), later.alertDurationValues)
            assertEquals(listOf("", "hours", "hours", "hours", "", ""), later.alertDurationUnits)
        }
}
