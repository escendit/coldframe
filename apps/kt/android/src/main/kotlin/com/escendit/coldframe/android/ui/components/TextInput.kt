package com.escendit.coldframe.android.ui.components

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
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * DS TextInput (UX-DR35): label above, `field-01` fill with a 1 dp `border-strong` bottom edge,
 * helper below. An [error] replaces the helper, draws the 2 dp `support-error` border with the
 * `error--filled` icon and is exposed to TalkBack as the field's error. [password] adds a reveal
 * toggle with the Carbon `view` icon. Not used on the Sign-in surface, which has no field.
 */
@Composable
fun TextInput(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    helper: String? = null,
    error: String? = null,
    password: Boolean = false,
    singleLine: Boolean = false,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    onFocusChange: (Boolean) -> Unit = {},
) {
    val colors = Coldframe.colors
    var revealed by rememberSaveable { mutableStateOf(false) }
    val invalid = error != null
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
                        if (invalid) {
                            Modifier.border(2.dp, colors.supportError)
                        } else {
                            Modifier.drawBehind {
                                drawLine(edge, Offset(0f, size.height), Offset(size.width, size.height), 1.dp.toPx())
                            }
                        },
                    ).padding(start = Spacing.STEP_5.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                keyboardOptions = keyboardOptions,
                keyboardActions = keyboardActions,
                singleLine = singleLine,
                textStyle = Typography.bodyLg.textStyle().copy(color = colors.textPrimary),
                cursorBrush = SolidColor(colors.textPrimary),
                visualTransformation =
                    if (password &&
                        !revealed
                    ) {
                        PasswordVisualTransformation()
                    } else {
                        VisualTransformation.None
                    },
                modifier =
                    Modifier
                        .weight(1f)
                        .padding(vertical = Spacing.STEP_4.dp)
                        .onFocusChanged { onFocusChange(it.isFocused) }
                        .semantics {
                            contentDescription = label
                            if (error != null) error(error)
                        },
            )
            if (invalid) {
                Icon(
                    ColdframeIcons.errorFilled,
                    contentDescription = null,
                    tint = colors.supportErrorText,
                    modifier = Modifier.size(16.dp),
                )
            }
            if (password) {
                val toggle =
                    stringResource(if (revealed) R.string.field_hide_password else R.string.field_show_password)
                Box(
                    modifier =
                        Modifier
                            .sizeIn(minWidth = Spacing.BUTTON_HEIGHT.dp, minHeight = Spacing.BUTTON_HEIGHT.dp)
                            .clickable(role = Role.Button, onClickLabel = toggle) { revealed = !revealed }
                            .semantics { contentDescription = toggle },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        ColdframeIcons.view,
                        contentDescription = null,
                        tint = colors.textPrimary,
                        modifier = Modifier.size(20.dp),
                    )
                }
            }
        }
        val below = error ?: helper
        if (below != null) {
            Text(
                text = below,
                style = Typography.helper.textStyle(),
                color = if (invalid) colors.supportErrorText else colors.textHelper,
            )
        }
    }
}
