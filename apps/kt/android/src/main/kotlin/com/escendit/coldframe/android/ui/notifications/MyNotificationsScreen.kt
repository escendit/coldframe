package com.escendit.coldframe.android.ui.notifications

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.TimeZoneActions
import com.escendit.coldframe.android.ui.components.TimeZonePanel
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.notifications.NotificationControl
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.notifications.SiteNotificationSettings
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * My notifications (UX-DR72): the Notification Window control (UX-DR47), the time-zone confirm
 * panel (UX-DR48), and for the current Site the "Mute ‹Site›" switch (UX-DR49) and my Reminder
 * cadence (UX-DR50). Without a current Site only the window and the time zone show. The switch
 * and the segmented choice apply at once; the window has Save. A change that was not saved shows
 * its notice under its control, which is back at the Server's value.
 *
 * While [notificationsOff] (the OS permission is denied or revoked, UX-DR88) the notifications-off notice stays at
 * the top with Open Settings, also over a failed load: the settings below still apply once notifications are on.
 */
@Composable
fun MyNotificationsScreen(
    state: NotificationSettingsState,
    actions: NotificationSettingsActions,
    modifier: Modifier = Modifier,
    notificationsOff: Boolean = false,
    onOpenSettings: () -> Unit = {},
) {
    val base = modifier.fillMaxSize().background(Coldframe.colors.background)
    when (state) {
        NotificationSettingsState.Idle, NotificationSettingsState.Loading -> {
            Box(base)
        }

        is NotificationSettingsState.Failed -> {
            Column(
                modifier = base.padding(Spacing.GUTTER_MOBILE.dp),
                verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
            ) {
                if (notificationsOff) NotificationsOffNotice(onOpenSettings)
                InlineNotice(
                    message = stringResource(state.notice.message()),
                    action =
                        if (state.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
                        } else {
                            null
                        },
                    announcement = Announcement.Assertive,
                )
            }
        }

        is NotificationSettingsState.Ready -> {
            Ready(state, actions, base, notificationsOff, onOpenSettings)
        }
    }
}

@Composable
private fun Ready(
    state: NotificationSettingsState.Ready,
    actions: NotificationSettingsActions,
    modifier: Modifier,
    notificationsOff: Boolean,
    onOpenSettings: () -> Unit,
) {
    val colors = Coldframe.colors
    Column(
        modifier = modifier.verticalScroll(rememberScrollState()).padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        if (notificationsOff) NotificationsOffNotice(onOpenSettings)
        Text(
            text = stringResource(R.string.notifications_window),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        NotificationWindowControl(
            draft = state.draft,
            outOfOrder = state.windowOutOfOrder,
            working = state.windowWorking,
            saved = state.windowSaved,
            actions = actions,
        )
        ControlNotice(state, NotificationControl.Window, actions)
        TimeZonePanel(
            shown = state.timeZone.shown,
            chosen = state.timeZone.chosen,
            changing = state.timeZone.changing,
            actions =
                TimeZoneActions(
                    confirm = actions.confirmTimeZone,
                    change = actions.changeTimeZone,
                    pick = actions.pickTimeZone,
                    zones = actions.timeZones,
                ),
            working = state.timeZone.working,
        )
        ControlNotice(state, NotificationControl.TimeZone, actions)
        state.site?.let { site ->
            MuteSwitch(site, actions)
            ControlNotice(state, NotificationControl.Mute, actions)
            SegmentedChoice<ReminderCadence?>(
                label = stringResource(R.string.notifications_cadence),
                segments =
                    listOf(
                        Segment(null, stringResource(R.string.notifications_cadence_use_site)),
                        Segment(ReminderCadence.Daily, stringResource(ReminderCadence.Daily.label())),
                        Segment(ReminderCadence.Every2Days, stringResource(ReminderCadence.Every2Days.label())),
                    ),
                selected = site.reminderCadence,
                onSelect = actions.setReminderCadence,
                helper =
                    stringResource(
                        R.string.notifications_cadence_helper,
                        stringResource(site.siteReminderCadence.label()),
                    ),
            )
            ControlNotice(state, NotificationControl.ReminderCadence, actions)
        }
    }
}

/** The notice of the change to [control] that was not saved, with Try again where that can help. */
@Composable
private fun ControlNotice(
    state: NotificationSettingsState.Ready,
    control: NotificationControl,
    actions: NotificationSettingsActions,
) {
    val notice = state.notice ?: return
    if (state.noticeControl != control) return
    InlineNotice(
        message =
            stringResource(
                notice.message(control),
                state.site
                    ?.site
                    ?.name
                    .orEmpty(),
            ),
        action =
            if (notice.tryAgain) {
                NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
            } else {
                null
            },
        announcement = Announcement.Assertive,
    )
}

/**
 * Mute toggle (UX-DR49): the Material switch, labelled "Mute ‹Site›". The whole row is the
 * switch, so TalkBack reads the label with its state and the target is the row.
 */
@Composable
private fun MuteSwitch(
    site: SiteNotificationSettings,
    actions: NotificationSettingsActions,
) {
    val colors = Coldframe.colors
    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .toggleable(value = site.muted, role = Role.Switch, onValueChange = actions.setMuted),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp)) {
            Text(
                text = stringResource(R.string.notifications_mute, site.site.name),
                style = Typography.bodyLg.textStyle(),
                color = colors.textPrimary,
            )
            Text(
                text = stringResource(R.string.notifications_mute_helper, site.site.name),
                style = Typography.helper.textStyle(),
                color = colors.textHelper,
            )
        }
        Switch(
            checked = site.muted,
            onCheckedChange = null,
            colors =
                SwitchDefaults.colors(
                    checkedThumbColor = colors.inkOnBright,
                    checkedTrackColor = colors.primary,
                    uncheckedThumbColor = colors.textSecondary,
                    uncheckedTrackColor = colors.layer01,
                    uncheckedBorderColor = colors.borderStrong,
                ),
        )
    }
}
