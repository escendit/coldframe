package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteMemberListDto

/*
 * Members as both shells draw them (Story 9.1, UX-DR73): one tile per Membership and the Invite form.
 * The Members surface exists for an Owner only; there is no pending-invitation list and no accept
 * surface (AD-3): an invitee appears once they accepted on the Phase Two page.
 */

/** The Members calls; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. Owner only. */
public interface MembersApi {
    /** The Site's Memberships, Owners first, then by name. */
    public suspend fun listMembers(siteId: String): ApiResult<SiteMemberListDto>

    /** Asks the Server to invite [email] with [role] (a `SiteRole` name); 202 has no body. */
    public suspend fun invite(
        siteId: String,
        email: String,
        role: String,
    ): ApiResult<Unit>

    /** Story 9.2: gives [userId] the Role [role] (a `SiteRole` name); 204 has no body. */
    public suspend fun changeRole(
        siteId: String,
        userId: String,
        role: String,
    ): ApiResult<Unit>

    /** Story 9.2: removes [userId] from the Site; 204 has no body. */
    public suspend fun removeMember(
        siteId: String,
        userId: String,
    ): ApiResult<Unit>
}

/** Who may see the Members surface: an Owner. For anyone else it, its Settings row and its form are hidden. */
public object MembersAccess {
    public fun canManage(role: SiteRole): Boolean = role == SiteRole.Owner
}

/**
 * One Member tile (UX-DR51): [initials] in the square, [name] (the email, or nothing, when Keycloak
 * has no name), the Role line. [you] marks the signed-in User's own tile (the `primary` square).
 * Story 9.2: a tile opens the [MemberEditor], except the [lastOwner]'s, which is locked: "Owner · last one"
 * with "A Site always keeps at least one Owner." Every other tile's Role line reads "<Role> · change".
 */
public data class Member(
    val userId: String,
    val name: String,
    val email: String?,
    val role: SiteRole,
    val you: Boolean,
    val lastOwner: Boolean = false,
) {
    /** What the tile names: the name, else the email, else nothing. */
    val shownName: String get() = name.ifBlank { email.orEmpty() }

    /** Up to two letters: the first letters of the first two words of the name, else of the email. */
    val initials: String get() = initialsOf(name, email)

    public companion object {
        public fun initialsOf(
            name: String,
            email: String?,
        ): String {
            // As the web: first and last word of the name, else of the email's local part.
            val source = name.trim().ifEmpty { email?.trim()?.substringBefore('@').orEmpty() }
            val words = source.split(Regex("[\\s._-]+")).filter { it.isNotEmpty() }
            val letters =
                listOfNotNull(
                    words.firstOrNull()?.firstOrNull(),
                    words.takeIf { it.size > 1 }?.last()?.firstOrNull(),
                )
            return letters.joinToString("").uppercase().ifEmpty { "?" }
        }
    }
}

/** The one line under the email field. */
public enum class InviteEmailError {
    /** Nothing entered: "Enter an email address." */
    Blank,

    /** Not one email address, or longer than 254 characters: "Enter one email address, like ana@example.com." */
    Invalid,
}

/**
 * The Invite form (UX-DR52). [role] is the selected segment, Member at first; the shells show the
 * one-line description of that Role under the choice. While [working] the request is on its way and
 * the button reads "Sending…".
 */
public data class InviteForm(
    val email: String = "",
    val role: SiteRole = SiteRole.Member,
    val emailError: InviteEmailError? = null,
    val working: Boolean = false,
) {
    public companion object {
        public const val MAX_EMAIL_LENGTH: Int = 254

        /** The segments of the Role choice, in order. */
        public val roles: List<SiteRole> = listOf(SiteRole.Member, SiteRole.Administrator, SiteRole.Owner)

        /** The client check, as the Server checks: one address with exactly one `@`, no spaces. `null` may be sent. */
        public fun validate(email: String): InviteEmailError? {
            val trimmed = email.trim()
            if (trimmed.isEmpty()) return InviteEmailError.Blank
            val at = trimmed.indexOf('@')
            val valid =
                trimmed.length <= MAX_EMAIL_LENGTH &&
                    at > 0 &&
                    at == trimmed.lastIndexOf('@') &&
                    at < trimmed.length - 1 &&
                    trimmed.none { it.isWhitespace() || it == ',' || it == ';' || it == '<' || it == '>' }
            return if (valid) null else InviteEmailError.Invalid
        }
    }
}

/** The one line under the editor's Role field (Story 9.2). */
public enum class MemberRoleError {
    /** 409 `last-owner`: "A Site always keeps at least one Owner." */
    LastOwner,
}

/**
 * The Role change and remove editor of one Member tile (Story 9.2, UX-DR84): the Role choice with [role]
 * selected (the member's Role at first), "Save Role" and "Remove from Site". While [working] the change is on its way.
 */
public data class MemberEditor(
    val member: Member,
    val role: SiteRole,
    val roleError: MemberRoleError? = null,
    val working: Boolean = false,
)

/**
 * The removal confirmation (Story 9.2, UX-DR113): "Remove <name> from <Site>?", "They lose access to <Site> and
 * stop getting its Alerts.", the destructive action "Remove".
 */
public data class RemoveMemberConfirmation(
    val member: Member,
    val working: Boolean = false,
)

/** The Inline notice on Members. */
public enum class MembersNotice(
    /** Whether Try again can help. */
    public val tryAgain: Boolean,
) {
    /** 202: "Invitation sent to ‹email›. They accept from the email, on the garden's Wi-Fi." */
    Sent(false),

    /** 409: "That person is already invited or a member of this Site." The form keeps its values. */
    AlreadyInvited(false),

    /** 503: "The invitation was not sent. Try again." */
    NotSent(true),

    /** 503 on the list: "Names and emails can't be read right now. Try again." */
    MembersUnavailable(true),

    /** Story 9.2, 204: "‹name› is now ‹Role›." */
    RoleChanged(false),

    /** Story 9.2, 204: "‹name› was removed from ‹Site›." */
    Removed(false),

    /** Story 9.2, 404 `member-not-found`: "‹name› is no longer a member of ‹Site›." The list is read again. */
    MemberGone(false),

    /** Story 9.2, 503: "The change was not saved. Try again." Try again sends the same change. */
    NotChanged(true),

    /** 403 race: "You can't change this on ‹Site›. Ask an Owner or Administrator." The Sites are read again. */
    Forbidden(false),

    /** 404: "‹Site› is no longer one of your Sites. Nothing was changed." The Sites are read again. */
    SiteGone(false),

    /** No answer. */
    Unreachable(true),

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate(false),

    /** Any other answer. */
    Unexpected(true),
}

/** What the Members surface shows. */
public sealed interface MembersState {
    /** No current Site, or a Role below Owner: the surface and its Settings row do not exist. */
    public data object Hidden : MembersState

    /** An Owner on [siteName]; the Memberships are being read. */
    public data class Loading(
        val siteName: String,
    ) : MembersState

    /** The Memberships could not be read; the notice carries Try again where it helps. */
    public data class Failed(
        val siteName: String,
        val notice: MembersNotice,
    ) : MembersState

    /**
     * The Member tiles in the Server's order and the Invite form. [notice] is the result of the last
     * invitation or change; [sentTo] is the email the [MembersNotice.Sent] notice names, [noticeName] the person
     * and [noticeRole] the Role the Story 9.2 notices name. [editor] is the open tile editor, [removing] the
     * removal confirmation over it.
     */
    public data class Ready(
        val siteName: String,
        val members: List<Member>,
        val form: InviteForm,
        val notice: MembersNotice? = null,
        val sentTo: String? = null,
        val editor: MemberEditor? = null,
        val removing: RemoveMemberConfirmation? = null,
        val noticeName: String? = null,
        val noticeRole: SiteRole? = null,
    ) : MembersState
}
