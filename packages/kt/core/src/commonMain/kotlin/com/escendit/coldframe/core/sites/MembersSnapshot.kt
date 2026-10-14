package com.escendit.coldframe.core.sites

/**
 * [MembersState] flattened for Swift (Story 9.1). Enum values cross as catalogue key suffixes
 * (`member`, `alreadyInvited`), the Members as parallel lists in the Server's order.
 *
 * [surface] is `hidden`, `loading`, `failed` or `ready`; [canManageMembers] is false only while
 * `hidden`, and then the Settings row and the surface do not exist. [notice] is the [MembersNotice]
 * key with [noticeTryAgain]; [sentTo] is the email the `sent` notice names. [role] is the selected
 * segment of the Invite form, [roles] the segments in order, [emailError] the [InviteEmailError] key.
 *
 * Story 9.2: [memberLastOwner] marks the locked tile of the only Owner. [editorUserId] is the member whose
 * editor is open (`null`: closed), [editorName] the name it shows, [editorRole] its selected segment and
 * [editorRoleError] the [MemberRoleError] key under it; [editorWorking] while a change or removal is on its way;
 * [confirmingRemove] while the removal confirmation is shown. [noticeName] and [noticeRole] are the person and
 * the Role key the `roleChanged`, `removed` and `memberGone` notices name.
 */
public data class MembersSnapshot(
    val surface: String,
    val canManageMembers: Boolean,
    val siteName: String,
    val memberIds: List<String>,
    val memberNames: List<String>,
    val memberEmails: List<String>,
    val memberRoles: List<String>,
    val memberInitials: List<String>,
    val memberIsYou: List<Boolean>,
    val email: String,
    val role: String,
    val roles: List<String>,
    val emailError: String?,
    val working: Boolean,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val sentTo: String,
    val memberLastOwner: List<Boolean>,
    val editorUserId: String?,
    val editorName: String,
    val editorRole: String,
    val editorRoleError: String?,
    val editorWorking: Boolean,
    val confirmingRemove: Boolean,
    val noticeName: String,
    val noticeRole: String,
)

/** The snapshot of [state]. */
public fun snapshotOf(state: MembersState): MembersSnapshot {
    val ready = state as? MembersState.Ready
    val members = ready?.members.orEmpty()
    val form = ready?.form ?: InviteForm()
    val notice =
        when (state) {
            is MembersState.Failed -> state.notice
            is MembersState.Ready -> state.notice
            else -> null
        }
    return MembersSnapshot(
        surface =
            when (state) {
                MembersState.Hidden -> "hidden"
                is MembersState.Loading -> "loading"
                is MembersState.Failed -> "failed"
                is MembersState.Ready -> "ready"
            },
        canManageMembers = state != MembersState.Hidden,
        siteName =
            when (state) {
                MembersState.Hidden -> ""
                is MembersState.Loading -> state.siteName
                is MembersState.Failed -> state.siteName
                is MembersState.Ready -> state.siteName
            },
        memberIds = members.map { it.userId },
        memberNames = members.map { it.shownName },
        memberEmails = members.map { it.email.orEmpty() },
        memberRoles = members.map { it.role.key() },
        memberInitials = members.map { it.initials },
        memberIsYou = members.map { it.you },
        email = form.email,
        role = form.role.key(),
        roles = InviteForm.roles.map { it.key() },
        emailError = form.emailError?.key(),
        working = form.working,
        notice = notice?.key(),
        noticeTryAgain = notice?.tryAgain == true,
        sentTo = ready?.sentTo.orEmpty(),
        memberLastOwner = members.map { it.lastOwner },
        editorUserId = ready?.editor?.member?.userId,
        editorName =
            ready
                ?.editor
                ?.member
                ?.shownName
                .orEmpty(),
        editorRole = (ready?.editor?.role ?: SiteRole.Member).key(),
        editorRoleError = ready?.editor?.roleError?.key(),
        editorWorking = ready?.editor?.working == true || ready?.removing?.working == true,
        confirmingRemove = ready?.removing != null,
        noticeName = ready?.noticeName.orEmpty(),
        noticeRole = ready?.noticeRole?.key().orEmpty(),
    )
}
