package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DevicePauseDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SitePauseDto
import com.escendit.coldframe.core.signin.SignInState
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
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * An in-memory Server for the four Pause calls. It keeps the Site's Pause in [sites], as the Server's
 * identity projection does, so the next `GET /sites` shows it; [paused] is what it did to a Device.
 */
class FakePauseApi(
    private val sites: FakeSitesApi? = null,
) : PauseApi {
    val calls = mutableListOf<String>()
    var failure: ApiFailure? = null
    var gate: CompletableDeferred<Unit>? = null

    /** The Device's own Pause after each call: the Device ID to its `until`, or to `null` when resumed. */
    val paused = mutableListOf<Pair<String, String?>>()

    /** Called with the Device ID after a Device pause or resume the Server accepted, to move its rows. */
    var onDevice: (deviceId: String, pausedByDevice: Boolean) -> Unit = { _, _ -> }

    private suspend fun <T> answer(ok: () -> T): ApiResult<T> {
        gate?.await()
        failure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(ok())
    }

    private fun store(
        siteId: String,
        paused: Boolean,
        until: String?,
    ): SitePauseDto {
        val ends = until?.let { "${it}T00:00:00.000Z" }
        sites?.let { fake ->
            val index = fake.sites.indexOfFirst { it.id == siteId }
            if (index >= 0) fake.sites[index] = fake.sites[index].copy(paused = paused, pausedUntil = ends)
        }
        return SitePauseDto(paused, ends)
    }

    override suspend fun pauseSite(
        siteId: String,
        until: String?,
    ): ApiResult<SitePauseDto> {
        calls += "pauseSite $siteId ${until ?: "open"}"
        return answer { store(siteId, paused = true, until = until) }
    }

    override suspend fun resumeSite(siteId: String): ApiResult<SitePauseDto> {
        calls += "resumeSite $siteId"
        return answer { store(siteId, paused = false, until = null) }
    }

    override suspend fun pauseDevice(
        siteId: String,
        deviceId: String,
        until: String?,
    ): ApiResult<DevicePauseDto> {
        calls += "pauseDevice $siteId $deviceId ${until ?: "open"}"
        return answer {
            paused += deviceId to until
            onDevice(deviceId, true)
            DevicePauseDto(listOf("device"), until?.let { "${it}T00:00:00.000Z" })
        }
    }

    override suspend fun resumeDevice(
        siteId: String,
        deviceId: String,
    ): ApiResult<DevicePauseDto> {
        calls += "resumeDevice $siteId $deviceId"
        return answer {
            paused += deviceId to null
            onDevice(deviceId, false)
            DevicePauseDto(emptyList())
        }
    }
}

/** Story 8.2: the Pause sheet and the one-tap Resume, as every surface has them. */
class PauseEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakePauseApi(sitesApi)
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var changes = 0
    private lateinit var sites: SitesEngine

    private val node = "7c19aaaaaaaaaaaa"

    private fun TestScope.start(
        role: String = "Administrator",
        vararg more: SiteDto,
    ): PauseEngine {
        sitesApi.sites += SiteDto("a", "Home garden", role)
        sitesApi.sites += more
        sites =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(MapSettings()),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "k" },
                zones = { emptyList() },
            )
        sites.onPauseChanged { changes++ }
        val engine = PauseEngine(api, sites, backgroundScope)
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return engine
    }

    private fun PauseEngine.sheet(): PauseForm = assertNotNull(state.value.sheet)

    private fun current(): SiteSummary = assertIs<SitesState.Ready>(sites.state.value).current

    @Test
    fun uxDr46FromTheSiteMenuTheSheetOffersWholeSiteOnlyAndNamesTheSite() =
        runTest {
            val engine = start()

            engine.openForSite()

            val sheet = engine.sheet()
            assertEquals(listOf(PauseScope.Site), sheet.scopes)
            assertEquals(PauseScope.Site, sheet.scope)
            assertEquals("Home garden", sheet.target)
            assertNull(sheet.until)
            assertNull(sheet.deviceName)
            // There is no Device here: This Device cannot be chosen.
            engine.setScope(PauseScope.Device)
            assertEquals(PauseScope.Site, engine.sheet().scope)
            assertTrue(api.calls.isEmpty())
        }

    @Test
    fun uxDr46FromADeviceTheSheetSelectsThisDeviceAndThePrimaryFollowsTheScope() =
        runTest {
            val engine = start()

            engine.openForDevice(node)

            assertEquals(listOf(PauseScope.Device, PauseScope.Site), engine.sheet().scopes)
            assertEquals(PauseScope.Device, engine.sheet().scope)
            assertEquals("7C19", engine.sheet().target)
            engine.setScope(PauseScope.Site)
            assertEquals("Home garden", engine.sheet().target)
            engine.setScope(PauseScope.Device)
            assertEquals("7C19", engine.sheet().target)
        }

    @Test
    fun uxDr70SavingWithoutADatePausesTheSiteClosesTheSheetAndReadsTheSitesAgain() =
        runTest {
            val engine = start()
            val listsBefore = sitesApi.lists
            engine.openForSite()

            engine.save()
            runCurrent()

            assertEquals(listOf("pauseSite a open"), api.calls)
            assertEquals(PauseState(), engine.state.value)
            assertTrue(current().paused)
            assertNull(current().pausedUntilEpochMs)
            // A read made right after the answer shows it: the Sites are read again, and so are the lists.
            assertEquals(listsBefore + 1, sitesApi.lists)
            assertEquals(1, changes)
        }

    @Test
    fun uxDr70SavingWithADateSendsThePickedCalendarDateAsItIs() =
        runTest {
            val engine = start()
            engine.openForSite()

            engine.setUntil(2027, 3, 1)
            assertEquals("2027-03-01", engine.sheet().until)
            assertEquals(PauseDate(2027, 3, 1), engine.sheet().untilDate)
            engine.save()
            runCurrent()

            assertEquals(listOf("pauseSite a 2027-03-01"), api.calls)
            assertTrue(current().paused)
            // The end is the Server's instant; the client does not derive it from the date.
            assertEquals(1_803_859_200_000L, current().pausedUntilEpochMs)
        }

    @Test
    fun uxDr46TheUntilDateIsACalendarDateOrEmpty() =
        runTest {
            val engine = start()
            engine.openForSite()

            engine.setUntil("2027-03-01")
            assertEquals("2027-03-01", engine.sheet().until)
            // Not a date of the calendar: ignored.
            engine.setUntil("2027-02-30")
            engine.setUntil("1 March")
            engine.setUntil(2027, 13, 1)
            assertEquals("2027-03-01", engine.sheet().until)
            engine.setUntil(2028, 2, 29)
            assertEquals("2028-02-29", engine.sheet().until)
            // "Leave empty to pause until someone resumes."
            engine.setUntil(null)
            assertNull(engine.sheet().until)
            engine.setUntil("2027-03-01")
            engine.setUntil("")
            assertNull(engine.sheet().until)
        }

    @Test
    fun uxDr70ThisDevicePausesTheDeviceAndWholeSiteFromADevicePausesTheSite() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            engine.setUntil("2026-11-01")

            engine.save()
            runCurrent()

            assertEquals(listOf("pauseDevice a $node 2026-11-01"), api.calls)
            assertNull(engine.state.value.sheet)
            assertFalse(current().paused)
            assertEquals(1, changes)

            // Whole Site from a Device context pauses the Site and leaves the Device's own source alone.
            engine.openForDevice(node)
            engine.setScope(PauseScope.Site)
            engine.save()
            runCurrent()

            assertEquals("pauseSite a open", api.calls.last())
            assertEquals(2, api.calls.size)
            assertTrue(current().paused)
        }

    @Test
    fun uxDr46ARefusedDateKeepsTheSheetOpenAndMarksTheDateField() =
        runTest {
            val engine = start()
            engine.openForSite()
            engine.setUntil("2020-01-01")
            api.failure = ApiFailure.Validation

            engine.save()
            runCurrent()

            val sheet = engine.sheet()
            assertTrue(sheet.dateRefused)
            assertFalse(sheet.working)
            assertEquals("2020-01-01", sheet.until)
            assertFalse(current().paused)
            assertEquals(0, changes)
            // Picking another date takes the mark away.
            engine.setUntil("2027-03-01")
            assertNull(engine.sheet().notice)
        }

    @Test
    fun uxDr84ARoleLostMeanwhileKeepsTheSheetOpenWithTheForbiddenNotice() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            engine.setUntil("2027-03-01")
            api.failure = ApiFailure.Forbidden

            engine.save()
            runCurrent()

            assertEquals(PauseNotice.Forbidden, engine.sheet().notice)
            assertFalse(engine.sheet().dateRefused)
            assertEquals("Home garden", engine.sheet().siteName)
            assertEquals("2027-03-01", engine.sheet().until)
            assertFalse(PauseNotice.Forbidden.tryAgain)
        }

    // A 400 is the date's only when a date was sent: without one there is no field to mark.
    @Test
    fun uxDr46A400OnAPauseWithoutADateMarksNoDateFieldAndIsUnexpected() =
        runTest {
            val engine = start()
            engine.openForSite()
            api.failure = ApiFailure.Validation

            engine.save()
            runCurrent()

            val sheet = engine.sheet()
            assertEquals(PauseNotice.Unexpected, sheet.notice)
            assertFalse(sheet.dateRefused)
            assertFalse(snapshotOf(engine.state.value).sheetDateRefused)

            // With a date, the same answer is the date's.
            engine.setUntil("2020-01-01")
            engine.save()
            runCurrent()

            assertTrue(engine.sheet().dateRefused)
        }

    // A Resume carries no date, so its 400 never reads "Choose a date after today."
    @Test
    fun uxDr70A400OnAResumeIsUnexpectedAndNeverADateRefusal() =
        runTest {
            val engine = start()
            api.failure = ApiFailure.Validation

            engine.resumeSite()
            runCurrent()
            assertEquals(PauseNotice.Unexpected, assertNotNull(engine.state.value.failure).notice)

            engine.resumeDevice(node)
            runCurrent()
            assertEquals(PauseNotice.Unexpected, assertNotNull(engine.state.value.failure).notice)
        }

    // A 404 on a Site request is the Site that is gone, not a Device: the Sites are read again.
    @Test
    fun uxDr70A404OnASiteRequestSaysTheSiteIsGoneAndReadsTheSitesAgain() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            engine.setScope(PauseScope.Site)
            api.failure = ApiFailure.NotFound
            val lists = sitesApi.lists

            engine.save()
            runCurrent()

            assertEquals(PauseNotice.SiteGone, engine.sheet().notice)
            assertEquals("siteGone", snapshotOf(engine.state.value).sheetNotice)
            assertFalse(PauseNotice.SiteGone.tryAgain)
            assertEquals(lists + 1, sitesApi.lists)
            assertEquals(1, changes)

            engine.cancel()
            engine.resumeSite()
            runCurrent()

            val failure = assertNotNull(engine.state.value.failure)
            assertEquals(PauseNotice.SiteGone, failure.notice)
            assertEquals("Home garden", failure.siteName)
            assertEquals(lists + 2, sitesApi.lists)
            assertEquals(2, changes)

            // A Device that is gone reads no Sites.
            engine.resumeDevice(node)
            runCurrent()
            assertEquals(PauseNotice.NotFound, assertNotNull(engine.state.value.failure).notice)
            assertEquals(lists + 2, sitesApi.lists)
        }

    @Test
    fun uxDr70AnUnreachableServerKeepsTheEntriesAndTryAgainSendsThemAgain() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            engine.setScope(PauseScope.Site)
            engine.setUntil("2027-03-01")
            api.failure = ApiFailure.Unreachable

            engine.save()
            runCurrent()

            val sheet = engine.sheet()
            assertEquals(PauseNotice.Unreachable, sheet.notice)
            assertTrue(PauseNotice.Unreachable.tryAgain)
            assertEquals(PauseScope.Site, sheet.scope)
            assertEquals("2027-03-01", sheet.until)

            api.failure = null
            engine.save()
            runCurrent()

            assertEquals(listOf("pauseSite a 2027-03-01", "pauseSite a 2027-03-01"), api.calls)
            assertNull(engine.state.value.sheet)
        }

    @Test
    fun uxDr70AnUnknownDeviceSaysSoAndTheListsAreReadAgain() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            api.failure = ApiFailure.NotFound

            engine.save()
            runCurrent()

            assertEquals(PauseNotice.NotFound, engine.sheet().notice)
            assertEquals(1, changes)
            assertEquals(
                listOf(PauseNotice.Certificate, PauseNotice.Unexpected),
                listOf(ApiFailure.Certificate, ApiFailure.Unexpected).map { failure ->
                    api.failure = failure
                    engine.save()
                    runCurrent()
                    engine.sheet().notice
                },
            )
            assertFalse(PauseNotice.Certificate.tryAgain)
        }

    @Test
    fun uxDr46WhileTheRequestRunsTheSheetIsWorkingAndCancelAndEditsAreIgnored() =
        runTest {
            val engine = start()
            engine.openForDevice(node)
            api.gate = CompletableDeferred()

            engine.save()
            runCurrent()

            assertTrue(engine.sheet().working)
            assertTrue(engine.state.value.workingOn(node))
            assertFalse(engine.state.value.workingOnSite)
            engine.cancel()
            engine.setScope(PauseScope.Site)
            engine.setUntil("2027-03-01")
            engine.save()
            engine.resumeSite()
            runCurrent()
            assertEquals(PauseScope.Device, engine.sheet().scope)
            assertNull(engine.sheet().until)
            assertEquals(1, api.calls.size)

            api.gate?.complete(Unit)
            runCurrent()
            assertNull(engine.state.value.sheet)
        }

    @Test
    fun uxDr46CancelClosesTheSheetAndSendsNothing() =
        runTest {
            val engine = start()
            engine.openForSite()
            engine.setUntil("2027-03-01")

            engine.cancel()

            assertEquals(PauseState(), engine.state.value)
            assertTrue(api.calls.isEmpty())
            // Opened again, the sheet starts empty.
            engine.openForSite()
            assertNull(engine.sheet().until)
        }

    @Test
    fun uxDr70ResumeTheSiteIsOneCallWithoutASheet() =
        runTest {
            sitesApi.sites +=
                SiteDto("a", "Home garden", "Owner", paused = true, pausedUntil = "2027-03-01T00:00:00.000Z")
            val engine = startExisting()
            assertTrue(current().paused)

            engine.resumeSite()
            assertEquals(PauseTarget(PauseScope.Site), engine.state.value.resuming)
            assertTrue(engine.state.value.workingOnSite)
            runCurrent()

            assertEquals(listOf("resumeSite a"), api.calls)
            assertEquals(PauseState(), engine.state.value)
            assertFalse(current().paused)
            assertNull(current().pausedUntilEpochMs)
            assertEquals(1, changes)
        }

    private fun TestScope.startExisting(): PauseEngine {
        sites =
            SitesEngine(
                api = sitesApi,
                choices = DeviceChoices(MapSettings()),
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "k" },
                zones = { emptyList() },
            )
        sites.onPauseChanged { changes++ }
        val engine = PauseEngine(api, sites, backgroundScope)
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return engine
    }

    @Test
    fun uxDr70ResumeADeviceIsOneCallAndAFailureIsANoticeWithTryAgain() =
        runTest {
            val engine = start()
            api.failure = ApiFailure.Unreachable

            engine.resumeDevice(node)
            assertTrue(engine.state.value.workingOn(node))
            runCurrent()

            assertEquals(
                PauseFailure(PauseTarget(PauseScope.Device, node), PauseNotice.Unreachable, "Home garden"),
                engine.state.value.failure,
            )
            assertNull(engine.state.value.resuming)
            assertEquals(0, changes)

            api.failure = null
            engine.retry()
            runCurrent()

            assertEquals(listOf("resumeDevice a $node", "resumeDevice a $node"), api.calls)
            assertEquals(PauseState(), engine.state.value)
            assertEquals(1, changes)
        }

    @Test
    fun uxDr84AForbiddenResumeNamesTheSiteAndHasNoTryAgain() =
        runTest {
            val engine = start()
            api.failure = ApiFailure.Forbidden

            engine.resumeSite()
            runCurrent()

            val failure = assertNotNull(engine.state.value.failure)
            assertEquals(PauseNotice.Forbidden, failure.notice)
            assertEquals("Home garden", failure.siteName)
            engine.retry()
            runCurrent()
            assertEquals(1, api.calls.size)
        }

    @Test
    fun uxDr84AMemberHasNoPauseAndNoResume() =
        runTest {
            val engine = start(role = "Member")

            engine.openForSite()
            engine.openForDevice(node)
            engine.resumeSite()
            engine.resumeDevice(node)
            runCurrent()

            assertEquals(PauseState(), engine.state.value)
            assertTrue(api.calls.isEmpty())
            assertFalse(DevicePause.allowed(SiteRole.Member))
            assertTrue(DevicePause.allowed(SiteRole.Administrator))
            assertTrue(DevicePause.allowed(SiteRole.Owner))
        }

    @Test
    fun switchingTheSiteClosesTheSheetAndAnAnswerForTheEarlierSiteIsDropped() =
        runTest {
            val engine = start("Owner", SiteDto("b", "Allotment", "Owner"))
            engine.openForSite()
            api.gate = CompletableDeferred()
            engine.save()
            runCurrent()

            sites.select("b")
            runCurrent()
            assertEquals(PauseState(), engine.state.value)

            api.gate?.complete(Unit)
            runCurrent()
            assertEquals(PauseState(), engine.state.value)
            assertFalse(current().paused)
            assertEquals(0, changes)
        }

    @Test
    fun a401AndASignOutCloseTheSheet() =
        runTest {
            val engine = start()
            engine.openForSite()
            api.failure = ApiFailure.Unauthorized

            engine.save()
            runCurrent()
            assertEquals(PauseState(), engine.state.value)

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            engine.openForSite()
            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            assertEquals(PauseState(), engine.state.value)
        }

    @Test
    fun uxDr70WhatARowOffersFollowsTheServersSourcesAndTheRole() {
        val admin = SiteRole.Administrator
        val none = DevicePause.None
        val own = DevicePause.of(listOf("device"), 5L)
        val bySite = DevicePause.of(listOf("site"), null)
        val both = DevicePause.of(listOf("device", "site"), null)

        assertFalse(none.paused)
        assertTrue(none.canPause(admin))
        assertFalse(none.canResume(admin))
        assertFalse(none.resumeSiteHint(admin))

        assertTrue(own.paused && own.byDevice && !own.bySite)
        assertEquals(5L, own.untilEpochMs)
        assertFalse(own.canPause(admin))
        assertTrue(own.canResume(admin))
        assertFalse(own.resumeSiteHint(admin))

        // Paused by the Site only: no Resume for the Device, the hint instead, and Pause stays available.
        assertTrue(bySite.paused && bySite.bySite)
        assertTrue(bySite.canPause(admin))
        assertFalse(bySite.canResume(admin))
        assertTrue(bySite.resumeSiteHint(admin))

        // Both: Resume lifts the Device's own source; it stays paused by the Site. The hint is for a
        // Device that has no Resume of its own.
        assertFalse(both.canPause(admin))
        assertTrue(both.canResume(admin))
        assertFalse(both.resumeSiteHint(admin))

        // A Member has none of the controls, whatever the Device's state.
        for (pause in listOf(none, own, bySite, both)) {
            assertFalse(pause.canPause(SiteRole.Member))
            assertFalse(pause.canResume(SiteRole.Member))
            assertFalse(pause.resumeSiteHint(SiteRole.Member))
        }
        // A source this client does not know is left out; an end without a Pause is dropped.
        assertEquals(DevicePause.None, DevicePause.of(listOf("garden"), 5L))
        assertEquals(DevicePause.None, DevicePause.of(null, null))
    }

    @Test
    fun uxDr46TheSnapshotCarriesTheSheetAndTheResumeForSwift() =
        runTest {
            val engine = start()
            val closed = snapshotOf(engine.state.value)
            assertFalse(closed.sheetOpen)
            assertEquals(emptyList(), closed.sheetScopes)
            assertEquals(
                listOf("", "", "", ""),
                listOf(closed.sheetScope, closed.sheetUntil, closed.sheetNotice, closed.failureNotice),
            )

            engine.openForDevice(node)
            engine.setUntil(2027, 3, 1)
            api.failure = ApiFailure.Validation
            engine.save()
            runCurrent()

            val refused = snapshotOf(engine.state.value)
            assertTrue(refused.sheetOpen)
            assertEquals(listOf("device", "site"), refused.sheetScopes)
            assertEquals("device", refused.sheetScope)
            assertEquals("7C19", refused.sheetTarget)
            assertEquals("7C19", refused.sheetDeviceName)
            assertEquals(node, refused.sheetDeviceId)
            assertEquals("Home garden", refused.sheetSiteName)
            assertEquals("2027-03-01", refused.sheetUntil)
            assertEquals(
                listOf(2027, 3, 1),
                listOf(refused.sheetUntilYear, refused.sheetUntilMonth, refused.sheetUntilDay),
            )
            assertTrue(refused.sheetDateRefused)
            // The refused date marks its field; it is not a second notice.
            assertEquals("", refused.sheetNotice)

            api.failure = ApiFailure.Unreachable
            engine.save()
            runCurrent()
            val unreachable = snapshotOf(engine.state.value)
            assertFalse(unreachable.sheetDateRefused)
            assertEquals("unreachable", unreachable.sheetNotice)
            assertTrue(unreachable.sheetNoticeTryAgain)

            engine.cancel()
            api.failure = ApiFailure.Forbidden
            engine.resumeDevice(node)
            val running = snapshotOf(engine.state.value)
            assertEquals("device", running.resumingScope)
            assertEquals(node, running.resumingDeviceId)
            runCurrent()

            val failed = snapshotOf(engine.state.value)
            assertEquals("", failed.resumingScope)
            assertEquals("device", failed.failureScope)
            assertEquals(node, failed.failureDeviceId)
            assertEquals("forbidden", failed.failureNotice)
            assertFalse(failed.failureTryAgain)
            assertEquals("Home garden", failed.failureSiteName)
        }
}
