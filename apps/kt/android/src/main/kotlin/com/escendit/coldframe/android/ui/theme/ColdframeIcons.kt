package com.escendit.coldframe.android.ui.theme

import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathBuilder
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.designtokens.CarbonIcon
import com.escendit.coldframe.designtokens.IconPath
import com.escendit.coldframe.designtokens.PathCommand

/** Carbon icons as Compose vectors, built from the generated path data (no other glyphs). */
object ColdframeIcons {
    private val cache = mutableMapOf<CarbonIcon, ImageVector>()

    fun of(icon: CarbonIcon): ImageVector = cache.getOrPut(icon) { build(icon) }

    val grid: ImageVector get() = of(CarbonIcon.GRID)
    val notification: ImageVector get() = of(CarbonIcon.NOTIFICATION)
    val box: ImageVector get() = of(CarbonIcon.BOX)
    val settings: ImageVector get() = of(CarbonIcon.SETTINGS)
    val checkmark: ImageVector get() = of(CarbonIcon.CHECKMARK)
    val errorFilled: ImageVector get() = of(CarbonIcon.ERROR_FILLED)
    val view: ImageVector get() = of(CarbonIcon.VIEW)
    val chevronDown: ImageVector get() = of(CarbonIcon.CHEVRON_DOWN)
    val overflowMenuVertical: ImageVector get() = of(CarbonIcon.OVERFLOW_MENU_VERTICAL)
    val add: ImageVector get() = of(CarbonIcon.ADD)
    val inProgress: ImageVector get() = of(CarbonIcon.IN_PROGRESS)

    private fun build(icon: CarbonIcon): ImageVector =
        ImageVector
            .Builder(
                name = icon.carbonName,
                defaultWidth = 24.dp,
                defaultHeight = 24.dp,
                viewportWidth = IconPath.VIEWPORT_SIZE,
                viewportHeight = IconPath.VIEWPORT_SIZE,
            ).path(fill = SolidColor(androidx.compose.ui.graphics.Color.Black)) {
                IconPath.parse(icon.pathData).forEach { draw(it) }
            }.build()

    private fun PathBuilder.draw(command: PathCommand) {
        when (command) {
            is PathCommand.MoveTo -> moveTo(command.x, command.y)
            is PathCommand.LineTo -> lineTo(command.x, command.y)
            is PathCommand.CubicTo -> curveTo(command.x1, command.y1, command.x2, command.y2, command.x, command.y)
            PathCommand.Close -> close()
        }
    }
}
