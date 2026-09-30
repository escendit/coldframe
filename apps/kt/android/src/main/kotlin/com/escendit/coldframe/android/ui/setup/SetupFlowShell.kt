package com.escendit.coldframe.android.ui.setup

import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Setup flow shell (UX-DR39): Cancel (step 1) or Back top-left, the big "01 / 05" counter
 * (current in `primary-text`, total in `text-helper`), the step title in `headline`, then one
 * question per screen with its primary action directly below, not pinned. On every step change
 * focus moves to the title, and the pane title makes TalkBack read it.
 */
@Composable
fun SetupFlowShell(
    step: Int,
    total: Int,
    title: String,
    showsCancel: Boolean,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    val colors = Coldframe.colors
    val titleFocus = remember { FocusRequester() }
    LaunchedEffect(step, title) { titleFocus.requestFocus() }
    val counter = stringResource(R.string.setup_step_description, step, total)
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(colors.background)
                .safeDrawingPadding()
                .semantics { paneTitle = title }
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        ColdframeButton(
            label = stringResource(if (showsCancel) R.string.modal_cancel else R.string.nav_back),
            onClick = onBack,
            variant = ButtonVariant.Ghost,
        )
        Row(
            modifier = Modifier.clearAndSetSemantics { contentDescription = counter },
            verticalAlignment = Alignment.Bottom,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Text(
                text = twoDigits(step),
                style = Typography.stepCounter.textStyle(),
                color = colors.primaryText,
            )
            Text(
                text = stringResource(R.string.setup_step_of, twoDigits(total)),
                style = Typography.stepCounter.textStyle(),
                color = colors.textHelper,
            )
        }
        Text(
            text = title,
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
            modifier =
                Modifier
                    .fillMaxWidth()
                    .focusRequester(titleFocus)
                    .focusable()
                    .semantics { heading() },
        )
        content()
    }
}

/** "01" to "05": numbers, not copy. */
fun twoDigits(value: Int): String = value.toString().padStart(2, '0')
