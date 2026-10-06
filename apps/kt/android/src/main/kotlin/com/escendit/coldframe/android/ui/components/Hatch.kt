package com.escendit.coldframe.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.designtokens.Spacing

/**
 * The hatch fill (UX-DR12, DESIGN.md Shapes): 135° lines, 1.5 dp wide, every 8 dp, on a solid
 * [ground]. It marks what the system cannot vouch for: unknown and needs-Calibration Lot tiles
 * and unsupported Wi-Fi rows. Text on it sits on a [plate]. The callers pass the
 * `status-hatch-ground` and `status-hatch-line` tokens.
 */
fun Modifier.hatched(
    ground: Color,
    line: Color,
): Modifier =
    clipToBounds().drawBehind {
        drawRect(ground)
        val step = 8.dp.toPx()
        val stroke = 1.5.dp.toPx()
        var x = -size.height
        while (x < size.width) {
            drawLine(line, Offset(x, size.height), Offset(x + size.height, 0f), stroke)
            x += step
        }
    }

/**
 * The solid text plate of a hatched surface: [ground] behind the text with a small inset, so
 * the hatch lines never cross a letter. With [on] false the text is left as it is.
 */
fun Modifier.plate(
    on: Boolean,
    ground: Color,
): Modifier = if (on) background(ground).padding(horizontal = Spacing.STEP_1.dp) else this
