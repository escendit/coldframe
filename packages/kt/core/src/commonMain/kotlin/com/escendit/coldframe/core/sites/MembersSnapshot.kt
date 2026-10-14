package com.escendit.coldframe.core.sites

/**
 * [MembersState] flattened for Swift (Story 9.1). Enum values cross as catalogue key suffixes
 * (`member`, `alreadyInvited`), the Members as parallel lists in the Server's order.
 *
 * [surface] is `hidden`, `loading`, `failed` or `ready`; [canManageMembers] is false only while
 * `hidden`, and then the Settings row and the surface do not exist. [notice] is the [MembersNotice]
 * key with [noticeTryAgain]; [sentTo] is the email the `sent` notice names. [role] is the selected
 * segment of the Invite form, [roles] the segments in order, [emailError] the [InviteEmailError] key.
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
    )
}
