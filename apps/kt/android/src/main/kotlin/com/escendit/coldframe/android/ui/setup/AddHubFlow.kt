package com.escendit.coldframe.android.ui.setup

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.view.accessibility.AccessibilityManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.AndroidRadioState
import com.escendit.coldframe.core.setup.AnnouncementKind
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.setup.NetworkSecurity
import com.escendit.coldframe.core.setup.OutcomeAction
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.core.setup.SetupAnnouncement
import com.escendit.coldframe.core.setup.SetupOutcome
import com.escendit.coldframe.core.setup.SetupStep
import com.escendit.coldframe.core.setup.WifiError
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.delay

/**
 * Add a Hub (UX-DR66): the five steps in the Setup flow shell, then an outcome screen, all from
 * [state]. Full screen over the tab shell; the screen stays on while the flow is open; system
 * back is the top-left action; the leave confirmation is a native dialog. Announcements
 * (UX-DR105) are live regions, and while TalkBack reads one the core's timeout waits (UX-DR103).
 */
@Composable
fun AddHubFlow(
    state: HubSetupState,
    actions: HubSetupActions,
    modifier: Modifier = Modifier,
) {
    SetupFlowEffects(
        open = state.open,
        keepAwake = state.keepAwake,
        radio = state.radio,
        onSystemBack = actions.back,
        onRecheckRadio = actions.recheckRadio,
    )

    val hub = state.hubId.orEmpty()
    val siteName =
        state.site.sites
            .firstOrNull { it.id == state.site.selectedId }
            ?.name
            .orEmpty()
    Box(modifier = modifier.fillMaxSize()) {
        val outcome = state.outcome
        if (outcome != null) {
            SetupOutcomeScreen(
                outcome = outcome,
                copy = outcomeCopy(outcome, hub, state.wifi.ssid, siteName, state.serverHost),
                onAction = actions.outcomeAction,
            )
        } else {
            SetupFlowShell(
                step = state.step.number,
                total = SetupStep.COUNT,
                title =
                    if (state.step == SetupStep.Progress) {
                        stringResource(state.step.title(), hub)
                    } else {
                        stringResource(state.step.title())
                    },
                showsCancel = state.showsCancel,
                onBack = if (state.showsCancel) actions.leave else actions.back,
            ) {
                when (state.step) {
                    SetupStep.Scan -> ScanStep(state, actions)
                    SetupStep.Code -> CodeStep(state, actions)
                    SetupStep.Wifi -> WifiStep(state, actions)
                    SetupStep.Site -> SiteStep(state, actions)
                    SetupStep.Progress -> SetupProgress(state.progress)
                }
            }
        }
        val announcement = state.announcement
        SetupAnnouncementRegion(
            id = announcement?.id,
            message = announcement?.let { announcementText(it, siteName) }.orEmpty(),
            assertive = announcement?.assertive == true,
            onAnnouncing = actions.announcing,
        )
    }
    if (state.confirmingLeave) {
        SetupLeaveDialog(
            question = stringResource(R.string.setup_leave_question, hub),
            detail = stringResource(R.string.setup_leave_detail),
            onConfirm = actions.confirmLeave,
            onStay = actions.stayInFlow,
        )
    }
}

@Composable
private fun ScanStep(
    state: HubSetupState,
    actions: HubSetupActions,
) {
    val colors = Coldframe.colors
    val context = LocalContext.current
    Text(stringResource(R.string.add_hub_scan_intro), style = Typography.body.textStyle(), color = colors.textPrimary)
    when (state.radio) {
        RadioState.Ready -> {
            Unit
        }

        RadioState.Unsupported -> {
            InlineNotice(message = stringResource(R.string.add_hub_bluetooth_unsupported))
        }

        RadioState.Off, RadioState.Unauthorized -> {
            InlineNotice(
                message = stringResource(R.string.add_hub_bluetooth_needed),
                action =
                    NoticeActionUi(stringResource(R.string.add_hub_open_settings)) {
                        context.openBluetoothSettings(state.radio == RadioState.Off)
                    },
                announcement = Announcement.Assertive,
            )
        }
    }
    if (state.noHubYet) InlineNotice(message = stringResource(R.string.add_hub_no_hub))
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        state.candidates.forEach { candidate ->
            val signal = stringResource(candidate.signal.label())
            CandidateTile(
                name = stringResource(R.string.add_hub_candidate, candidate.shortId),
                signal = signal,
                description = stringResource(R.string.add_hub_candidate_description, candidate.shortId, signal),
                selected = candidate.peripheralId == state.selected?.peripheralId,
                onClick = { actions.select(candidate.peripheralId) },
            )
        }
    }
    if (state.radio == RadioState.Ready) {
        Text(
            stringResource(R.string.add_hub_still_scanning),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
    state.selected?.let { selected ->
        PrimaryButton(
            label = stringResource(R.string.add_hub_select_action, selected.shortId),
            onClick = actions.continueFromScan,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun CodeStep(
    state: HubSetupState,
    actions: HubSetupActions,
) {
    val colors = Coldframe.colors
    val hub = state.hubId.orEmpty()
    Text(stringResource(R.string.add_hub_code_intro), style = Typography.body.textStyle(), color = colors.textPrimary)
    SetupCodeField(
        label = stringResource(R.string.add_hub_code_label),
        value = state.code.text,
        onValueChange = actions.setCode,
        helper = stringResource(R.string.add_hub_code_helper),
        acceptedLabel = stringResource(R.string.add_hub_code_accepted),
        accepted = state.code.accepted,
        error = state.code.error?.let { stringResource(it.message(), hub) },
    )
    state.identity?.let { identity ->
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(
                stringResource(R.string.add_hub_device_id),
                style = Typography.helper.textStyle(),
                color = colors.textHelper,
            )
            Text(identity.deviceId, style = Typography.metaMono.textStyle(), color = colors.textPrimary)
        }
    }
    if (state.code.accepted) {
        PrimaryButton(
            label = stringResource(R.string.add_hub_code_continue),
            onClick = actions.continueFromCode,
            modifier = Modifier.fillMaxWidth(),
        )
    } else {
        PrimaryButton(
            label = stringResource(R.string.add_hub_code_action),
            onClick = actions.submitCode,
            working = state.code.working,
            workingLabel = stringResource(R.string.add_hub_code_working),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun WifiStep(
    state: HubSetupState,
    actions: HubSetupActions,
) {
    val colors = Coldframe.colors
    val hub = state.hubId.orEmpty()
    val wifi = state.wifi
    val networks = wifi.networks
    if (networks == null) {
        Text(
            stringResource(R.string.add_hub_wifi_waiting, hub),
            style = Typography.body.textStyle(),
            color = colors.textPrimary,
        )
        return
    }
    Text(
        stringResource(R.string.add_hub_wifi_intro, hub),
        style = Typography.body.textStyle(),
        color = colors.textPrimary,
    )
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        networks.forEach { row ->
            WifiNetworkRowView(
                row = row,
                selected = !wifi.other && wifi.ssid == row.ssid,
                onClick = { actions.chooseNetwork(row.ssid) },
            )
        }
        ColdframeButton(
            label = stringResource(R.string.add_hub_wifi_other),
            onClick = actions.chooseOtherNetwork,
            variant = ButtonVariant.Ghost,
        )
    }
    val ssidError = wifi.error?.takeIf { it != WifiError.PasswordTooLong }
    if (wifi.other) {
        TextInput(
            label = stringResource(R.string.add_hub_wifi_ssid),
            value = wifi.ssid,
            onValueChange = actions.setOtherSsid,
            helper = stringResource(R.string.add_hub_wifi_ssid_helper),
            error = ssidError?.let { stringResource(it.message()) },
        )
    } else if (ssidError != null) {
        InlineNotice(message = stringResource(ssidError.message()), announcement = Announcement.Assertive)
    }
    if (wifi.chosenSecurity != NetworkSecurity.Open) {
        TextInput(
            label = stringResource(R.string.add_hub_wifi_password),
            value = wifi.password,
            onValueChange = actions.setPassword,
            helper = stringResource(R.string.add_hub_wifi_password_helper),
            error =
                wifi.error
                    ?.takeIf { it == WifiError.PasswordTooLong }
                    ?.let { stringResource(it.message()) },
            password = true,
        )
    }
    if (wifi.ssid.isNotEmpty() || wifi.other) {
        PrimaryButton(
            label =
                if (wifi.ssid.isEmpty()) {
                    stringResource(R.string.add_hub_wifi_action_other)
                } else {
                    stringResource(R.string.add_hub_wifi_action, wifi.ssid)
                },
            onClick = actions.continueFromWifi,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun SiteStep(
    state: HubSetupState,
    actions: HubSetupActions,
) {
    val colors = Coldframe.colors
    val hub = state.hubId.orEmpty()
    val site = state.site
    Text(
        stringResource(R.string.add_hub_site_intro, hub),
        style = Typography.body.textStyle(),
        color = colors.textPrimary,
    )
    if (site.sites.size > 1) {
        SegmentedChoice(
            label = stringResource(R.string.add_hub_title_site),
            segments = site.sites.map { Segment(it.id, it.name) },
            selected = site.selectedId.orEmpty(),
            onSelect = actions.chooseSite,
        )
    } else {
        site.sites.firstOrNull()?.let {
            Text(it.name, style = Typography.section.textStyle(), color = colors.textPrimary)
        }
    }
    val fingerprint = site.fingerprint
    val notice = site.notice
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
        Text(
            stringResource(R.string.add_hub_fingerprint_label),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        when {
            fingerprint != null -> {
                Text(
                    fingerprint.chunked(4).joinToString(" "),
                    style = Typography.metaMono.textStyle(),
                    color = colors.textPrimary,
                )
                Text(
                    stringResource(R.string.add_hub_fingerprint_helper, hub),
                    style = Typography.helper.textStyle(),
                    color = colors.textHelper,
                )
            }

            notice != null -> {
                InlineNotice(
                    message = stringResource(notice.message()),
                    action = NoticeActionUi(stringResource(R.string.notice_try_again), actions.retryKey),
                    announcement = Announcement.Assertive,
                )
            }

            else -> {
                Text(
                    stringResource(R.string.add_hub_fingerprint_loading),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }
        }
    }
    if (fingerprint != null && siteName(state).isNotEmpty()) {
        PrimaryButton(
            label = stringResource(R.string.add_hub_site_action, hub, siteName(state)),
            onClick = actions.start,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

private fun siteName(state: HubSetupState): String =
    state.site.sites
        .firstOrNull { it.id == state.site.selectedId }
        ?.name
        .orEmpty()

@Composable
private fun announcementText(
    announcement: SetupAnnouncement,
    siteName: String,
): String {
    val hub = announcement.hub.orEmpty()
    return when (announcement.kind) {
        AnnouncementKind.CandidateFound -> {
            stringResource(R.string.add_hub_found, hub, stringResource((announcement.signal ?: return "").label()))
        }

        AnnouncementKind.WifiSent -> {
            stringResource(R.string.add_hub_announce_wifi_sent, announcement.ssid.orEmpty())
        }

        AnnouncementKind.ServerSees -> {
            stringResource(R.string.add_hub_announce_server, hub)
        }

        AnnouncementKind.WrongCode -> {
            stringResource(R.string.add_hub_code_wrong, hub)
        }

        AnnouncementKind.Error -> {
            val kind = announcement.outcome ?: return ""
            val outcome = SetupOutcome(kind)
            val copy = outcomeCopy(outcome, hub, announcement.ssid.orEmpty(), siteName, "")
            stringResource(
                R.string.add_hub_announce_error,
                copy.title.trimEnd('.'),
                stringResource(outcome.primary.label()),
            )
        }
    }
}
