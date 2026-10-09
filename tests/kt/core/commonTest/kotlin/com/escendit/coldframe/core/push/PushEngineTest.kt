package com.escendit.coldframe.core.push

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.RegisterPushDeviceRequestDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesCache
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.core.sites.SitesState
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

/** An in-memory Server for the push registrations of one User. */
class FakePushApi : PushApi {
    val registrations = mutableMapOf<String, RegisterPushDeviceRequestDto>()
    val calls = mutableListOf<String>()
    val puts = mutableListOf<Pair<String, RegisterPushDeviceRequestDto>>()

    /** Fail the next registrations, one each. */
    val registerFailures = ArrayDeque<ApiFailure>()
    var removeFailure: ApiFailure? = null
    var registerGate: CompletableDeferred<Unit>? = null
    var removeGate: CompletableDeferred<Unit>? = null

    override suspend fun registerPushDevice(
        installationId: String,
        request: RegisterPushDeviceRequestDto,
    ): ApiResult<Unit> {
        calls += "put $installationId"
        puts += installationId to request
        registerGate?.await()
        registerFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        registrations[installationId] = request
        return ApiResult.Ok(Unit)
    }

    override suspend fun removePushDevice(installationId: String): ApiResult<Unit> {
        calls += "delete $installationId"
        removeGate?.await()
        removeFailure?.let { return ApiResult.Failed(it) }
        registrations -= installationId
        return ApiResult.Ok(Unit)
    }
}

/**
 * Story 6.5: the push engine of the core. Permission state and the one prompt (UX-DR122), the notice of a denied
 * permission (UX-DR88), the registration of the device's token (UX-DR115) and where a tap leads (UX-DR120).
 */
class PushEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakePushApi()
    private val settings = MapSettings()
    private val choices = DeviceChoices(settings)
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private val lots = MutableStateFlow<LotsState>(LotsState.Idle)
    private lateinit var sites: SitesEngine

    private val home = SiteDto("a", "Home garden", "Owner")
    private val allotment = SiteDto("b", "Allotment", "Member")

    private fun TestScope.engine(
        vararg held: SiteDto = arrayOf(home),
        platform: PushPlatform = PushPlatform.Fcm,
    ): PushEngine {
        sitesApi.sites += held
        sites =
            SitesEngine(
                api = sitesApi,
                choices = choices,
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { "Europe/Zurich" },
                newKey = { "k" },
                zones = { listOf("Europe/Zurich") },
            )
        return PushEngine(
            api = api,
            sites = sites,
            lots = lots,
            choices = choices,
            platform = platform,
            scope = backgroundScope,
            signIn = signIn,
            newInstallationId = { "inst-1" },
        ).also { runCurrent() }
    }

    private fun TestScope.signedIn(
        vararg held: SiteDto = arrayOf(home),
        platform: PushPlatform = PushPlatform.Fcm,
    ): PushEngine =
        engine(*held, platform = platform).also {
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
        }

    private fun lotsOf(
        site: SiteDto,
        vararg lots: Pair<String, String>,
        staleReason: StaleReason? = null,
        refreshing: Boolean = false,
    ) = LotsState.Ready(
        site = SiteSummary(site.id, site.name, SiteRole.fromServer(site.role)),
        lots = lots.map { (id, name) -> LotSummary(id, name, LotStatus.Ok) },
        siteName = SiteNameForm(site.name, null, working = false),
        create = CreateLotForm("", null, working = false, idempotencyKey = "key"),
        renaming = null,
        removing = null,
        notice = null,
        staleReason = staleReason,
        refreshing = refreshing,
    )

    private fun alert(
        site: String = "a",
        lot: String? = "lot-1",
        kind: String = "alert",
    ): Map<String, String> =
        buildMap {
            put("kind", kind)
            put("siteId", site)
            if (lot != null) put("lotId", lot)
            put("alertId", "alert-1")
            put("collapseId", "c1")
        }

    private fun summary(site: String = "a") = mapOf("kind" to "summary", "siteId" to site, "collapseId" to "c2")

    private fun current(): String = assertIs<SitesState.Ready>(sites.state.value).current.id

    // Permission and the one prompt

    @Test
    fun uxDr122TheWhyLineAndThePromptAreDueOnTheFirstSiteOverview() =
        runTest {
            val engine = engine()
            engine.reportPermission(OsPermission.NotDetermined)

            // Signed out, and signed in without a Site on screen yet: nothing to ask on.
            assertFalse(engine.state.value.promptDue)

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()

            assertTrue(engine.state.value.promptDue)
            assertEquals(PushPermission.Unknown, engine.state.value.permission)
            assertFalse(engine.state.value.noticeVisible)
        }

    @Test
    fun uxDr122NoPromptWithoutASiteToLandOn() =
        runTest {
            val engine = signedIn(*emptyArray())
            engine.reportPermission(OsPermission.NotDetermined)

            assertIs<SitesState.NeedsSite>(sites.state.value)
            assertFalse(engine.state.value.promptDue)

            // The creator lands on the overview right after Create Site.
            sites.setName("Home")
            sites.submit()
            runCurrent()

            assertTrue(engine.state.value.promptDue)
        }

    @Test
    fun uxDr122ThePromptIsAskedOncePerDeviceAndAllowingItEndsIt() =
        runTest {
            val engine = signedIn()
            engine.reportPermission(OsPermission.NotDetermined)

            engine.promptAnswered(OsPermission.Granted)

            assertFalse(engine.state.value.promptDue)
            assertEquals(PushPermission.Granted, engine.state.value.permission)
            assertTrue(choices.notificationPermissionAsked)
        }

    @Test
    fun uxDr122UxDr88DenyingThePromptLeadsToTheNoticeAndIsNeverAskedAgain() =
        runTest {
            val engine = signedIn()
            engine.reportPermission(OsPermission.NotDetermined)

            engine.promptAnswered(OsPermission.Denied)

            assertFalse(engine.state.value.promptDue)
            assertEquals(PushPermission.Denied, engine.state.value.permission)
            assertTrue(engine.state.value.noticeVisible)

            // Android cannot tell "never asked" from "denied": once asked, not granted is denied.
            engine.reportPermission(OsPermission.NotDetermined)

            assertFalse(engine.state.value.promptDue)
            assertTrue(engine.state.value.noticeVisible)
        }

    @Test
    fun uxDr122TheAnswerOutlivesTheSessionAndTheProcess() =
        runTest {
            val first = signedIn()
            first.reportPermission(OsPermission.NotDetermined)
            first.promptAnswered(OsPermission.Denied)
            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            // A new process on the same device, and another User signing in.
            val next =
                PushEngine(api, sites, lots, choices, PushPlatform.Fcm, backgroundScope, signIn, { "inst-2" })
            signIn.value = SignInState.SignedIn("Ana")
            runCurrent()
            next.reportPermission(OsPermission.NotDetermined)

            assertFalse(next.state.value.promptDue)
            assertTrue(next.state.value.noticeVisible)
        }

    @Test
    fun uxDr122AndroidBelow13HasNoPromptAndCountsAsGranted() =
        runTest {
            val engine = signedIn()

            // The shell reports "granted" where the OS has nothing to ask.
            engine.reportPermission(OsPermission.Granted)

            assertFalse(engine.state.value.promptDue)
            assertFalse(engine.state.value.noticeVisible)
            assertEquals(PushPermission.Granted, engine.state.value.permission)
            assertFalse(choices.notificationPermissionAsked)
        }

    @Test
    fun uxDr88NothingShowsBeforeTheShellHasReportedThePermission() =
        runTest {
            val engine = signedIn()

            assertEquals(PushState(PushPermission.Unknown, promptDue = false, route = null), engine.state.value)
        }

    @Test
    fun uxDr88ADeniedOrRevokedPermissionShowsTheNoticeUntilTheNextForegroundFindsItGranted() =
        runTest {
            val engine = signedIn()
            engine.reportPermission(OsPermission.Granted)
            assertFalse(engine.state.value.noticeVisible)

            // Revoked in the OS settings; the shell reports on every foreground.
            engine.reportPermission(OsPermission.Denied)

            assertTrue(engine.state.value.noticeVisible)
            assertFalse(engine.state.value.promptDue)

            engine.reportPermission(OsPermission.Granted)

            assertFalse(engine.state.value.noticeVisible)
        }

    // Registration

    @Test
    fun uxDr115TheTokenIsRegisteredAfterSignIn() =
        runTest {
            val engine = engine()

            engine.tokenReceived("token-1")
            runCurrent()

            assertEquals(emptyList(), api.calls)

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()

            assertEquals(listOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-1")), api.puts)
            assertEquals("inst-1", choices.installationId)
        }

    @Test
    fun uxDr115ATokenThatArrivesWhileSignedInIsRegisteredAtOnce() =
        runTest {
            val engine = signedIn()

            engine.tokenReceived("token-1")
            runCurrent()

            assertEquals(mapOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-1")), api.registrations)
        }

    @Test
    fun uxDr115ARotatedTokenReplacesTheRegistrationOfTheSameInstallation() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()

            engine.tokenReceived("token-2")
            runCurrent()

            assertEquals(listOf("put inst-1", "put inst-1"), api.calls)
            assertEquals(mapOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-2")), api.registrations)
        }

    @Test
    fun uxDr115AnUnchangedRegistrationIsNotSentAgain() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()

            // The OS hands the same token over again, and the app comes to the front.
            engine.tokenReceived("token-1")
            engine.reportPermission(OsPermission.Granted)
            runCurrent()

            assertEquals(listOf("put inst-1"), api.calls)
        }

    @Test
    fun uxDr115TheInstallationIdIsMadeOnceAndKept() =
        runTest {
            choices.installationId = "kept"
            val engine = signedIn()

            engine.tokenReceived("token-1")
            runCurrent()

            assertEquals(listOf("put kept"), api.calls)
            assertEquals("kept", choices.installationId)
        }

    @Test
    fun uxDr115AnApnsTokenGoesWithItsEnvironmentAndAnFcmTokenWithNone() =
        runTest {
            val engine = signedIn(platform = PushPlatform.Apns)

            engine.tokenReceived("6f1d", ApnsEnvironment.Sandbox)
            runCurrent()

            assertEquals(listOf("inst-1" to RegisterPushDeviceRequestDto("apns", "6f1d", "sandbox")), api.puts)
        }

    @Test
    fun uxDr115AnApnsTokenWithoutAnEnvironmentAndABlankTokenAreNotSent() =
        runTest {
            val engine = signedIn(platform = PushPlatform.Apns)

            engine.tokenReceived("6f1d")
            engine.tokenReceived(" ", ApnsEnvironment.Production)
            runCurrent()

            assertEquals(emptyList(), api.calls)
        }

    @Test
    fun uxDr115AnFcmRegistrationNeverCarriesAnEnvironment() =
        runTest {
            val engine = signedIn()

            engine.tokenReceived("token-1", ApnsEnvironment.Production)
            runCurrent()

            assertEquals(listOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-1")), api.puts)
        }

    @Test
    fun uxDr115ARegistrationThatDidNotArriveIsSentAgainOnTheNextForeground() =
        runTest {
            val engine = signedIn()
            api.registerFailures += ApiFailure.Unreachable

            engine.tokenReceived("token-1")
            runCurrent()

            assertEquals(emptyMap(), api.registrations)

            engine.reportPermission(OsPermission.Granted)
            runCurrent()

            assertEquals(mapOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-1")), api.registrations)
        }

    @Test
    fun uxDr115ATokenTheServerRefusedIsNotSentAgainUntilItChanges() =
        runTest {
            val engine = signedIn()
            api.registerFailures += ApiFailure.Validation

            engine.tokenReceived("bad")
            runCurrent()
            engine.reportPermission(OsPermission.Granted)
            runCurrent()

            assertEquals(listOf("put inst-1"), api.calls)

            engine.tokenReceived("good")
            runCurrent()

            assertEquals(mapOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "good")), api.registrations)
        }

    @Test
    fun uxDr115ATokenThatChangesWhileARegistrationIsOnItsWayIsSentNext() =
        runTest {
            val engine = signedIn()
            api.registerGate = CompletableDeferred()
            engine.tokenReceived("token-1")
            runCurrent()

            engine.tokenReceived("token-2")
            runCurrent()
            assertEquals(1, api.puts.size)
            api.registerGate?.complete(Unit)
            runCurrent()

            assertEquals(mapOf("inst-1" to RegisterPushDeviceRequestDto("fcm", "token-2")), api.registrations)
        }

    @Test
    fun uxDr115TheNextUserOnThisPhoneRegistersTheSameTokenUnderTheirOwnSession() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()
            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            signIn.value = SignInState.SignedIn("Ana")
            runCurrent()

            assertEquals(listOf("put inst-1", "put inst-1"), api.calls)
            assertTrue(engine.state.value.route == null)
        }

    // Sign-out

    @Test
    fun uxDr115SignOutRemovesTheRegistrationOfThisInstallation() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()

            engine.unregister()

            assertEquals(listOf("put inst-1", "delete inst-1"), api.calls)
            assertEquals(emptyMap(), api.registrations)
        }

    @Test
    fun uxDr115SignOutRemovesARegistrationAnEarlierStartOfTheAppMade() =
        runTest {
            // This process has not been handed a token yet, but the installation registered before.
            choices.installationId = "kept"
            val engine = signedIn()

            engine.unregister()

            assertEquals(listOf("delete kept"), api.calls)
        }

    @Test
    fun uxDr115AnInstallationThatNeverRegisteredHasNothingToRemove() =
        runTest {
            val engine = signedIn()

            engine.unregister()

            assertEquals(emptyList(), api.calls)
        }

    @Test
    fun uxDr115ARemovalThatFailsIsLeftAtThat() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()
            api.removeFailure = ApiFailure.Unreachable

            engine.unregister()
            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            assertEquals(listOf("put inst-1", "delete inst-1"), api.calls)
        }

    @Test
    fun uxDr115ARegistrationOnItsWayNeverLandsAfterTheRemoval() =
        runTest {
            val engine = signedIn()
            api.registerGate = CompletableDeferred()
            engine.tokenReceived("token-1")
            runCurrent()

            engine.unregister()
            api.registerGate?.complete(Unit)
            runCurrent()

            assertEquals(emptyMap(), api.registrations)
            assertEquals("delete inst-1", api.calls.last())
        }

    @Test
    fun uxDr115NothingIsRegisteredBetweenTheRemovalAndTheEndOfTheSession() =
        runTest {
            val engine = signedIn()
            engine.tokenReceived("token-1")
            runCurrent()
            engine.unregister()

            engine.tokenReceived("token-2")
            engine.reportPermission(OsPermission.Granted)
            runCurrent()

            assertEquals(listOf("put inst-1", "delete inst-1"), api.calls)
        }

    // Tap routing

    @Test
    fun uxDr120ATapOnAnAlertOfTheCurrentSiteOpensItsLot() =
        runTest {
            val engine = signedIn()
            lots.value = lotsOf(home, "lot-1" to "Tomatoes")
            runCurrent()

            engine.opened(alert())
            runCurrent()

            assertEquals(PushRoute.LotDetail("a", "lot-1", "Tomatoes"), engine.state.value.route)
        }

    @Test
    fun uxDr120ATapOnAReminderOpensItsLotToo() =
        runTest {
            val engine = signedIn()
            lots.value = lotsOf(home, "lot-1" to "Tomatoes")

            engine.opened(alert(kind = "reminder"))
            runCurrent()

            assertEquals(PushRoute.LotDetail("a", "lot-1", "Tomatoes"), engine.state.value.route)
        }

    @Test
    fun uxDr120ATapOnAnAlertOfAnotherSiteSwitchesToItThenOpensTheLot() =
        runTest {
            val engine = signedIn(home, allotment)
            lots.value = lotsOf(home, "lot-1" to "Tomatoes")
            runCurrent()

            engine.opened(alert(site = "b", lot = "lot-9"))
            runCurrent()

            // The Site is switched, and the route waits for that Site's Lots.
            assertEquals("b", current())
            assertEquals("b", choices.currentSiteId)
            assertNull(engine.state.value.route)

            lots.value = LotsState.Loading(SiteSummary("b", "Allotment", SiteRole.Member))
            runCurrent()
            assertNull(engine.state.value.route)

            lots.value = lotsOf(allotment, "lot-9" to "Beans")
            runCurrent()

            assertEquals(PushRoute.LotDetail("b", "lot-9", "Beans"), engine.state.value.route)
        }

    @Test
    fun uxDr120ATapOnASummaryOpensTheOverviewOfItsSite() =
        runTest {
            val engine = signedIn(home, allotment)

            engine.opened(summary(site = "b"))
            runCurrent()

            assertEquals("b", current())
            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120AKindThisAppDoesNotKnowOpensTheOverviewOfItsSite() =
        runTest {
            val engine = signedIn(home, allotment)

            engine.opened(alert(site = "b", kind = "health"))
            runCurrent()

            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120OnAColdStartTheRouteWaitsUntilTheSitesAreLoaded() =
        runTest {
            val engine = engine(home, allotment)
            sitesApi.listGate = CompletableDeferred()

            // The tap starts the app: the session is still restoring.
            engine.opened(summary(site = "b"))
            runCurrent()
            assertNull(engine.state.value.route)

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            assertIs<SitesState.Loading>(sites.state.value)
            assertNull(engine.state.value.route)

            sitesApi.listGate?.complete(Unit)
            runCurrent()

            assertEquals("b", current())
            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120OnAColdStartASiteKeptOnThisDeviceIsOpenedWithoutWaitingForTheServer() =
        runTest {
            SitesCache(settings).store(listOf(home, allotment))
            val engine = engine(home, allotment)
            sitesApi.listGate = CompletableDeferred()
            engine.opened(summary(site = "b"))

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()

            // Stale mode: the Server has not answered, the kept Sites show.
            assertTrue(assertIs<SitesState.Ready>(sites.state.value).fromCache)
            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120OnAColdStartASiteThisDeviceDoesNotKnowYetWaitsForTheServersList() =
        runTest {
            SitesCache(settings).store(listOf(home))
            val engine = engine(home, allotment)
            sitesApi.listGate = CompletableDeferred()
            engine.opened(summary(site = "b"))

            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            assertNull(engine.state.value.route)
            assertEquals("a", current())

            sitesApi.listGate?.complete(Unit)
            runCurrent()

            assertEquals("b", current())
            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120AnUnknownSiteEndsOnTheOverviewOfTheCurrentSite() =
        runTest {
            val engine = signedIn(home, allotment)

            engine.opened(alert(site = "gone"))
            runCurrent()

            assertEquals("a", current())
            assertEquals(PushRoute.Overview("a"), engine.state.value.route)
        }

    @Test
    fun uxDr120AnUnknownLotEndsOnTheOverviewOfItsSite() =
        runTest {
            val engine = signedIn(home, allotment)
            lots.value = lotsOf(home, "lot-1" to "Tomatoes")
            runCurrent()

            engine.opened(alert(site = "b", lot = "gone"))
            runCurrent()
            lots.value = lotsOf(allotment, "lot-9" to "Beans")
            runCurrent()

            assertEquals("b", current())
            assertEquals(PushRoute.Overview("b"), engine.state.value.route)
        }

    @Test
    fun uxDr120ALotMissingFromLotsKeptOnThisDeviceWaitsForTheReadThatIsRunning() =
        runTest {
            val engine = signedIn()
            lots.value = lotsOf(home, "lot-1" to "Tomatoes", staleReason = StaleReason.Cached, refreshing = true)
            runCurrent()

            engine.opened(alert(lot = "lot-2"))
            runCurrent()
            assertNull(engine.state.value.route)

            lots.value = lotsOf(home, "lot-1" to "Tomatoes", "lot-2" to "Peppers")
            runCurrent()

            assertEquals(PushRoute.LotDetail("a", "lot-2", "Peppers"), engine.state.value.route)
        }

    @Test
    fun uxDr120ALotKeptOnThisDeviceOpensInStaleMode() =
        runTest {
            val engine = signedIn()
            lots.value = lotsOf(home, "lot-1" to "Tomatoes", staleReason = StaleReason.Unreachable)

            engine.opened(alert())
            runCurrent()

            assertEquals(PushRoute.LotDetail("a", "lot-1", "Tomatoes"), engine.state.value.route)
        }

    @Test
    fun uxDr120LotsThatCouldNotBeReadEndOnTheOverview() =
        runTest {
            val engine = signedIn()
            lots.value = LotsState.Failed(SiteSummary("a", "Home garden", SiteRole.Owner), SitesNotice.Unreachable)

            engine.opened(alert())
            runCurrent()

            assertEquals(PushRoute.Overview("a"), engine.state.value.route)
        }

    @Test
    fun uxDr120TheRouteIsHandedOverOnce() =
        runTest {
            val engine = signedIn()
            engine.opened(summary())
            runCurrent()

            engine.routeHandled()

            assertNull(engine.state.value.route)

            // Later changes of the Sites and the Lots do not bring it back.
            lots.value = lotsOf(home, "lot-1" to "Tomatoes")
            runCurrent()
            assertNull(engine.state.value.route)
        }

    @Test
    fun uxDr120ALaterTapReplacesOneThatIsStillWaiting() =
        runTest {
            val engine = signedIn(home, allotment)

            engine.opened(alert(site = "b", lot = "lot-9"))
            runCurrent()
            engine.opened(summary(site = "a"))
            runCurrent()

            assertEquals("a", current())
            assertEquals(PushRoute.Overview("a"), engine.state.value.route)
        }

    @Test
    fun uxDr120ATapWhileSignedOutLeadsNowhere() =
        runTest {
            val engine = engine()
            engine.opened(summary())

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            signIn.value = SignInState.SignedIn("Ana")
            runCurrent()

            assertNull(engine.state.value.route)
        }

    @Test
    fun uxDr120ATapWithoutAnySiteLeadsNowhere() =
        runTest {
            val engine = signedIn(*emptyArray())

            engine.opened(summary())
            runCurrent()
            sites.setName("Home")
            sites.submit()
            runCurrent()

            assertNull(engine.state.value.route)
        }

    @Test
    fun uxDr120AMessageThatIsNotOursLeadsNowhere() =
        runTest {
            val engine = signedIn()

            engine.opened(mapOf("google.message_id" to "1"))
            runCurrent()

            assertNull(engine.state.value.route)
        }
}
