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
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.dashedBorder
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.WifiNetworkRow
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Wi-Fi network row (UX-DR42): SSID left, security right in `meta-mono`; selected = 2 dp
 * `primary-text` border plus a checkmark. A WPA3-only or other unsupported network is hatched
 * with a dashed border, cannot be selected and says why inline.
 */
@Composable
fun WifiNetworkRowView(
    row: WifiNetworkRow,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val supported = row.security.supported
    val surface =
        when {
            !supported -> {
                Modifier
                    .hatched(
                        colors.statusHatchGround,
                        colors.statusHatchLine,
                    ).dashedBorder(colors.statusUnknownBorder)
            }

            selected -> {
                Modifier.background(colors.background).border(2.dp, colors.primaryText)
            }

            else -> {
                Modifier.background(colors.background).border(1.dp, colors.borderSubtle)
            }
        }
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                .then(surface)
                .then(
                    if (supported) {
                        Modifier.selectable(selected = selected, role = Role.RadioButton, onClick = onClick)
                    } else {
                        Modifier.semantics { disabled() }
                    },
                ).semantics(mergeDescendants = true) {}
                .padding(Spacing.STEP_4.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Text(
                text = row.ssid,
                style = Typography.bodyLg.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.weight(1f).plate(!supported, colors.statusHatchGround),
            )
            Text(
                text = stringResource(row.security.label()),
                style = Typography.metaMono.textStyle(),
                color = colors.textSecondary,
                modifier = Modifier.plate(!supported, colors.statusHatchGround),
            )
            if (selected) {
                Icon(
                    ColdframeIcons.checkmark,
                    contentDescription = null,
                    tint = colors.primaryText,
                    modifier = Modifier.size(20.dp),
                )
            }
        }
        if (!supported) {
            Text(
                text = stringResource(R.string.add_hub_wifi_unsupported),
                style = Typography.helper.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.plate(true, colors.statusHatchGround),
            )
        }
    }
}

/** Text on a hatched row sits on a solid plate, so the lines never cross it. */
private fun Modifier.plate(
    on: Boolean,
    ground: Color,
): Modifier = if (on) background(ground).padding(horizontal = Spacing.STEP_1.dp) else this

/** 135° hatch: 1.5 dp lines every 8 dp on [ground] (DESIGN.md Shapes, unknown). */
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
