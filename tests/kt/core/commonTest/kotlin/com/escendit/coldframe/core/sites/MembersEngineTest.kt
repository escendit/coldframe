package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteMemberDto
import com.escendit.coldframe.core.api.SiteMemberListDto
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

/** An in-memory Server for the two Members calls. An invitation adds no Member: they appear once accepted. */
class FakeMembersApi : MembersApi {
    val members = mutableListOf<SiteMemberDto>()
    val invited = mutableListOf<Triple<String, String, String>>()
    var lists = 0
    var listFailure: ApiFailure? = null
    var inviteFailures = ArrayDeque<ApiFailure>()
    var gate: CompletableDeferred<Unit>? = null

    override suspend fun listMembers(siteId: String): ApiResult<SiteMemberListDto> {
        lists++
        listFailure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(SiteMemberListDto(members.toList()))
    }

    override suspend fun invite(
        siteId: String,
        email: String,
        role: String,
    ): ApiResult<Unit> {
        invited += Triple(siteId, email, role)
        gate?.await()
        inviteFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(Unit)
    }
}

/** Story 9.1: Members, the Member tiles and the Invite form, for an Owner only. */
class MembersEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeMembersApi()
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private lateinit var sites: SitesEngine

    private val simon = SiteMemberDto("u-simon", "Simon Novak", "Owner", "simon@example.com")
    private val ana = SiteMemberDto("u-ana", "", "Member", "ana@example.com")

    private fun TestScope.start(
        role: String = "Owner",
        vararg more: SiteDto,
    ): MembersEngine {
        sitesApi.sites += SiteDto("a", "Home garden", role)
        sitesApi.sites += more
        api.members += listOf(simon, ana)
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
        val engine = MembersEngine(api, sites, backgroundScope, currentUserId = { "u-simon" })
        signIn.value = SignInState.SignedIn("Simon")
        runCurrent()
        return engine
    }

    private fun TestScope.loaded(role: String = "Owner"): MembersEngine =
        start(role).also {
            it.load()
            runCurrent()
        }

    private fun MembersEngine.ready(): MembersState.Ready = assertIs<MembersState.Ready>(state.value)

    @Test
    fun uxDr76MembersIsHiddenForAnAdministratorAndAMemberAndSendsNothing() =
        runTest {
            for (role in listOf("Administrator", "Member")) {
                sitesApi.sites.clear()
                api.members.clear()
                val engine = start(role)

                engine.load()
                engine.setEmail("ana@example.com")
                engine.sendInvite()
                runCurrent()

                assertEquals(MembersState.Hidden, engine.state.value)
                assertFalse(engine.canManageMembers)
                assertFalse(snapshotOf(engine.state.value).canManageMembers)
            }
            assertEquals(0, api.lists)
            assertTrue(api.invited.isEmpty())
        }

    @Test
    fun uxDr51AnOwnerSeesOneTilePerMembershipInTheServerOrderWithInitialsAndYou() =
        runTest {
            val engine = loaded()

            val ready = engine.ready()
            assertTrue(engine.canManageMembers)
            assertEquals("Home garden", ready.siteName)
            assertEquals(listOf("u-simon", "u-ana"), ready.members.map { it.userId })
            assertEquals(listOf("SN", "A"), ready.members.map { it.initials })
            assertEquals(listOf("Simon Novak", "ana@example.com"), ready.members.map { it.shownName })
            assertEquals(listOf(SiteRole.Owner, SiteRole.Member), ready.members.map { it.role })
            assertEquals(listOf(true, false), ready.members.map { it.you })
            assertEquals(InviteForm(), ready.form)
        }

    @Test
    fun uxDr52TheRoleChoiceStartsOnMemberAndOffersMemberAdministratorOwner() =
        runTest {
            val engine = loaded()

            assertEquals(SiteRole.Member, engine.ready().form.role)
            assertEquals(listOf(SiteRole.Member, SiteRole.Administrator, SiteRole.Owner), InviteForm.roles)
            engine.setRole(SiteRole.Administrator)
            assertEquals(SiteRole.Administrator, engine.ready().form.role)
            val snapshot = snapshotOf(engine.state.value)
            assertEquals("administrator", snapshot.role)
            assertEquals(listOf("member", "administrator", "owner"), snapshot.roles)
        }

    @Test
    fun uxDr52SendInviteClearsTheFormNamesTheEmailAndLeavesTheMembersUnchanged() =
        runTest {
            val engine = loaded()
            engine.setEmail("  ana.b@example.com ")
            engine.setRole(SiteRole.Administrator)

            engine.sendInvite()
            runCurrent()

            assertEquals(listOf(Triple("a", "ana.b@example.com", "Administrator")), api.invited)
            val ready = engine.ready()
            assertEquals(InviteForm(), ready.form)
            assertEquals(MembersNotice.Sent, ready.notice)
            assertEquals("ana.b@example.com", ready.sentTo)
            assertEquals(listOf("u-simon", "u-ana"), ready.members.map { it.userId })
            // The list is not read again: the invitee appears only once they accepted.
            assertEquals(1, api.lists)
        }

    @Test
    fun uxDr52TheButtonWorksWhileSendingAndIgnoresEdits() =
        runTest {
            val engine = loaded()
            api.gate = CompletableDeferred()
            engine.setEmail("ana.b@example.com")

            engine.sendInvite()
            runCurrent()
            engine.setEmail("other@example.com")
            engine.sendInvite()
            runCurrent()

            assertTrue(engine.ready().form.working)
            assertEquals("ana.b@example.com", engine.ready().form.email)
            assertEquals(1, api.invited.size)
            api.gate?.complete(Unit)
            runCurrent()
            assertFalse(engine.ready().form.working)
        }

    @Test
    fun uxDr52AnEmptyOrInvalidEmailShowsOneLineUnderTheFieldAndSendsNothing() =
        runTest {
            val engine = loaded()

            engine.sendInvite()
            assertEquals(InviteEmailError.Blank, engine.ready().form.emailError)
            for (bad in listOf(
                "ana",
                "ana@",
                "@example.com",
                "a@b@example.com",
                "ana @example.com",
                "a,b@example.com",
            )) {
                engine.setEmail(bad)
                assertNull(engine.ready().form.emailError)
                engine.sendInvite()
                assertEquals(InviteEmailError.Invalid, engine.ready().form.emailError, bad)
            }
            engine.setEmail("a".repeat(250) + "@b.ch")
            engine.sendInvite()
            assertEquals(InviteEmailError.Invalid, engine.ready().form.emailError)
            runCurrent()
            assertTrue(api.invited.isEmpty())
        }

    @Test
    fun uxDr52AServerValidationMarksTheEmailField() =
        runTest {
            val engine = loaded()
            api.inviteFailures += ApiFailure.Validation
            engine.setEmail("ana@example")

            engine.sendInvite()
            runCurrent()

            val ready = engine.ready()
            assertEquals(InviteEmailError.Invalid, ready.form.emailError)
            assertEquals("ana@example", ready.form.email)
            assertNull(ready.notice)
        }

    @Test
    fun uxDr52AlreadyInvitedKeepsTheValuesAndSaysSo() =
        runTest {
            val engine = loaded()
            api.inviteFailures += ApiFailure.AlreadyInvited
            engine.setEmail("ana@example.com")
            engine.setRole(SiteRole.Owner)

            engine.sendInvite()
            runCurrent()

            val ready = engine.ready()
            assertEquals(MembersNotice.AlreadyInvited, ready.notice)
            assertFalse(ready.notice!!.tryAgain)
            assertEquals(InviteForm("ana@example.com", SiteRole.Owner), ready.form)
        }

    @Test
    fun uxDr52AnInvitationNotSentOffersTryAgainWhichSendsTheSameInvitation() =
        runTest {
            val engine = loaded()
            api.inviteFailures += ApiFailure.IdentityProviderUnavailable
            engine.setEmail("ana@example.com")

            engine.sendInvite()
            runCurrent()

            assertEquals(MembersNotice.NotSent, engine.ready().notice)
            assertTrue(snapshotOf(engine.state.value).noticeTryAgain)
            assertEquals("ana@example.com", engine.ready().form.email)

            engine.retry()
            runCurrent()

            assertEquals(2, api.invited.size)
            assertEquals(MembersNotice.Sent, engine.ready().notice)
            assertEquals(InviteForm(), engine.ready().form)
        }

    @Test
    fun uxDr84AForbiddenInvitationSaysSoAndReadsTheSitesAgain() =
        runTest {
            val engine = loaded()
            api.inviteFailures += ApiFailure.Forbidden
            val before = sitesApi.lists
            engine.setEmail("ana@example.com")

            engine.sendInvite()
            runCurrent()

            assertEquals(MembersNotice.Forbidden, engine.ready().notice)
            assertTrue(sitesApi.lists > before)
        }

    @Test
    fun aListThatCannotBeReadOffersTryAgain() =
        runTest {
            val engine = start()
            api.listFailure = ApiFailure.IdentityProviderUnavailable

            engine.load()
            runCurrent()

            val failed = assertIs<MembersState.Failed>(engine.state.value)
            assertEquals(MembersNotice.MembersUnavailable, failed.notice)
            api.listFailure = null
            engine.retry()
            runCurrent()
            assertEquals(2, engine.ready().members.size)
        }

    @Test
    fun aSuccessfulReadTakesAwayTheNoticeOfAnEarlierFailedRead() =
        runTest {
            val engine = loaded()
            api.listFailure = ApiFailure.IdentityProviderUnavailable
            engine.load()
            runCurrent()
            assertEquals(MembersNotice.MembersUnavailable, engine.ready().notice)

            api.listFailure = null
            engine.retry()
            runCurrent()

            assertNull(engine.ready().notice)
            assertFalse(snapshotOf(engine.state.value).noticeTryAgain)
        }

    @Test
    fun aSuccessfulReadKeepsTheInvitationNotice() =
        runTest {
            val engine = loaded()
            engine.setEmail("ana@example.com")
            engine.sendInvite()
            runCurrent()

            engine.load()
            runCurrent()

            assertEquals(MembersNotice.Sent, engine.ready().notice)
        }

    @Test
    fun tryAgainAfterAFailedReadReadsAgainAndDoesNotResendAnEarlierInvitation() =
        runTest {
            val engine = loaded()
            api.inviteFailures += ApiFailure.IdentityProviderUnavailable
            engine.setEmail("ana@example.com")
            engine.sendInvite()
            runCurrent()
            assertEquals(MembersNotice.NotSent, engine.ready().notice)

            api.listFailure = ApiFailure.IdentityProviderUnavailable
            engine.load()
            runCurrent()
            assertEquals(MembersNotice.MembersUnavailable, engine.ready().notice)
            val lists = api.lists

            api.listFailure = null
            engine.retry()
            runCurrent()

            assertEquals(1, api.invited.size)
            assertEquals(lists + 1, api.lists)
            assertNull(engine.ready().notice)
        }

    @Test
    fun switchingToASiteWhereTheUserIsNotOwnerHidesMembers() =
        runTest {
            val engine = start("Owner", SiteDto("b", "Allotment", "Member"))
            engine.load()
            runCurrent()
            engine.ready()

            sites.select("b")
            runCurrent()

            assertEquals(MembersState.Hidden, engine.state.value)
            sites.select("a")
            runCurrent()
            assertIs<MembersState.Loading>(engine.state.value)
        }

    @Test
    fun theSnapshotFlattensTilesAndTheSentNotice() =
        runTest {
            val engine = loaded()
            engine.setEmail("bo@example.com")
            engine.sendInvite()
            runCurrent()

            val snapshot = snapshotOf(engine.state.value)
            assertEquals("ready", snapshot.surface)
            assertEquals(listOf("Simon Novak", "ana@example.com"), snapshot.memberNames)
            assertEquals(listOf("owner", "member"), snapshot.memberRoles)
            assertEquals(listOf("SN", "A"), snapshot.memberInitials)
            assertEquals(listOf(true, false), snapshot.memberIsYou)
            assertEquals("sent", snapshot.notice)
            assertEquals("bo@example.com", snapshot.sentTo)
            assertEquals("", snapshot.email)
            assertEquals("member", snapshot.role)
        }

    @Test
    fun initialsComeFromTheFirstAndLastWordElseTheEmailLikeTheWeb() {
        assertEquals("SN", Member.initialsOf("simon novak", null))
        assertEquals("AC", Member.initialsOf("Ana Bella Costa", null))
        assertEquals("A", Member.initialsOf(" ", "ana@example.com"))
        assertEquals("AN", Member.initialsOf("", "ana.bella_novak@example.com"))
        assertEquals("AN", Member.initialsOf("", "ana-novak@example.com"))
        assertEquals("?", Member.initialsOf("", null))
        assertEquals("?", Member.initialsOf("", "@example.com"))
    }
}
