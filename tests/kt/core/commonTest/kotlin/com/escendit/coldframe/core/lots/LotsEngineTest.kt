package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
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

/** An in-memory Server for the Lot calls, per Site, in the order it chooses. */
class FakeLotsApi : LotsApi {
    val lots = mutableMapOf<String, MutableList<LotDto>>()
    val claimed = mutableSetOf<String>()
    val calls = mutableListOf<String>()
    val keys = mutableListOf<String>()
    val failures = ArrayDeque<ApiFailure>()

    /** Fails the next list calls, one each; then [listFailure] decides. */
    val listFailures = ArrayDeque<ApiFailure>()
    var listFailure: ApiFailure? = null
    var listGate: CompletableDeferred<Unit>? = null
    private val byKey = mutableMapOf<String, LotDto>()
    private var next = 0

    override suspend fun listLots(siteId: String): ApiResult<LotListDto> {
        calls += "list $siteId"
        listGate?.await()
        listFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        listFailure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(LotListDto(lots[siteId].orEmpty().toList()))
    }

    override suspend fun createLot(
        siteId: String,
        name: String,
        idempotencyKey: String,
    ): ApiResult<LotDto> {
        calls += "create $siteId $name"
        keys += idempotencyKey
        failures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        byKey[idempotencyKey]?.let { return ApiResult.Ok(it) }
        val lot = LotDto("lot-${++next}", name, "noNode")
        byKey[idempotencyKey] = lot
        lots.getOrPut(siteId) { mutableListOf() } += lot
        return ApiResult.Ok(lot)
    }

    override suspend fun renameLot(
        siteId: String,
        lotId: String,
        name: String,
    ): ApiResult<LotDto> {
        calls += "rename $lotId $name"
        failures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        val list = lots[siteId] ?: return ApiResult.Failed(ApiFailure.NotFound)
        val index = list.indexOfFirst { it.id == lotId }
        if (index < 0) return ApiResult.Failed(ApiFailure.NotFound)
        list[index] = list[index].copy(name = name)
        return ApiResult.Ok(list[index])
    }

    override suspend fun removeLot(
        siteId: String,
        lotId: String,
    ): ApiResult<Unit> {
        calls += "remove $lotId"
        failures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        if (lotId in claimed) return ApiResult.Failed(ApiFailure.LotClaimed)
        val list = lots[siteId] ?: return ApiResult.Failed(ApiFailure.NotFound)
        list.removeAll { it.id == lotId }
        return ApiResult.Ok(Unit)
    }
}

class LotsEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeLotsApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var keys = 0

    private fun site(
        id: String,
        name: String,
        role: String,
    ) = SiteDto(id, name, role)

    private fun TestScope.engines(vararg sites: SiteDto): Pair<SitesEngine, LotsEngine> {
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
        val lots = LotsEngine(api, sitesEngine, MapSettings(), backgroundScope, newKey = { "key-${++keys}" })
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return sitesEngine to lots
    }

    private fun TestScope.lotsFor(role: String = "Owner"): LotsEngine = engines(site("a", "Home", role)).second

    private fun LotsEngine.ready(): LotsState.Ready = assertIs<LotsState.Ready>(state.value)

    private fun seed(vararg names: Pair<String, String>) {
        api.lots["a"] = names.map { (name, status) -> LotDto("id-$name", name, status) }.toMutableList()
    }

    @Test
    fun uxDr20LoadsTheCurrentSitesLotsInTheServerOrderWithoutReSorting() =
        runTest {
            seed("Tomatoes" to "noNode", "Beans" to "unknown", "Herbs" to "ok")

            val lots = lotsFor()

            assertEquals(listOf("Tomatoes", "Beans", "Herbs"), lots.ready().lots.map { it.name })
            assertEquals(listOf(LotStatus.NoNode, LotStatus.Unknown, LotStatus.Ok), lots.ready().lots.map { it.status })
            assertEquals("Home", lots.ready().siteName.draft)
        }

    @Test
    fun uxDr20AnUnknownStatusReadsAsUnknownAndRemovedLotsAreNotShown() =
        runTest {
            api.lots["a"] = mutableListOf(LotDto("x", "Old", "noNode", removed = true), LotDto("y", "New", "later"))

            val lots = lotsFor()

            assertEquals(listOf(LotSummary("y", "New", LotStatus.Unknown)), lots.ready().lots)
        }

    @Test
    fun staysIdleWhileSignedOutAndGoesIdleOnSignOut() =
        runTest {
            val lots =
                LotsEngine(
                    api,
                    SitesEngine(sitesApi, DeviceChoices(MapSettings()), backgroundScope, signIn),
                    MapSettings(),
                    backgroundScope,
                )
            runCurrent()
            assertEquals(LotsState.Idle, lots.state.value)

            signIn.value = SignInState.SignedIn("Simon")
            sitesApi.sites += site("a", "Home", "Owner")
            runCurrent()
            assertIs<LotsState.Ready>(lots.state.value)

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            assertEquals(LotsState.Idle, lots.state.value)
        }

    @Test
    fun uxDr20SwitchingSiteReloadsTheLotsOfTheNewSite() =
        runTest {
            api.lots["a"] = mutableListOf(LotDto("t", "Tomatoes", "noNode"))
            api.lots["b"] = mutableListOf(LotDto("p", "Potatoes", "noNode"))
            val (sites, lots) = engines(site("a", "Home", "Owner"), site("b", "Allotment", "Member"))

            sites.select("b")
            runCurrent()

            assertEquals("b", lots.ready().siteId)
            assertEquals(listOf("Potatoes"), lots.ready().lots.map { it.name })
            assertEquals(
                SiteSettings(canRenameSite = false, canEditLots = false, readOnlyNotice = true),
                lots.ready().settings,
            )
        }

    @Test
    fun anAnswerForTheSiteLeftBehindIsDropped() =
        runTest {
            api.lots["a"] = mutableListOf(LotDto("t", "Tomatoes", "noNode"))
            api.lots["b"] = mutableListOf(LotDto("p", "Potatoes", "noNode"))
            val gate = CompletableDeferred<Unit>()
            api.listGate = gate
            val (sites, lots) = engines(site("a", "Home", "Owner"), site("b", "Allotment", "Member"))

            sites.select("b")
            runCurrent()
            api.listGate = null
            gate.complete(Unit)
            runCurrent()

            assertEquals("b", lots.ready().siteId)
            assertEquals(listOf("Potatoes"), lots.ready().lots.map { it.name })
        }

    @Test
    fun aFailedLoadShowsTheLoadNoticeAndTryAgainReloads() =
        runTest {
            api.listFailure = ApiFailure.Unreachable
            val lots = lotsFor()
            assertEquals(SitesNotice.Unreachable, assertIs<LotsState.Failed>(lots.state.value).notice)

            api.listFailure = null
            seed("Tomatoes" to "noNode")
            lots.load()
            runCurrent()

            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
        }

    @Test
    fun aCertificateFailureHasNoTryAgain() =
        runTest {
            api.listFailure = ApiFailure.Certificate

            val lots = lotsFor()

            val failed = assertIs<LotsState.Failed>(lots.state.value)
            assertEquals(SitesNotice.Certificate, failed.notice)
            assertFalse(failed.notice.tryAgain)
        }

    // UX-DR84 gating

    @Test
    fun uxDr84OnlyAnOwnerRenamesTheSiteAndAdministratorsEditLots() {
        assertEquals(SiteSettings(true, true, false), SiteSettings.of(SiteRole.Owner))
        assertEquals(SiteSettings(false, true, false), SiteSettings.of(SiteRole.Administrator))
        assertEquals(SiteSettings(false, false, true), SiteSettings.of(SiteRole.Member))
    }

    @Test
    fun uxDr84AMemberCannotCreateRenameOrRemoveAndNothingIsSent() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor("Member")

            lots.setNewLotName("Beans")
            lots.createLot()
            lots.startRename("id-Tomatoes")
            lots.askRemove("id-Tomatoes")
            lots.setSiteName("Garden")
            lots.renameSite()
            runCurrent()

            assertEquals(listOf("list a"), api.calls)
            assertTrue(sitesApi.renamed.isEmpty())
            assertNull(lots.ready().renaming)
            assertNull(lots.ready().removing)
        }

    @Test
    fun uxDr84AnAdministratorCannotRenameTheSite() =
        runTest {
            val lots = lotsFor("Administrator")

            lots.setSiteName("Garden")
            lots.renameSite()
            runCurrent()

            assertTrue(sitesApi.renamed.isEmpty())
        }

    // UX-DR74 Rename Site

    @Test
    fun uxDr74AnOwnerRenamesTheSiteAndTheNewNameFollowsFromTheSites() =
        runTest {
            val (sites, lots) = engines(site("a", "Home", "Owner"))

            lots.setSiteName("  Home garden ")
            lots.renameSite()
            runCurrent()

            assertEquals(listOf("a" to "Home garden"), sitesApi.renamed)
            assertEquals("Home garden", lots.ready().site.name)
            assertEquals(SiteNameForm("Home garden", null, false), lots.ready().siteName)
            assertEquals(
                "Home garden",
                (sites.state.value as com.escendit.coldframe.core.sites.SitesState.Ready).current.name,
            )
        }

    @Test
    fun uxDr74AnInvalidSiteNameShowsItsReasonAndSendsNothing() =
        runTest {
            val lots = lotsFor()

            lots.setSiteName("   ")
            lots.renameSite()
            assertEquals(NameError.Blank, lots.ready().siteName.error)
            lots.setSiteName("x".repeat(101))
            lots.renameSite()
            runCurrent()

            assertEquals(NameError.TooLong, lots.ready().siteName.error)
            assertTrue(sitesApi.renamed.isEmpty())
        }

    @Test
    fun uxDr74KeycloakDownOnRenameSiteChangesNothingAndSaysSo() =
        runTest {
            val lots = lotsFor()
            sitesApi.renameFailure = ApiFailure.IdentityProviderUnavailable

            lots.setSiteName("Home garden")
            lots.renameSite()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.RenameSiteUnavailable), lots.ready().notice)
            assertEquals("Home", lots.ready().site.name)
            assertEquals(SiteNameForm("Home garden", null, false), lots.ready().siteName)
        }

    @Test
    fun uxDr84ARenameSite403NamesTheSite() =
        runTest {
            val lots = lotsFor()
            sitesApi.renameFailure = ApiFailure.Forbidden

            lots.setSiteName("Home garden")
            lots.renameSite()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.Forbidden, "Home"), lots.ready().notice)
        }

    // UX-DR74 Create Lot

    @Test
    fun uxDr74CreatingTomatoesThenBeansUsesANewKeyEachTimeAndReloads() =
        runTest {
            val lots = lotsFor("Administrator")

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()
            lots.setNewLotName("Beans")
            lots.createLot()
            runCurrent()

            assertEquals(listOf("key-1", "key-2"), api.keys)
            assertEquals(listOf("Tomatoes", "Beans"), lots.ready().lots.map { it.name })
            assertEquals(listOf(LotStatus.NoNode, LotStatus.NoNode), lots.ready().lots.map { it.status })
            assertEquals(CreateLotForm("", null, false, "key-3"), lots.ready().create)
        }

    @Test
    fun uxDr74AFailedCreateKeepsTheKeyAndARetryCreatesOneLot() =
        runTest {
            val lots = lotsFor()
            api.failures += ApiFailure.Unreachable

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()
            assertEquals(LotsNotice(LotsNoticeKind.Unreachable), lots.ready().notice)
            lots.createLot()
            runCurrent()

            assertEquals(listOf("key-1", "key-1"), api.keys)
            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
            assertNull(lots.ready().notice)
        }

    @Test
    fun uxDr74A503OnCreateLotIsUnexpectedNotASiteRenameAndKeepsTheKey() =
        runTest {
            val lots = lotsFor()
            api.failures += ApiFailure.IdentityProviderUnavailable

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.Unexpected), lots.ready().notice)
            assertEquals("key-1", lots.ready().create.idempotencyKey)
        }

    @Test
    fun uxDr74AReusedKeyGetsANewKeyForTheNextAttempt() =
        runTest {
            val lots = lotsFor()
            api.failures += ApiFailure.KeyReused

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.KeyReused), lots.ready().notice)
            assertEquals("key-2", lots.ready().create.idempotencyKey)
            assertEquals("Tomatoes", lots.ready().create.name)
            lots.createLot()
            runCurrent()
            assertEquals(listOf("key-1", "key-2"), api.keys)
        }

    @Test
    fun uxDr74ABlankLotNameShowsItsReasonAndSendsNothing() =
        runTest {
            val lots = lotsFor()

            lots.setNewLotName("  ")
            lots.createLot()
            runCurrent()

            assertEquals(NameError.Blank, lots.ready().create.error)
            assertTrue(api.keys.isEmpty())
        }

    @Test
    fun uxDr74AServerValidationOnCreateBecomesTheFieldReason() =
        runTest {
            val lots = lotsFor()
            api.failures += ApiFailure.Validation

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()

            assertEquals(NameError.Blank, lots.ready().create.error)
            assertFalse(lots.ready().create.working)
        }

    @Test
    fun uxDr84ACreate403NamesTheSite() =
        runTest {
            val lots = lotsFor("Administrator")
            api.failures += ApiFailure.Forbidden

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.Forbidden, "Home"), lots.ready().notice)
        }

    @Test
    fun aWorkingCreateIgnoresASecondPress() =
        runTest {
            val lots = lotsFor()

            lots.setNewLotName("Tomatoes")
            lots.createLot()
            lots.createLot()
            runCurrent()

            assertEquals(1, api.keys.size)
        }

    // UX-DR74 Rename Lot

    @Test
    fun uxDr74RenameLotOpensWithTheCurrentNameAndReloads() =
        runTest {
            seed("Tomatoes" to "noNode", "Beans" to "noNode")
            val lots = lotsFor()

            lots.startRename("id-Beans")
            assertEquals(RenameLotForm("id-Beans", "Beans", null, false), lots.ready().renaming)
            lots.setRename("Peppers")
            lots.rename()
            runCurrent()

            assertNull(lots.ready().renaming)
            assertEquals(listOf("Tomatoes", "Peppers"), lots.ready().lots.map { it.name })
        }

    @Test
    fun uxDr74ABlankRenameStaysOpenWithItsReason() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor()

            lots.startRename("id-Tomatoes")
            lots.setRename(" ")
            lots.rename()
            runCurrent()

            assertEquals(NameError.Blank, lots.ready().renaming?.error)
            assertFalse(api.calls.any { it.startsWith("rename") })
            lots.cancelRename()
            assertNull(lots.ready().renaming)
        }

    @Test
    fun uxDr74RenamingARemovedLotSaysItNoLongerExistsAndReloads() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor()
            lots.startRename("id-Tomatoes")
            api.lots["a"]!!.clear()

            lots.setRename("Peppers")
            lots.rename()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.LotNotFound), lots.ready().notice)
            assertNull(lots.ready().renaming)
            assertTrue(lots.ready().lots.isEmpty())
        }

    // UX-DR74 Remove Lot

    @Test
    fun uxDr74RemoveAsksFirstAndRemovesOnConfirm() =
        runTest {
            seed("Tomatoes" to "noNode", "Beans" to "noNode")
            val lots = lotsFor("Administrator")

            lots.askRemove("id-Tomatoes")
            assertEquals(RemoveLotConfirmation("id-Tomatoes", "Tomatoes", false), lots.ready().removing)
            assertFalse(api.calls.any { it.startsWith("remove") })
            lots.confirmRemove()
            runCurrent()

            assertNull(lots.ready().removing)
            assertEquals(listOf("Beans"), lots.ready().lots.map { it.name })
        }

    @Test
    fun uxDr74CancelRemoveSendsNothing() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor()

            lots.askRemove("id-Tomatoes")
            lots.cancelRemove()
            runCurrent()

            assertNull(lots.ready().removing)
            assertFalse(api.calls.any { it.startsWith("remove") })
        }

    @Test
    fun uxDr74ALotHoldingANodeIsRefusedNamedAndStaysListed() =
        runTest {
            seed("Tomatoes" to "unknown")
            api.claimed += "id-Tomatoes"
            val lots = lotsFor()

            lots.askRemove("id-Tomatoes")
            lots.confirmRemove()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.LotClaimed, "Tomatoes"), lots.ready().notice)
            assertNull(lots.ready().removing)
            assertEquals(listOf("Tomatoes"), lots.ready().lots.map { it.name })
        }

    @Test
    fun uxDr84ARemove403NamesTheSite() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor()
            api.failures += ApiFailure.Forbidden

            lots.askRemove("id-Tomatoes")
            lots.confirmRemove()
            runCurrent()

            assertEquals(LotsNotice(LotsNoticeKind.Forbidden, "Home"), lots.ready().notice)
        }

    @Test
    fun a401OnAChangeGoesIdle() =
        runTest {
            seed("Tomatoes" to "noNode")
            val lots = lotsFor()
            api.failures += ApiFailure.Unauthorized

            lots.askRemove("id-Tomatoes")
            lots.confirmRemove()
            runCurrent()

            assertEquals(LotsState.Idle, lots.state.value)
        }
}
