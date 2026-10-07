package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DeviceListItemDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SiteRole
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

/** An in-memory Server for the Devices list, per Site. */
class FakeDevicesApi : DevicesApi {
    val devices = mutableMapOf<String, List<DeviceListItemDto>>()
    val calls = mutableListOf<String>()
    val lots = mutableMapOf<String, List<LotDto>>()
    var failure: ApiFailure? = null
    var gate: CompletableDeferred<Unit>? = null

    /** The answer of the next move or unassign, or `null` to accept it as the Server would. */
    var actionFailure: ApiFailure? = null

    override suspend fun listDevices(siteId: String): ApiResult<DeviceListDto> {
        calls += "list $siteId"
        val answer = failure?.let { ApiResult.Failed(it) } ?: ApiResult.Ok(DeviceListDto(devices[siteId].orEmpty()))
        gate?.await()
        return answer
    }

    override suspend fun listLots(siteId: String): ApiResult<LotListDto> {
        calls += "lots $siteId"
        return ApiResult.Ok(LotListDto(lots[siteId].orEmpty()))
    }

    override suspend fun moveDevice(
        siteId: String,
        deviceId: String,
        lotId: String,
    ): ApiResult<DeviceDto> {
        calls += "move $siteId $deviceId $lotId"
        actionFailure?.let { return ApiResult.Failed(it) }
        // The Server's bookkeeping: the Node is on the Lot, the old Lot is free, the new one has a Node.
        val node = devices[siteId].orEmpty().first { it.id == deviceId }
        val target = lots[siteId].orEmpty().first { it.id == lotId }
        devices[siteId] =
            devices[siteId].orEmpty().map {
                if (it.id ==
                    deviceId
                ) {
                    it.copy(lotId = lotId, lotName = target.name)
                } else {
                    it
                }
            }
        lots[siteId] =
            lots[siteId].orEmpty().map {
                when (it.id) {
                    node.lotId -> it.copy(status = "noNode")
                    lotId -> it.copy(status = "unknown")
                    else -> it
                }
            }
        return ApiResult.Ok(DeviceDto(deviceId, "node", siteId, lotId))
    }

    override suspend fun unassignDevice(
        siteId: String,
        deviceId: String,
    ): ApiResult<DeviceDto> {
        calls += "unassign $siteId $deviceId"
        actionFailure?.let { return ApiResult.Failed(it) }
        val node = devices[siteId].orEmpty().first { it.id == deviceId }
        devices[siteId] =
            devices[siteId].orEmpty().map { if (it.id == deviceId) it.copy(lotId = null, lotName = null) else it }
        lots[siteId] = lots[siteId].orEmpty().map { if (it.id == node.lotId) it.copy(status = "noNode") else it }
        return ApiResult.Ok(DeviceDto(deviceId, "node", siteId))
    }
}

class DevicesEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeDevicesApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)

    private val seenAt = "2026-10-06T07:02:00.000Z"
    private val seenAtMs = 1_791_270_120_000L

    private fun hub(
        id: String,
        online: Boolean,
        lastSeenAt: String? = seenAt,
    ) = DeviceListItemDto(id, "hub", online, lastSeenAt = lastSeenAt)

    private fun TestScope.engines(vararg sites: SiteDto): Pair<SitesEngine, DevicesEngine> {
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
        val devices = DevicesEngine(api, sitesEngine, backgroundScope)
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return sitesEngine to devices
    }

    private fun TestScope.devicesFor(role: String = "Owner"): DevicesEngine = engines(SiteDto("a", "Home", role)).second

    private fun DevicesEngine.ready(): DevicesState.Ready = assertIs<DevicesState.Ready>(state.value)

    @Test
    fun uxDr30AnOnlineHubIsListedWithItsFullIdAndLastSeenTime() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))

            val devices = devicesFor()

            assertEquals(
                listOf(HubSummary("3f2a9c0d1e4b5a67", online = true, lastSeenAtEpochMs = seenAtMs)),
                devices.ready().hubs,
            )
            assertEquals(listOf("list a"), api.calls)
        }

    @Test
    fun uxDr65ReenteringDevicesAfterTheHeartbeatStoppedReadsOfflineWithTheUnchangedLastSeenTime() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))
            val devices = devicesFor()
            assertTrue(
                devices
                    .ready()
                    .hubs
                    .single()
                    .online,
            )

            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = false))
            devices.load()
            runCurrent()

            assertEquals(
                listOf(HubSummary("3f2a9c0d1e4b5a67", online = false, lastSeenAtEpochMs = seenAtMs)),
                devices.ready().hubs,
            )
            assertEquals(listOf("list a", "list a"), api.calls)
        }

    @Test
    fun uxDr30OnlineIsTheServersFlagAndIsNeverComputedFromTheLastSeenTime() =
        runTest {
            // A last-seen time far in the past with online, and a fresh one with offline: both pass through.
            api.devices["a"] =
                listOf(
                    hub("1b00aa11bb22cc33", online = true, lastSeenAt = "2020-01-01T00:00:00Z"),
                    hub("3f2a9c0d1e4b5a67", online = false, lastSeenAt = "2099-01-01T00:00:00.000Z"),
                )

            val devices = devicesFor()

            assertEquals(listOf(true, false), devices.ready().hubs.map { it.online })
        }

    @Test
    fun uxDr30AHubThatNeverHeartbeatedHasNoLastSeenTimeAndIsOffline() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = false, lastSeenAt = null))

            val devices = devicesFor()

            assertEquals(
                listOf(HubSummary("3f2a9c0d1e4b5a67", online = false, lastSeenAtEpochMs = null)),
                devices.ready().hubs,
            )
        }

    @Test
    fun aLastSeenTimeThatIsNotAnInstantReadsAsNeverSeen() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = false, lastSeenAt = "yesterday"))

            assertNull(
                devicesFor()
                    .ready()
                    .hubs
                    .single()
                    .lastSeenAtEpochMs,
            )
            assertEquals(seenAtMs, DevicesEngine.epochMsOf("2026-10-06T09:02:00+02:00"))
        }

    @Test
    fun uxDr30ASiteWithoutDevicesIsReadyWithNoHubs() =
        runTest {
            val devices = devicesFor()

            assertEquals(emptyList(), devices.ready().hubs)
        }

    @Test
    fun uxDr30OnlyHubsAreShownByDeviceIdAndNodesAreLeftOut() =
        runTest {
            api.devices["a"] =
                listOf(
                    hub("3f2a9c0d1e4b5a67", online = true),
                    DeviceListItemDto("0a0a0a0a0a0a0a0a", "node", false, lotId = "l1"),
                    hub("1b00aa11bb22cc33", online = false),
                    DeviceListItemDto("2c2c2c2c2c2c2c2c", "later", true),
                )

            val devices = devicesFor()

            assertEquals(listOf("1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"), devices.ready().hubs.map { it.id })
        }

    @Test
    fun uxDr65AnotherSitesDevicesAreNeverShownAndSwitchingSiteLoadsTheNewSite() =
        runTest {
            api.devices["a"] = listOf(hub("1b00aa11bb22cc33", online = true))
            api.devices["b"] = listOf(hub("3f2a9c0d1e4b5a67", online = false))
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"), SiteDto("b", "Allotment", "Member"))
            assertEquals(listOf("1b00aa11bb22cc33"), devices.ready().hubs.map { it.id })

            sites.select("b")
            runCurrent()

            assertEquals("b", devices.ready().site.id)
            assertEquals(listOf("3f2a9c0d1e4b5a67"), devices.ready().hubs.map { it.id })
            assertEquals(listOf("list a", "list b"), api.calls)
        }

    @Test
    fun uxDr84AddAHubIsForAdministratorsAndOwnersAndHiddenForMembers() =
        runTest {
            assertTrue(devicesFor("Owner").state.value.canAddHub)
            assertTrue(DevicesState.canAddHub(com.escendit.coldframe.core.sites.SiteRole.Administrator))
            assertFalse(DevicesState.canAddHub(com.escendit.coldframe.core.sites.SiteRole.Member))
            assertFalse(DevicesState.Idle.canAddHub)
        }

    @Test
    fun aRenameOfTheCurrentSiteKeepsTheHubsWithoutReadingTheListAgain() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"))

            sites.renameSite("Home garden")
            runCurrent()

            assertEquals("Home garden", devices.ready().site.name)
            assertTrue(devices.state.value.canAddHub)
            assertEquals(listOf("3f2a9c0d1e4b5a67"), devices.ready().hubs.map { it.id })
            assertEquals(listOf("list a"), api.calls)
        }

    @Test
    fun uxDr84ARoleChangeOnTheCurrentSiteFollowsWhenTheSitesAreReadAgain() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"))
            assertTrue(devices.state.value.canAddHub)

            sitesApi.sites[0] = SiteDto("a", "Home", "Member")
            sites.load()
            runCurrent()

            assertFalse(devices.state.value.canAddHub)
            assertEquals(listOf("3f2a9c0d1e4b5a67"), devices.ready().hubs.map { it.id })
        }

    @Test
    fun uxDr84AMemberSeesTheListWithoutAddAHub() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))

            val devices = devicesFor("Member")

            assertEquals(1, devices.ready().hubs.size)
            assertFalse(devices.state.value.canAddHub)
            assertFalse(snapshotOf(devices.state.value).canAddHub)
            assertFalse(snapshotOf(devices.state.value).canAddNode)
        }

    @Test
    fun uxDr65AFailedReloadDropsTheRowsSoNothingStaysOnline() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))
            val devices = devicesFor()
            assertTrue(
                devices
                    .ready()
                    .hubs
                    .single()
                    .online,
            )

            api.failure = ApiFailure.Unreachable
            devices.load()
            runCurrent()

            val failed = assertIs<DevicesState.Failed>(devices.state.value)
            assertEquals(DevicesNotice.Unreachable, failed.notice)
            assertTrue(failed.notice.tryAgain)
            assertEquals(emptyList(), snapshotOf(failed).hubIds)

            // Try again reads the list again.
            api.failure = null
            devices.load()
            runCurrent()
            assertTrue(
                devices
                    .ready()
                    .hubs
                    .single()
                    .online,
            )
        }

    @Test
    fun uxDr65NoRowIsShownWhileAReloadIsUnderWay() =
        runTest {
            api.devices["a"] = listOf(hub("3f2a9c0d1e4b5a67", online = true))
            val devices = devicesFor()
            val gate = CompletableDeferred<Unit>()
            api.gate = gate

            devices.load()
            runCurrent()
            assertIs<DevicesState.Loading>(devices.state.value)
            // A second entry while it loads sends nothing more.
            devices.load()
            runCurrent()
            assertEquals(listOf("list a", "list a"), api.calls)

            gate.complete(Unit)
            runCurrent()
            assertIs<DevicesState.Ready>(devices.state.value)
        }

    @Test
    fun aServerErrorAForbiddenOrAMissingSiteReadAsUnreachableAndACertificateFailureAsCertificate() =
        runTest {
            val devices = devicesFor()
            for (failure in listOf(
                ApiFailure.Unexpected,
                ApiFailure.Forbidden,
                ApiFailure.NotFound,
                ApiFailure.IdentityProviderUnavailable,
            )) {
                api.failure = failure
                devices.load()
                runCurrent()
                assertEquals(
                    DevicesNotice.Unreachable,
                    assertIs<DevicesState.Failed>(devices.state.value).notice,
                    "$failure",
                )
            }

            api.failure = ApiFailure.Certificate
            devices.load()
            runCurrent()
            val failed = assertIs<DevicesState.Failed>(devices.state.value)
            assertEquals(DevicesNotice.Certificate, failed.notice)
            assertFalse(failed.notice.tryAgain)
        }

    @Test
    fun a401GoesIdleLikeTheSignedOutPath() =
        runTest {
            val devices = devicesFor()

            api.failure = ApiFailure.Unauthorized
            devices.load()
            runCurrent()

            assertEquals(DevicesState.Idle, devices.state.value)
        }

    @Test
    fun staysIdleWhileSignedOutAndGoesIdleOnSignOut() =
        runTest {
            val devices =
                DevicesEngine(
                    api,
                    SitesEngine(sitesApi, DeviceChoices(MapSettings()), backgroundScope, signIn),
                    backgroundScope,
                )
            runCurrent()
            assertEquals(DevicesState.Idle, devices.state.value)
            devices.load()
            runCurrent()
            assertEquals(emptyList(), api.calls)

            signIn.value = SignInState.SignedIn("Simon")
            sitesApi.sites += SiteDto("a", "Home", "Owner")
            runCurrent()
            assertIs<DevicesState.Ready>(devices.state.value)

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            assertEquals(DevicesState.Idle, devices.state.value)
        }

    @Test
    fun anAnswerForAnEarlierSiteIsDropped() =
        runTest {
            api.devices["a"] = listOf(hub("1b00aa11bb22cc33", online = true))
            api.devices["b"] = emptyList()
            val gate = CompletableDeferred<Unit>()
            api.gate = gate
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"), SiteDto("b", "Allotment", "Member"))
            assertIs<DevicesState.Loading>(devices.state.value)

            sites.select("b")
            runCurrent()
            gate.complete(Unit)
            runCurrent()

            assertEquals("b", devices.ready().site.id)
            assertEquals(emptyList(), devices.ready().hubs)
        }

    @Test
    fun theSnapshotCarriesTheHubsAsStringListsForSwift() =
        runTest {
            api.devices["a"] =
                listOf(
                    hub("1b00aa11bb22cc33", online = false, lastSeenAt = null),
                    hub("3f2a9c0d1e4b5a67", online = true),
                )

            val snapshot = snapshotOf(devicesFor("Administrator").state.value)

            assertEquals(
                DevicesSnapshot(
                    surface = "ready",
                    notice = null,
                    noticeTryAgain = false,
                    siteId = "a",
                    siteName = "Home",
                    canAddHub = true,
                    canAddNode = true,
                    hubIds = listOf("1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"),
                    hubStatuses = listOf("offline", "online"),
                    hubLastSeen = listOf("", "1791270120000"),
                    canManageNodes = true,
                ),
                snapshot,
            )
            assertEquals("idle", snapshotOf(DevicesState.Idle).surface)
        }

    @Test
    fun theSnapshotOfAFailedLoadHasTheNoticeAndNoHubs() =
        runTest {
            api.failure = ApiFailure.Unreachable

            val snapshot = snapshotOf(devicesFor().state.value)

            assertEquals("failed", snapshot.surface)
            assertEquals("unreachable", snapshot.notice)
            assertTrue(snapshot.noticeTryAgain)
            assertEquals(emptyList(), snapshot.hubIds)
            assertTrue(snapshot.canAddHub)
        }

    private fun node(
        id: String,
        lotId: String?,
        lotName: String?,
    ) = DeviceListItemDto(id, "node", false, lotId = lotId, lotName = lotName)

    private fun seedGarden() {
        api.devices["a"] =
            listOf(
                node("7c19000000000001", "l-peppers", "Peppers"),
                node("7c19000000000002", "l-tomatoes", "Tomatoes"),
                node("7c19000000000003", null, null),
            )
        api.lots["a"] =
            listOf(
                LotDto("l-peppers", "Peppers", "unknown"),
                LotDto("l-tomatoes", "Tomatoes", "ok"),
                LotDto("l-basil", "Basil", "noNode"),
            )
    }

    @Test
    fun uxDr31AnAdministratorOrOwnerCanManageNodesAndAMemberCannot() =
        runTest {
            seedGarden()
            assertTrue(snapshotOf(devicesFor("Administrator").state.value).canManageNodes)
            assertTrue(DevicesState.canManageNodes(SiteRole.Owner))
            assertFalse(DevicesState.canManageNodes(SiteRole.Member))
        }

    @Test
    fun uxDr31AMemberSeesNoMoveOrUnassignAndTheLotsAreNeverRead() =
        runTest {
            seedGarden()
            val devices = devicesFor("Member")

            assertFalse(snapshotOf(devices.state.value).canManageNodes)
            assertEquals(emptyList(), devices.ready().lots)
            assertEquals(listOf("list a"), api.calls)

            // Even a call straight at the engine does nothing for a Member.
            devices.moveNode("7c19000000000001", "l-basil")
            devices.unassignNode("7c19000000000001")
            runCurrent()
            assertEquals(listOf("list a"), api.calls)
        }

    @Test
    fun uxDr31OccupiedLotsAreNotSelectableAndTheNodesOwnLotIsMarkedCurrent() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")
            val ready = devices.ready()

            assertEquals(listOf("list a", "lots a"), api.calls)
            val peppers = ready.nodes.first { it.id == "7c19000000000001" }
            assertEquals(
                listOf(
                    MoveLotChoice("l-peppers", "Peppers", hasNode = false, current = true),
                    MoveLotChoice("l-tomatoes", "Tomatoes", hasNode = true, current = false),
                    MoveLotChoice("l-basil", "Basil", hasNode = false, current = false),
                ),
                ready.choicesFor(peppers),
            )
            assertEquals(listOf(false, false, true), ready.choicesFor(peppers).map { it.selectable })

            // An unassigned Node has no current Lot: Peppers and Tomatoes both have a Node.
            val spare = ready.nodes.first { it.id == "7c19000000000003" }
            assertEquals(listOf(false, false, true), ready.choicesFor(spare).map { it.selectable })
        }

    @Test
    fun uxDr31MovingANodeSendsTheLotThenReadsTheServersAnswerAgain() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")

            devices.moveNode("7c19000000000001", "l-basil")
            runCurrent()

            assertEquals(
                listOf("list a", "lots a", "move a 7c19000000000001 l-basil", "list a", "lots a"),
                api.calls,
            )
            val ready = devices.ready()
            assertEquals("Basil", ready.nodes.first { it.id == "7c19000000000001" }.lotName)
            assertNull(ready.workingNodeId)
            assertNull(ready.failure)
            // Peppers is free again; Basil has a Node.
            assertEquals(false, ready.lots.first { it.id == "l-peppers" }.hasNode)
            assertEquals(true, ready.lots.first { it.id == "l-basil" }.hasNode)
        }

    @Test
    fun uxDr31AnOccupiedOrCurrentLotIsNeverSent() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")

            devices.moveNode("7c19000000000001", "l-tomatoes")
            devices.moveNode("7c19000000000001", "l-peppers")
            devices.moveNode("7c19000000000001", "l-gone")
            devices.moveNode("nope", "l-basil")
            runCurrent()

            assertEquals(listOf("list a", "lots a"), api.calls)
            // The last Lot that could be chosen is reported, not dropped silently.
            assertEquals(NodeActionFailure("7c19000000000001", NodeActionNotice.LotTaken), devices.ready().failure)
        }

    @Test
    fun uxDr31ARefusedMoveShowsItsReasonForThatNodeAndChangesNothing() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")
            val cases =
                listOf(
                    ApiFailure.LotClaimed to NodeActionNotice.LotTaken,
                    ApiFailure.Forbidden to NodeActionNotice.Forbidden,
                    ApiFailure.NotFound to NodeActionNotice.NotFound,
                    ApiFailure.Unreachable to NodeActionNotice.Unreachable,
                    ApiFailure.Certificate to NodeActionNotice.Certificate,
                    ApiFailure.Unexpected to NodeActionNotice.Unexpected,
                )

            for ((failure, notice) in cases) {
                api.actionFailure = failure
                devices.moveNode("7c19000000000001", "l-basil")
                runCurrent()

                assertEquals(NodeActionFailure("7c19000000000001", notice), devices.ready().failure, failure.name)
                assertNull(devices.ready().workingNodeId)
                assertEquals(
                    "Peppers",
                    devices
                        .ready()
                        .nodes
                        .first { it.id == "7c19000000000001" }
                        .lotName,
                )
            }

            api.actionFailure = ApiFailure.LotClaimed
            devices.moveNode("7c19000000000001", "l-basil")
            runCurrent()
            val snapshot = snapshotOf(devices.state.value)
            assertEquals("7c19000000000001", snapshot.failedNodeId)
            assertEquals("lotTaken", snapshot.actionNotice)
        }

    @Test
    fun uxDr31UnassigningANodeReadsTheListAgainAndAnUnassignedNodeHasNothingToUnassign() =
        runTest {
            seedGarden()
            val devices = devicesFor("Owner")

            devices.unassignNode("7c19000000000003")
            runCurrent()
            assertEquals(listOf("list a", "lots a"), api.calls)

            devices.unassignNode("7c19000000000002")
            runCurrent()

            assertEquals(listOf("list a", "lots a", "unassign a 7c19000000000002", "list a", "lots a"), api.calls)
            val ready = devices.ready()
            assertNull(ready.nodes.first { it.id == "7c19000000000002" }.lotId)
            assertEquals(false, ready.lots.first { it.id == "l-tomatoes" }.hasNode)
        }

    @Test
    fun uxDr31AMoveThatIsUnderWayBlocksAnotherAndTheSnapshotNamesTheNode() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")
            api.actionFailure = null

            devices.moveNode("7c19000000000001", "l-basil")
            // Before the answer lands: the Node is working and a second action is ignored.
            assertEquals("7c19000000000001", snapshotOf(devices.state.value).workingNodeId)
            devices.unassignNode("7c19000000000002")
            runCurrent()

            assertEquals(listOf("list a", "lots a", "move a 7c19000000000001 l-basil", "list a", "lots a"), api.calls)
            assertEquals("", snapshotOf(devices.state.value).workingNodeId)
        }

    @Test
    fun uxDr31TheSnapshotCarriesTheLotPickerAsParallelListsForSwift() =
        runTest {
            seedGarden()

            val snapshot = snapshotOf(devicesFor("Administrator").state.value)

            assertEquals(listOf("l-peppers", "l-tomatoes", ""), snapshot.nodeLotIds)
            assertEquals(listOf("l-peppers", "l-tomatoes", "l-basil"), snapshot.lotIds)
            assertEquals(listOf("Peppers", "Tomatoes", "Basil"), snapshot.lotNames)
            assertEquals(listOf(true, true, false), snapshot.lotHasNode)
            assertTrue(snapshot.canManageNodes)
        }

    @Test
    fun uxDr31A401OnAMoveEndsTheSessionLikeEveryOtherCall() =
        runTest {
            seedGarden()
            val devices = devicesFor("Administrator")
            api.actionFailure = ApiFailure.Unauthorized

            devices.moveNode("7c19000000000001", "l-basil")
            runCurrent()

            assertIs<DevicesState.Idle>(devices.state.value)
        }
}
