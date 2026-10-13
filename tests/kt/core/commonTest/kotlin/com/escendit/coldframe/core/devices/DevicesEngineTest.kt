package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DeviceListItemDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.api.SilenceWindowDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.LotDurationUnit
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.DevicePause
import com.escendit.coldframe.core.sites.FakePauseApi
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.PauseForm
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.PauseState
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** 2026-10-06T07:10:00Z: the `now` every snapshot of these tests is taken at. No test reads a wall clock. */
private const val NOW_MS = 1_791_270_600_000L

private fun snapshotOf(state: DevicesState): DevicesSnapshot = snapshotOf(state, NOW_MS)

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

    /** The answer of the next Silence Window change, or `null` to accept it as the Server would. */
    var windowFailure: ApiFailure? = null

    override suspend fun setSilenceWindow(
        siteId: String,
        deviceId: String,
        windowSeconds: Int?,
    ): ApiResult<SilenceWindowDto> {
        calls += "window $siteId $deviceId ${windowSeconds ?: "default"}"
        windowFailure?.let { return ApiResult.Failed(it) }
        val device = devices[siteId].orEmpty().first { it.id == deviceId }
        // The Server's bookkeeping: the window in force, the default of the kind when none is given.
        val inForce = windowSeconds ?: if (device.kind == "hub") 300 else 21_600
        devices[siteId] =
            devices[siteId].orEmpty().map {
                if (it.id == deviceId) {
                    it.copy(silenceWindowSeconds = inForce, silenceWindowOverridden = windowSeconds != null)
                } else {
                    it
                }
            }
        return ApiResult.Ok(SilenceWindowDto(inForce, windowSeconds != null))
    }
}

class DevicesEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeDevicesApi()
    private val pauseApi = FakePauseApi(sitesApi)
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
        val devices = DevicesEngine(api, sitesEngine, backgroundScope, now = { NOW_MS }, pauseApi = pauseApi)
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
                    hubShortNames = listOf("1B00", "3F2A"),
                    hubSilentSince = listOf("", ""),
                    hubSilenceValues = listOf("", ""),
                    hubSilenceUnits = listOf("", ""),
                    hubWindowSeconds = listOf("", ""),
                    hubWindowValues = listOf("", ""),
                    hubWindowUnits = listOf("", ""),
                    hubWindowOverridden = listOf(false, false),
                    canSetSilenceWindow = true,
                    silenceWindowMinSeconds = 60,
                    silenceWindowMaxSeconds = 2_592_000,
                    canPause = true,
                    hubPaused = listOf(false, false),
                    hubPausedBySite = listOf(false, false),
                    hubPausedBy = listOf("", ""),
                    hubPausedUntil = listOf("", ""),
                    hubCanPause = listOf(true, true),
                    hubCanResume = listOf(false, false),
                    hubResumeSiteHint = listOf(false, false),
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

    // Story 7.2: silence on Device rows and the Silence Window.

    private val hubId = "3f2a9c0d1e4b5a67"
    private val nodeId = "9c0d1e4b5a673f2a"

    /** A Hub silent for 12 min and a Node on Beans silent for 6 h 5 min at [NOW_MS], and a Node that is heard. */
    private fun seedSilence() {
        api.devices["a"] =
            listOf(
                DeviceListItemDto(
                    hubId,
                    "hub",
                    online = false,
                    lastSeenAt = "2026-10-06T06:58:00.000Z",
                    silentSince = "2026-10-06T06:58:00.000Z",
                    silenceWindowSeconds = 300,
                    silenceWindowOverridden = false,
                ),
                DeviceListItemDto(
                    nodeId,
                    "node",
                    online = false,
                    lotId = "l-beans",
                    lotName = "Beans",
                    lastSeenAt = "2026-10-06T01:05:00.000Z",
                    silentSince = "2026-10-06T01:05:00.000Z",
                    silenceWindowSeconds = 28_800,
                    silenceWindowOverridden = true,
                ),
                DeviceListItemDto("7c19000000000001", "node", online = true, lotId = "l-peas", lotName = "Peas"),
            )
    }

    @Test
    fun uxDr30ASilentHubAndNodeCarryTheServersSilentSinceAndTheirSilenceRoundedDown() =
        runTest {
            seedSilence()

            val ready = devicesFor("Member").ready()

            val hub = ready.hubs.single()
            assertTrue(hub.silent)
            assertEquals("3F2A", hub.shortName)
            assertEquals(LotDuration(12, LotDurationUnit.Minutes), hub.silence(NOW_MS))
            assertEquals(SilenceWindowLength(5, SilenceWindowUnit.Minutes), hub.silenceWindow)
            assertFalse(hub.silenceWindowOverridden)
            val node = ready.nodes.first()
            assertTrue(node.silent)
            assertEquals("9C0D", node.shortName)
            // 6 h 5 min is "6 h": rounded down, never up.
            assertEquals(LotDuration(6, LotDurationUnit.Hours), node.silence(NOW_MS))
            assertEquals(SilenceWindowLength(8, SilenceWindowUnit.Hours), node.silenceWindow)
            assertTrue(node.silenceWindowOverridden)
        }

    @Test
    fun uxDr30ADeviceWithoutTheSilenceFieldsIsARowAsBefore() =
        runTest {
            seedSilence()

            val heard = devicesFor().ready().nodes.last()

            assertFalse(heard.silent)
            assertNull(heard.silence(NOW_MS))
            assertNull(heard.silenceWindow)
            assertFalse(heard.silenceWindowOverridden)
        }

    @Test
    fun aSilentSinceThatIsNotAnInstantLeavesTheRowNotSilentInsteadOfFailingTheList() =
        runTest {
            api.devices["a"] = listOf(DeviceListItemDto(hubId, "hub", online = false, silentSince = "a while ago"))

            assertFalse(
                devicesFor()
                    .ready()
                    .hubs
                    .single()
                    .silent,
            )
        }

    @Test
    fun uxDr84OnlyAdministratorsAndOwnersGetTheSilenceWindowAction() =
        runTest {
            seedSilence()
            assertTrue(DevicesState.canSetSilenceWindow(SiteRole.Owner))
            assertTrue(DevicesState.canSetSilenceWindow(SiteRole.Administrator))
            assertFalse(DevicesState.canSetSilenceWindow(SiteRole.Member))

            val devices = devicesFor("Member")

            assertFalse(devices.state.value.canSetSilenceWindow)
            assertFalse(snapshotOf(devices.state.value).canSetSilenceWindow)
            // Even a call straight at the engine does nothing for a Member.
            devices.setSilenceWindow(nodeId, 3_600)
            devices.setSilenceWindow(nodeId, null)
            runCurrent()
            assertEquals(listOf("list a"), api.calls)
            assertNull(devices.ready().silenceWindowFailure)
        }

    @Test
    fun uxDr31SettingTheSilenceWindowSendsWholeSecondsThenReadsTheServersAnswerAgain() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            api.calls.clear()

            devices.setSilenceWindow(hubId, 600)
            assertEquals(hubId, snapshotOf(devices.state.value).silenceWindowWorkingId)
            runCurrent()

            assertEquals(listOf("window a $hubId 600", "list a", "lots a"), api.calls)
            val hub = devices.ready().hubs.single()
            assertEquals(600, hub.silenceWindowSeconds)
            assertTrue(hub.silenceWindowOverridden)
            assertNull(devices.ready().silenceWindowWorkingId)
            assertNull(devices.ready().silenceWindowFailure)
        }

    @Test
    fun uxDr31UseDefaultSendsNoValueAndTheRowShowsTheDefaultOfTheKind() =
        runTest {
            seedSilence()
            val devices = devicesFor("Owner")
            api.calls.clear()

            devices.setSilenceWindow(nodeId, null)
            runCurrent()

            assertEquals("window a $nodeId default", api.calls.first())
            val node = devices.ready().nodes.first()
            assertEquals(SilenceWindowLength(6, SilenceWindowUnit.Hours), node.silenceWindow)
            assertFalse(node.silenceWindowOverridden)
        }

    @Test
    fun uxDr31AWindowOutsideOneMinuteToThirtyDaysIsRefusedBeforeAnythingIsSent() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            api.calls.clear()

            for (seconds in listOf(59, 2_592_001, 0, -60)) {
                devices.setSilenceWindow(nodeId, seconds)
                runCurrent()

                assertEquals(
                    SilenceWindowFailure(nodeId, SilenceWindowNotice.OutOfRange),
                    devices.ready().silenceWindowFailure,
                    seconds.toString(),
                )
            }
            assertEquals(emptyList(), api.calls)
            // The ends of the range are sent.
            devices.setSilenceWindow(nodeId, 60)
            runCurrent()
            devices.setSilenceWindow(nodeId, 2_592_000)
            runCurrent()
            assertEquals(
                listOf("window a $nodeId 60", "window a $nodeId 2592000"),
                api.calls.filter { it.startsWith("window") },
            )
            assertNull(devices.ready().silenceWindowFailure)
        }

    @Test
    fun uxDr84ARefusedSilenceWindowShowsItsReasonForThatDeviceAndChangesNothing() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            val cases =
                listOf(
                    // The Role changed since the list loaded: "You can't change this on ‹Site›…".
                    ApiFailure.Forbidden to SilenceWindowNotice.Forbidden,
                    ApiFailure.NotFound to SilenceWindowNotice.NotFound,
                    ApiFailure.Validation to SilenceWindowNotice.OutOfRange,
                    ApiFailure.Unreachable to SilenceWindowNotice.Unreachable,
                    ApiFailure.Certificate to SilenceWindowNotice.Certificate,
                    ApiFailure.Unexpected to SilenceWindowNotice.Unexpected,
                )

            for ((failure, notice) in cases) {
                api.windowFailure = failure
                devices.setSilenceWindow(hubId, 600)
                runCurrent()

                assertEquals(SilenceWindowFailure(hubId, notice), devices.ready().silenceWindowFailure, failure.name)
                assertNull(devices.ready().silenceWindowWorkingId)
                assertEquals(
                    300,
                    devices
                        .ready()
                        .hubs
                        .single()
                        .silenceWindowSeconds,
                )
            }

            api.windowFailure = ApiFailure.Forbidden
            devices.setSilenceWindow(hubId, 600)
            runCurrent()
            val snapshot = snapshotOf(devices.state.value)
            assertEquals(hubId, snapshot.silenceWindowFailedId)
            assertEquals("forbidden", snapshot.silenceWindowNotice)
            assertEquals("", snapshot.silenceWindowWorkingId)
        }

    @Test
    fun aSilenceWindowForADeviceThatIsNotListedIsNeverSentAndA401EndsTheSession() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            api.calls.clear()

            devices.setSilenceWindow("nope", 600)
            runCurrent()
            assertEquals(emptyList(), api.calls)

            api.windowFailure = ApiFailure.Unauthorized
            devices.setSilenceWindow(hubId, 600)
            runCurrent()
            assertIs<DevicesState.Idle>(devices.state.value)
        }

    @Test
    fun aSilenceWindowChangeUnderWayBlocksAnotherOne() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            api.calls.clear()

            devices.setSilenceWindow(hubId, 600)
            devices.setSilenceWindow(nodeId, 3_600)
            runCurrent()

            assertEquals(listOf("window a $hubId 600"), api.calls.filter { it.startsWith("window") })
        }

    @Test
    fun aSilenceWindowChangeUnderWayBlocksAMoveAndAnUnassignSoItsAnswerIsNotDropped() =
        runTest {
            seedSilence()
            val devices = devicesFor("Administrator")
            api.calls.clear()
            api.windowFailure = ApiFailure.Forbidden

            devices.setSilenceWindow(hubId, 600)
            devices.unassignNode(nodeId)
            devices.moveNode("7c19000000000001", "l-basil")
            runCurrent()

            assertEquals(listOf("window a $hubId 600"), api.calls)
            assertEquals(
                SilenceWindowFailure(hubId, SilenceWindowNotice.Forbidden),
                devices.ready().silenceWindowFailure,
            )
        }

    @Test
    fun uxDr31TheSilenceWindowIsAWholeNumberOfMinutesHoursOrDaysWithinTheRange() {
        assertEquals(60, SilenceWindow.MIN_SECONDS)
        assertEquals(2_592_000, SilenceWindow.MAX_SECONDS)
        assertEquals(28_800, SilenceWindow.secondsOf("8", SilenceWindowUnit.Hours))
        assertEquals(60, SilenceWindow.secondsOf(" 1 ", SilenceWindowUnit.Minutes))
        assertEquals(2_592_000, SilenceWindow.secondsOf("30", SilenceWindowUnit.Days))
        assertEquals(2_592_000, SilenceWindow.secondsOf("43200", SilenceWindowUnit.Minutes))
        for ((text, unit) in listOf(
            "0" to SilenceWindowUnit.Minutes,
            "31" to SilenceWindowUnit.Days,
            "721" to SilenceWindowUnit.Hours,
            "90.5" to SilenceWindowUnit.Minutes,
            "1,5" to SilenceWindowUnit.Hours,
            "-5" to SilenceWindowUnit.Minutes,
            "" to SilenceWindowUnit.Hours,
            "six" to SilenceWindowUnit.Hours,
            "99999999999999999999" to SilenceWindowUnit.Minutes,
        )) {
            assertNull(SilenceWindow.secondsOf(text, unit), "$text ${unit.key}")
        }
        assertEquals(listOf("minutes", "hours", "days"), SilenceWindowUnit.entries.map { it.key })
        assertEquals(SilenceWindowUnit.Hours, SilenceWindowUnit.fromKey("hours"))
        assertNull(SilenceWindowUnit.fromKey("weeks"))
    }

    @Test
    fun theSilenceWindowIsShownInTheLargestUnitThatHoldsItExactly() {
        assertEquals(SilenceWindowLength(5, SilenceWindowUnit.Minutes), SilenceWindow.lengthOf(300))
        assertEquals(SilenceWindowLength(6, SilenceWindowUnit.Hours), SilenceWindow.lengthOf(21_600))
        assertEquals(SilenceWindowLength(8, SilenceWindowUnit.Hours), SilenceWindow.lengthOf(28_800))
        assertEquals(SilenceWindowLength(90, SilenceWindowUnit.Minutes), SilenceWindow.lengthOf(5_400))
        assertEquals(SilenceWindowLength(2, SilenceWindowUnit.Days), SilenceWindow.lengthOf(172_800))
        assertEquals(SilenceWindowLength(30, SilenceWindowUnit.Days), SilenceWindow.lengthOf(2_592_000))
        // Whole seconds the apps cannot enter are told in minutes, rounded down.
        assertEquals(SilenceWindowLength(1, SilenceWindowUnit.Minutes), SilenceWindow.lengthOf(90))
    }

    @Test
    fun theHubShortNameIsTheFirstFourHexDigitsOfTheDeviceIdInUpperCase() {
        assertEquals("3F2A", DeviceName.short("3f2a9c0d1e4b5a67"))
        assertEquals("9C0D", DeviceName.short("9c0d1e4b5a673f2a"))
        assertEquals("AB", DeviceName.short("ab"))
    }

    @Test
    fun theSnapshotCarriesSilenceAndTheSilenceWindowAsParallelListsForSwift() =
        runTest {
            seedSilence()

            val devices = devicesFor("Owner")
            val snapshot = snapshotOf(devices.state.value)

            assertEquals(listOf("3F2A"), snapshot.hubShortNames)
            assertEquals(listOf("1791269880000"), snapshot.hubSilentSince)
            assertEquals(listOf("12"), snapshot.hubSilenceValues)
            assertEquals(listOf("minutes"), snapshot.hubSilenceUnits)
            assertEquals(listOf("300"), snapshot.hubWindowSeconds)
            assertEquals(listOf("5"), snapshot.hubWindowValues)
            assertEquals(listOf("minutes"), snapshot.hubWindowUnits)
            assertEquals(listOf(false), snapshot.hubWindowOverridden)
            assertEquals(listOf("9C0D", "7C19"), snapshot.nodeShortNames)
            assertEquals(listOf("1791248700000", ""), snapshot.nodeSilentSince)
            assertEquals(listOf("6", ""), snapshot.nodeSilenceValues)
            assertEquals(listOf("hours", ""), snapshot.nodeSilenceUnits)
            assertEquals(listOf("28800", ""), snapshot.nodeWindowSeconds)
            assertEquals(listOf("8", ""), snapshot.nodeWindowValues)
            assertEquals(listOf("hours", ""), snapshot.nodeWindowUnits)
            assertEquals(listOf(true, false), snapshot.nodeWindowOverridden)
            assertTrue(snapshot.canSetSilenceWindow)
            assertEquals(60, snapshot.silenceWindowMinSeconds)
            assertEquals(2_592_000, snapshot.silenceWindowMaxSeconds)
            // A minute tick later the durations have moved and nothing else has.
            val later = snapshotOf(devices.state.value, NOW_MS + 48 * 60_000L)
            assertEquals(listOf("1"), later.hubSilenceValues)
            assertEquals(listOf("hours"), later.hubSilenceUnits)
        }

    // Pause and Resume on Device rows (Story 8.2)

    private val until = "2027-03-01T00:00:00.000Z"
    private val untilMs = 1_803_859_200_000L

    /** A Hub paused by itself, a Node paused by its Site with an end, a Node paused by both, and a live Node. */
    private fun seedPauses() {
        api.devices["a"] =
            listOf(
                hub("3f2a9c0d1e4b5a67", online = true).copy(pausedBy = listOf("device")),
                node("7c19aaaaaaaaaaaa", "beans", "Beans").copy(pausedBy = listOf("site"), pausedUntil = until),
                node("8d2a000000000002", "herbs", "Herbs").copy(pausedBy = listOf("device", "site")),
                node("9c0d1e4b5a673f2a", null, null),
            )
        // The Server's bookkeeping of a Device pause: the row gains or loses its own source.
        pauseApi.onDevice = { deviceId, paused ->
            api.devices["a"] =
                api.devices["a"].orEmpty().map { row ->
                    if (row.id != deviceId) {
                        row
                    } else {
                        val rest = row.pausedBy.orEmpty().filter { it != "device" }
                        val sources = if (paused) listOf("device") + rest else rest
                        row.copy(pausedBy = sources.ifEmpty { null })
                    }
                }
        }
    }

    @Test
    fun uxDr70HubAndNodeRowsCarryTheServersPauseSourcesAndEnd() =
        runTest {
            seedPauses()

            val ready = devicesFor("Administrator").ready()

            assertEquals(listOf(LotPauseSource.Device), ready.hubs.single().pausedBy)
            assertNull(ready.hubs.single().pausedUntilEpochMs)
            assertEquals(
                listOf(
                    listOf(LotPauseSource.Site),
                    listOf(LotPauseSource.Device, LotPauseSource.Site),
                    emptyList(),
                ),
                ready.nodes.map { it.pausedBy },
            )
            assertEquals(listOf(untilMs, null, null), ready.nodes.map { it.pausedUntilEpochMs })
            // An unpaused Device has neither member; a Node on no Lot can be paused too.
            assertEquals(DevicePause.None, ready.nodes.last().pause)
            assertTrue(
                ready.nodes
                    .first()
                    .pause.bySite,
            )
            assertTrue(
                ready.hubs
                    .single()
                    .pause.paused,
            )
        }

    @Test
    fun uxDr84OnlyAdministratorsAndOwnersGetPauseAndResumeOnDeviceRows() =
        runTest {
            assertTrue(DevicesState.canPause(SiteRole.Owner))
            assertTrue(DevicesState.canPause(SiteRole.Administrator))
            assertFalse(DevicesState.canPause(SiteRole.Member))
            assertFalse(DevicesState.Idle.canPause)
            seedPauses()

            val member = devicesFor("Member")

            assertFalse(member.state.value.canPause)
            // The paused state is there for anyone; the controls are not.
            val snapshot = snapshotOf(member.state.value)
            assertEquals(listOf(true, true, false), snapshot.nodePaused)
            assertEquals(listOf(false, false, false), snapshot.nodeCanPause)
            assertEquals(listOf(false, false, false), snapshot.nodeCanResume)
            assertEquals(listOf(false, false, false), snapshot.nodeResumeSiteHint)
            assertEquals(listOf(false), snapshot.hubCanResume)
            member.pause.openForDevice("9c0d1e4b5a673f2a")
            member.pause.resumeDevice("3f2a9c0d1e4b5a67")
            runCurrent()
            assertEquals(PauseState(), member.pause.state.value)
            assertTrue(pauseApi.calls.isEmpty())
        }

    @Test
    fun uxDr70PausingADeviceFromItsRowReadsTheListAgainWithoutDroppingTheRows() =
        runTest {
            seedPauses()
            val devices = devicesFor("Administrator")
            val states = mutableListOf<DevicesState>()
            val watching = backgroundScope.launch { devices.state.collect { states += it } }
            runCurrent()
            val lists = api.calls.count { it == "list a" }

            devices.pause.openForDevice("9c0d1e4b5a673f2a")
            assertEquals(
                "9C0D",
                devices.pause.state.value.sheet!!
                    .target,
            )
            devices.pause.save()
            runCurrent()

            assertEquals(listOf("pauseDevice a 9c0d1e4b5a673f2a open"), pauseApi.calls)
            assertEquals(PauseState(), devices.pause.state.value)
            assertEquals(lists + 1, api.calls.count { it == "list a" })
            assertEquals(
                listOf(LotPauseSource.Device),
                devices
                    .ready()
                    .nodes
                    .last()
                    .pausedBy,
            )
            // The rows stayed while the list was read again.
            assertTrue(states.none { it is DevicesState.Loading })
            watching.cancel()
        }

    @Test
    fun uxDr70ResumingADevicePausedByBothLeavesItPausedByTheSite() =
        runTest {
            seedPauses()
            val devices = devicesFor("Owner")

            devices.pause.resumeDevice("8d2a000000000002")
            runCurrent()

            assertEquals(listOf("resumeDevice a 8d2a000000000002"), pauseApi.calls)
            val node = devices.ready().nodes[1]
            assertEquals(listOf(LotPauseSource.Site), node.pausedBy)
            assertFalse(node.pause.canResume(SiteRole.Owner))
            assertTrue(node.pause.resumeSiteHint(SiteRole.Owner))
            assertTrue(node.pause.canPause(SiteRole.Owner))

            devices.pause.resumeDevice("3f2a9c0d1e4b5a67")
            runCurrent()
            assertFalse(
                devices
                    .ready()
                    .hubs
                    .single()
                    .pause.paused,
            )
        }

    @Test
    fun uxDr84ARefusedPauseOrResumeChangesNoRow() =
        runTest {
            seedPauses()
            val devices = devicesFor("Administrator")
            val before = devices.ready()
            val lists = api.calls.count { it == "list a" }

            pauseApi.failure = ApiFailure.Forbidden
            devices.pause.resumeDevice("3f2a9c0d1e4b5a67")
            runCurrent()

            assertEquals(
                PauseNotice.Forbidden,
                devices.pause.state.value.failure!!
                    .notice,
            )
            assertEquals(
                "Home",
                devices.pause.state.value.failure!!
                    .siteName,
            )
            assertEquals(before, devices.ready())
            assertEquals(lists, api.calls.count { it == "list a" })

            pauseApi.failure = ApiFailure.Validation
            devices.pause.openForDevice("9c0d1e4b5a673f2a")
            devices.pause.setUntil("2020-01-01")
            devices.pause.save()
            runCurrent()
            assertTrue(
                devices.pause.state.value.sheet!!
                    .dateRefused,
            )
            assertEquals(before, devices.ready())
        }

    @Test
    fun uxDr70ADeviceThatIsGoneSaysSoAndTheListIsReadAgain() =
        runTest {
            seedPauses()
            val devices = devicesFor("Administrator")
            val lists = api.calls.count { it == "list a" }
            pauseApi.failure = ApiFailure.NotFound
            api.devices["a"] = api.devices["a"].orEmpty().dropLast(1)

            devices.pause.openForDevice("9c0d1e4b5a673f2a")
            devices.pause.save()
            runCurrent()

            assertEquals(
                PauseNotice.NotFound,
                devices.pause.state.value.sheet!!
                    .notice,
            )
            assertEquals(lists + 1, api.calls.count { it == "list a" })
            assertEquals(2, devices.ready().nodes.size)
        }

    @Test
    fun uxDr70WholeSiteFromADeviceRowPausesTheSite() =
        runTest {
            seedPauses()
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"))

            devices.pause.openForDevice("9c0d1e4b5a673f2a")
            devices.pause.setScope(PauseScope.Site)
            assertEquals(
                "Home",
                devices.pause.state.value.sheet!!
                    .target,
            )
            devices.pause.save()
            runCurrent()

            assertEquals(listOf("pauseSite a open"), pauseApi.calls)
            assertTrue(assertIs<com.escendit.coldframe.core.sites.SitesState.Ready>(sites.state.value).current.paused)
            // The Site's new Pause member leaves the rows in place: only the Role's actions follow the Site.
            assertIs<DevicesState.Ready>(devices.state.value)
        }

    @Test
    fun aPauseElsewhereReadsAShownListAgainAndAListThatIsNotShownNotAtAll() =
        runTest {
            api.failure = ApiFailure.Unreachable
            val (sites, devices) = engines(SiteDto("a", "Home", "Owner"))
            assertIs<DevicesState.Failed>(devices.state.value)
            val lists = api.calls.size

            sites.pauseChanged()
            runCurrent()
            assertEquals(lists, api.calls.size)

            api.failure = null
            devices.load()
            runCurrent()
            sites.pauseChanged()
            runCurrent()
            assertEquals(lists + 2, api.calls.size)
        }

    @Test
    fun uxDr70TheSnapshotCarriesThePausedRowsTheirControlsAndTheSheetForSwift() =
        runTest {
            seedPauses()
            val devices = devicesFor("Administrator")
            val sheet = PauseState(sheet = PauseForm("Home", "9c0d1e4b5a673f2a", PauseScope.Device))

            val snapshot = snapshotOf(devices.state.value, NOW_MS, sheet)

            assertTrue(snapshot.canPause)
            assertEquals(listOf(true), snapshot.hubPaused)
            assertEquals(listOf(false), snapshot.hubPausedBySite)
            assertEquals(listOf("device"), snapshot.hubPausedBy)
            assertEquals(listOf(""), snapshot.hubPausedUntil)
            assertEquals(listOf(false), snapshot.hubCanPause)
            assertEquals(listOf(true), snapshot.hubCanResume)
            assertEquals(listOf(false), snapshot.hubResumeSiteHint)
            assertEquals(listOf(true, true, false), snapshot.nodePaused)
            assertEquals(listOf(true, true, false), snapshot.nodePausedBySite)
            assertEquals(listOf("site", "device,site", ""), snapshot.nodePausedBy)
            assertEquals(listOf(untilMs.toString(), "", ""), snapshot.nodePausedUntil)
            // Paused by the Site only: no Resume, the hint, and Pause stays available.
            assertEquals(listOf(true, false, true), snapshot.nodeCanPause)
            assertEquals(listOf(false, true, false), snapshot.nodeCanResume)
            assertEquals(listOf(true, false, false), snapshot.nodeResumeSiteHint)
            assertTrue(snapshot.pause.sheetOpen)
            assertEquals("9C0D", snapshot.pause.sheetTarget)
            assertFalse(snapshotOf(devices.state.value).pause.sheetOpen)
        }
}
