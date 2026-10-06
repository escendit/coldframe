package com.escendit.coldframe.android.ui.setup

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.components.dottedBorder
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * One Lot of the Lot picker (UX-DR38): `selectable-tile` tokens with the Device candidate's
 * selected state (2 dp `primary-text` border plus a checkmark, exposed as selected). A Lot that
 * has a Node is disabled: its name is dimmed and [reason] ("Has a Node") says why, in words and
 * not by colour alone. One accessibility element that reads [description].
 */
@Composable
fun LotPickerRow(
    name: String,
    description: String,
    selectable: Boolean,
    selected: Boolean,
    reason: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .then(
                    when {
                        !selectable -> Modifier.border(1.dp, colors.borderSubtle)
                        selected -> Modifier.background(colors.layer01).border(2.dp, colors.primaryText)
                        else -> Modifier.background(colors.layer01)
                    },
                ).then(
                    if (selectable) {
                        Modifier.selectable(selected = selected, role = Role.RadioButton, onClick = onClick)
                    } else {
                        Modifier
                    },
                ).clearAndSetSemantics {
                    contentDescription = description
                    role = Role.RadioButton
                    this.selected = selected
                    if (selectable) {
                        onClick {
                            onClick()
                            true
                        }
                    } else {
                        disabled()
                    }
                }.padding(Spacing.TILE_PADDING.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(
                text = name,
                style = Typography.tileName.textStyle(),
                color = if (selectable) colors.textPrimary else colors.textSecondary,
            )
            if (!selectable) {
                Text(
                    text = styledText(reason, Typography.statusLabel),
                    style = Typography.statusLabel.textStyle(),
                    color = colors.textSecondary,
                )
            }
        }
        if (selected && selectable) {
            Icon(
                ColdframeIcons.checkmark,
                contentDescription = null,
                tint = colors.primaryText,
                modifier = Modifier.size(20.dp),
            )
        }
    }
}

/** "+ New Lot" (UX-DR38): a dotted tile that opens the inline name field. */
@Composable
fun NewLotTile(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .dottedBorder(colors.statusNoNodeBorder)
                .clickable(role = Role.Button, onClick = onClick)
                .padding(Spacing.TILE_PADDING.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, style = Typography.tileName.textStyle(), color = colors.textPrimary)
    }
}
