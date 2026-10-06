package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.ui.platform.LocalDensity
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
import com.escendit.coldframe.android.ui.components.dottedBorder
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** Font scale from which the Lot grid drops to one column (DESIGN.md Layout). */
const val ONE_COLUMN_FONT_SCALE = 1.5f

/**
 * The Garden Lot grid (UX-DR20): the Lots exactly in the order the Server sent them, never
 * re-sorted. Two columns, one from font scale 1.5; tiles are at least square and grow in height.
 * With [onAddNode] (Administrators and Owners) a *no Node* tile is a button that starts Add a Node
 * with its Lot; no other tile is tappable yet (Lot detail arrives later).
 */
@Composable
fun LotTiles(
    lots: List<LotSummary>,
    modifier: Modifier = Modifier,
    onAddNode: ((lotId: String) -> Unit)? = null,
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
                lots.chunked(columns).forEach { row ->
                    Row(
                        modifier = Modifier.fillMaxWidth().height(IntrinsicSize.Min),
                        horizontalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp),
                    ) {
                        row.forEach { lot ->
                            LotTile(
                                lot,
                                minHeight = side,
                                modifier = Modifier.weight(1f).fillMaxHeight(),
                                onAddNode = onAddNode?.let { add -> { add(lot.id) } },
                            )
                        }
                        repeat(columns - row.size) { Spacer(Modifier.weight(1f)) }
                    }
                }
            }
        }
    }
}

/**
 * One Lot tile (UX-DR18), one accessibility element. *no Node*: transparent, 1 dp dotted
 * `status-no-node-border`, `status-no-node-ink`, the `add` icon with "no Node", a large "+" and
 * the foot "add a Node", spoken "{lot}, no Node, add a Node". With [onAddNode] it is a button with
 * the same spoken label; without (a Member) it is not interactive. Any other status shows only the
 * name until its variant arrives (Epic 5); the client claims no status it was not given.
 */
@Composable
fun LotTile(
    lot: LotSummary,
    minHeight: Dp,
    modifier: Modifier = Modifier,
    onAddNode: (() -> Unit)? = null,
) {
    val colors = Coldframe.colors
    if (lot.status != LotStatus.NoNode) {
        Column(
            modifier =
                modifier
                    .heightIn(min = minHeight)
                    .border(1.dp, colors.borderSubtle)
                    .clearAndSetSemantics { contentDescription = lot.name }
                    .padding(Spacing.TILE_PADDING.dp),
        ) {
            Text(text = lot.name, style = Typography.tileName.textStyle(), color = colors.textPrimary)
        }
        return
    }
    val ink = colors.statusNoNodeInk
    val spoken = stringResource(R.string.lot_tile_description_no_node, lot.name)
    Column(
        modifier =
            modifier
                .heightIn(min = minHeight)
                .dottedBorder(colors.statusNoNodeBorder)
                .then(if (onAddNode != null) Modifier.clickable(role = Role.Button, onClick = onAddNode) else Modifier)
                .clearAndSetSemantics {
                    contentDescription = spoken
                    if (onAddNode != null) {
                        role = Role.Button
                        onClick {
                            onAddNode()
                            true
                        }
                    }
                }.padding(Spacing.TILE_PADDING.dp),
        verticalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(text = lot.name, style = Typography.tileName.textStyle(), color = ink)
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                Icon(ColdframeIcons.add, contentDescription = null, tint = ink, modifier = Modifier.size(16.dp))
                Text(
                    text = styledText(stringResource(R.string.lot_tile_no_node), Typography.statusLabel),
                    style = Typography.statusLabel.textStyle(),
                    color = ink,
                )
            }
        }
        Column(modifier = Modifier.padding(top = Spacing.STEP_3.dp)) {
            Text(
                text = stringResource(R.string.lot_tile_no_node_value),
                style = Typography.tileValue.textStyle(),
                color = ink,
            )
            Text(
                text = stringResource(R.string.lot_tile_add_node),
                style = Typography.metaMono.textStyle(),
                color = ink,
            )
        }
    }
}
