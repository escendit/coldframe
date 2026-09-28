package com.escendit.coldframe.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.intl.Locale
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.toUpperCase
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.TypeRole
import com.escendit.coldframe.designtokens.Typography

enum class ButtonVariant { Primary, Secondary, Ghost }

/** Uppercase by style: the catalogue string stays in sentence case (UX-DR124). */
@Composable
fun styledText(
    text: String,
    role: TypeRole,
): String = if (role.uppercase) text.toUpperCase(Locale.current) else text

/**
 * DS Button (UX-DR34): square, at least `button-height` (48 dp) tall, label naming the result.
 * While [working] the label is replaced in place by [workingLabel] and presses are ignored; never
 * a spinner. The label wraps and the button grows instead of clipping (UX-DR126).
 */
@Composable
fun ColdframeButton(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    variant: ButtonVariant = ButtonVariant.Primary,
    working: Boolean = false,
    workingLabel: String? = null,
) {
    val colors = Coldframe.colors
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val focused by interaction.collectIsFocusedAsState()
    val (fill, ink) =
        when (variant) {
            ButtonVariant.Primary -> (if (pressed) colors.primaryActive else colors.primary) to colors.inkOnBright
            ButtonVariant.Secondary -> colors.buttonSecondary to colors.textOnColor
            ButtonVariant.Ghost -> Color.Transparent to colors.primaryText
        }
    val shown = if (working && workingLabel != null) workingLabel else label
    val minHeight = Spacing.BUTTON_HEIGHT.dp
    Box(
        modifier =
            modifier
                .heightIn(min = minHeight)
                .widthIn(min = minHeight)
                .focusRing(focused)
                .background(fill)
                .clickable(
                    interactionSource = interaction,
                    indication = null,
                    enabled = !working,
                    role = Role.Button,
                    onClick = onClick,
                ).semantics { if (working) disabled() }
                .padding(horizontal = Spacing.STEP_5.dp, vertical = Spacing.STEP_4.dp),
        contentAlignment = Alignment.CenterStart,
    ) {
        Text(
            text = styledText(shown, Typography.button),
            style = Typography.button.textStyle(),
            color = ink,
            textAlign = TextAlign.Start,
        )
    }
}

/** The two-tone focus indicator: ring outside, gap between ring and fill, never orange. */
@Composable
fun Modifier.focusRing(focused: Boolean): Modifier {
    if (!focused) return this
    val colors = Coldframe.colors
    return this
        .border(Spacing.FOCUS_RING.dp, colors.focus)
        .padding(Spacing.FOCUS_RING.dp)
        .border(Spacing.FOCUS_OFFSET.dp, colors.focusGap)
        .padding(Spacing.FOCUS_OFFSET.dp)
}

/** The primary DS Button: orange fill, `ink-on-bright` label, working label in place. */
@Composable
fun PrimaryButton(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    working: Boolean = false,
    workingLabel: String? = null,
) {
    ColdframeButton(
        label = label,
        onClick = onClick,
        modifier = modifier,
        variant = ButtonVariant.Primary,
        working = working,
        workingLabel = workingLabel,
    )
}
