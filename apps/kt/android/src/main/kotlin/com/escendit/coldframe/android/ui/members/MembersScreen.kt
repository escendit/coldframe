package com.escendit.coldframe.android.ui.members

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
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
import com.escendit.coldframe.core.sites.MembersNotice
import com.escendit.coldframe.core.sites.MembersState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Members (Story 9.1, UX-DR73): one Member tile per Membership (UX-DR51) and the Invite form
 * (UX-DR52). Owner only: for anyone else the Settings row does not exist and this screen shows
 * nothing. There is no pending-invitation list: an invitee appears once they accepted on the Phase
 * Two page. Tiles are display-only until Role changes arrive.
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
                Notice(state.notice, state.siteName, sentTo = null, actions = actions)
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
            state.members.forEach { MemberTile(it) }
        }
        Text(
            text = stringResource(R.string.members_invite),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        InviteFormFields(state.form, actions)
        state.notice?.let { Notice(it, state.siteName, state.sentTo, actions) }
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

@Composable
private fun Notice(
    notice: MembersNotice,
    siteName: String,
    sentTo: String?,
    actions: MembersActions,
) {
    val subject = if (notice == MembersNotice.Sent) sentTo.orEmpty() else siteName
    InlineNotice(
        message = stringResource(notice.message(), subject),
        action =
            if (notice.tryAgain) {
                NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
            } else {
                null
            },
        announcement = if (notice == MembersNotice.Sent) Announcement.Polite else Announcement.Assertive,
    )
}

/**
 * The Member tile (UX-DR51), outlined so the square reads on it: the initials square (the signed-in User's own in `primary` with
 * `ink-on-bright`, the others `layer-01` with `text-primary`), the name and the Role line, upper
 * case by style. One accessibility element: "‹name›, ‹Role›" and ", you" on the User's own.
 */
@Composable
fun MemberTile(
    member: Member,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val role = stringResource(member.role.label())
    val spoken =
        if (member.you) {
            stringResource(R.string.members_tile_you, member.shownName, role)
        } else {
            stringResource(R.string.members_tile, member.shownName, role)
        }
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .border(1.dp, colors.borderSubtle)
                .padding(Spacing.STEP_4.dp)
                .testTag("member-tile")
                .clearAndSetSemantics { contentDescription = spoken },
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
        }
    }
}
