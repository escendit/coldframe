package com.escendit.coldframe.android.ui.pause

import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
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
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.PauseDate
import com.escendit.coldframe.core.sites.PauseFailure
import com.escendit.coldframe.core.sites.PauseForm
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.flow.drop
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/** Marks the "Until" field of the Pause sheet for tests. */
const val PAUSE_UNTIL_TAG = "pause-until"

/** Marks the calendar of the Pause sheet for tests. */
const val PAUSE_CALENDAR_TAG = "pause-calendar"

/**
 * The Pause sheet (UX-DR46): a Material `ModalBottomSheet` on the `overlay` scrim. Everything in it
 * is the core's [PauseForm]; see [PauseSheetContent]. It closes when the core closes it: Cancel, a
 * dismissal, or a Pause the Server accepted. While the request runs it cannot be dismissed, so an
 * answer that keeps it open is never given to a sheet that is gone.
 *
 * [unreachable] is the "can't reach" copy of the surface the sheet was opened on.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PauseSheet(
    form: PauseForm,
    actions: PauseActions,
    @StringRes unreachable: Int = R.string.notice_unreachable,
) {
    val colors = Coldframe.colors
    val working by rememberUpdatedState(form.working)
    ModalBottomSheet(
        onDismissRequest = actions.cancel,
        sheetState =
            rememberModalBottomSheetState(
                skipPartiallyExpanded = true,
                confirmValueChange = { it != SheetValue.Hidden || !working },
            ),
        shape = RectangleShape,
        containerColor = colors.background,
        contentColor = colors.textPrimary,
        tonalElevation = 0.dp,
        scrimColor = colors.overlay,
        dragHandle = null,
    ) {
        PauseSheetContent(form, actions, unreachable = unreachable)
    }
}

/**
 * The sheet's content, also rendered on its own in tests and snapshots: the scope as a Segmented
 * choice (Whole Site alone from the Site menu; This Device and Whole Site from a Device), the
 * optional "Until" date, and Cancel beside a primary button that names the result ("Pause Home
 * garden", "Pause 7C19").
 *
 * The date is picked in a calendar that opens inside the sheet, never in a dialog on top of it: one
 * modal level at a time. What is sent is the calendar date that was picked. A date the Server
 * refused marks the field ("Choose a date after today."); any other refusal is an Inline notice
 * above the buttons, and the sheet keeps what was entered.
 *
 * [calendarOpen] starts with the calendar showing, and [displayedMonth] is the month it opens on
 * when no date is picked (the current one by default).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun PauseSheetContent(
    form: PauseForm,
    actions: PauseActions,
    modifier: Modifier = Modifier,
    @StringRes unreachable: Int = R.string.notice_unreachable,
    calendarOpen: Boolean = false,
    displayedMonth: LocalDate? = null,
) {
    val colors = Coldframe.colors
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        Text(
            text = stringResource(R.string.pause_title),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        SegmentedChoice(
            label = stringResource(R.string.pause_scope),
            segments = form.scopes.map { Segment(it, stringResource(it.label())) },
            selected = form.scope,
            onSelect = actions.setScope,
        )
        UntilField(form, actions, calendarOpen, displayedMonth)
        val notice = form.notice?.takeIf { it != PauseNotice.DateRefused }
        if (notice != null) {
            InlineNotice(
                message = stringResource(notice.message(unreachable), form.siteName),
                action =
                    if (notice.tryAgain) {
                        NoticeActionUi(stringResource(R.string.notice_try_again), actions.save)
                    } else {
                        null
                    },
                announcement = Announcement.Assertive,
            )
        }
        FlowRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp, Alignment.End),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            ColdframeButton(
                label = stringResource(R.string.modal_cancel),
                onClick = actions.cancel,
                variant = ButtonVariant.Ghost,
                working = form.working,
            )
            PrimaryButton(
                label = stringResource(R.string.pause_primary, form.target),
                onClick = actions.save,
                working = form.working,
                workingLabel = stringResource(R.string.pause_working),
            )
        }
    }
}

@StringRes
private fun PauseScope.label(): Int =
    when (this) {
        PauseScope.Device -> R.string.pause_scope_device
        PauseScope.Site -> R.string.pause_scope_site
    }

/**
 * "Until": a field that names the picked date, or "No end date". Tapping it opens the calendar
 * below, inside the sheet; a picked day closes it again. "Clear date" empties the field. The field
 * is drawn as a DS TextInput is, with the `support-error` border and the reason when the Server
 * refused the date.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun UntilField(
    form: PauseForm,
    actions: PauseActions,
    calendarOpen: Boolean,
    displayedMonth: LocalDate?,
) {
    val colors = Coldframe.colors
    val locale = ComposeLocale.current.platformLocale
    var open by rememberSaveable { mutableStateOf(calendarOpen) }
    val picked = form.untilDate
    val refused = stringResource(R.string.pause_until_refused)
    val edge = colors.borderStrong
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        // One element for TalkBack: "Until, 1 Mar 2027", a button that opens the calendar.
        Column(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .testTag(PAUSE_UNTIL_TAG)
                    .clickable(enabled = !form.working, role = Role.Button) { open = !open }
                    .semantics(mergeDescendants = true) { if (form.dateRefused) error(refused) },
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Text(
                text = stringResource(R.string.pause_until),
                style = Typography.body.textStyle(),
                color = colors.textSecondary,
            )
            Row(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                        .background(colors.field01)
                        .then(
                            if (form.dateRefused) {
                                Modifier.border(2.dp, colors.supportError)
                            } else {
                                Modifier.drawBehind {
                                    drawLine(
                                        edge,
                                        Offset(0f, size.height),
                                        Offset(size.width, size.height),
                                        1.dp.toPx(),
                                    )
                                }
                            },
                        ).padding(horizontal = Spacing.STEP_5.dp, vertical = Spacing.STEP_4.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
            ) {
                Text(
                    text =
                        picked?.let { Formats.date(it.toLocalDate(), locale) }
                            ?: stringResource(R.string.pause_until_empty),
                    style = Typography.bodyLg.textStyle(),
                    color = if (picked != null) colors.textPrimary else colors.textHelper,
                    modifier = Modifier.weight(1f),
                )
                if (form.dateRefused) {
                    Icon(
                        ColdframeIcons.errorFilled,
                        contentDescription = null,
                        tint = colors.supportErrorText,
                        modifier = Modifier.size(16.dp),
                    )
                }
            }
            Text(
                text = if (form.dateRefused) refused else stringResource(R.string.pause_until_helper),
                style = Typography.helper.textStyle(),
                color = if (form.dateRefused) colors.supportErrorText else colors.textHelper,
            )
        }
        if (picked != null) {
            ColdframeButton(
                label = stringResource(R.string.pause_until_clear),
                onClick = {
                    open = false
                    actions.setUntil(null)
                },
                variant = ButtonVariant.Ghost,
                working = form.working,
            )
        }
        if (open && !form.working) {
            UntilCalendar(
                picked = picked,
                displayedMonth = displayedMonth,
                onPick = { date ->
                    open = false
                    actions.setUntil(date.iso)
                },
            )
        }
    }
}

/**
 * The Material calendar, drawn inside the sheet. Its selection is a calendar date at UTC midnight,
 * which is read back as that date: no time zone of the phone or of the User is applied. Which dates
 * are allowed is the Server's rule, so none is greyed out here.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun UntilCalendar(
    picked: PauseDate?,
    displayedMonth: LocalDate?,
    onPick: (PauseDate) -> Unit,
) {
    val colors = Coldframe.colors
    val shown = picked?.toLocalDate() ?: displayedMonth
    val state =
        rememberDatePickerState(
            initialSelectedDateMillis = picked?.toLocalDate()?.utcMillis(),
            initialDisplayedMonthMillis = shown?.withDayOfMonth(1)?.utcMillis(),
        )
    val pick by rememberUpdatedState(onPick)
    LaunchedEffect(state) {
        // The first value is the date the calendar opened with; only a tap picks.
        snapshotFlow { state.selectedDateMillis }.drop(1).collect { millis ->
            if (millis != null) {
                val date = Instant.ofEpochMilli(millis).atOffset(ZoneOffset.UTC).toLocalDate()
                PauseDate.of(date.year, date.monthValue, date.dayOfMonth)?.let { pick(it) }
            }
        }
    }
    DatePicker(
        state = state,
        modifier = Modifier.fillMaxWidth().testTag(PAUSE_CALENDAR_TAG),
        title = null,
        headline = null,
        showModeToggle = false,
        colors =
            DatePickerDefaults.colors(
                containerColor = colors.background,
                selectedDayContainerColor = colors.primary,
                selectedDayContentColor = colors.inkOnBright,
                todayContentColor = colors.textPrimary,
                todayDateBorderColor = colors.borderStrong,
                dayContentColor = colors.textPrimary,
                weekdayContentColor = colors.textSecondary,
                navigationContentColor = colors.textPrimary,
                yearContentColor = colors.textPrimary,
                currentYearContentColor = colors.textPrimary,
                selectedYearContainerColor = colors.primary,
                selectedYearContentColor = colors.inkOnBright,
            ),
    )
}

private fun PauseDate.toLocalDate(): LocalDate = LocalDate.of(year, month, day)

private fun LocalDate.utcMillis(): Long = atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()

/**
 * A Resume that did not happen, as an Inline notice on the surface it was asked on: the surface's
 * "can't reach" copy with Try again, or "You can't change this on ‹Site›…" after a 403.
 */
@Composable
fun PauseFailureNotice(
    failure: PauseFailure,
    actions: PauseActions,
    modifier: Modifier = Modifier,
    @StringRes unreachable: Int = R.string.notice_unreachable,
) {
    InlineNotice(
        message = stringResource(failure.notice.message(unreachable), failure.siteName),
        modifier = modifier,
        action =
            if (failure.notice.tryAgain) {
                NoticeActionUi(stringResource(R.string.notice_try_again), actions.retry)
            } else {
                null
            },
        announcement = Announcement.Assertive,
    )
}
