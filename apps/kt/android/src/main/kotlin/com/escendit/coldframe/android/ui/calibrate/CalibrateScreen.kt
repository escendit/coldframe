package com.escendit.coldframe.android.ui.calibrate

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.android.ui.setup.SetupAnnouncementRegion
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.calibrate.CalibrateAnnouncement
import com.escendit.coldframe.core.calibrate.CalibrateAnnouncementKind
import com.escendit.coldframe.core.calibrate.CalibrateState
import com.escendit.coldframe.core.calibrate.CalibrateStep
import com.escendit.coldframe.core.calibrate.CalibrationReading
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * Calibrate (UX-DR66), full screen: dry, then wet, then the confirmation. Every rule, state and
 * the moment a Reading is fresh come from [CalibrateState]; this only draws them (AD-14). The
 * waiting panel is never announced; a fresh Reading and the first percentage are, politely, from
 * the core's announcement ids. Resume after a Pause is the core's `offersResume`; no Resume call
 * exists until Epic 8, so the paused step only explains.
 */
@Composable
fun CalibrateScreen(
    state: CalibrateState,
    actions: CalibrateActions,
    modifier: Modifier = Modifier,
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
) {
    val colors = Coldframe.colors
    val resources = LocalResources.current
    val locale = ComposeLocale.current.platformLocale
    val clock = remember(state) { now() }
    val lotName = state.lotNameOrEmpty()
    val title = stringResource(R.string.calibrate_title, lotName)
    val focus = remember { FocusRequester() }
    LaunchedEffect(state::class, (state as? CalibrateState.Ready)?.step) { focus.requestFocus() }
    BackHandler(enabled = state !is CalibrateState.Idle) { actions.close() }
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
        ColdframeButton(
            label = stringResource(R.string.nav_back),
            onClick = actions.close,
            variant = ButtonVariant.Ghost,
        )
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
            CalibrateState.Idle -> {
                Unit
            }

            is CalibrateState.Loading -> {
                Text(
                    text = stringResource(R.string.garden_loading, state.lotName),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }

            is CalibrateState.Failed -> {
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

            is CalibrateState.Paused -> {
                Text(
                    text =
                        stringResource(
                            if (state.bySite) R.string.calibrate_paused_site else R.string.calibrate_paused,
                        ),
                    style = Typography.bodyLg.textStyle(),
                    color = colors.textPrimary,
                )
            }

            is CalibrateState.Ready -> {
                val words = CalibrateWords(resources, clock, zone, locale)
                if (state.step == CalibrateStep.Confirm) {
                    Confirmation(state, actions, words)
                } else {
                    StepPanel(state, actions, words)
                }
                state.notice?.let {
                    InlineNotice(
                        message = stringResource(it.message(), state.site.name),
                        announcement = Announcement.Polite,
                    )
                }
                SetupAnnouncementRegion(
                    id = state.announcement?.id,
                    message = state.announcement?.let(words::announcement).orEmpty(),
                    assertive = false,
                    onAnnouncing = {},
                )
            }
        }
    }
}

private fun CalibrateState.lotNameOrEmpty(): String =
    when (this) {
        CalibrateState.Idle -> ""
        is CalibrateState.Loading -> lotName
        is CalibrateState.Failed -> lotName
        is CalibrateState.Paused -> lotName
        is CalibrateState.Ready -> lotName
    }

@Composable
private fun StepPanel(
    state: CalibrateState.Ready,
    actions: CalibrateActions,
    words: CalibrateWords,
) {
    val colors = Coldframe.colors
    val dry = state.step == CalibrateStep.Dry
    Text(
        text = stringResource(if (dry) R.string.calibrate_step_dry else R.string.calibrate_step_wet),
        style = Typography.section.textStyle(),
        color = colors.primaryText,
        modifier = Modifier.semantics { heading() },
    )
    Text(
        text = stringResource(if (dry) R.string.calibrate_intro_dry else R.string.calibrate_intro_wet),
        style = Typography.bodyLg.textStyle(),
        color = colors.textPrimary,
    )
    // The waiting panel: plain text, deliberately not a live region.
    Column(
        modifier =
            Modifier
                .fillMaxWidth()
                .background(colors.layer01)
                .border(1.dp, colors.borderSubtle)
                .padding(Spacing.TILE_PADDING.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
    ) {
        val fresh = state.fresh
        if (fresh == null) {
            Text(
                text = stringResource(R.string.calibrate_waiting),
                style = Typography.section.textStyle(),
                color = colors.textPrimary,
            )
        } else {
            Text(
                text = words.fresh(fresh),
                style = Typography.section.textStyle(),
                color = colors.textPrimary,
            )
        }
        Text(
            text = words.last(state.lastReading),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        Text(
            text = stringResource(R.string.calibrate_hint),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
    val record = stringResource(if (dry) R.string.calibrate_record_dry else R.string.calibrate_record_wet)
    ColdframeButton(
        label = record,
        onClick = actions.record,
        working = state.working || !state.canRecord,
        workingLabel = if (state.working) stringResource(R.string.calibrate_recording) else null,
        // Disabled until a Reading is chosen or fresh: dimmed, still in place (the core decides).
        modifier = Modifier.fillMaxWidth().alpha(if (state.canRecord || state.working) 1f else DISABLED_ALPHA),
    )
    RecentReadings(state, actions, words)
}

@Composable
private fun RecentReadings(
    state: CalibrateState.Ready,
    actions: CalibrateActions,
    words: CalibrateWords,
) {
    val colors = Coldframe.colors
    Text(
        text = stringResource(R.string.calibrate_recent),
        style = Typography.section.textStyle(),
        color = colors.textPrimary,
        modifier = Modifier.semantics { heading() },
    )
    if (state.readings.isEmpty()) {
        Text(
            text = stringResource(R.string.calibrate_recent_empty),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        return
    }
    val chosen = state.candidate?.readingSeq
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
        for (reading in state.readings) {
            val isChosen = reading.readingSeq == chosen
            val label = words.item(reading)
            Text(
                text = label,
                style = Typography.metaMono.textStyle(),
                color = if (isChosen) colors.primaryText else colors.textPrimary,
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .background(if (isChosen) colors.layer01 else colors.background)
                        .border(1.dp, if (isChosen) colors.borderStrong else colors.borderSubtle)
                        .clickable(role = Role.RadioButton, enabled = !state.working) {
                            actions.pick(reading.readingSeq)
                        }.semantics { selected = isChosen }
                        .padding(Spacing.STEP_4.dp),
            )
        }
    }
}

@Composable
private fun Confirmation(
    state: CalibrateState.Ready,
    actions: CalibrateActions,
    words: CalibrateWords,
) {
    val colors = Coldframe.colors
    Text(
        text = stringResource(R.string.calibrate_step_confirm),
        style = Typography.section.textStyle(),
        color = colors.primaryText,
        modifier = Modifier.semantics { heading() },
    )
    Text(
        text = stringResource(R.string.calibrate_confirm_points, state.dryRaw ?: 0, state.wetRaw ?: 0),
        style = Typography.bodyLg.textStyle(),
        color = colors.textPrimary,
    )
    // Updates in place: no percentage is shown before the Server stored a calibrated Reading.
    val percent = state.percent
    Text(
        text =
            if (percent == null) {
                stringResource(R.string.calibrate_confirm_pending)
            } else {
                stringResource(R.string.calibrate_confirm_reads, state.lotName, percent)
            },
        style = Typography.bodyLg.textStyle(),
        color = colors.textPrimary,
    )
    ColdframeButton(
        label = stringResource(R.string.add_node_done),
        onClick = actions.close,
        variant = ButtonVariant.Secondary,
        modifier = Modifier.fillMaxWidth(),
    )
}

/** The words of Calibrate for the phone's locale, told against [now] in [zone]. */
internal class CalibrateWords(
    private val resources: android.content.res.Resources,
    private val now: Instant,
    private val zone: ZoneId,
    private val locale: Locale,
) {
    private fun time(epochMs: Long?): String =
        epochMs?.let { Formats.whenText(Instant.ofEpochMilli(it), now, zone, locale) }.orEmpty()

    fun last(reading: CalibrationReading?): String =
        if (reading == null) {
            resources.getString(R.string.calibrate_none_yet)
        } else {
            resources.getString(R.string.calibrate_last, reading.rawValue.toString(), time(reading.measuredAtEpochMs))
        }

    fun fresh(reading: CalibrationReading): String =
        resources.getString(R.string.calibrate_fresh, time(reading.measuredAtEpochMs), reading.rawValue.toString())

    fun item(reading: CalibrationReading): String =
        resources.getString(
            R.string.calibrate_recent_item,
            time(reading.measuredAtEpochMs),
            reading.rawValue.toString(),
        )

    /** The polite announcement of a fresh Reading or of the first percentage. */
    fun announcement(announcement: CalibrateAnnouncement): String =
        when (announcement.kind) {
            CalibrateAnnouncementKind.FreshReading -> {
                resources.getString(
                    if (announcement.step == CalibrateStep.Dry) {
                        R.string.calibrate_announce_dry
                    } else {
                        R.string.calibrate_announce_wet
                    },
                    time(announcement.atEpochMs),
                    (announcement.rawValue ?: 0).toString(),
                )
            }

            CalibrateAnnouncementKind.FirstPercent -> {
                resources.getString(
                    R.string.calibrate_announce_percent,
                    announcement.lotName,
                    (announcement.percent ?: 0).toString(),
                )
            }
        }
}

private const val DISABLED_ALPHA = 0.4f
