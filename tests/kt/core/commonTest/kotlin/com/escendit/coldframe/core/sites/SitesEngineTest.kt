package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
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
import kotlin.test.assertNull
import kotlin.test.assertTrue

class FakeSitesApi : SitesApi {
    val sites = mutableListOf<SiteDto>()
    var listFailure: ApiFailure? = null
    var createFailures = ArrayDeque<ApiFailure>()
    val created = mutableListOf<Pair<String, String>>()
    var lists = 0
    var createGate: CompletableDeferred<Unit>? = null

    override suspend fun listSites(): ApiResult<SiteListDto> {
        lists++
        listFailure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(SiteListDto(sites.toList()))
    }

    override suspend fun createSite(
        name: String,
        idempotencyKey: String,
    ): ApiResult<SiteDto> {
        created += name to idempotencyKey
        createGate?.await()
        createFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        val site = SiteDto("new-${created.size}", name, "Owner")
        sites += site
        return ApiResult.Ok(site)
    }
}

class SitesEngineTest {
    private val api = FakeSitesApi()
    private val settings = MapSettings()
    private val choices = DeviceChoices(settings)
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var keys = 0
    private var detected = "Europe/Zurich"

    private val home = SiteDto("a", "Home", "Owner")
    private val allotment = SiteDto("b", "Allotment", "Member")

    private fun TestScope.engine(): SitesEngine =
        SitesEngine(
            api = api,
            choices = choices,
            scope = backgroundScope,
            signIn = signIn,
            detectTimeZone = { detected },
            newKey = { "key-${++keys}" },
            zones = { listOf("America/New_York", "Europe/Zurich", "Pacific/Auckland") },
        )

    private fun TestScope.signedIn(): SitesEngine =
        engine().also {
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
        }

    private fun SitesEngine.form(): CreateSiteForm =
        when (val s = state.value) {
            is SitesState.NeedsSite -> s.form
            is SitesState.Ready -> s.creating ?: error("no form")
            else -> error("no form in $s")
        }

    @Test
    fun staysIdleWhileSignedOut() =
        runTest {
            val engine = engine()
            runCurrent()

            assertEquals(SitesState.Idle, engine.state.value)
            assertEquals(0, api.lists)
        }

    @Test
    fun uxDr23LoadsOnSignInAndKeepsTheServerOrderAndRoles() =
        runTest {
            api.sites += listOf(allotment, home)

            val engine = signedIn()

            val ready = assertIs<SitesState.Ready>(engine.state.value)
            assertEquals(
                listOf(SiteSummary("b", "Allotment", SiteRole.Member), SiteSummary("a", "Home", SiteRole.Owner)),
                ready.sites,
            )
            assertEquals("b", ready.current.id)
            assertNull(ready.creating)
        }

    @Test
    fun uxDr61NoMembershipShowsCreateSiteThatCannotBeCancelled() =
        runTest {
            val engine = signedIn()

            val form = assertIs<SitesState.NeedsSite>(engine.state.value).form
            assertFalse(form.cancellable)
            assertEquals("", form.name)
            assertEquals("key-1", form.idempotencyKey)
            assertEquals(TimeZoneProposal("Europe/Zurich", null, false), form.timeZone)
        }

    @Test
    fun uxDr61CreateSiteSendsTheTrimmedNameWithTheKeyAndLandsOnTheNewSite() =
        runTest {
            val engine = signedIn()

            engine.setName("  Home ")
            engine.confirmTimeZone()
            engine.submit()
            runCurrent()

            assertEquals(listOf("Home" to "key-1"), api.created)
            val ready = assertIs<SitesState.Ready>(engine.state.value)
            assertEquals(SiteSummary("new-1", "Home", SiteRole.Owner), ready.current)
            assertEquals("new-1", choices.currentSiteId)
            assertEquals("Europe/Zurich", choices.timeZone)
        }

    @Test
    fun uxDr61AWorkingSubmitIgnoresASecondPress() =
        runTest {
            val engine = signedIn()
            api.createGate = CompletableDeferred()
            engine.setName("Home")

            engine.submit()
            runCurrent()
            assertTrue(engine.form().working)
            engine.submit()
            engine.setName("Other")
            runCurrent()

            assertEquals("Home", engine.form().name)
            api.createGate?.complete(Unit)
            runCurrent()
            assertEquals(1, api.created.size)
        }

    @Test
    fun uxDr61ABlankNameShowsItsReasonAndSendsNothing() =
        runTest {
            val engine = signedIn()

            engine.setName("   ")
            engine.submit()
            runCurrent()

            assertEquals(NameError.Blank, engine.form().nameError)
            assertEquals(0, api.created.size)
        }

    @Test
    fun uxDr61ANameLongerThan100CharactersAfterTrimmingShowsItsReasonAndSendsNothing() =
        runTest {
            val engine = signedIn()

            engine.setName(" " + "a".repeat(101) + " ")
            engine.submit()
            runCurrent()
            assertEquals(NameError.TooLong, engine.form().nameError)

            engine.setName(" " + "a".repeat(100) + " ")
            assertNull(engine.form().nameError)
            engine.submit()
            runCurrent()
            assertEquals(1, api.created.size)
        }

    @Test
    fun uxDr61AServerValidationErrorIsTheSameFieldError() =
        runTest {
            val engine = signedIn()
            api.createFailures += ApiFailure.Validation

            engine.setName("Home")
            engine.submit()
            runCurrent()

            assertEquals(NameError.Blank, engine.form().nameError)
            assertFalse(engine.form().working)
        }

    @Test
    fun uxDr61A503KeepsTheKeyForTryAgain() =
        runTest {
            val engine = signedIn()
            api.createFailures += ApiFailure.IdentityProviderUnavailable

            engine.setName("Home")
            engine.submit()
            runCurrent()
            assertEquals(SitesNotice.IdentityProviderUnavailable, engine.form().notice)
            assertEquals("key-1", engine.form().idempotencyKey)

            engine.submit()
            runCurrent()

            assertEquals(listOf("Home" to "key-1", "Home" to "key-1"), api.created)
            assertIs<SitesState.Ready>(engine.state.value)
        }

    @Test
    fun uxDr61A422MakesANewKey() =
        runTest {
            val engine = signedIn()
            api.createFailures += ApiFailure.KeyReused

            engine.setName("Home")
            engine.submit()
            runCurrent()
            assertEquals(SitesNotice.KeyReused, engine.form().notice)

            engine.submit()
            runCurrent()

            assertEquals(listOf("Home" to "key-1", "Home" to "key-2"), api.created)
        }

    @Test
    fun uxDr61UnreachableAndCertificateKeepTheKey() =
        runTest {
            val engine = signedIn()
            api.createFailures += listOf(ApiFailure.Unreachable, ApiFailure.Certificate)

            engine.setName("Home")
            engine.submit()
            runCurrent()
            assertEquals(SitesNotice.Unreachable, engine.form().notice)
            assertTrue(SitesNotice.Unreachable.tryAgain)
            engine.submit()
            runCurrent()
            assertEquals(SitesNotice.Certificate, engine.form().notice)
            assertFalse(SitesNotice.Certificate.tryAgain)

            assertEquals(listOf("key-1", "key-1"), api.created.map { it.second })
        }

    @Test
    fun aSuccessThenNewSiteUsesANewKey() =
        runTest {
            val engine = signedIn()
            engine.setName("Home")
            engine.submit()
            runCurrent()

            engine.newSite()

            assertEquals("key-2", engine.form().idempotencyKey)
            assertTrue(engine.form().cancellable)
        }

    @Test
    fun uxDr61ACreatedSiteOpensEvenWhenTheFollowUpListFails() =
        runTest {
            val engine = signedIn()
            assertIs<SitesState.NeedsSite>(engine.state.value)
            api.listFailure = ApiFailure.Unreachable

            engine.setName("Home")
            engine.submit()
            runCurrent()

            val new = SiteSummary("new-1", "Home", SiteRole.Owner)
            assertEquals(SitesState.Ready(listOf(new), new, creating = null), engine.state.value)
            assertEquals("new-1", choices.currentSiteId)
        }

    @Test
    fun signingOutDuringTheFollowUpListKeepsIdle() =
        runTest {
            val gate = CompletableDeferred<Unit>()
            val gated =
                object : SitesApi by api {
                    override suspend fun listSites(): ApiResult<SiteListDto> {
                        if (api.created.isNotEmpty()) gate.await()
                        return api.listSites()
                    }
                }
            val engine =
                SitesEngine(
                    api = gated,
                    choices = choices,
                    scope = backgroundScope,
                    signIn = signIn,
                    detectTimeZone = { detected },
                    newKey = { "key-${++keys}" },
                    zones = { listOf("Europe/Zurich") },
                )
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            engine.setName("Home")
            engine.submit()
            runCurrent()

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            gate.complete(Unit)
            runCurrent()

            assertEquals(SitesState.Idle, engine.state.value)
        }

    @Test
    fun a401WhileCreatingGoesIdle() =
        runTest {
            val engine = signedIn()
            api.createFailures += ApiFailure.Unauthorized

            engine.setName("Home")
            engine.submit()
            runCurrent()

            assertEquals(SitesState.Idle, engine.state.value)
        }

    @Test
    fun uxDr23NewSiteOpensCreateSiteOverTheShellAndCancelReturns() =
        runTest {
            api.sites += home
            val engine = signedIn()

            engine.newSite()
            val ready = assertIs<SitesState.Ready>(engine.state.value)
            assertTrue(ready.creating!!.cancellable)

            engine.cancelNewSite()
            assertNull(assertIs<SitesState.Ready>(engine.state.value).creating)
        }

    @Test
    fun uxDr23CreatingASecondSiteKeepsTheServerOrderAndSwitchesToIt() =
        runTest {
            api.sites += home
            val engine = signedIn()

            engine.newSite()
            engine.setName("Allotment")
            engine.submit()
            runCurrent()

            val ready = assertIs<SitesState.Ready>(engine.state.value)
            assertEquals(listOf("a", "new-1"), ready.sites.map { it.id })
            assertEquals("new-1", ready.current.id)
        }

    @Test
    fun uxDr23SwitchingSitesPersistsPerDevice() =
        runTest {
            api.sites += listOf(home, allotment)
            val engine = signedIn()

            engine.select("b")

            assertEquals("b", assertIs<SitesState.Ready>(engine.state.value).current.id)
            assertEquals("b", DeviceChoices(settings).currentSiteId)
            val again = engine()
            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            assertEquals("b", assertIs<SitesState.Ready>(again.state.value).current.id)
        }

    @Test
    fun uxDr23AStaleCurrentSiteFallsBackToTheFirstListed() =
        runTest {
            api.sites += listOf(allotment, home)
            choices.currentSiteId = "gone"

            val engine = signedIn()

            assertEquals("b", assertIs<SitesState.Ready>(engine.state.value).current.id)
        }

    @Test
    fun selectingAnUnknownSiteChangesNothing() =
        runTest {
            api.sites += home
            val engine = signedIn()

            engine.select("zzz")

            assertEquals("a", assertIs<SitesState.Ready>(engine.state.value).current.id)
            assertNull(choices.currentSiteId)
        }

    @Test
    fun aFailedLoadShowsItsNoticeAndTryAgainLoads() =
        runTest {
            api.listFailure = ApiFailure.Unreachable
            val engine = signedIn()
            assertEquals(SitesState.Failed(SitesNotice.Unreachable), engine.state.value)

            api.listFailure = null
            api.sites += home
            engine.load()
            runCurrent()

            assertIs<SitesState.Ready>(engine.state.value)
        }

    @Test
    fun aCertificateFailureOnLoadHasNoTryAgain() =
        runTest {
            api.listFailure = ApiFailure.Certificate
            val engine = signedIn()

            assertEquals(SitesState.Failed(SitesNotice.Certificate), engine.state.value)
        }

    @Test
    fun a401OnLoadGoesIdle() =
        runTest {
            api.listFailure = ApiFailure.Unauthorized
            val engine = signedIn()

            assertEquals(SitesState.Idle, engine.state.value)
        }

    @Test
    fun signingOutGoesIdleAndDropsALateAnswer() =
        runTest {
            val engine = signedIn()
            api.createGate = CompletableDeferred()
            engine.setName("Home")
            engine.submit()
            runCurrent()

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            api.createGate?.complete(Unit)
            runCurrent()

            assertEquals(SitesState.Idle, engine.state.value)
        }

    @Test
    fun uxDr61TheDetectedZoneIsProposedAndChangeShowsTheList() =
        runTest {
            val engine = signedIn()

            engine.changeTimeZone()
            assertTrue(engine.form().timeZone.changing)
            engine.pickTimeZone("Pacific/Auckland")

            assertEquals(TimeZoneProposal("Europe/Zurich", "Pacific/Auckland", false), engine.form().timeZone)
            assertEquals("Pacific/Auckland", choices.timeZone)
        }

    @Test
    fun uxDr61AnUnknownZoneIsIgnored() =
        runTest {
            val engine = signedIn()

            engine.pickTimeZone("Mars/Olympus")

            assertNull(engine.form().timeZone.chosen)
            assertNull(choices.timeZone)
        }

    @Test
    fun uxDr61DetectionNeverOverwritesAChosenZone() =
        runTest {
            choices.timeZone = "Pacific/Auckland"
            detected = "Europe/Zurich"
            val engine = signedIn()

            assertEquals(TimeZoneProposal("Europe/Zurich", "Pacific/Auckland", false), engine.form().timeZone)
            assertEquals("Pacific/Auckland", engine.form().timeZone.shown)
            engine.confirmTimeZone()
            assertEquals("Pacific/Auckland", choices.timeZone)
        }

    @Test
    fun uxDr61TheZoneIsNotSentWithCreateSite() =
        runTest {
            val engine = signedIn()
            engine.pickTimeZone("Pacific/Auckland")
            engine.setName("Home")
            engine.submit()
            runCurrent()

            assertEquals(listOf("Home" to "key-1"), api.created)
        }
}
