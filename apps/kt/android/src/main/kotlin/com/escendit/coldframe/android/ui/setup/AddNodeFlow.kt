package com.escendit.coldframe.android.ui.setup

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.sites.lotMessage
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.NodeAnnouncement
import com.escendit.coldframe.core.setup.NodeAnnouncementKind
import com.escendit.coldframe.core.setup.NodeOutcome
import com.escendit.coldframe.core.setup.NodeOutcomeKind
import com.escendit.coldframe.core.setup.NodeSetupState
import com.escendit.coldframe.core.setup.NodeSetupStep
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Add a Node (UX-DR67): press the setup button, pick the Node, its setup code, the Lot picker,
 * then the outcome, all from [state]. Full screen over the tab shell, in the same Setup flow
 * shell as Add a Hub; the screen stays on while the flow is open. Top-left is Cancel on step 1
 * and Back afterwards; system back asks before it leaves once a Node is selected. Announcements
 * (UX-DR105) are live regions.
 */
@Composable
fun AddNodeFlow(
    state: NodeSetupState,
    actions: NodeSetupActions,
    modifier: Modifier = Modifier,
) {
    SetupFlowEffects(
        open = state.open,
        keepAwake = state.keepAwake,
        radio = state.radio,
        onSystemBack = actions.leave,
        onRecheckRadio = actions.recheckRadio,
    )
    val node = state.nodeId.orEmpty()
    Box(modifier = modifier.fillMaxSize()) {
        val outcome = state.outcome
        if (outcome != null) {
            SetupOutcomeLayout(
                key = outcome.kind,
                success = outcome.success,
                eyebrow = stringResource(R.string.add_node_stopped, outcome.stoppedStep),
                copy = nodeOutcomeCopy(outcome.kind, node, state.lotName.orEmpty(), state.siteName),
                primaryLabel = stringResource(outcome.primary.label()),
                onPrimary = { actions.outcomeAction(outcome.primary) },
                secondaryLabel = outcome.secondary?.let { stringResource(it.label()) },
                onSecondary = { outcome.secondary?.let(actions.outcomeAction) },
            )
        } else {
            SetupFlowShell(
                step = state.step.number,
                total = NodeSetupStep.COUNT,
                title = stringResource(state.step.title()),
                showsCancel = state.showsCancel,
                onBack = actions.back,
            ) {
                when (state.step) {
                    NodeSetupStep.Press -> PressStep(actions)
                    NodeSetupStep.Scan -> ScanStep(state, actions)
                    NodeSetupStep.Code -> CodeStep(state, actions)
                    NodeSetupStep.Lot, NodeSetupStep.Outcome -> LotStep(state, actions)
                }
            }
        }
        val announcement = state.announcement
        SetupAnnouncementRegion(
            id = announcement?.id,
            message = announcement?.let { announcementText(it, state.siteName) }.orEmpty(),
            assertive = announcement?.assertive == true,
            onAnnouncing = actions.announcing,
        )
    }
    if (state.confirmingLeave) {
        SetupLeaveDialog(
            question = stringResource(R.string.add_node_leave_question, node),
            detail = stringResource(R.string.add_node_leave_detail),
            onConfirm = actions.confirmLeave,
            onStay = actions.stayInFlow,
        )
    }
}

@Composable
private fun PressStep(actions: NodeSetupActions) {
    Text(
        stringResource(R.string.add_node_press_body),
        style = Typography.body.textStyle(),
        color = Coldframe.colors.textPrimary,
    )
    PrimaryButton(
        label = stringResource(R.string.add_node_press_action),
        onClick = actions.continueFromPress,
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun ScanStep(
    state: NodeSetupState,
    actions: NodeSetupActions,
) {
    val colors = Coldframe.colors
    val context = LocalContext.current
    Text(stringResource(R.string.add_node_scan_intro), style = Typography.body.textStyle(), color = colors.textPrimary)
    when (state.radio) {
        RadioState.Ready -> {
            Unit
        }

        RadioState.Unsupported -> {
            InlineNotice(message = stringResource(R.string.add_node_bluetooth_unsupported))
        }

        RadioState.Off, RadioState.Unauthorized -> {
            InlineNotice(
                message = stringResource(R.string.add_node_bluetooth_needed),
                action =
                    NoticeActionUi(stringResource(R.string.add_node_open_settings)) {
                        context.openBluetoothSettings(state.radio == RadioState.Off)
                    },
                announcement = Announcement.Assertive,
            )
        }
    }
    if (state.noNodeYet) InlineNotice(message = stringResource(R.string.add_node_no_node))
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        state.candidates.forEach { candidate ->
            val signal = stringResource(candidate.signal.label())
            CandidateTile(
                name = stringResource(R.string.add_node_candidate, candidate.shortId),
                signal = signal,
                description =
                    if (candidate.pressedJustNow) {
                        stringResource(R.string.add_node_candidate_description_pressed, candidate.shortId, signal)
                    } else {
                        stringResource(R.string.add_node_candidate_description, candidate.shortId, signal)
                    },
                selected = candidate.peripheralId == state.selected?.peripheralId,
                onClick = { actions.select(candidate.peripheralId) },
                badge = if (candidate.pressedJustNow) stringResource(R.string.add_node_pressed) else null,
            )
        }
    }
    if (state.radio == RadioState.Ready) {
        Text(
            stringResource(R.string.add_node_still_scanning),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
    state.selected?.let { selected ->
        PrimaryButton(
            label = stringResource(R.string.add_node_select_action, selected.shortId),
            onClick = actions.continueFromScan,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun CodeStep(
    state: NodeSetupState,
    actions: NodeSetupActions,
) {
    val colors = Coldframe.colors
    val node = state.nodeId.orEmpty()
    Text(stringResource(R.string.add_node_code_intro), style = Typography.body.textStyle(), color = colors.textPrimary)
    SetupCodeField(
        label = stringResource(R.string.add_node_code_label),
        value = state.code.text,
        onValueChange = actions.setCode,
        helper = stringResource(R.string.add_node_code_helper),
        acceptedLabel = stringResource(R.string.add_node_code_accepted),
        accepted = state.code.accepted,
        error = state.code.error?.let { stringResource(it.nodeMessage(), node) },
    )
    state.deviceId?.let { deviceId ->
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp)) {
            Text(
                stringResource(R.string.add_node_device_id),
                style = Typography.helper.textStyle(),
                color = colors.textHelper,
            )
            Text(deviceId, style = Typography.metaMono.textStyle(), color = colors.textPrimary)
        }
    }
    if (state.code.accepted) {
        PrimaryButton(
            label = stringResource(R.string.add_node_code_continue),
            onClick = actions.continueFromCode,
            modifier = Modifier.fillMaxWidth(),
        )
    } else {
        PrimaryButton(
            label = stringResource(R.string.add_node_code_action),
            onClick = actions.submitCode,
            working = state.code.working,
            workingLabel = stringResource(R.string.add_node_code_working),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun LotStep(
    state: NodeSetupState,
    actions: NodeSetupActions,
) {
    val colors = Coldframe.colors
    val node = state.nodeId.orEmpty()
    val lots = state.lots
    Text(
        stringResource(R.string.add_node_lot_intro, node, state.siteName),
        style = Typography.body.textStyle(),
        color = colors.textPrimary,
    )
    lots.notice?.let { notice ->
        InlineNotice(
            message = stringResource(notice.kind.message(), notice.lotName.orEmpty()),
            action =
                if (lots.retryable) {
                    NoticeActionUi(stringResource(R.string.notice_try_again), actions.retryLots)
                } else {
                    null
                },
            announcement = Announcement.Assertive,
        )
    }
    val choices = lots.choices
    if (choices == null) {
        if (lots.notice == null) {
            Text(
                stringResource(R.string.add_node_lots_loading, state.siteName),
                style = Typography.body.textStyle(),
                color = colors.textSecondary,
            )
        }
        return
    }
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        val hasNode = stringResource(R.string.add_node_lot_has_node)
        choices.forEach { lot ->
            LotPickerRow(
                name = lot.name,
                description =
                    if (lot.selectable) {
                        lot.name
                    } else {
                        stringResource(R.string.add_node_lot_description_has_node, lot.name)
                    },
                selectable = lot.selectable,
                selected = lot.id == lots.selectedId,
                reason = hasNode,
                onClick = { actions.chooseLot(lot.id) },
            )
        }
        if (!lots.newLotOpen) {
            NewLotTile(label = stringResource(R.string.add_node_new_lot), onClick = actions.openNewLot)
        }
    }
    if (lots.newLotOpen) {
        // The same field, check and copy as Create Lot in Site settings.
        TextInput(
            label = stringResource(R.string.site_settings_lot_name),
            value = lots.newLotName,
            onValueChange = actions.setNewLotName,
            helper = stringResource(R.string.site_settings_lot_name_helper),
            error = lots.newLotError?.let { stringResource(it.lotMessage()) },
        )
        PrimaryButton(
            label = stringResource(R.string.site_settings_create_lot),
            onClick = actions.createLot,
            working = lots.creating,
            workingLabel = stringResource(R.string.site_settings_creating_lot),
            modifier = Modifier.fillMaxWidth(),
        )
    }
    lots.selected?.let { lot ->
        PrimaryButton(
            label = stringResource(R.string.add_node_assign_action, node, lot.name),
            onClick = actions.assign,
            working = lots.assigning,
            workingLabel = stringResource(R.string.add_node_assign_working, node, lot.name),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/** The copy of an outcome for Node [node] in [lot] on [siteName]. */
@Composable
fun nodeOutcomeCopy(
    kind: NodeOutcomeKind,
    node: String,
    lot: String,
    siteName: String,
): OutcomeCopy =
    when (kind) {
        NodeOutcomeKind.Assigned -> {
            OutcomeCopy(
                stringResource(R.string.add_node_assigned_title, lot),
                stringResource(R.string.add_node_assigned_body, node, lot),
            )
        }

        NodeOutcomeKind.StoppedListening -> {
            OutcomeCopy(
                stringResource(R.string.add_node_stopped_listening_title, node),
                stringResource(R.string.add_node_stopped_listening_body),
            )
        }

        NodeOutcomeKind.LostConnection -> {
            OutcomeCopy(
                stringResource(R.string.add_node_lost_title, node),
                stringResource(R.string.add_node_lost_body),
            )
        }

        NodeOutcomeKind.NodeRefused -> {
            OutcomeCopy(
                stringResource(R.string.add_node_refused_title, node),
                stringResource(R.string.add_node_refused_body),
            )
        }

        NodeOutcomeKind.ServerUnreachable -> {
            OutcomeCopy(
                stringResource(R.string.add_node_unreachable_title),
                stringResource(R.string.add_node_unreachable_body, node),
            )
        }

        NodeOutcomeKind.FingerprintMismatch -> {
            OutcomeCopy(
                stringResource(R.string.add_node_fingerprint_title),
                stringResource(R.string.add_node_fingerprint_body),
            )
        }

        NodeOutcomeKind.AlreadyAssigned -> {
            OutcomeCopy(
                stringResource(R.string.add_node_already_assigned_title, node),
                stringResource(R.string.add_node_already_assigned_body),
            )
        }

        NodeOutcomeKind.OnAnotherSite -> {
            OutcomeCopy(
                stringResource(R.string.add_node_on_another_site_title, node),
                stringResource(R.string.add_node_on_another_site_body),
            )
        }

        NodeOutcomeKind.NotAllowed -> {
            OutcomeCopy(
                stringResource(R.string.add_node_not_allowed_title, siteName),
                stringResource(R.string.add_node_not_allowed_body),
            )
        }
    }

@Composable
private fun announcementText(
    announcement: NodeAnnouncement,
    siteName: String,
): String {
    val node = announcement.node.orEmpty()
    val lot = announcement.lot.orEmpty()
    return when (announcement.kind) {
        NodeAnnouncementKind.CandidateFound -> {
            stringResource(R.string.add_node_found, node, stringResource((announcement.signal ?: return "").label()))
        }

        NodeAnnouncementKind.WrongCode -> {
            stringResource(R.string.add_node_code_wrong, node)
        }

        // The Lot-taken notice on step 4 is an assertive live region itself; said once, there.
        NodeAnnouncementKind.LotTaken -> {
            ""
        }

        NodeAnnouncementKind.Assigned -> {
            stringResource(R.string.add_node_announce_assigned, lot)
        }

        NodeAnnouncementKind.Error -> {
            val kind = announcement.outcome ?: return ""
            val copy = nodeOutcomeCopy(kind, node, lot, siteName)
            stringResource(
                R.string.add_node_announce_error,
                copy.title.trimEnd('.'),
                stringResource(NodeOutcome(kind, 0).primary.label()),
            )
        }
    }
}
