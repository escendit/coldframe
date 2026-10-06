package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DeviceListItemDto
import com.escendit.coldframe.core.api.SiteDto
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

/** An in-memory Server for the Devices list, per Site. */
class FakeDevicesApi : DevicesApi {
    val devices = mutableMapOf<String, List<DeviceListItemDto>>()
    val calls = mutableListOf<String>()
    var failure: ApiFailure? = null
    var gate: CompletableDeferred<Unit>? = null

    override suspend fun listDevices(siteId: String): ApiResult<DeviceListDto> {
        calls += "list $siteId"
        val answer = failure?.let { ApiResult.Failed(it) } ?: ApiResult.Ok(DeviceListDto(devices[siteId].orEmpty()))
        gate?.await()
        return answer
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
}
