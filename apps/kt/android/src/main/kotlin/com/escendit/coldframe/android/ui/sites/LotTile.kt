package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.dashedBorder
import com.escendit.coldframe.android.ui.components.dottedBorder
import com.escendit.coldframe.android.ui.components.hatched
import com.escendit.coldframe.android.ui.components.plate
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeColors
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.lots.LotTile
import com.escendit.coldframe.core.lots.LotTileVariant
import com.escendit.coldframe.designtokens.CarbonIcon
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** Font scale from which the Lot grid drops to one column (DESIGN.md Layout). */
const val ONE_COLUMN_FONT_SCALE = 1.5f

/** Marks a skeleton tile for tests; it carries nothing a screen reader could focus. */
const val LOT_TILE_SKELETON_TAG = "lot-tile-skeleton"

/** How many outline tiles a cold start without stored Lots shows: two rows of the phone grid. */
private const val SKELETON_TILES = 4

/** What fills a tile (DESIGN.md Shapes). */
enum class TileFill {
    /** Solid status colour with the soil level and its edge: needs water. */
    Solid,

    /** Soil fill with the level and its edge: OK. */
    Soil,

    /** Hatch lines; the text sits on a plate: unknown, needs Calibration. */
    Hatch,

    /** One flat colour, no level: paused. */
    Flat,

    /** Nothing: no Node, stale. */
    Empty,
}

/** The outline of a tile (DESIGN.md Shapes). */
enum class TileBorder {
    None,
    Solid,
    Dashed,
    Dotted,
}

/** Fill and outline of a variant. With colour and text removed, no two variants share one (UX-DR99). */
data class TileShape(
    val fill: TileFill,
    val border: TileBorder,
    val borderWidthDp: Int,
)

fun LotTileVariant.shape(): TileShape =
    when (this) {
        LotTileVariant.NeedsWater -> TileShape(TileFill.Solid, TileBorder.None, 0)
        LotTileVariant.Ok -> TileShape(TileFill.Soil, TileBorder.Solid, 1)
        LotTileVariant.Unknown -> TileShape(TileFill.Hatch, TileBorder.Dashed, 1)
        LotTileVariant.NeedsCalibration -> TileShape(TileFill.Hatch, TileBorder.Dashed, 2)
        LotTileVariant.Paused -> TileShape(TileFill.Flat, TileBorder.Solid, 2)
        LotTileVariant.NoNode -> TileShape(TileFill.Empty, TileBorder.Dotted, 1)
        LotTileVariant.Stale -> TileShape(TileFill.Empty, TileBorder.Solid, 1)
    }

/** The one Carbon icon of a variant, shown before its status label. */
fun LotTileVariant.carbonIcon(): CarbonIcon =
    when (this) {
        LotTileVariant.NeedsWater -> CarbonIcon.RAIN_DROP
        LotTileVariant.Ok -> CarbonIcon.CHECKMARK_OUTLINE
        LotTileVariant.Unknown -> CarbonIcon.HELP
        LotTileVariant.NeedsCalibration -> CarbonIcon.TOOLS
        LotTileVariant.Paused -> CarbonIcon.PAUSE_OUTLINE
        LotTileVariant.NoNode -> CarbonIcon.ADD
        LotTileVariant.Stale -> CarbonIcon.CLOUD_OFFLINE
    }

/** The tokens of one variant (DESIGN.md components `lot-tile-*`). */
private class TilePaint(
    val ink: Color,
    val labelInk: Color = ink,
    val footInk: Color = ink,
    val ground: Color = Color.Unspecified,
    val level: Color = Color.Unspecified,
    val levelEdge: Color = Color.Unspecified,
    val lowMarker: Color = Color.Unspecified,
    val hatchLine: Color = Color.Unspecified,
    val border: Color = Color.Unspecified,
)

private fun ColdframeColors.paint(variant: LotTileVariant): TilePaint =
    when (variant) {
        LotTileVariant.NeedsWater -> {
            TilePaint(
                ink = statusWaterInk,
                ground = statusWaterFill,
                level = statusWaterLevel,
                levelEdge = statusWaterInk,
                lowMarker = statusWaterInk,
            )
        }

        LotTileVariant.Ok -> {
            TilePaint(
                ink = textPrimary,
                footInk = textSecondary,
                ground = statusOkFill,
                level = statusOkLevel,
                levelEdge = statusLevelEdge,
                lowMarker = statusLowMarker,
                border = statusOkBorder,
            )
        }

        LotTileVariant.Unknown -> {
            TilePaint(
                ink = textPrimary,
                footInk = textSecondary,
                ground = statusHatchGround,
                hatchLine = statusHatchLine,
                border = statusUnknownBorder,
            )
        }

        LotTileVariant.NeedsCalibration -> {
            TilePaint(
                ink = textPrimary,
                labelInk = statusCalibrationInk,
                footInk = textSecondary,
                ground = statusHatchGround,
                hatchLine = statusHatchLine,
                border = statusCalibrationBorder,
            )
        }

        LotTileVariant.Paused -> {
            TilePaint(ink = statusPausedInk, ground = statusPausedFill, border = statusPausedBorder)
        }

        LotTileVariant.NoNode -> {
            TilePaint(ink = statusNoNodeInk, border = statusNoNodeBorder)
        }

        LotTileVariant.Stale -> {
            TilePaint(ink = textSecondary, footInk = staleInk, border = staleBorder)
        }
    }

/**
 * Fill, soil level and outline of a tile. The level rises from the bottom edge to [soilPercent]
 * under a 2 dp edge; a 12 dp tick on the right edge marks [lowPercent]. Both are drawn only on a
 * measured tile, with the numbers the core gave.
 */
private fun Modifier.tileSurface(
    shape: TileShape,
    paint: TilePaint,
    soilPercent: Int?,
    lowPercent: Int?,
): Modifier {
    val filled =
        when (shape.fill) {
            TileFill.Hatch -> {
                hatched(paint.ground, paint.hatchLine)
            }

            TileFill.Flat -> {
                drawBehind { drawRect(paint.ground) }
            }

            TileFill.Solid, TileFill.Soil -> {
                drawBehind {
                    drawRect(paint.ground)
                    val edge = 2.dp.toPx()
                    if (soilPercent != null) {
                        val top = size.height * (1f - soilPercent / 100f)
                        drawRect(paint.level, topLeft = Offset(0f, top), size = Size(size.width, size.height - top))
                        drawRect(
                            paint.levelEdge,
                            topLeft = Offset(0f, (top - edge).coerceAtLeast(0f)),
                            size = Size(size.width, edge),
                        )
                    }
                    if (lowPercent != null) {
                        val tick = 12.dp.toPx()
                        val at = (size.height * (1f - lowPercent / 100f) - edge / 2).coerceIn(0f, size.height - edge)
                        drawRect(paint.lowMarker, topLeft = Offset(size.width - tick, at), size = Size(tick, edge))
                    }
                }
            }

            TileFill.Empty -> {
                this
            }
        }
    val width = shape.borderWidthDp.dp
    return when (shape.border) {
        TileBorder.None -> filled
        TileBorder.Solid -> filled.border(width, paint.border)
        TileBorder.Dashed -> filled.dashedBorder(paint.border, width)
        TileBorder.Dotted -> filled.dottedBorder(paint.border, width)
    }
}

/** The phone grid: two columns, one from font scale 1.5 (UX-DR97, UX-DR107). Cells are at least square. */
@Composable
private fun <T> TileGrid(
    items: List<T>,
    modifier: Modifier = Modifier,
    cell: @Composable (item: T, side: Dp, oneColumn: Boolean, modifier: Modifier) -> Unit,
) {
    val columns = if (LocalDensity.current.fontScale >= ONE_COLUMN_FONT_SCALE) 1 else 2
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        Text(
            text = stringResource(R.string.garden_lots),
            style = Typography.section.textStyle(),
            color = Coldframe.colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
            val side = (maxWidth - Spacing.TILE_GAP.dp * (columns - 1)) / columns
            Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
                items.chunked(columns).forEach { row ->
                    Row(
                        modifier = Modifier.fillMaxWidth().height(IntrinsicSize.Min),
                        horizontalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp),
                    ) {
                        row.forEach { item -> cell(item, side, columns == 1, Modifier.weight(1f).fillMaxHeight()) }
                        repeat(columns - row.size) { Spacer(Modifier.weight(1f)) }
                    }
                }
            }
        }
    }
}

/**
 * The Garden Lot grid (UX-DR20): one tile per Lot, exactly in the order the core's overview has
 * them, which is the Server's. Nothing here sorts, filters or reads a status. With [onAddNode], a
 * tile the core marks [LotTile.opensAddNode] starts Add a Node with its Lot; no other tile is a
 * tap target (Lot detail arrives later).
 */
@Composable
fun LotTiles(
    tiles: List<LotTile>,
    copy: OverviewCopy,
    modifier: Modifier = Modifier,
    onAddNode: ((lotId: String) -> Unit)? = null,
) {
    TileGrid(tiles, modifier) { tile, side, oneColumn, cell ->
        LotTileView(
            tile = tile,
            copy = copy,
            minHeight = side,
            oneColumn = oneColumn,
            modifier = cell,
            onAddNode = onAddNode?.let { add -> { add(tile.id) } },
        )
    }
}

/**
 * The cold start without stored Lots (UX-DR19, UX-DR80): `border-subtle` outlines with no icon,
 * label or value. They carry no semantics a screen reader could focus; the header says
 * "Loading ‹Site›".
 */
@Composable
fun SkeletonLotTiles(modifier: Modifier = Modifier) {
    val outline = Coldframe.colors.borderSubtle
    TileGrid(List(SKELETON_TILES) { it }, modifier) { _, side, _, cell ->
        Box(modifier = cell.heightIn(min = side).border(1.dp, outline).testTag(LOT_TILE_SKELETON_TAG))
    }
}

/**
 * One Lot tile (UX-DR17, UX-DR18, UX-DR19): the variant's fill, outline and Carbon icon, then the
 * words of [copy]. It is one accessibility element with the complete spoken label (UX-DR98). A
 * tile the core marks [LotTile.opensAddNode] is a button when [onAddNode] is given; every other
 * tile is not interactive.
 */
@Composable
fun LotTileView(
    tile: LotTile,
    copy: OverviewCopy,
    minHeight: Dp,
    oneColumn: Boolean,
    modifier: Modifier = Modifier,
    onAddNode: (() -> Unit)? = null,
) {
    val paint = Coldframe.colors.paint(tile.variant)
    val spoken = copy.spoken(tile)
    val act = onAddNode?.takeIf { tile.opensAddNode }
    Box(
        modifier =
            modifier
                .heightIn(min = minHeight)
                .tileSurface(tile.variant.shape(), paint, tile.soilPercent, tile.lowPercent)
                .then(if (act != null) Modifier.clickable(role = Role.Button, onClick = act) else Modifier)
                .clearAndSetSemantics {
                    contentDescription = spoken
                    if (act != null) {
                        role = Role.Button
                        onClick {
                            act()
                            true
                        }
                    }
                },
        propagateMinConstraints = true,
    ) {
        LotTileContent(tile, copy, oneColumn)
    }
}

/**
 * The text of a tile (UX-DR17): the Lot name over the status icon and label at the top, the big
 * value over the foot line at the bottom. In [oneColumn] the value follows the status label
 * directly and the tile grows downward. On a hatched tile every text sits on a solid plate.
 * Nothing is limited to a number of lines, so large text wraps and the tile grows.
 */
@Composable
internal fun LotTileContent(
    tile: LotTile,
    copy: OverviewCopy,
    oneColumn: Boolean,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val paint = colors.paint(tile.variant)
    val plated = tile.variant.shape().fill == TileFill.Hatch
    val plate = Modifier.plate(plated, colors.statusHatchGround)
    val value = copy.value(tile)
    val foot = copy.foot(tile)
    val labelStyle = Typography.statusLabel.textStyle()
    val iconSize = maxOf(16.dp, with(LocalDensity.current) { labelStyle.fontSize.toDp() } + Spacing.STEP_1.dp)
    Column(
        modifier = modifier.fillMaxWidth().padding(Spacing.TILE_PADDING.dp),
        verticalArrangement =
            if (oneColumn) Arrangement.spacedBy(Spacing.STEP_4.dp) else Arrangement.SpaceBetween,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(
                text = tile.name,
                style = Typography.tileName.textStyle(),
                color = paint.ink,
                modifier = plate,
            )
            Row(
                modifier = plate,
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                Icon(
                    ColdframeIcons.of(tile.variant.carbonIcon()),
                    contentDescription = null,
                    tint = paint.labelInk,
                    modifier = Modifier.size(iconSize),
                )
                Text(
                    text = styledText(copy.label(tile), Typography.statusLabel),
                    style = labelStyle,
                    color = paint.labelInk,
                    modifier = Modifier.weight(1f, fill = false),
                )
            }
        }
        if (value != null || foot != null) {
            Column(modifier = if (oneColumn) Modifier else Modifier.padding(top = Spacing.STEP_3.dp)) {
                if (value != null) {
                    Text(
                        text = value,
                        style = Typography.tileValue.textStyle(),
                        color = paint.ink,
                        modifier = plate,
                    )
                }
                if (foot != null) {
                    Text(
                        text = foot,
                        style = Typography.metaMono.textStyle(),
                        color = paint.footInk,
                        modifier = plate,
                    )
                }
            }
        }
    }
}
