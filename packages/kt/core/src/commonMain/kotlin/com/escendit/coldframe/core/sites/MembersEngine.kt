package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteMemberDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Members of the current Site of [sites] (Story 9.1, UX-DR73): the Member tiles and the Invite form.
 * Only an Owner has it ([canManageMembers]); for anyone else the state is [MembersState.Hidden] and
 * every call is ignored, as the surface does not exist.
 *
 * The shell calls [load] on every entry. [sendInvite] checks the email as the Server does, then asks
 * the Server to invite. Accepted (202), the form clears to Member and the notice names the email;
 * the Member tiles stay as they are, because an invitee appears only once they accepted. 409 keeps
 * what was entered with [MembersNotice.AlreadyInvited]; 400 marks the email field; 503 and no answer
 * offer Try again ([retry]). A 403 or 404 reads the Sites again: the Role or the Site changed.
 *
 * A change of the current Site, of its Role, or the end of the session drops what was shown.
 * [currentUserId] names the signed-in User, whose tile is marked as theirs.
 */
public class MembersEngine internal constructor(
    private val api: MembersApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
    private val currentUserId: suspend () -> String?,
) {
    private val mutableState = MutableStateFlow<MembersState>(MembersState.Hidden)

    /** One observable state for the shells. */
    public val state: StateFlow<MembersState> = mutableState.asStateFlow()

    /** Bumped when the current Site or its Role changes or the session ends, so older answers are dropped. */
    private var generation = 0

    /** The invitation whose notice offers Try again. */
    private var failedInvite: InviteForm? = null

    /** Whether the notice shown came from a failed read, which a successful read takes away. */
    private var readNotice = false

    init {
        sites.onSessionEnded { follow(null) }
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current?.let { site -> site.id to site.role } }
                .distinctUntilChanged()
                .collect { follow(site()) }
        }
    }

    /** Whether the current Site's Role may see Members: the Settings row and the surface exist only then. */
    public val canManageMembers: Boolean get() = site() != null

    /** The current Site, when the caller is its Owner. */
    private fun site(): SiteSummary? =
        (sites.state.value as? SitesState.Ready)?.current?.takeIf { MembersAccess.canManage(it.role) }

    private fun follow(site: SiteSummary?) {
        generation++
        failedInvite = null
        readNotice = false
        mutableState.value = if (site == null) MembersState.Hidden else MembersState.Loading(site.name)
    }

    /** Reads the Memberships: every entry of Members, and Try again after a failed read. The form stays. */
    public fun load() {
        val site = site() ?: return
        val shown = mutableState.value
        if (shown !is MembersState.Ready) mutableState.value = MembersState.Loading(site.name)
        val started = generation
        scope.launch {
            val result = api.listMembers(site.id)
            val you = if (result is ApiResult.Ok) currentUserId() else null
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    val members = result.value.members.map { it.toMember(you) }
                    val dropNotice = readNotice
                    readNotice = false
                    mutableState.value =
                        when (val now = mutableState.value) {
                            // A notice of an earlier failed read goes; the invitation notices stay.
                            is MembersState.Ready -> {
                                now.copy(
                                    siteName = site.name,
                                    members = members,
                                    notice = now.notice.takeUnless { dropNotice },
                                )
                            }

                            else -> {
                                MembersState.Ready(site.name, members, InviteForm())
                            }
                        }
                }

                is ApiResult.Failed -> {
                    loadFailed(result.failure, site)
                }
            }
        }
    }

    private fun loadFailed(
        failure: ApiFailure,
        site: SiteSummary,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            follow(null)
            return
        }
        val notice =
            when (failure) {
                ApiFailure.IdentityProviderUnavailable -> MembersNotice.MembersUnavailable
                else -> noticeOf(failure)
            }
        // Try again on this notice reads again; it never re-sends an earlier invitation.
        failedInvite = null
        readNotice = true
        // A list already shown stays; its notice says the read did not happen.
        mutableState.value =
            when (val now = mutableState.value) {
                is MembersState.Ready -> now.copy(notice = notice, sentTo = null)
                else -> MembersState.Failed(site.name, notice)
            }
        sitesChanged(failure)
    }

    /** The email field; editing takes its mark away. Ignored while sending. */
    public fun setEmail(email: String) {
        editForm { it.copy(email = email, emailError = null) }
    }

    /** A segment of the Role choice; the description under it follows. Ignored while sending. */
    public fun setRole(role: SiteRole) {
        editForm { it.copy(role = role) }
    }

    private fun editForm(change: (InviteForm) -> InviteForm) {
        val ready = mutableState.value as? MembersState.Ready ?: return
        if (ready.form.working) return
        mutableState.value = ready.copy(form = change(ready.form))
    }

    /** "Send invite". An email that is not one address shows its reason under the field and sends nothing. */
    public fun sendInvite() {
        val ready = mutableState.value as? MembersState.Ready ?: return
        if (ready.form.working) return
        val error = InviteForm.validate(ready.form.email)
        if (error != null) {
            mutableState.value = ready.copy(form = ready.form.copy(emailError = error), notice = null, sentTo = null)
            return
        }
        send(ready.form.copy(email = ready.form.email.trim()))
    }

    /** Try again: the invitation that was not sent, or the read that failed. */
    public fun retry() {
        when (val now = mutableState.value) {
            is MembersState.Failed -> {
                if (now.notice.tryAgain) load()
            }

            is MembersState.Ready -> {
                if (now.notice?.tryAgain != true) return
                val invite = failedInvite
                if (invite != null) send(invite) else load()
            }

            else -> {
                Unit
            }
        }
    }

    private fun send(form: InviteForm) {
        val site = site() ?: return
        val ready = mutableState.value as? MembersState.Ready ?: return
        failedInvite = null
        readNotice = false
        mutableState.value =
            ready.copy(form = form.copy(working = true, emailError = null), notice = null, sentTo = null)
        val started = generation
        scope.launch {
            val result = api.invite(site.id, form.email, form.role.name)
            if (started != generation) return@launch
            val now = mutableState.value as? MembersState.Ready ?: return@launch
            when (result) {
                is ApiResult.Ok -> {
                    mutableState.value = now.copy(form = InviteForm(), notice = MembersNotice.Sent, sentTo = form.email)
                }

                is ApiResult.Failed -> {
                    inviteFailed(result.failure, form, now)
                }
            }
        }
    }

    private fun inviteFailed(
        failure: ApiFailure,
        sent: InviteForm,
        now: MembersState.Ready,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            follow(null)
            return
        }
        val kept = now.form.copy(working = false)
        mutableState.value =
            when (failure) {
                ApiFailure.Validation -> {
                    now.copy(form = kept.copy(emailError = InviteEmailError.Invalid))
                }

                ApiFailure.AlreadyInvited -> {
                    now.copy(form = kept, notice = MembersNotice.AlreadyInvited)
                }

                ApiFailure.IdentityProviderUnavailable -> {
                    now.copy(form = kept, notice = MembersNotice.NotSent)
                }

                else -> {
                    now.copy(form = kept, notice = noticeOf(failure))
                }
            }
        val notice = (mutableState.value as? MembersState.Ready)?.notice
        if (notice?.tryAgain == true) failedInvite = sent
        sitesChanged(failure)
    }

    /** A 403 or 404 means the Role or the Site changed: the Sites are read again. */
    private fun sitesChanged(failure: ApiFailure) {
        if (failure == ApiFailure.Forbidden || failure == ApiFailure.NotFound) sites.refresh()
    }

    private companion object {
        fun noticeOf(failure: ApiFailure): MembersNotice =
            when (failure) {
                ApiFailure.Forbidden -> MembersNotice.Forbidden
                ApiFailure.NotFound -> MembersNotice.SiteGone
                ApiFailure.AlreadyInvited -> MembersNotice.AlreadyInvited
                ApiFailure.IdentityProviderUnavailable -> MembersNotice.NotSent
                ApiFailure.Unreachable -> MembersNotice.Unreachable
                ApiFailure.Certificate -> MembersNotice.Certificate
                else -> MembersNotice.Unexpected
            }

        fun SiteMemberDto.toMember(you: String?): Member =
            Member(
                userId = userId,
                name = name,
                email = email,
                role = SiteRole.fromServer(role),
                you = you != null && userId == you,
            )
    }
}
