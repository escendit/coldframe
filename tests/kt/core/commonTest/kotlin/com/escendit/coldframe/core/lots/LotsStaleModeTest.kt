package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.core.sites.SitesState
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

/**
 * Story 4.7: the engine's clock, its one retry, the last good Lots per Site, stale mode and the
 * two events the shells announce. The network is [FakeLotsApi]; the clock and the settings are
 * in memory.
 */
class LotsStaleModeTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeLotsApi()
    private val settings = MapSettings()
    private var clock = SEVEN
    private val events = mutableListOf<LotsEvent>()
    private var signIn = MutableStateFlow<SignInState>(SignInState.Restoring)

    /** A process: its own engines over the shared Server and the device's [settings]. */
    private fun TestScope.start(
        vararg sites: SiteDto = arrayOf(SiteDto("a", "Home", "Owner")),
    ): Pair<SitesEngine, LotsEngine> {
        if (sitesApi.sites.isEmpty()) sitesApi.sites += sites
        signIn = MutableStateFlow(SignInState.Restoring)
        val sitesEngine =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(settings),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "site-key" },
                zones = { emptyList() },
            )
        val lots = LotsEngine(api, sitesEngine, settings, backgroundScope, newKey = { "key" }, now = { clock })
        backgroundScope.launch { lots.events.collect { events += it } }
        runCurrent()
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return sitesEngine to lots
    }

    private fun LotsEngine.ready(): LotsState.Ready = assertIs<LotsState.Ready>(state.value)

    private fun seed(vararg lots: LotDto) {
        api.lots["a"] = lots.toMutableList()
    }

    private val tomatoes = LotDto("t", "Tomatoes", "needsCalibration", statusSince = "2026-10-06T07:02:00.000Z")
    private val beans = LotDto("b", "Beans", "noNode", statusSince = "2026-10-06T06:00:00.000Z")

    @Test
    fun uxDr77EveryServerFieldOfALotIsMappedAndAbsentOnesStayAbsent() =
        runTest {
            seed(
                LotDto(
                    id = "w",
                    name = "Tomatoes",
                    status = "needsWater",
                    statusSince = "2026-10-06T05:45:00.000Z",
                    lastReadingAt = "2026-10-06T07:02:00Z",
                    moisturePercent = 21.7,
                    lowThresholdPercent = 30.0,
                ),
                LotDto("u", "Beans", "unknown", statusSince = "2026-10-06T01:05:00.000Z", unknownCause = "hub"),
                LotDto(
                    id = "p",
                    name = "Strawberries",
                    status = "paused",
                    statusSince = "2026-10-01T09:00:00.000Z",
                    pausedBy = listOf("device", "site", "later"),
                    pausedUntil = "2026-11-01T00:00:00.000Z",
                ),
                LotDto("n", "Potatoes", "noNode"),
            )

            val lots = start().second.ready().lots

            assertEquals(
                listOf(
                    LotSummary(
                        id = "w",
                        name = "Tomatoes",
                        status = LotStatus.NeedsWater,
                        statusSinceEpochMs = 1_791_265_500_000,
                        lastReadingAtEpochMs = 1_791_270_120_000,
                        moisturePercent = 21.7,
                        lowThresholdPercent = 30.0,
                    ),
                    LotSummary(
                        id = "u",
                        name = "Beans",
                        status = LotStatus.Unknown,
                        statusSinceEpochMs = 1_791_248_700_000,
                        unknownCause = LotUnknownCause.Hub,
                    ),
                    LotSummary(
                        id = "p",
                        name = "Strawberries",
                        status = LotStatus.Paused,
                        statusSinceEpochMs = 1_790_845_200_000,
                        pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site),
                        pausedUntilEpochMs = 1_793_491_200_000,
                    ),
                    LotSummary("n", "Potatoes", LotStatus.NoNode),
                ),
                lots,
            )
        }

    @Test
    fun uxDr24FetchedAtIsTheTimeOfTheLastSuccessfulRefresh() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            assertEquals(SEVEN, lots.ready().fetchedAtEpochMs)

            clock = SEVEN + 5 * MINUTE
            lots.refresh()
            runCurrent()
            assertEquals(SEVEN + 5 * MINUTE, lots.ready().fetchedAtEpochMs)

            clock = SEVEN + 9 * MINUTE
            api.listFailure = ApiFailure.Unreachable
            lots.refresh()
            runCurrent()
            assertEquals(SEVEN + 5 * MINUTE, lots.ready().fetchedAtEpochMs)
        }

    @Test
    fun uxDr79ARefreshThatFailsOnceIsRetriedAndTheLotsStayLive() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailures += ApiFailure.Unreachable

            lots.refresh()
            runCurrent()

            assertEquals(listOf("list a", "list a", "list a"), api.calls)
            assertFalse(lots.ready().stale)
            assertFalse(lots.ready().refreshing)
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr79ARefreshAndItsOneRetryFailingEntersStaleModeAndKeepsTheLots() =
        runTest {
            seed(tomatoes, beans)
            val lots = start().second
            api.listFailure = ApiFailure.Unreachable
            clock = SEVEN + 9 * MINUTE

            lots.refresh()
            runCurrent()

            val ready = lots.ready()
            assertEquals(listOf("list a", "list a", "list a"), api.calls)
            assertEquals(StaleReason.Unreachable, ready.staleReason)
            assertTrue(ready.stale)
            assertTrue(lots.state.value.stale)
            assertFalse(ready.refreshing)
            assertEquals(listOf("Tomatoes", "Beans"), ready.lots.map { it.name })
            assertEquals(SEVEN, ready.fetchedAtEpochMs)
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", SEVEN)), events)
        }

    @Test
    fun uxDr106AnotherFailedRefreshWhileStaleIsNotAnnouncedAgain() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unreachable

            repeat(3) {
                lots.refresh()
                runCurrent()
            }

            assertTrue(lots.ready().stale)
            assertEquals(1, events.size)
        }

    @Test
    fun uxDr79TheFirstSuccessfulRefreshLeavesStaleModeAndSaysLiveAgain() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unreachable
            lots.refresh()
            runCurrent()

            api.listFailure = null
            seed(tomatoes.copy(status = "ok"))
            clock = SEVEN + HOUR
            lots.refresh()
            runCurrent()

            val ready = lots.ready()
            assertNull(ready.staleReason)
            assertEquals(listOf(LotStatus.Ok), ready.lots.map { it.status })
            assertEquals(SEVEN + HOUR, ready.fetchedAtEpochMs)
            assertEquals(listOf(LotsEvent.EnteredStale("a", SEVEN), LotsEvent.LeftStale("a")), events)
        }

    @Test
    fun uxDr106AFirstLoadAndARefreshThatChangesNothingAnnounceNothing() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            val first = lots.ready().lots

            lots.refresh()
            runCurrent()

            assertEquals(first, lots.ready().lots)
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr112ARefreshKeepsTheLotsShownAndMarksThemRefreshing() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate

            lots.refresh()
            runCurrent()

            assertTrue(lots.ready().refreshing)
            assertFalse(lots.ready().stale)
            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
            api.listGate = null
            gate.complete(Unit)
            runCurrent()
            assertFalse(lots.ready().refreshing)
        }

    @Test
    fun uxDr79AFailedReloadAfterAChangeEntersStaleModeInsteadOfBeingSwallowed() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unreachable

            lots.setNewLotName("Peppers")
            lots.createLot()
            runCurrent()

            assertEquals(StaleReason.Unreachable, lots.ready().staleReason)
            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", SEVEN)), events)
        }

    // Only transport failures are served stale

    @Test
    fun uxDr79AServerErrorOrAnUnexpectedAnswerEntersStaleModeLikeNoAnswer() =
        runTest {
            seed(tomatoes)
            val lots = start().second

            for (failure in listOf(ApiFailure.Unexpected, ApiFailure.IdentityProviderUnavailable)) {
                api.listFailure = failure
                lots.refresh()
                runCurrent()
                assertEquals(StaleReason.Unreachable, lots.ready().staleReason, failure.name)
                api.listFailure = null
                lots.refresh()
                runCurrent()
                assertNull(lots.ready().staleReason)
            }
            assertEquals(4, events.size)
        }

    @Test
    fun uxDr79A403Or404OnARefreshOverLiveLotsShowsTheNoticeAndDropsTheSitesKeptLots() =
        runTest {
            for (failure in listOf(ApiFailure.Forbidden, ApiFailure.NotFound)) {
                seed(tomatoes)
                api.listFailure = null
                api.calls.clear()
                val lots = start().second
                assertTrue(settings.hasKey("lots.lastGood.a"))
                api.calls.clear()
                api.listFailure = failure

                lots.refresh()
                runCurrent()

                // Not the caller's to read any more: the notice replaces the Lots; no retry.
                assertEquals(SitesNotice.Unexpected, assertIs<LotsState.Failed>(lots.state.value, failure.name).notice)
                assertEquals(listOf("list a"), api.calls)
                assertFalse(settings.hasKey("lots.lastGood.a"))
                assertTrue(events.isEmpty())
            }
        }

    @Test
    fun uxDr80KeptLotsAreNotShownOnceTheServerRefusesTheSite() =
        runTest {
            seed(tomatoes)
            start()
            api.listFailure = ApiFailure.Forbidden

            val lots = start().second

            assertEquals(SitesNotice.Unexpected, assertIs<LotsState.Failed>(lots.state.value).notice)
            assertFalse(settings.hasKey("lots.lastGood.a"))
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr79StaleLotsGiveWayToTheNoticeWhenTheServerThenRefusesTheSite() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unreachable
            lots.refresh()
            runCurrent()
            assertTrue(lots.ready().stale)

            api.listFailure = ApiFailure.NotFound
            lots.refresh()
            runCurrent()

            assertIs<LotsState.Failed>(lots.state.value)
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", SEVEN)), events)
        }

    @Test
    fun aCertificateFailureIsNeverServedStaleAndNotRetried() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.calls.clear()
            api.listFailure = ApiFailure.Certificate

            lots.refresh()
            runCurrent()

            val failed = assertIs<LotsState.Failed>(lots.state.value)
            assertEquals(SitesNotice.Certificate, failed.notice)
            assertEquals(listOf("list a"), api.calls)
            assertTrue(events.isEmpty())

            // Nor on a cold start with kept Lots.
            val again = start().second
            assertEquals(SitesNotice.Certificate, assertIs<LotsState.Failed>(again.state.value).notice)
            assertTrue(events.isEmpty())
        }

    // Cold start without the Server: the kept Sites keep a current Site

    @Test
    fun uxDr80AColdStartWithoutTheServerShowsTheKeptLotsStaleAndAnnouncesItOnce() =
        runTest {
            seed(tomatoes, beans)
            start()
            events.clear()

            sitesApi.listFailure = ApiFailure.Unreachable
            api.listFailure = ApiFailure.Unreachable
            clock = SEVEN + 3 * HOUR
            val (sites, lots) = start()

            assertTrue(assertIs<SitesState.Ready>(sites.state.value).fromCache)
            val ready = lots.ready()
            assertEquals("Home", ready.site.name)
            assertEquals(listOf("Tomatoes", "Beans"), ready.lots.map { it.name })
            assertEquals(StaleReason.Unreachable, ready.staleReason)
            assertEquals(SEVEN, ready.fetchedAtEpochMs)
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", SEVEN)), events)
        }

    @Test
    fun uxDr79ARefreshAfterSuchAColdStartReadsTheSitesAgainAndLeavesStaleMode() =
        runTest {
            seed(tomatoes)
            start()
            events.clear()
            sitesApi.listFailure = ApiFailure.Unreachable
            api.listFailure = ApiFailure.Unreachable
            val (sites, lots) = start()
            assertTrue(lots.ready().stale)

            // Still away: both reads fail again, nothing new is announced.
            lots.refresh()
            runCurrent()
            assertTrue(lots.ready().stale)
            assertTrue(assertIs<SitesState.Ready>(sites.state.value).fromCache)
            assertEquals(1, events.size)

            sitesApi.listFailure = null
            api.listFailure = null
            sitesApi.sites[0] = SiteDto("a", "Home garden", "Administrator")
            clock = SEVEN + 4 * HOUR
            lots.refresh()
            runCurrent()

            val current = assertIs<SitesState.Ready>(sites.state.value)
            assertFalse(current.fromCache)
            assertEquals("Home garden", current.current.name)
            assertNull(lots.ready().staleReason)
            assertEquals("Home garden", lots.ready().site.name)
            assertEquals(SEVEN + 4 * HOUR, lots.ready().fetchedAtEpochMs)
            assertEquals(listOf(LotsEvent.EnteredStale("a", SEVEN), LotsEvent.LeftStale("a")), events)
        }

    @Test
    fun uxDr80AColdStartWithoutTheServerAndWithoutKeptLotsShowsTheUnreachableNotice() =
        runTest {
            seed(tomatoes)
            start()
            settings.remove("lots.lastGood.a")
            sitesApi.listFailure = ApiFailure.Unreachable
            api.listFailure = ApiFailure.Unreachable

            val lots = start().second

            assertEquals(SitesNotice.Unreachable, assertIs<LotsState.Failed>(lots.state.value).notice)
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr80AColdStartWithoutTheServerAndWithoutKeptSitesShowsTheSitesNotice() =
        runTest {
            sitesApi.listFailure = ApiFailure.Unreachable

            val (sites, lots) = start()

            assertEquals(SitesState.Failed(SitesNotice.Unreachable), sites.state.value)
            assertEquals(LotsState.Idle, lots.state.value)
            assertTrue(api.calls.isEmpty())
        }

    // Nothing kept outlives the session

    @Test
    fun signingOutClearsTheKeptLotsAndSitesSoTheNextUserSeesNoneOfThem() =
        runTest {
            api.lots["a"] = mutableListOf(tomatoes)
            api.lots["b"] = mutableListOf(LotDto("p", "Potatoes", "noNode"))
            val (sites, _) = start(SiteDto("a", "Home", "Owner"), SiteDto("b", "Allotment", "Member"))
            sites.select("b")
            runCurrent()
            assertEquals(3, settings.keys.count { it.startsWith("lots.lastGood.") || it == "sites.lastGood" })

            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            assertTrue(settings.keys.none { it.startsWith("lots.lastGood.") || it == "sites.lastGood" })

            // Another user signs in on the same phone while the Server is away.
            sitesApi.listFailure = ApiFailure.Unreachable
            api.listFailure = ApiFailure.Unreachable
            val (nextSites, nextLots) = start()
            assertEquals(SitesState.Failed(SitesNotice.Unreachable), nextSites.state.value)
            assertEquals(LotsState.Idle, nextLots.state.value)
        }

    @Test
    fun readsInFlightAcrossTheSignOutStoreNothing() =
        runTest {
            seed(tomatoes)
            val (sites, lots) = start()
            val gate = CompletableDeferred<Unit>()
            sitesApi.listGate = gate
            api.listGate = gate
            signIn.value = SignInState.Restoring
            runCurrent()
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            assertTrue(lots.ready().refreshing)

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            gate.complete(Unit)
            runCurrent()

            assertEquals(SitesState.Idle, sites.state.value)
            assertEquals(LotsState.Idle, lots.state.value)
            assertTrue(settings.keys.none { it.startsWith("lots.lastGood.") || it == "sites.lastGood" })
        }

    @Test
    fun aSitesReadInFlightWhenA401EndsTheSessionStoresNothing() =
        runTest {
            seed(tomatoes)
            start()
            // A new start: the kept Sites show while their read is in flight, and the Lots answer 401.
            val gate = CompletableDeferred<Unit>()
            sitesApi.listGate = gate
            api.listFailure = ApiFailure.Unauthorized

            val (sites, lots) = start()
            assertEquals(LotsState.Idle, lots.state.value)
            assertFalse(settings.hasKey("sites.lastGood"))

            gate.complete(Unit)
            runCurrent()

            assertFalse(settings.hasKey("sites.lastGood"))
            assertTrue(settings.keys.none { it.startsWith("lots.lastGood.") })
            assertIs<SitesState.Ready>(sites.state.value).let { assertTrue(it.fromCache) }
        }

    @Test
    fun a401OnTheLotsClearsEverythingKept() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unauthorized

            lots.refresh()
            runCurrent()

            assertEquals(LotsState.Idle, lots.state.value)
            assertTrue(settings.keys.none { it.startsWith("lots.lastGood.") || it == "sites.lastGood" })
        }

    @Test
    fun restoringTheSessionOnAColdStartClearsNothing() =
        runTest {
            seed(tomatoes)
            start()

            // A new process: the session is still Restoring when the engines are built.
            api.listGate = CompletableDeferred()
            val lots = start().second

            assertEquals(StaleReason.Cached, lots.ready().staleReason)
            assertTrue(settings.hasKey("sites.lastGood"))
        }

    @Test
    fun a401OnARefreshIsNotRetriedAndEndsTheSessionWithoutStaleMode() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            api.listFailure = ApiFailure.Unauthorized

            lots.refresh()
            runCurrent()

            assertEquals(LotsState.Idle, lots.state.value)
            assertEquals(listOf("list a", "list a"), api.calls)
            assertTrue(events.isEmpty())
        }

    // Cold start (UX-DR80)

    @Test
    fun uxDr80AColdStartShowsTheCachedLotsInStaleModeUntilTheFirstRefreshLands() =
        runTest {
            seed(tomatoes, beans)
            start()
            events.clear()

            // The app starts again later; the Server has moved on and answers slowly.
            seed(tomatoes.copy(status = "ok"))
            clock = SEVEN + 3 * HOUR
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate
            val lots = start().second

            val cached = lots.ready()
            assertEquals(StaleReason.Cached, cached.staleReason)
            assertTrue(cached.refreshing)
            assertEquals(SEVEN, cached.fetchedAtEpochMs)
            assertEquals(listOf("Tomatoes", "Beans"), cached.lots.map { it.name })
            assertEquals(LotStatus.NeedsCalibration, cached.lots.first().status)
            assertEquals(1_791_270_120_000, cached.lots.first().statusSinceEpochMs)

            api.listGate = null
            gate.complete(Unit)
            runCurrent()

            val live = lots.ready()
            assertNull(live.staleReason)
            assertFalse(live.refreshing)
            assertEquals(SEVEN + 3 * HOUR, live.fetchedAtEpochMs)
            assertEquals(listOf(LotStatus.Ok), live.lots.map { it.status })
            // Neither "Can't reach your Server" nor "Live again": it was a normal first load.
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr80AColdStartWithCachedLotsAndNoServerEntersStaleModeOnce() =
        runTest {
            seed(tomatoes)
            start()
            events.clear()

            api.listFailure = ApiFailure.Unreachable
            clock = SEVEN + 3 * HOUR
            val lots = start().second

            assertEquals(StaleReason.Unreachable, lots.ready().staleReason)
            assertEquals(SEVEN, lots.ready().fetchedAtEpochMs)
            assertEquals(listOf<LotsEvent>(LotsEvent.EnteredStale("a", SEVEN)), events)

            api.listFailure = null
            lots.refresh()
            runCurrent()
            assertEquals(listOf(LotsEvent.EnteredStale("a", SEVEN), LotsEvent.LeftStale("a")), events)
        }

    @Test
    fun uxDr80WithoutCachedLotsTheSiteIsLoadingUntilTheFirstAnswer() =
        runTest {
            seed(tomatoes)
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate

            val lots = start().second

            assertEquals("Home", assertIs<LotsState.Loading>(lots.state.value).site.name)
            assertFalse(lots.state.value.stale)
            api.listGate = null
            gate.complete(Unit)
            runCurrent()
            assertNull(lots.ready().staleReason)
            assertTrue(events.isEmpty())
        }

    @Test
    fun uxDr80WithoutCachedLotsAndNoServerTheUnreachableNoticeShowsAfterOneRetry() =
        runTest {
            api.listFailure = ApiFailure.Unreachable

            val lots = start().second

            assertEquals(SitesNotice.Unreachable, assertIs<LotsState.Failed>(lots.state.value).notice)
            assertEquals(listOf("list a", "list a"), api.calls)
            assertTrue(events.isEmpty())

            // Try again goes back to Loading, never to stale mode: there is nothing to show.
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate
            lots.refresh()
            runCurrent()
            assertIs<LotsState.Loading>(lots.state.value)
            api.listGate = null
            gate.complete(Unit)
            runCurrent()
            assertIs<LotsState.Failed>(lots.state.value)
        }

    @Test
    fun uxDr80TheLastGoodLotsAreKeptPerSite() =
        runTest {
            api.lots["a"] = mutableListOf(tomatoes)
            api.lots["b"] = mutableListOf(LotDto("p", "Potatoes", "noNode"))
            val (sites, lots) = start(SiteDto("a", "Home", "Owner"), SiteDto("b", "Allotment", "Member"))
            sites.select("b")
            runCurrent()

            api.listFailure = ApiFailure.Unreachable
            sites.select("a")
            runCurrent()

            assertEquals("a", lots.ready().siteId)
            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
            assertEquals(StaleReason.Unreachable, lots.ready().staleReason)
            assertEquals(
                setOf("lots.lastGood.a", "lots.lastGood.b"),
                settings.keys.filter { it.startsWith("lots.lastGood.") }.toSet(),
            )
        }

    @Test
    fun uxDr80CachedLotsThatCannotBeReadAreDroppedAndTheSiteLoads() =
        runTest {
            settings.putString("lots.lastGood.a", "{not json")
            api.listFailure = ApiFailure.Unreachable

            val lots = start().second

            assertIs<LotsState.Failed>(lots.state.value)
            assertFalse(settings.hasKey("lots.lastGood.a"))
        }

    @Test
    fun anAnswerOfAnOlderReadIsDroppedWhenANewerOneIsOnItsWay() =
        runTest {
            seed(tomatoes)
            val lots = start().second
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate
            lots.refresh()
            runCurrent()

            // A second read starts while the first still waits; only its answer counts.
            api.listGate = null
            seed(tomatoes, beans)
            lots.refresh()
            runCurrent()
            assertEquals(listOf("Tomatoes", "Beans"), lots.ready().lots.map { it.name })
            assertFalse(lots.ready().refreshing)

            seed()
            gate.complete(Unit)
            runCurrent()
            assertEquals(listOf("Tomatoes", "Beans"), lots.ready().lots.map { it.name })
        }

    private companion object {
        const val MINUTE = 60_000L
        const val HOUR = 60 * MINUTE

        /** 2026-10-06T07:02:00Z. */
        const val SEVEN = 1_791_270_120_000L
    }
}
