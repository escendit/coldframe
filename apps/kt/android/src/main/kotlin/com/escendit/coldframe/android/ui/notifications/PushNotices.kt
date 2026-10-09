package com.escendit.coldframe.android.ui.notifications

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.core.push.PushState

/**
 * What the push notices and the tap route can ask of the shell's OS side. The core decides what shows
 * ([PushState]); the activity holds the permission prompt and the system settings.
 */
class PushActions(
    /** Continue on the why-line: the OS prompt, where Android has one. */
    val ask: () -> Unit = {},
    /** Open Settings on the notice: this app's notification settings. */
    val openSettings: () -> Unit = {},
    /** The shell has shown the route of a tapped notification. */
    val routeHandled: () -> Unit = {},
) {
    companion object {
        val None = PushActions()
    }
}

/**
 * The notifications-off notice (UX-DR88): persistent in My notifications and above the tiles of the overview while
 * the permission is denied or revoked. Never dismissable; Open Settings is its one action.
 */
@Composable
fun NotificationsOffNotice(
    onOpenSettings: () -> Unit,
    modifier: Modifier = Modifier,
) {
    InlineNotice(
        message = stringResource(R.string.push_off),
        modifier = modifier,
        action = NoticeActionUi(stringResource(R.string.push_open_settings), onOpenSettings),
    )
}

/**
 * What the overview shows about notifications above its tiles: on the first landing the one line of why, whose
 * action leads to the OS prompt (UX-DR122); afterwards the notifications-off notice while the permission is denied.
 */
@Composable
fun OverviewPushNotice(
    push: PushState,
    actions: PushActions,
    modifier: Modifier = Modifier,
) {
    when {
        push.promptDue -> {
            InlineNotice(
                message = stringResource(R.string.push_why),
                modifier = modifier,
                action = NoticeActionUi(stringResource(R.string.push_why_continue), actions.ask),
            )
        }

        push.noticeVisible -> {
            NotificationsOffNotice(onOpenSettings = actions.openSettings, modifier = modifier)
        }
    }
}
