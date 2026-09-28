package com.escendit.coldframe.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

data class Segment<T>(
    val value: T,
    val label: String,
)

/**
 * Segmented choice (UX-DR36): equal-width square segments on `layer-01`; the selected one is
 * orange with `ink-on-bright` and a leading checkmark, and is exposed as selected. Single select,
 * applied immediately. When the labels no longer fit side by side (large text) the segments wrap
 * to one per row instead of clipping (UX-DR126).
 */
@Composable
fun <T> SegmentedChoice(
    label: String,
    segments: List<Segment<T>>,
    selected: T,
    onSelect: (T) -> Unit,
    modifier: Modifier = Modifier,
    helper: String? = null,
) {
    val colors = Coldframe.colors
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        Text(
            text = label,
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
            modifier = Modifier.semantics { heading() },
        )
        EqualWidthWrap {
            segments.forEach { segment ->
                SegmentButton(
                    label = segment.label,
                    selected = segment.value == selected,
                    onClick = { if (segment.value != selected) onSelect(segment.value) },
                )
            }
        }
        if (helper != null) {
            Text(text = helper, style = Typography.helper.textStyle(), color = colors.textHelper)
        }
    }
}

@Composable
private fun SegmentButton(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
) {
    val colors = Coldframe.colors
    val interaction = remember { MutableInteractionSource() }
    val focused by interaction.collectIsFocusedAsState()
    val fill = if (selected) colors.primary else colors.layer01
    val ink = if (selected) colors.inkOnBright else colors.textPrimary
    Row(
        modifier =
            Modifier
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .focusRing(focused)
                .background(fill)
                .selectable(
                    selected = selected,
                    interactionSource = interaction,
                    indication = null,
                    role = Role.Button,
                    onClick = onClick,
                ).padding(horizontal = Spacing.STEP_4.dp, vertical = Spacing.STEP_4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
    ) {
        if (selected) {
            // Selection is never colour alone: the checkmark is the non-colour cue.
            Icon(ColdframeIcons.checkmark, contentDescription = null, tint = ink, modifier = Modifier.size(16.dp))
        }
        Text(text = styledText(label, Typography.button), style = Typography.button.textStyle(), color = ink)
    }
}

/** Children side by side at equal width when they all fit, otherwise one per row at full width. */
@Composable
private fun EqualWidthWrap(content: @Composable () -> Unit) {
    Layout(content = content) { measurables, constraints ->
        val gap = Spacing.STEP_1.dp.roundToPx()
        val count = measurables.size.coerceAtLeast(1)
        val widest = measurables.maxOfOrNull { it.maxIntrinsicWidth(Constraints.Infinity) } ?: 0
        val available = constraints.maxWidth
        val fits = widest * count + gap * (count - 1) <= available
        if (fits) {
            val width = (available - gap * (count - 1)) / count
            val tallest = measurables.maxOfOrNull { it.minIntrinsicHeight(width) } ?: 0
            val placeables = measurables.map { it.measure(Constraints.fixed(width, tallest)) }
            layout(available, tallest) {
                placeables.forEachIndexed { index, placeable -> placeable.place(index * (width + gap), 0) }
            }
        } else {
            val placeables = measurables.map { it.measure(Constraints(minWidth = available, maxWidth = available)) }
            val height = placeables.sumOf { it.height } + gap * (placeables.size - 1).coerceAtLeast(0)
            layout(available, height) {
                var y = 0
                placeables.forEach { placeable ->
                    placeable.place(0, y)
                    y += placeable.height + gap
                }
            }
        }
    }
}
