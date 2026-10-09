package com.escendit.coldframe.android.ui.notifications

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimeInput
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.components.focusRing
import com.escendit.coldframe.android.ui.components.hatched
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.notifications.NotificationWindow
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** The tag of the decorative 24 h bar, which has no semantics to find it by. */
const val NOTIFICATION_WINDOW_BAR = "notification-window-bar"

private const val BAR_HEIGHT_DP = 24
private val AXIS_HOURS = listOf(0, 6, 12, 18, 24)

/**
 * Notification Window control (UX-DR47): two time fields, a 24 h bar that previews the window
 * (`primary` inside, hatched outside; decorative and hidden from TalkBack), the window in big
 * type, the helper naming the window's own start, and Save. Every value comes from the core's
 * [draft]; the fields only hand a picked time back to it.
 */
@Composable
fun NotificationWindowControl(
    draft: NotificationWindow,
    outOfOrder: Boolean,
    working: Boolean,
    saved: Boolean,
    actions: NotificationSettingsActions,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    var picking by rememberSaveable { mutableStateOf<Boolean?>(null) }
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp)) {
            TimeField(
                label = stringResource(R.string.notifications_window_from),
                value = draft.from,
                onClick = { picking = true },
                modifier = Modifier.weight(1f),
            )
            TimeField(
                label = stringResource(R.string.notifications_window_to),
                value = draft.to,
                onClick = { picking = false },
                modifier = Modifier.weight(1f),
            )
        }
        WindowBar(draft)
        Text(
            text = stringResource(R.string.notifications_window_range, draft.from, draft.to),
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
        )
        Text(
            text = stringResource(R.string.notifications_window_helper, draft.from),
            style = Typography.helper.textStyle(),
            color = colors.textHelper,
        )
        if (outOfOrder) {
            val reason = stringResource(R.string.notifications_window_not_before)
            Text(
                text = reason,
                style = Typography.helper.textStyle(),
                color = colors.supportErrorText,
                modifier = Modifier.semantics { error(reason) },
            )
        }
        PrimaryButton(
            label = stringResource(R.string.notifications_window_save),
            onClick = actions.saveWindow,
            working = working,
            workingLabel = stringResource(R.string.notifications_window_saving),
        )
        if (saved) {
            Text(
                text = stringResource(R.string.notifications_window_saved),
                style = Typography.body.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
            )
        }
    }
    picking?.let { from ->
        TimeDialog(
            heading =
                stringResource(
                    if (from) R.string.notifications_window_from_title else R.string.notifications_window_to_title,
                ),
            time = if (from) draft.from else draft.to,
            onSet = { time ->
                picking = null
                if (from) actions.setWindowFrom(time) else actions.setWindowTo(time)
            },
            onDismiss = { picking = null },
        )
    }
}

/** A time shown like a field: the label above the value, one button that opens the picker. */
@Composable
private fun TimeField(
    label: String,
    value: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val interaction = remember { MutableInteractionSource() }
    val focused by interaction.collectIsFocusedAsState()
    val edge = colors.borderStrong
    Column(
        modifier =
            modifier
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .focusRing(focused)
                .background(colors.field01)
                .drawBehind {
                    val stroke = 1.dp.toPx()
                    drawRect(edge, topLeft = Offset(0f, size.height - stroke), size = Size(size.width, stroke))
                }.clickable(
                    interactionSource = interaction,
                    indication = null,
                    role = Role.Button,
                    onClick = onClick,
                ).padding(horizontal = Spacing.STEP_4.dp, vertical = Spacing.STEP_3.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp),
    ) {
        Text(text = label, style = Typography.helper.textStyle(), color = colors.textSecondary)
        Text(text = value, style = Typography.bodyLg.textStyle(), color = colors.textPrimary)
    }
}

/** The 24 h preview: a picture of the two times above it, so it carries no semantics at all. */
@Composable
private fun WindowBar(window: NotificationWindow) {
    val colors = Coldframe.colors
    val inside = colors.primary
    val from = window.fromMinutes.toFloat() / NotificationWindow.MINUTES_PER_DAY
    val to = window.toMinutes.toFloat() / NotificationWindow.MINUTES_PER_DAY
    Column(
        modifier = Modifier.fillMaxWidth().testTag(NOTIFICATION_WINDOW_BAR).clearAndSetSemantics {},
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp),
    ) {
        Box(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .height(BAR_HEIGHT_DP.dp)
                    .hatched(colors.statusHatchGround, colors.statusHatchLine)
                    .drawBehind {
                        if (to > from) {
                            drawRect(
                                inside,
                                topLeft = Offset(size.width * from, 0f),
                                size = Size(size.width * (to - from), size.height),
                            )
                        }
                    },
        )
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            AXIS_HOURS.forEach { hour ->
                Text(
                    text = hour.toString().padStart(2, '0'),
                    style = Typography.metaMono.textStyle(),
                    color = colors.textSecondary,
                )
            }
        }
    }
}

/** The Material time input (24 h) in a native dialog whose button names the result (UX-DR113). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(
    heading: String,
    time: String,
    onSet: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Coldframe.colors
    val minutes = NotificationWindow.minutesOf(time) ?: 0
    val picker =
        rememberTimePickerState(
            initialHour = minutes / MINUTES_PER_HOUR,
            initialMinute = minutes % MINUTES_PER_HOUR,
            is24Hour = true,
        )
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(heading, style = Typography.section.textStyle()) },
        text = { TimeInput(state = picker) },
        confirmButton = {
            DialogButton(stringResource(R.string.notifications_window_set_time)) {
                onSet(NotificationWindow.timeOf(picker.hour, picker.minute))
            }
        },
        dismissButton = { DialogButton(stringResource(R.string.modal_cancel), onDismiss) },
        containerColor = colors.background,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textPrimary,
        tonalElevation = 0.dp,
    )
}

@Composable
private fun DialogButton(
    label: String,
    onClick: () -> Unit,
) {
    TextButton(onClick = onClick, modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp)) {
        Text(
            styledText(label, Typography.button),
            style = Typography.button.textStyle(),
            color = Coldframe.colors.primaryText,
        )
    }
}

private const val MINUTES_PER_HOUR = 60
