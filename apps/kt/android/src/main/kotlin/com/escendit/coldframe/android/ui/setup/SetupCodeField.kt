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
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Setup code field (UX-DR41): a text input in `meta-mono`, format-agnostic, auto-uppercase and
 * without autocorrect; the core normalizes what was typed. On success an "Accepted" chip
 * (`layer-01`, checkmark) sits at the field's right and the field is read-only. An [error]
 * replaces the helper, draws the invalid border and is exposed as the field's error.
 */
@Composable
fun SetupCodeField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    helper: String,
    acceptedLabel: String,
    accepted: Boolean,
    modifier: Modifier = Modifier,
    error: String? = null,
) {
    val colors = Coldframe.colors
    val edge = colors.borderStrong
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        Text(text = label, style = Typography.body.textStyle(), color = colors.textSecondary)
        Row(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                    .background(colors.field01)
                    .then(
                        if (error != null) {
                            Modifier.border(2.dp, colors.supportError)
                        } else {
                            Modifier.drawBehind {
                                drawLine(edge, Offset(0f, size.height), Offset(size.width, size.height), 1.dp.toPx())
                            }
                        },
                    ).padding(start = Spacing.STEP_5.dp, end = Spacing.STEP_3.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                readOnly = accepted,
                singleLine = true,
                textStyle = Typography.metaMono.textStyle().copy(color = colors.textPrimary),
                cursorBrush = SolidColor(colors.textPrimary),
                keyboardOptions =
                    KeyboardOptions(
                        capitalization = KeyboardCapitalization.Characters,
                        autoCorrectEnabled = false,
                        keyboardType = KeyboardType.Ascii,
                    ),
                modifier =
                    Modifier
                        .weight(1f)
                        .padding(vertical = Spacing.STEP_4.dp)
                        .semantics {
                            contentDescription = label
                            if (accepted) stateDescription = acceptedLabel
                            if (error != null) error(error)
                        },
            )
            if (error != null) {
                Icon(
                    ColdframeIcons.errorFilled,
                    contentDescription = null,
                    tint = colors.supportErrorText,
                    modifier = Modifier.size(16.dp),
                )
            }
            if (accepted) {
                Row(
                    modifier = Modifier.background(colors.layer01).padding(Spacing.STEP_3.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
                ) {
                    Icon(
                        ColdframeIcons.checkmark,
                        contentDescription = null,
                        tint = colors.textPrimary,
                        modifier = Modifier.size(16.dp),
                    )
                    Text(
                        text = styledText(acceptedLabel, Typography.statusLabel),
                        style = Typography.statusLabel.textStyle(),
                        color = colors.textPrimary,
                    )
                }
            }
        }
        Text(
            text = error ?: helper,
            style = Typography.helper.textStyle(),
            color = if (error != null) colors.supportErrorText else colors.textHelper,
        )
    }
}
