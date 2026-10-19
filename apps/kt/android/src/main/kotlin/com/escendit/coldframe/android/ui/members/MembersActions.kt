package com.escendit.coldframe.android.ui.members

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.sites.InviteEmailError
import com.escendit.coldframe.core.sites.MembersEngine
import com.escendit.coldframe.core.sites.MembersNotice
import com.escendit.coldframe.core.sites.SiteRole

/**
 * What Members can ask of the core (Story 9.1). The shell never checks an email or decides what a
 * refused invitation leaves behind; it forwards taps to [MembersEngine] and renders its state (AD-14).
 */
class MembersActions(
    /** Every entry of the surface reads the Memberships again. */
    val load: () -> Unit = {},
    val setEmail: (String) -> Unit = {},
    val setRole: (SiteRole) -> Unit = {},
    val sendInvite: () -> Unit = {},
    /** Try again, where the notice offers it. */
    val retry: () -> Unit = {},
    /** Story 9.2: a tap on a Member tile opens its editor; the last Owner's tile is locked. */
    val select: (String) -> Unit = {},
    val closeEditor: () -> Unit = {},
    val setEditRole: (SiteRole) -> Unit = {},
    val saveRole: () -> Unit = {},
    val askRemove: () -> Unit = {},
    val cancelRemove: () -> Unit = {},
    val confirmRemove: () -> Unit = {},
) {
    companion object {
        val None = MembersActions()

        fun of(engine: MembersEngine): MembersActions =
            MembersActions(
                load = engine::load,
                setEmail = engine::setEmail,
                setRole = engine::setRole,
                sendInvite = engine::sendInvite,
                retry = engine::retry,
                select = engine::select,
                closeEditor = engine::closeEditor,
                setEditRole = engine::setEditRole,
                saveRole = engine::saveRole,
                askRemove = engine::askRemove,
                cancelRemove = engine::cancelRemove,
                confirmRemove = engine::confirmRemove,
            )
    }
}

/** The one line under the Role choice (UX-DR52). */
@StringRes
fun SiteRole.description(): Int =
    when (this) {
        SiteRole.Member -> R.string.members_role_member_description
        SiteRole.Administrator -> R.string.members_role_administrator_description
        SiteRole.Owner -> R.string.members_role_owner_description
    }

@StringRes
fun InviteEmailError.message(): Int =
    when (this) {
        InviteEmailError.Blank -> R.string.members_email_blank
        InviteEmailError.Invalid -> R.string.members_email_invalid
    }

/**
 * The notice's copy; `%1$s` is the email ([MembersNotice.Sent]) or the Site name. The Story 9.2 notices
 * take the person as `%1$s` and the Role or the Site as `%2$s`.
 */
@StringRes
fun MembersNotice.message(): Int =
    when (this) {
        MembersNotice.Sent -> R.string.members_sent
        MembersNotice.AlreadyInvited -> R.string.members_already_invited
        MembersNotice.NotSent -> R.string.members_not_sent
        MembersNotice.MembersUnavailable -> R.string.members_unavailable
        MembersNotice.Forbidden -> R.string.members_forbidden
        MembersNotice.SiteGone -> R.string.members_site_gone
        MembersNotice.Unreachable -> R.string.members_unreachable
        MembersNotice.Certificate -> R.string.notice_certificate
        MembersNotice.Unexpected -> R.string.members_unexpected
        MembersNotice.RoleChanged -> R.string.members_role_changed
        MembersNotice.Removed -> R.string.members_removed
        MembersNotice.MemberGone -> R.string.members_member_gone
        MembersNotice.NotChanged -> R.string.members_not_changed
    }
