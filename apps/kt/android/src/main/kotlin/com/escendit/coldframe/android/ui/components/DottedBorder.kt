package com.escendit.coldframe.android.ui.components

import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * A square dotted outline drawn inside the bounds: the *no Node* Lot tile (DESIGN.md Shapes).
 * Dots are as long as the stroke is wide, so it reads apart from the dashed first-run tiles.
 */
fun Modifier.dottedBorder(
    color: Color,
    width: Dp = 1.dp,
): Modifier =
    drawBehind {
        val stroke = width.toPx()
        val dot = stroke
        val gap = 2.dp.toPx()
        drawRect(
            color = color,
            topLeft = Offset(stroke / 2, stroke / 2),
            size = Size(size.width - stroke, size.height - stroke),
            style = Stroke(width = stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(dot, gap))),
        )
    }
