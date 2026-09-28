package com.escendit.coldframe.android.ui.settings

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Settings index (UX-DR71). In this story only Appearance and Account exist; My notifications,
 * Members and Site settings join above them, in that order, with their stories.
 */
@Composable
fun SettingsScreen(
    onOpenAppearance: () -> Unit,
    onSignOut: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    var confirmSignOut by rememberSaveable { mutableStateOf(false) }
    Column(modifier = modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        Column(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                    .clickable(role = Role.Button, onClick = onOpenAppearance)
                    .padding(horizontal = Spacing.GUTTER_MOBILE.dp, vertical = Spacing.STEP_4.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp),
        ) {
            Text(
                stringResource(R.string.settings_appearance),
                style = Typography.bodyLg.textStyle(),
                color = colors.textPrimary,
            )
            Text(
                stringResource(R.string.settings_appearance_helper),
                style = Typography.helper.textStyle(),
                color = colors.textHelper,
            )
        }
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        Column(
            modifier = Modifier.padding(Spacing.GUTTER_MOBILE.dp).padding(top = Spacing.STEP_5.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp),
        ) {
            Text(
                text = stringResource(R.string.settings_account),
                style = Typography.section.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.semantics { heading() },
            )
            ColdframeButton(
                label = stringResource(R.string.settings_sign_out),
                onClick = { confirmSignOut = true },
                variant = ButtonVariant.Secondary,
            )
        }
    }
    if (confirmSignOut) {
        // A native dialog that names the result (UX-DR113); Cancel changes nothing.
        AlertDialog(
            onDismissRequest = { confirmSignOut = false },
            title = {
                Text(
                    stringResource(R.string.settings_sign_out_question),
                    style = Typography.section.textStyle(),
                )
            },
            text = { Text(stringResource(R.string.settings_sign_out_detail), style = Typography.body.textStyle()) },
            confirmButton = {
                TextButton(
                    onClick = {
                        confirmSignOut = false
                        onSignOut()
                    },
                    modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
                ) {
                    Text(
                        styledText(stringResource(R.string.settings_sign_out), Typography.button),
                        style = Typography.button.textStyle(),
                        color = colors.primaryText,
                    )
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { confirmSignOut = false },
                    modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
                ) {
                    Text(
                        styledText(stringResource(R.string.modal_cancel), Typography.button),
                        style = Typography.button.textStyle(),
                        color = colors.primaryText,
                    )
                }
            },
            containerColor = colors.background,
            titleContentColor = colors.textPrimary,
            textContentColor = colors.textPrimary,
            tonalElevation = 0.dp,
        )
    }
}
