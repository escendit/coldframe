package com.escendit.coldframe.android.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
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
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.components.TextInput
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.loadMessage
import com.escendit.coldframe.android.ui.sites.lotMessage
import com.escendit.coldframe.android.ui.sites.message
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.RemoveLotConfirmation
import com.escendit.coldframe.core.lots.RenameLotForm
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Site settings (UX-DR74, UX-DR84): one surface for the Site name and its Lots. The Owner
 * renames the Site; Owners and Administrators create, rename (in a dialog) and remove (after a
 * dialog naming the Lot) Lots. Members see the name and the Lots read-only with one notice;
 * controls a Role cannot use are hidden, not disabled. Lots keep the Server's order.
 */
@Composable
fun SiteSettingsScreen(
    lots: LotsState,
    actions: LotsActions,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val base = modifier.fillMaxSize().background(colors.background)
    when (lots) {
        LotsState.Idle, is LotsState.Loading -> {
            Box(base)
        }

        is LotsState.Failed -> {
            Column(base.padding(Spacing.GUTTER_MOBILE.dp)) {
                InlineNotice(
                    message = stringResource(lots.notice.loadMessage()),
                    action =
                        if (lots.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.load)
                        } else {
                            null
                        },
                    announcement = Announcement.Assertive,
                )
            }
        }

        is LotsState.Ready -> {
            Ready(lots, actions, base)
        }
    }
}

@Composable
private fun Ready(
    state: LotsState.Ready,
    actions: LotsActions,
    modifier: Modifier,
) {
    val colors = Coldframe.colors
    val settings = state.settings
    Column(
        modifier = modifier.verticalScroll(rememberScrollState()).padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        state.notice?.let { notice ->
            InlineNotice(
                message = stringResource(notice.kind.message(), notice.subject.orEmpty()),
                announcement = Announcement.Assertive,
            )
        }
        if (settings.canRenameSite) {
            Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp)) {
                TextInput(
                    label = stringResource(R.string.site_settings_site_name),
                    value = state.siteName.draft,
                    onValueChange = actions.setSiteName,
                    helper = stringResource(R.string.site_settings_site_name_helper),
                    error = state.siteName.error?.let { stringResource(it.message()) },
                )
                PrimaryButton(
                    label = stringResource(R.string.site_settings_rename_site),
                    onClick = actions.renameSite,
                    working = state.siteName.working,
                    workingLabel = stringResource(R.string.site_settings_renaming_site),
                )
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
                Text(
                    text = stringResource(R.string.site_settings_site_name),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
                Text(text = state.site.name, style = Typography.bodyLg.textStyle(), color = colors.textPrimary)
            }
        }
        Text(
            text = stringResource(R.string.site_settings_lots),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        if (settings.readOnlyNotice) {
            InlineNotice(message = stringResource(R.string.site_settings_read_only))
        }
        if (settings.canEditLots) {
            Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_5.dp)) {
                TextInput(
                    label = stringResource(R.string.site_settings_lot_name),
                    value = state.create.name,
                    onValueChange = actions.setNewLotName,
                    helper = stringResource(R.string.site_settings_lot_name_helper),
                    error = state.create.error?.let { stringResource(it.lotMessage()) },
                )
                PrimaryButton(
                    label = stringResource(R.string.site_settings_create_lot),
                    onClick = actions.createLot,
                    working = state.create.working,
                    workingLabel = stringResource(R.string.site_settings_creating_lot),
                )
            }
        }
        LotList(state.lots, editable = settings.canEditLots, actions = actions)
    }
    state.renaming?.let { RenameLotDialog(it, actions) }
    state.removing?.let { RemoveLotDialog(it, actions) }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun LotList(
    lots: List<LotSummary>,
    editable: Boolean,
    actions: LotsActions,
) {
    val colors = Coldframe.colors
    if (lots.isEmpty()) {
        Text(
            text = stringResource(R.string.site_settings_no_lots),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        return
    }
    Column(modifier = Modifier.fillMaxWidth()) {
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        // The Server's order (UX-DR20); rows are never re-sorted here.
        lots.forEach { lot ->
            Column(
                modifier = Modifier.fillMaxWidth().padding(vertical = Spacing.STEP_4.dp),
                verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
            ) {
                Text(text = lot.name, style = Typography.bodyLg.textStyle(), color = colors.textPrimary)
                if (editable) {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
                        ColdframeButton(
                            label = stringResource(R.string.site_settings_rename_lot),
                            onClick = { actions.startRename(lot.id) },
                            variant = ButtonVariant.Ghost,
                        )
                        ColdframeButton(
                            label = stringResource(R.string.site_settings_remove_lot),
                            onClick = { actions.askRemove(lot.id) },
                            variant = ButtonVariant.Ghost,
                        )
                    }
                }
            }
            HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        }
    }
}

@Composable
private fun RenameLotDialog(
    form: RenameLotForm,
    actions: LotsActions,
) {
    SiteSettingsDialog(
        title = stringResource(R.string.site_settings_rename_lot),
        confirm =
            stringResource(
                if (form.working) R.string.site_settings_renaming_lot else R.string.site_settings_rename_lot,
            ),
        working = form.working,
        onConfirm = actions.rename,
        onDismiss = actions.cancelRename,
    ) {
        TextInput(
            label = stringResource(R.string.site_settings_lot_name),
            value = form.draft,
            onValueChange = actions.setRename,
            error = form.error?.let { stringResource(it.lotMessage()) },
        )
    }
}

@Composable
private fun RemoveLotDialog(
    confirmation: RemoveLotConfirmation,
    actions: LotsActions,
) {
    SiteSettingsDialog(
        title = stringResource(R.string.site_settings_remove_lot_question, confirmation.lotName),
        confirm =
            stringResource(
                if (confirmation.working) R.string.site_settings_removing_lot else R.string.site_settings_remove_lot,
            ),
        working = confirmation.working,
        onConfirm = actions.confirmRemove,
        onDismiss = actions.cancelRemove,
    ) {
        Text(stringResource(R.string.site_settings_remove_lot_detail), style = Typography.body.textStyle())
    }
}

/** A native dialog whose confirm button names the result and shows its working label in place (UX-DR113). */
@Composable
private fun SiteSettingsDialog(
    title: String,
    confirm: String,
    working: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    content: @Composable () -> Unit,
) {
    val colors = Coldframe.colors
    AlertDialog(
        onDismissRequest = { if (!working) onDismiss() },
        title = { Text(title, style = Typography.section.textStyle()) },
        text = content,
        confirmButton = {
            DialogButton(confirm, enabled = !working, color = colors.primaryText, onClick = onConfirm)
        },
        dismissButton = {
            if (!working) {
                DialogButton(
                    stringResource(R.string.site_settings_cancel),
                    enabled = true,
                    color = colors.primaryText,
                    onClick = onDismiss,
                )
            }
        },
        containerColor = colors.background,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textPrimary,
        tonalElevation = 0.dp,
    )
}

@Composable
private fun DialogButton(
    label: String,
    enabled: Boolean,
    color: Color,
    onClick: () -> Unit,
) {
    TextButton(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
    ) {
        Text(styledText(label, Typography.button), style = Typography.button.textStyle(), color = color)
    }
}
