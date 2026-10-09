package com.escendit.coldframe.android.ui.alerts

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.components.dashedBorder
import com.escendit.coldframe.android.ui.components.hatched
import com.escendit.coldframe.android.ui.components.plate
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeColors
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.alerts.AlertIcon
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.alerts.AlertVariant
import com.escendit.coldframe.designtokens.CarbonIcon
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** What fills a row. */
enum class AlertRowFill { Solid, Flat, Hatch, Empty }

/** How a row is outlined. */
enum class AlertRowBorder { None, Solid, Dashed }

/** Fill and outline of a variant. With colour and text removed, no two variants share one (UX-DR99). */
data class AlertRowShape(
    val fill: AlertRowFill,
    val border: AlertRowBorder,
    val borderWidthDp: Int,
)

/** DESIGN.md components `alert-row-*`. */
fun AlertVariant.rowShape(): AlertRowShape =
    when (this) {
        AlertVariant.NeedsWater -> AlertRowShape(AlertRowFill.Solid, AlertRowBorder.None, 0)
        AlertVariant.Threshold -> AlertRowShape(AlertRowFill.Flat, AlertRowBorder.Solid, 2)
        AlertVariant.Health -> AlertRowShape(AlertRowFill.Hatch, AlertRowBorder.Dashed, 1)
        AlertVariant.Closed -> AlertRowShape(AlertRowFill.Empty, AlertRowBorder.Solid, 1)
    }

/** The Carbon icon the core named for the row. */
fun AlertIcon.carbonIcon(): CarbonIcon =
    when (this) {
        AlertIcon.RainDrop -> CarbonIcon.RAIN_DROP
        AlertIcon.ArrowDown -> CarbonIcon.ARROW_DOWN
        AlertIcon.ArrowUp -> CarbonIcon.ARROW_UP
        AlertIcon.Help -> CarbonIcon.HELP
        AlertIcon.BatteryLow -> CarbonIcon.BATTERY_LOW
        AlertIcon.Tools -> CarbonIcon.TOOLS
    }

/** The tokens of one variant. Orange is the needs-water row's and no other's (UX-DR14). */
class AlertPaint(
    val ink: Color,
    val ground: Color = Color.Unspecified,
    val border: Color = Color.Unspecified,
    val hatchLine: Color = Color.Unspecified,
)

fun ColdframeColors.alertPaint(variant: AlertVariant): AlertPaint =
    when (variant) {
        AlertVariant.NeedsWater -> {
            AlertPaint(ink = statusWaterInk, ground = statusWaterFill)
        }

        AlertVariant.Threshold -> {
            AlertPaint(ink = textPrimary, ground = layer01, border = borderStrong)
        }

        AlertVariant.Health -> {
            AlertPaint(
                ink = textPrimary,
                ground = statusHatchGround,
                border = statusUnknownBorder,
                hatchLine = statusHatchLine,
            )
        }

        AlertVariant.Closed -> {
            AlertPaint(ink = textSecondary, border = borderSubtle)
        }
    }

private fun Modifier.rowSurface(
    shape: AlertRowShape,
    paint: AlertPaint,
): Modifier {
    val filled =
        when (shape.fill) {
            AlertRowFill.Solid, AlertRowFill.Flat -> background(paint.ground)
            AlertRowFill.Hatch -> hatched(paint.ground, paint.hatchLine)
            AlertRowFill.Empty -> this
        }
    val width = shape.borderWidthDp.dp
    return when (shape.border) {
        AlertRowBorder.None -> filled
        AlertRowBorder.Solid -> filled.border(width, paint.border)
        AlertRowBorder.Dashed -> filled.dashedBorder(paint.border, width)
    }
}

/**
 * One Alert (UX-DR25): the icon and the eyebrow over the title in `section` type. The whole row is
 * one tap target of at least 48 dp with one spoken label, and it has no other action (UX-DR26).
 * On the hatched Health row every text sits on a solid plate. Nothing is limited to a number of
 * lines, so large text wraps and the row grows.
 */
@Composable
fun AlertRow(
    alert: AlertSummary,
    copy: AlertsCopy,
    onOpen: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val shape = alert.variant.rowShape()
    val paint = colors.alertPaint(alert.variant)
    val plate = Modifier.plate(shape.fill == AlertRowFill.Hatch, colors.statusHatchGround)
    val spoken = copy.spoken(alert)
    val eyebrowStyle = Typography.statusLabel.textStyle()
    val iconSize = maxOf(16.dp, with(LocalDensity.current) { eyebrowStyle.fontSize.toDp() } + Spacing.STEP_1.dp)
    Box(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .rowSurface(shape, paint)
                .clickable(role = Role.Button, onClick = onOpen)
                .clearAndSetSemantics {
                    contentDescription = spoken
                    role = Role.Button
                    onClick {
                        onOpen()
                        true
                    }
                },
    ) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(Spacing.TILE_PADDING.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            Row(
                modifier = plate,
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                Icon(
                    ColdframeIcons.of(alert.icon.carbonIcon()),
                    contentDescription = null,
                    tint = paint.ink,
                    modifier = Modifier.size(iconSize),
                )
                Text(
                    text = styledText(copy.eyebrow(alert), Typography.statusLabel),
                    style = eyebrowStyle,
                    color = paint.ink,
                    modifier = Modifier.weight(1f, fill = false),
                )
            }
            Text(
                text = copy.title(alert),
                style = Typography.section.textStyle(),
                color = paint.ink,
                modifier = plate,
            )
        }
    }
}
