package com.escendit.coldframe.android.ui.thresholds

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.sites.LotDetailCopy
import com.escendit.coldframe.android.ui.sites.rememberLotDetailCopy
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.thresholds.ThresholdColumn
import com.escendit.coldframe.core.thresholds.ThresholdsState
import com.escendit.coldframe.core.thresholds.site
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import java.time.Instant
import java.time.ZoneId
import kotlin.math.abs
import kotlin.math.roundToLong

/** Marks the Threshold column of a Sensor for tests: `threshold-column-‹sensorId›`. */
const val THRESHOLD_COLUMN_TAG = "threshold-column-"

/** Marks the Save control for tests. */
const val THRESHOLDS_SAVE_TAG = "thresholds-save"

private const val TRACK_HEIGHT_DP = 160
private const val TRACK_WIDTH_DP = 40
private const val DISABLED_ALPHA = 0.4f

/**
 * Thresholds (UX-DR45, UX-DR69, UX-DR84, UX-DR91), full screen with Cancel and Save: a Threshold column per
 * Sensor, each a vertical track on `layer-01` with a 2 dp `primary-text` low line, the current Reading marker, a
 * dashed "no high" marker, the values to the right, and a drag or a typed value. Every rule (steps, ranges, who
 * may edit, whether Save is enabled) comes from [ThresholdsState]; this only draws it (AD-14). A Member sees the
 * same columns with no edit control at all: hidden, not disabled.
 */
@Composable
fun ThresholdsScreen(
    state: ThresholdsState,
    actions: ThresholdsActions,
    modifier: Modifier = Modifier,
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
) {
    val colors = Coldframe.colors
    val copy = rememberLotDetailCopy(remember(state) { now() }, zone)
    val ready = state as? ThresholdsState.Ready
    val lotName = state.lotNameOrEmpty()
    val title = stringResource(R.string.thresholds_title, lotName)
    val focus = remember { FocusRequester() }
    LaunchedEffect(state::class) { focus.requestFocus() }
    // A saved change closes the screen; Lot detail has already been asked to read again.
    LaunchedEffect(ready?.saved) { if (ready?.saved == true) actions.close() }
    BackHandler(enabled = state !is ThresholdsState.Idle) { actions.close() }
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(colors.background)
                .safeDrawingPadding()
                .semantics { paneTitle = title }
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            // Cancel for an editor, Back where nothing can be changed (UX-DR69, UX-DR84).
            ColdframeButton(
                label = stringResource(if (ready?.canEdit == false) R.string.nav_back else R.string.modal_cancel),
                onClick = actions.close,
                variant = ButtonVariant.Ghost,
            )
            if (ready != null && ready.canEdit) {
                ColdframeButton(
                    label = stringResource(R.string.thresholds_save),
                    onClick = actions.save,
                    working = ready.working || !ready.canSave,
                    workingLabel = if (ready.working) stringResource(R.string.thresholds_saving) else null,
                    modifier =
                        Modifier
                            .testTag(THRESHOLDS_SAVE_TAG)
                            .alpha(if (ready.canSave || ready.working) 1f else DISABLED_ALPHA),
                )
            }
        }
        Text(
            text = title,
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
            modifier =
                Modifier
                    .fillMaxWidth()
                    .focusRequester(focus)
                    .focusable()
                    .semantics { heading() },
        )
        when (state) {
            ThresholdsState.Idle -> {
                Unit
            }

            is ThresholdsState.Loading -> {
                Text(
                    text = stringResource(R.string.garden_loading, state.lotName),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }

            is ThresholdsState.Failed -> {
                InlineNotice(
                    message = stringResource(state.notice.message(), state.site.name),
                    action =
                        if (state.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
                        } else {
                            null
                        },
                    announcement = Announcement.Polite,
                )
            }

            is ThresholdsState.Ready -> {
                ReadyThresholds(state, actions, copy)
            }
        }
    }
}

private fun ThresholdsState.lotNameOrEmpty(): String =
    when (this) {
        ThresholdsState.Idle -> ""
        is ThresholdsState.Loading -> lotName
        is ThresholdsState.Failed -> lotName
        is ThresholdsState.Ready -> lotName
    }

@Composable
private fun ReadyThresholds(
    state: ThresholdsState.Ready,
    actions: ThresholdsActions,
    copy: LotDetailCopy,
) {
    val colors = Coldframe.colors
    if (!state.canEdit) {
        Text(
            text = stringResource(R.string.thresholds_read_only),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
    // A failed save keeps every edit; the notice says what did not change (assertive: a save that did not happen).
    state.notice?.let {
        InlineNotice(
            message = stringResource(it.message(), state.site.name),
            action =
                if (it.tryAgain &&
                    state.canSave
                ) {
                    NoticeActionUi(stringResource(R.string.notice_try_again), actions.save)
                } else {
                    null
                },
            announcement = Announcement.Assertive,
        )
    }
    for (column in state.columns) {
        Column(
            modifier = Modifier.fillMaxWidth().testTag(THRESHOLD_COLUMN_TAG + column.sensorId),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
        ) {
            Text(
                text = copy.quantity(column.quantity),
                style = Typography.section.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.semantics { heading() },
            )
            ThresholdColumnView(column, state.canEdit, actions, copy, state.focusSensorId == column.sensorId)
        }
    }
}

@Composable
private fun ThresholdColumnView(
    column: ThresholdColumn,
    canEdit: Boolean,
    actions: ThresholdsActions,
    copy: LotDetailCopy,
    focused: Boolean,
) {
    val colors = Coldframe.colors
    val focus = remember { FocusRequester() }
    LaunchedEffect(focused) { if (focused) focus.requestFocus() }
    Row(
        modifier = Modifier.fillMaxWidth().then(if (focused) Modifier.focusRequester(focus).focusable() else Modifier),
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp),
    ) {
        Track(column, canEdit, actions, copy)
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp)) {
            Text(
                text =
                    column.current?.let {
                        stringResource(
                            R.string.thresholds_now,
                            copy.value(numberText(it), column.unit),
                        )
                    }
                        ?: stringResource(R.string.thresholds_no_reading),
                style = Typography.metaMono.textStyle(),
                color = colors.textSecondary,
            )
            if (column.alerting) {
                ThresholdField(
                    label = stringResource(R.string.thresholds_low_for, copy.quantity(column.quantity)),
                    value = column.low,
                    unitText = { copy.value(it, column.unit) },
                    editable = canEdit,
                    onCommit = { actions.setLowText(column.sensorId, it) },
                )
            } else {
                Text(
                    text = stringResource(R.string.thresholds_alerts_off),
                    style = Typography.body.textStyle(),
                    color = colors.textPrimary,
                )
            }
            if (column.hasHigh) {
                ThresholdField(
                    label = stringResource(R.string.thresholds_high_for, copy.quantity(column.quantity)),
                    value = column.high,
                    unitText = { copy.value(it, column.unit) },
                    editable = canEdit,
                    onCommit = { actions.setHighText(column.sensorId, it) },
                )
            } else if (column.alerting) {
                Text(
                    text = stringResource(R.string.thresholds_no_high),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }
            val error =
                when {
                    column.lowMustStayBelowHigh -> R.string.thresholds_low_not_below_high
                    column.lowRequired -> R.string.thresholds_low_required
                    else -> null
                }
            if (error != null) {
                Text(
                    text = stringResource(error),
                    style = Typography.helper.textStyle(),
                    color = colors.supportErrorText,
                    modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
                )
            }
            if (canEdit) {
                if (column.alerting && column.high == null) {
                    ColdframeButton(
                        label = stringResource(R.string.thresholds_add_high),
                        onClick = { actions.addHigh(column.sensorId) },
                        variant = ButtonVariant.Secondary,
                    )
                }
                if (column.high != null) {
                    ColdframeButton(
                        label = stringResource(R.string.thresholds_clear_high),
                        onClick = { actions.clearHigh(column.sensorId) },
                        variant = ButtonVariant.Secondary,
                    )
                }
                ColdframeButton(
                    label =
                        stringResource(
                            if (column.alerting) R.string.thresholds_turn_off else R.string.thresholds_turn_on,
                        ),
                    onClick = {
                        if (column.alerting) {
                            actions.turnOffAlerts(
                                column.sensorId,
                            )
                        } else {
                            actions.turnOnAlerts(column.sensorId)
                        }
                    },
                    variant = ButtonVariant.Ghost,
                )
            }
        }
    }
}

private val ThresholdColumn.hasHigh: Boolean get() = high != null

/** A value typed in the column's unit; the core snaps and keeps it, this only holds the text while it is being typed. */
@Composable
private fun ThresholdField(
    label: String,
    value: Double?,
    unitText: (String) -> String,
    editable: Boolean,
    onCommit: (String) -> Unit,
) {
    val shown = value?.let(::numberText).orEmpty()
    if (!editable) {
        Text(
            text = "$label: ${unitText(shown)}",
            style = Typography.bodyLg.textStyle(),
            color = Coldframe.colors.textPrimary,
        )
        return
    }
    var text by remember { mutableStateOf(shown) }
    var editing by remember { mutableStateOf(false) }
    // While not being typed in, the field follows the core (a drag, a snap, Add high, Turn on).
    LaunchedEffect(shown, editing) { if (!editing) text = shown }
    TextInput(
        label = label,
        value = text,
        onValueChange = { text = it },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal, imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = { onCommit(text) }),
        onFocusChange = { focused ->
            // The core snaps on every call, so a value is sent once typing is finished, never digit by digit.
            if (editing && !focused) onCommit(text)
            editing = focused
        },
    )
}

/** The whole number as text, or one decimal; the core wrote the number, this only prints it. */
private fun numberText(value: Double): String =
    if (value % 1.0 == 0.0) value.roundToLong().toString() else ((value * TENTHS).roundToLong() / TENTHS).toString()

private const val TENTHS = 10.0

@Composable
private fun Track(
    column: ThresholdColumn,
    canEdit: Boolean,
    actions: ThresholdsActions,
    copy: LotDetailCopy,
) {
    val colors = Coldframe.colors
    val name = copy.quantity(column.quantity)
    val range = column.trackMax - column.trackMin

    fun fraction(value: Double?): Float? = value?.let { ((it - column.trackMin) / range).coerceIn(0.0, 1.0).toFloat() }
    val low = fraction(column.low)
    val high = fraction(column.high)
    val current = fraction(column.current)
    val track = colors.layer01
    val border = colors.borderSubtle
    val lowInk = colors.primaryText
    val highInk = colors.chartHighLine
    val markInk = colors.textPrimary
    val lowLabel = stringResource(R.string.thresholds_slider_low, name)
    val highLabel = stringResource(R.string.thresholds_slider_high, name)
    val step = column.step ?: 1.0
    val currentLow by rememberUpdatedState(low)
    val currentHigh by rememberUpdatedState(high)
    Box(
        modifier = Modifier.width(TRACK_WIDTH_DP.dp).height(TRACK_HEIGHT_DP.dp),
    ) {
        Box(
            modifier =
                Modifier
                    .fillMaxSize()
                    .background(track)
                    .border(1.dp, border)
                    .drawBehind { drawTrack(low, high, current, lowInk, highInk, markInk) }
                    .then(
                        if (canEdit && column.draggable) {
                            // Keyed on the Sensor only: a new value must not restart the drag it came from.
                            Modifier.pointerInput(column.sensorId) {
                                var moving = 0
                                detectVerticalDragGestures(
                                    onDragStart = { start ->
                                        moving = nearest(start.y / size.height, currentLow, currentHigh)
                                    },
                                    onVerticalDrag = { change, _ ->
                                        val value = column.trackMin + (1 - change.position.y / size.height) * range
                                        if (moving == LOW) {
                                            actions.setLow(column.sensorId, value)
                                        } else if (moving == HIGH) {
                                            actions.setHigh(column.sensorId, value)
                                        }
                                        change.consume()
                                    },
                                )
                            }
                        } else {
                            Modifier
                        },
                    ),
        )
        // Accessible handles: the lines themselves, adjustable by TalkBack in the column's step.
        for ((label, value, isLow) in listOf(
            Triple(lowLabel, column.low, true),
            Triple(highLabel, column.high, false),
        )) {
            if (value == null || !canEdit) continue
            Box(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .semantics {
                            contentDescription = label
                            role = Role.Button
                            progressBarRangeInfo =
                                ProgressBarRangeInfo(
                                    value.toFloat(),
                                    column.trackMin.toFloat()..column.trackMax.toFloat(),
                                )
                            setProgress { target ->
                                val snapped =
                                    column.trackMin + (abs(target - column.trackMin) / step).roundToLong() * step
                                if (isLow) {
                                    actions.setLow(
                                        column.sensorId,
                                        snapped,
                                    )
                                } else {
                                    actions.setHigh(column.sensorId, snapped)
                                }
                                true
                            }
                        },
            )
        }
    }
}

private const val LOW = 1
private const val HIGH = 2

/** The line nearer to a touch (a fraction from the bottom of the track): the low, else the high. */
private fun nearest(
    y: Float,
    low: Float?,
    high: Float?,
): Int {
    val touched = 1f - y
    val toLow = low?.let { abs(it - touched) } ?: Float.MAX_VALUE
    val toHigh = high?.let { abs(it - touched) } ?: Float.MAX_VALUE
    return when {
        low == null && high == null -> 0
        toLow <= toHigh -> LOW
        else -> HIGH
    }
}

/**
 * The track: a 2 dp low line across, a 1 dp dashed high line, and with no high a dashed marker at the top of the
 * track; the current Reading is a small pointer on the left edge.
 */
private fun DrawScope.drawTrack(
    low: Float?,
    high: Float?,
    current: Float?,
    lowInk: Color,
    highInk: Color,
    markInk: Color,
) {
    fun y(fraction: Float) = size.height - fraction * size.height
    val dash = PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 4.dp.toPx()))
    if (low != null) {
        drawLine(lowInk, Offset(0f, y(low)), Offset(size.width, y(low)), 2.dp.toPx())
    }
    val highY = if (high != null) y(high) else 1.dp.toPx()
    drawLine(highInk, Offset(0f, highY), Offset(size.width, highY), 1.dp.toPx(), pathEffect = dash)
    if (current != null) {
        val tip = 8.dp.toPx()
        val path =
            Path().apply {
                moveTo(0f, y(current) - tip / 2)
                lineTo(tip, y(current))
                lineTo(0f, y(current) + tip / 2)
                close()
            }
        drawPath(path, markInk, style = Fill)
    }
}
