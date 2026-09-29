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
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.HubCandidate
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Device candidate tile (UX-DR37): `selectable-tile` tokens, "Hub 3F2A" in `meta-mono` and its
 * signal in words; selected = 2 dp `primary-text` border plus a checkmark, exposed as selected.
 * One accessibility element: "Hub 3F2A, strong signal".
 */
@Composable
fun CandidateTile(
    candidate: HubCandidate,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val name = stringResource(R.string.add_hub_candidate, candidate.shortId)
    val signal = stringResource(candidate.signal.label())
    val description = stringResource(R.string.add_hub_candidate_description, candidate.shortId, signal)
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .background(colors.layer01)
                .then(if (selected) Modifier.border(2.dp, colors.primaryText) else Modifier)
                .selectable(selected = selected, role = Role.RadioButton, onClick = onClick)
                .clearAndSetSemantics {
                    contentDescription = description
                    role = Role.RadioButton
                    this.selected = selected
                    onClick {
                        onClick()
                        true
                    }
                }.padding(Spacing.TILE_PADDING.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(text = name, style = Typography.metaMono.textStyle(), color = colors.textPrimary)
            Text(
                text = styledText(signal, Typography.statusLabel),
                style = Typography.statusLabel.textStyle(),
                color = colors.textSecondary,
            )
        }
        if (selected) {
            Icon(
                ColdframeIcons.checkmark,
                contentDescription = null,
                tint = colors.primaryText,
                modifier = Modifier.size(20.dp),
            )
        }
    }
}
