package com.escendit.coldframe.android.ui.members

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.sites.label
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.InviteForm
import com.escendit.coldframe.core.sites.Member
import com.escendit.coldframe.core.sites.MemberEditor
import com.escendit.coldframe.core.sites.MemberRoleError
import com.escendit.coldframe.core.sites.MembersNotice
import com.escendit.coldframe.core.sites.MembersState
import com.escendit.coldframe.core.sites.RemoveMemberConfirmation
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Members (Story 9.1, UX-DR73): one Member tile per Membership (UX-DR51) and the Invite form
 * (UX-DR52). Owner only: for anyone else the Settings row does not exist and this screen shows
 * nothing. There is no pending-invitation list: an invitee appears once they accepted on the Phase
 * Two page. Story 9.2 (UX-DR84, UX-DR113): a tile opens its editor (Role choice, "Save Role", "Remove from
 * Site"), except the last Owner's, which is locked with "A Site always keeps at least one Owner."; removal
 * confirms in a native dialog naming the person and the Site.
 */
@Composable
fun MembersScreen(
    state: MembersState,
    actions: MembersActions,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val base = modifier.fillMaxSize().background(colors.background)
    when (state) {
        MembersState.Hidden, is MembersState.Loading -> {
            Box(base)
        }

        is MembersState.Failed -> {
            Column(base.padding(Spacing.GUTTER_MOBILE.dp)) {
                Notice(state.notice, state.siteName, sentTo = null, name = null, role = null, actions = actions)
            }
        }

        is MembersState.Ready -> {
            Ready(state, actions, base)
        }
    }
}

@Composable
private fun Ready(
    state: MembersState.Ready,
    actions: MembersActions,
    modifier: Modifier,
) {
    val colors = Coldframe.colors
    Column(
        modifier = modifier.verticalScroll(rememberScrollState()).padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
            state.members.forEach { member ->
                MemberTile(member, onClick = if (member.lastOwner) null else ({ actions.select(member.userId) }))
                state.editor?.takeIf { it.member.userId == member.userId }?.let { MemberEditorFields(it, actions) }
            }
        }
        state.removing?.let { RemoveMemberDialog(it, state.siteName, actions) }
        Text(
            text = stringResource(R.string.members_invite),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        InviteFormFields(state.form, actions)
        state.notice?.let { Notice(it, state.siteName, state.sentTo, state.noticeName, state.noticeRole, actions) }
        PrimaryButton(
            label = stringResource(R.string.members_send_invite),
            onClick = actions.sendInvite,
            working = state.form.working,
            workingLabel = stringResource(R.string.members_sending_invite),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun InviteFormFields(
    form: InviteForm,
    actions: MembersActions,
) {
    TextInput(
        label = stringResource(R.string.members_email),
        value = form.email,
        onValueChange = actions.setEmail,
        helper = stringResource(R.string.members_email_helper),
        error = form.emailError?.let { stringResource(it.message()) },
        singleLine = true,
        keyboardOptions =
            KeyboardOptions(
                capitalization = KeyboardCapitalization.None,
                autoCorrectEnabled = false,
                keyboardType = KeyboardType.Email,
                imeAction = ImeAction.Done,
            ),
    )
    SegmentedChoice(
        label = stringResource(R.string.members_role),
        segments = InviteForm.roles.map { Segment(it, stringResource(it.label())) },
        selected = form.role,
        onSelect = actions.setRole,
        helper = stringResource(form.role.description()),
    )
}

/** The editor under the selected tile (UX-DR84): Role choice, one line under it, "Save Role", "Remove from Site". */
@Composable
private fun MemberEditorFields(
    editor: MemberEditor,
    actions: MembersActions,
) {
    val colors = Coldframe.colors
    Column(
        modifier = Modifier.fillMaxWidth().testTag("member-editor"),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        SegmentedChoice(
            label = stringResource(R.string.members_role),
            segments = InviteForm.roles.map { Segment(it, stringResource(it.label())) },
            selected = editor.role,
            onSelect = actions.setEditRole,
            helper = if (editor.roleError == null) stringResource(editor.role.description()) else null,
        )
        if (editor.roleError == MemberRoleError.LastOwner) {
            Text(
                text = stringResource(R.string.members_last_owner),
                style = Typography.helper.textStyle(),
                color = colors.supportErrorText,
                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Assertive },
            )
        }
        PrimaryButton(
            label = stringResource(R.string.members_save_role),
            onClick = actions.saveRole,
            working = editor.working,
            workingLabel = stringResource(R.string.members_saving_role),
            modifier = Modifier.fillMaxWidth(),
        )
        ColdframeButton(
            label = stringResource(R.string.members_remove),
            onClick = actions.askRemove,
            variant = ButtonVariant.Ghost,
            modifier = Modifier.fillMaxWidth(),
        )
        ColdframeButton(
            label = stringResource(R.string.members_cancel),
            onClick = actions.closeEditor,
            variant = ButtonVariant.Ghost,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/** The removal confirmation (UX-DR113): a native dialog naming the person and the Site. */
@Composable
private fun RemoveMemberDialog(
    confirmation: RemoveMemberConfirmation,
    siteName: String,
    actions: MembersActions,
) {
    val colors = Coldframe.colors
    AlertDialog(
        onDismissRequest = { if (!confirmation.working) actions.cancelRemove() },
        title = {
            Text(
                stringResource(R.string.members_remove_question, confirmation.member.shownName, siteName),
                style = Typography.section.textStyle(),
            )
        },
        text = { Text(stringResource(R.string.members_remove_detail, siteName), style = Typography.body.textStyle()) },
        confirmButton = {
            DialogButton(
                stringResource(
                    if (confirmation.working) R.string.members_removing else R.string.members_remove_confirm,
                ),
                enabled = !confirmation.working,
                color = colors.supportErrorText,
                onClick = actions.confirmRemove,
            )
        },
        dismissButton = {
            if (!confirmation.working) {
                DialogButton(
                    stringResource(R.string.members_cancel),
                    enabled = true,
                    color = colors.primaryText,
                    onClick = actions.cancelRemove,
                )
            }
        },
        containerColor = colors.background,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textPrimary,
        tonalElevation = 0.dp,
    )
}

@Composable
private fun DialogButton(
    label: String,
    enabled: Boolean,
    color: Color,
    onClick: () -> Unit,
) {
    TextButton(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
    ) {
        Text(styledText(label, Typography.button), style = Typography.button.textStyle(), color = color)
    }
}

@Composable
private fun Notice(
    notice: MembersNotice,
    siteName: String,
    sentTo: String?,
    name: String?,
    role: SiteRole?,
    actions: MembersActions,
) {
    val message =
        when (notice) {
            MembersNotice.Sent -> {
                stringResource(notice.message(), sentTo.orEmpty())
            }

            MembersNotice.RoleChanged -> {
                stringResource(notice.message(), name.orEmpty(), role?.let { stringResource(it.label()) }.orEmpty())
            }

            MembersNotice.Removed, MembersNotice.MemberGone -> {
                stringResource(notice.message(), name.orEmpty(), siteName)
            }

            else -> {
                stringResource(notice.message(), siteName)
            }
        }
    val polite = notice == MembersNotice.Sent || notice == MembersNotice.RoleChanged || notice == MembersNotice.Removed
    InlineNotice(
        message = message,
        action =
            if (notice.tryAgain) {
                NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
            } else {
                null
            },
        announcement = if (polite) Announcement.Polite else Announcement.Assertive,
    )
}

/**
 * The Member tile (UX-DR51), outlined so the square reads on it: the initials square (the signed-in User's own in `primary` with
 * `ink-on-bright`, the others `layer-01` with `text-primary`), the name and the Role line, upper
 * case by style. One accessibility element: "‹name›, ‹Role line›" and ", you" on the User's own.
 * Story 9.2: with [onClick] the tile is a button whose Role line reads "‹Role› · change"; the last Owner's
 * (no [onClick]) reads "Owner · last one" with "A Site always keeps at least one Owner." under it.
 */
@Composable
fun MemberTile(
    member: Member,
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
) {
    val colors = Coldframe.colors
    val roleName = stringResource(member.role.label())
    val role =
        when {
            member.lastOwner -> stringResource(R.string.members_tile_last_owner)
            onClick != null -> stringResource(R.string.members_tile_change, roleName)
            else -> roleName
        }
    val lock = stringResource(R.string.members_last_owner)
    val named =
        if (member.you) {
            stringResource(R.string.members_tile_you, member.shownName, role)
        } else {
            stringResource(R.string.members_tile, member.shownName, role)
        }
    val spoken = if (member.lastOwner) "$named. $lock" else named
    val clickable =
        if (onClick != null) {
            Modifier.clickable(role = Role.Button, onClick = onClick)
        } else {
            Modifier
        }
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .then(clickable)
                .border(1.dp, colors.borderSubtle)
                .padding(Spacing.STEP_4.dp)
                .testTag("member-tile")
                .clearAndSetSemantics {
                    contentDescription = spoken
                    if (onClick != null) {
                        this.role = Role.Button
                        onClick(action = {
                            onClick()
                            true
                        })
                    }
                },
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier =
                Modifier
                    .sizeIn(minWidth = Spacing.BUTTON_HEIGHT.dp, minHeight = Spacing.BUTTON_HEIGHT.dp)
                    .background(if (member.you) colors.primary else colors.layer01)
                    .testTag(if (member.you) "member-initials-you" else "member-initials"),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = member.initials,
                style = Typography.section.textStyle(),
                color = if (member.you) colors.inkOnBright else colors.textPrimary,
            )
        }
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp)) {
            Text(text = member.shownName, style = Typography.bodyLg.textStyle(), color = colors.textPrimary)
            Text(
                text = styledText(role, Typography.statusLabel),
                style = Typography.statusLabel.textStyle(),
                color = colors.textSecondary,
            )
            if (member.lastOwner) {
                Text(text = lock, style = Typography.helper.textStyle(), color = colors.textHelper)
            }
        }
    }
}
