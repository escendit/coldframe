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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.OutcomeAction
import com.escendit.coldframe.core.setup.OutcomeKind
import com.escendit.coldframe.core.setup.SetupOutcome
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** The words an outcome screen shows, with every name filled in. */
data class OutcomeCopy(
    val title: String,
    val body: String,
    val help: String?,
)

/** The copy of [outcome] for Hub [hub] on [ssid], added to [siteName]. */
@Composable
fun outcomeCopy(
    outcome: SetupOutcome,
    hub: String,
    ssid: String,
    siteName: String,
    serverHost: String,
): OutcomeCopy {
    val (title, body) =
        when (outcome.kind) {
            OutcomeKind.Online -> {
                stringResource(R.string.add_hub_online_title) to
                    stringResource(R.string.add_hub_online_body, hub, ssid, siteName)
            }

            OutcomeKind.WrongPassword -> {
                stringResource(R.string.add_hub_wrong_password_title) to
                    stringResource(R.string.add_hub_wrong_password_body, hub, ssid)
            }

            OutcomeKind.NetworkNotFound -> {
                stringResource(R.string.add_hub_not_found_title, hub, ssid) to
                    stringResource(R.string.add_hub_not_found_body)
            }

            OutcomeKind.UnsupportedSecurity -> {
                stringResource(R.string.add_hub_unsupported_title, ssid) to
                    stringResource(R.string.add_hub_unsupported_body)
            }

            OutcomeKind.NoServer -> {
                stringResource(R.string.add_hub_no_server_title, hub, ssid) to
                    stringResource(R.string.add_hub_no_server_body)
            }

            OutcomeKind.Timeout -> {
                stringResource(R.string.add_hub_timeout_title, hub) to stringResource(R.string.add_hub_timeout_body)
            }

            OutcomeKind.LostConnection -> {
                stringResource(R.string.add_hub_lost_title, hub) to stringResource(R.string.add_hub_lost_body)
            }

            OutcomeKind.OnAnotherSite -> {
                stringResource(R.string.add_hub_on_another_site_title, hub) to
                    stringResource(R.string.add_hub_on_another_site_body)
            }

            OutcomeKind.NotAllowed -> {
                stringResource(R.string.add_hub_not_allowed_title, siteName) to
                    stringResource(R.string.add_hub_not_allowed_body)
            }

            OutcomeKind.SiteGone -> {
                stringResource(R.string.add_hub_site_gone_title, siteName) to
                    stringResource(R.string.add_hub_site_gone_body)
            }

            OutcomeKind.ServerUnreachable -> {
                stringResource(R.string.add_hub_unreachable_title) to
                    stringResource(R.string.add_hub_unreachable_body, hub)
            }

            OutcomeKind.FingerprintMismatch -> {
                stringResource(R.string.add_hub_fingerprint_title) to stringResource(R.string.add_hub_fingerprint_body)
            }

            OutcomeKind.HubRefused -> {
                stringResource(R.string.add_hub_refused_title, hub) to stringResource(R.string.add_hub_refused_body)
            }
        }
    val help =
        if (outcome.kind == OutcomeKind.NoServer && outcome.helpShown) {
            stringResource(R.string.add_hub_no_server_help, ssid, serverHost)
        } else {
            null
        }
    return OutcomeCopy(title, body, help)
}

/**
 * Outcome screens (UX-DR55): full screen, focus on the headline, one primary next action.
 * Success is full-bleed `support-success` with `ink-on-bright` ("Hub is online"); an error is
 * the neutral background with `error--filled` and the eyebrow "Step 5 stopped" in
 * `support-error-text`, a plain headline, and never dismisses itself.
 */
@Composable
fun SetupOutcomeScreen(
    outcome: SetupOutcome,
    copy: OutcomeCopy,
    onAction: (OutcomeAction) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val success = outcome.success
    val ink = if (success) colors.inkOnBright else colors.textPrimary
    val headlineFocus = remember { FocusRequester() }
    LaunchedEffect(outcome.kind) { headlineFocus.requestFocus() }
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(if (success) colors.supportSuccess else colors.background)
                .safeDrawingPadding()
                .semantics { paneTitle = copy.title }
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        if (!success) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
            ) {
                Icon(
                    ColdframeIcons.errorFilled,
                    contentDescription = null,
                    tint = colors.supportErrorText,
                    modifier = Modifier.size(20.dp),
                )
                Text(
                    text =
                        styledText(
                            stringResource(R.string.add_hub_stopped, outcome.stoppedStep),
                            Typography.statusLabel,
                        ),
                    style = Typography.statusLabel.textStyle(),
                    color = colors.supportErrorText,
                )
            }
        }
        Text(
            text = copy.title,
            style = Typography.headline.textStyle(),
            color = ink,
            modifier =
                Modifier
                    .fillMaxWidth()
                    .focusRequester(headlineFocus)
                    .focusable()
                    .semantics { heading() },
        )
        Text(text = copy.body, style = Typography.bodyLg.textStyle(), color = ink)
        copy.help?.let { Text(text = it, style = Typography.body.textStyle(), color = ink) }
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp)) {
            ColdframeButton(
                label = stringResource(outcome.primary.label()),
                onClick = { onAction(outcome.primary) },
                variant = if (success) ButtonVariant.Secondary else ButtonVariant.Primary,
                modifier = Modifier.fillMaxWidth(),
            )
            outcome.secondary?.let { secondary ->
                ColdframeButton(
                    label = stringResource(secondary.label()),
                    onClick = { onAction(secondary) },
                    variant = ButtonVariant.Ghost,
                )
            }
        }
    }
}
