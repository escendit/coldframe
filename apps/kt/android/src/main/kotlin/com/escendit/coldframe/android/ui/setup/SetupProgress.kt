package com.escendit.coldframe.android.ui.setup

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.ProgressSegment
import com.escendit.coldframe.core.setup.ProgressState
import com.escendit.coldframe.core.setup.SegmentState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Setup progress (UX-DR40): BLUETOOTH · WI-FI SENT · JOINING… · SERVER. Done = `setup-done`
 * fill with a checkmark; active = orange fill with the static `in-progress` icon; pending = a
 * 1 dp `border-strong` outline. The four are one progress element ("Step 3 of 4, joining
 * Wi-Fi"); the elapsed time below is never announced.
 */
@Composable
fun SetupProgress(
    progress: ProgressState,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val total = ProgressSegment.entries.size
    val position = minOf(progress.reached + 1, total)
    val description =
        stringResource(
            R.string.add_hub_progress_description,
            position,
            total,
            stringResource(progress.active.activity()),
        )
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp)) {
        Column(
            modifier =
                Modifier.fillMaxWidth().clearAndSetSemantics {
                    contentDescription = description
                    progressBarRangeInfo = ProgressBarRangeInfo(progress.reached.toFloat(), 0f..total.toFloat(), total)
                },
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            ProgressSegment.entries.forEach { segment -> Segment(segment, progress.stateOf(segment)) }
        }
        val elapsed = progress.elapsedSeconds
        Text(
            text =
                if (elapsed < 60) {
                    stringResource(R.string.add_hub_elapsed_seconds, elapsed)
                } else {
                    stringResource(R.string.add_hub_elapsed_minutes, elapsed / 60, elapsed % 60)
                },
            style = Typography.metaMono.textStyle(),
            color = colors.textSecondary,
        )
        Text(
            text = stringResource(R.string.add_hub_usually),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
}

@Composable
private fun Segment(
    segment: ProgressSegment,
    state: SegmentState,
) {
    val colors = Coldframe.colors
    val (surface, ink) =
        when (state) {
            SegmentState.Done -> Modifier.background(colors.setupDone) to colors.inkOnBright
            SegmentState.Active -> Modifier.background(colors.primary) to colors.inkOnBright
            SegmentState.Pending -> Modifier.border(1.dp, colors.borderStrong) to colors.textPrimary
        }
    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .then(surface)
                .padding(horizontal = Spacing.STEP_4.dp, vertical = Spacing.STEP_3.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
    ) {
        when (state) {
            SegmentState.Done -> {
                Icon(ColdframeIcons.checkmark, contentDescription = null, tint = ink, modifier = Modifier.size(20.dp))
            }

            SegmentState.Active -> {
                Icon(ColdframeIcons.inProgress, contentDescription = null, tint = ink, modifier = Modifier.size(20.dp))
            }

            SegmentState.Pending -> {
                Unit
            }
        }
        Text(
            text = styledText(stringResource(segment.label()), Typography.statusLabel),
            style = Typography.statusLabel.textStyle(),
            color = ink,
        )
    }
}
