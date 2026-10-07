package com.escendit.coldframe.android.ui.devices

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.android.ui.setup.LotPickerRow
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.core.devices.MoveLotChoice
import com.escendit.coldframe.core.devices.NodeActionFailure
import com.escendit.coldframe.core.devices.NodeSummary
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * The Devices tab (UX-DR30, UX-DR65): a "Hubs" section with one row per Hub, by Device ID, then a "Nodes" section (Story 4.8). A row
 * shows the full Device ID in `meta-mono`, the status as a word with an icon, and when the Hub was
 * last seen. Whether a Hub is online is the Server's answer; nothing here computes it. A failed
 * load shows the notice and no rows. Add a Hub is the shell's header action.
 *
 * [now] is the clock the last-seen times are told against, read once per shown list.
 */
@Composable
fun DevicesScreen(
    devices: DevicesState,
    actions: DevicesActions,
    modifier: Modifier = Modifier,
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
) {
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = Spacing.GUTTER_MOBILE.dp, vertical = Spacing.STEP_5.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp),
    ) {
        when (devices) {
            is DevicesState.Ready -> {
                if (devices.hubs.isEmpty() && devices.nodes.isEmpty()) {
                    Text(
                        text = stringResource(R.string.devices_empty),
                        style = Typography.body.textStyle(),
                        color = Coldframe.colors.textSecondary,
                    )
                } else {
                    val locale = ComposeLocale.current.platformLocale
                    val clock = remember(devices) { now() }
                    if (devices.hubs.isNotEmpty()) Hubs(devices.hubs, clock, zone, locale)
                    if (devices.nodes.isNotEmpty()) Nodes(devices, actions, clock, zone, locale)
                }
            }

            is DevicesState.Failed -> {
                InlineNotice(
                    message = stringResource(devices.notice.message()),
                    action =
                        if (devices.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.load)
                        } else {
                            null
                        },
                    announcement = Announcement.Polite,
                )
            }

            // Nothing is shown while the list is read: an earlier answer's status is not kept.
            DevicesState.Idle, is DevicesState.Loading -> {
                Unit
            }
        }
    }
}

@Composable
private fun Hubs(
    hubs: List<HubSummary>,
    now: Instant,
    zone: ZoneId,
    locale: Locale,
) {
    val colors = Coldframe.colors
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = stringResource(R.string.devices_hubs),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.padding(bottom = Spacing.STEP_4.dp).semantics { heading() },
        )
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        hubs.forEach { hub ->
            HubRow(hub, now, zone, locale)
            HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        }
    }
}

/**
 * The "Nodes" section (UX-DR30), after Hubs, in the Server's order (Lot name, unassigned last): a
 * row per Node with its Device ID in `meta-mono`, the Lot name in `body-lg`, and its last seen,
 * battery and charging in `helper`. `battery--low` shows below 20 %. Nothing here sorts.
 */
@Composable
private fun Nodes(
    devices: DevicesState.Ready,
    actions: DevicesActions,
    now: Instant,
    zone: ZoneId,
    locale: Locale,
) {
    val colors = Coldframe.colors
    val manage = DevicesState.canManageNodes(devices.site.role)
    var moving by remember { mutableStateOf<NodeSummary?>(null) }
    var unassigning by remember { mutableStateOf<NodeSummary?>(null) }
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = stringResource(R.string.devices_nodes),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.padding(bottom = Spacing.STEP_4.dp).semantics { heading() },
        )
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        devices.nodes.forEach { node ->
            NodeRow(
                node = node,
                now = now,
                zone = zone,
                locale = locale,
                manage = manage,
                working = devices.workingNodeId == node.id,
                failure = devices.failure?.takeIf { it.nodeId == node.id },
                onMove = { moving = node },
                onUnassign = { unassigning = node },
            )
            HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        }
    }
    moving?.let { node ->
        MoveNodeDialog(
            node = node,
            choices = devices.choicesFor(node),
            onConfirm = { lotId ->
                moving = null
                actions.moveNode(node.id, lotId)
            },
            onDismiss = { moving = null },
        )
    }
    unassigning?.let { node ->
        UnassignNodeDialog(
            node = node,
            onConfirm = {
                unassigning = null
                actions.unassignNode(node.id)
            },
            onDismiss = { unassigning = null },
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun NodeRow(
    node: NodeSummary,
    now: Instant,
    zone: ZoneId,
    locale: Locale,
    manage: Boolean,
    working: Boolean,
    failure: NodeActionFailure?,
    onMove: () -> Unit,
    onUnassign: () -> Unit,
) {
    val colors = Coldframe.colors
    val lastSeen =
        node.lastSeenAtEpochMs?.let {
            stringResource(R.string.devices_last_seen, Formats.whenText(Instant.ofEpochMilli(it), now, zone, locale))
        } ?: stringResource(R.string.devices_not_seen)
    val battery = node.batteryPercent?.let { stringResource(R.string.lot_detail_value_percent, it.toString()) }
    val charging =
        when (node.charging) {
            ChargeState.Charging -> stringResource(R.string.devices_charging)
            ChargeState.NotCharging -> stringResource(R.string.devices_not_charging)
            null -> null
        }
    val iconSize =
        with(LocalDensity.current) {
            Typography.helper
                .textStyle()
                .fontSize
                .toDp()
        } + Spacing.STEP_2.dp
    val moveLabel = stringResource(R.string.devices_move_description, node.id)
    val unassignLabel = stringResource(R.string.devices_unassign_description, node.id)
    Column(
        modifier = Modifier.fillMaxWidth().padding(vertical = Spacing.STEP_4.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
    ) {
        // One element per row for TalkBack: the ID, the Lot, then last seen, battery and charging. The
        // actions below are also custom accessibility actions of this element (UX-DR31).
        Column(
            modifier =
                Modifier.fillMaxWidth().semantics(mergeDescendants = true) {
                    if (manage) {
                        customActions =
                            buildList {
                                add(
                                    CustomAccessibilityAction(moveLabel) {
                                        onMove()
                                        true
                                    },
                                )
                                if (node.lotId !=
                                    null
                                ) {
                                    add(
                                        CustomAccessibilityAction(unassignLabel) {
                                            onUnassign()
                                            true
                                        },
                                    )
                                }
                            }
                    }
                },
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            Text(text = node.id, style = Typography.metaMono.textStyle(), color = colors.textPrimary)
            Text(
                text = node.lotName ?: stringResource(R.string.devices_no_lot),
                style = Typography.bodyLg.textStyle(),
                color = colors.textPrimary,
            )
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp),
                verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                Text(text = lastSeen, style = Typography.helper.textStyle(), color = colors.textSecondary)
                if (battery != null) {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        if (node.batteryLow) {
                            Icon(
                                imageVector = ColdframeIcons.batteryLow,
                                contentDescription = null,
                                tint = colors.textPrimary,
                                modifier = Modifier.size(iconSize),
                            )
                        }
                        Text(text = battery, style = Typography.helper.textStyle(), color = colors.textSecondary)
                    }
                }
                if (charging != null) {
                    Text(text = charging, style = Typography.helper.textStyle(), color = colors.textSecondary)
                }
            }
        }
        // Admin+ only, hidden for a Member (UX-DR31); no Bluetooth is needed to move or unassign.
        if (manage) {
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
                verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                ColdframeButton(
                    label = stringResource(R.string.devices_move),
                    onClick = onMove,
                    variant = ButtonVariant.Ghost,
                    working = working,
                    modifier = Modifier.semantics { contentDescription = moveLabel },
                )
                if (node.lotId != null) {
                    ColdframeButton(
                        label = stringResource(R.string.devices_unassign),
                        onClick = onUnassign,
                        variant = ButtonVariant.Ghost,
                        working = working,
                        modifier = Modifier.semantics { contentDescription = unassignLabel },
                    )
                }
            }
        }
        if (failure != null) {
            InlineNotice(message = stringResource(failure.notice.message()), announcement = Announcement.Assertive)
        }
    }
}

/**
 * Move a Node (UX-DR31): the Lots in the Server's order. A Lot that has another Node is disabled with
 * "Has a Node", in words and not by colour alone, and the Node's own Lot says so; neither can be picked.
 */
@Composable
private fun MoveNodeDialog(
    node: NodeSummary,
    choices: List<MoveLotChoice>,
    onConfirm: (lotId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Coldframe.colors
    var selected by remember { mutableStateOf<String?>(null) }
    val hasNode = stringResource(R.string.add_node_lot_has_node)
    val currentLot = stringResource(R.string.devices_current_lot)
    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(stringResource(R.string.devices_move_choose, node.id), style = Typography.section.textStyle())
        },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp),
            ) {
                if (choices.isEmpty()) {
                    Text(
                        text = stringResource(R.string.devices_move_no_lots),
                        style = Typography.body.textStyle(),
                        color = colors.textSecondary,
                    )
                }
                choices.forEach { lot ->
                    LotPickerRow(
                        name = lot.name,
                        description =
                            when {
                                lot.current -> stringResource(R.string.devices_current_lot_description, lot.name)
                                lot.hasNode -> stringResource(R.string.add_node_lot_description_has_node, lot.name)
                                else -> lot.name
                            },
                        selectable = lot.selectable,
                        selected = lot.id == selected,
                        reason = if (lot.current) currentLot else hasNode,
                        onClick = { selected = lot.id },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { selected?.let(onConfirm) },
                enabled = selected != null,
                modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
            ) {
                Text(
                    styledText(stringResource(R.string.devices_move_confirm, node.id), Typography.button),
                    style = Typography.button.textStyle(),
                    color = colors.primaryText,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp)) {
                Text(
                    styledText(stringResource(R.string.devices_cancel), Typography.button),
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

/** Unassign confirms in a native dialog naming the Node, with the result as the action's verb (EXPERIENCE.md). */
@Composable
private fun UnassignNodeDialog(
    node: NodeSummary,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Coldframe.colors
    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(stringResource(R.string.devices_unassign_question, node.id), style = Typography.section.textStyle())
        },
        text = {
            Text(
                text =
                    stringResource(
                        R.string.devices_unassign_detail,
                        node.lotName ?: stringResource(R.string.devices_no_lot),
                    ),
                style = Typography.body.textStyle(),
            )
        },
        confirmButton = {
            TextButton(onClick = onConfirm, modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp)) {
                Text(
                    styledText(stringResource(R.string.devices_unassign), Typography.button),
                    style = Typography.button.textStyle(),
                    color = colors.primaryText,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp)) {
                Text(
                    styledText(stringResource(R.string.devices_cancel), Typography.button),
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

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun HubRow(
    hub: HubSummary,
    now: Instant,
    zone: ZoneId,
    locale: Locale,
) {
    val colors = Coldframe.colors
    val lastSeen =
        hub.lastSeenAtEpochMs?.let {
            stringResource(R.string.devices_last_seen, Formats.whenText(Instant.ofEpochMilli(it), now, zone, locale))
        } ?: stringResource(R.string.devices_not_seen)
    val iconSize =
        with(LocalDensity.current) {
            Typography.helper
                .textStyle()
                .fontSize
                .toDp()
        } + Spacing.STEP_2.dp
    // One element per row for TalkBack: the ID, the status word, then the last-seen time.
    Column(
        modifier =
            Modifier
                .fillMaxWidth()
                .padding(vertical = Spacing.STEP_4.dp)
                .semantics(mergeDescendants = true) {},
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
    ) {
        // The full Device ID, never shortened or reformatted.
        Text(text = hub.id, style = Typography.metaMono.textStyle(), color = colors.textPrimary)
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // The status is the word; the icon is its shape, never colour alone.
                Icon(
                    imageVector = if (hub.online) ColdframeIcons.checkmarkOutline else ColdframeIcons.help,
                    contentDescription = null,
                    tint = colors.textPrimary,
                    modifier = Modifier.size(iconSize),
                )
                Text(
                    text = stringResource(if (hub.online) R.string.devices_online else R.string.devices_offline),
                    style = Typography.helper.textStyle(),
                    color = colors.textPrimary,
                )
            }
            Text(text = lastSeen, style = Typography.helper.textStyle(), color = colors.textSecondary)
        }
    }
}
