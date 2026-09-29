package com.escendit.coldframe.android.ui.sites

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.selected
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
import com.escendit.coldframe.android.ui.components.dashedBorder
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.TimeZoneProposal
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Create Site (UX-DR61): the Site name, the time-zone confirm panel (UX-DR61) and "Create Site",
 * whose working label replaces it in place. Shown on first sign-in with no Membership, and over
 * the shell from "New Site", where Cancel (top-left) and back return to the Garden. Every value
 * and decision comes from [form]; this screen only renders it.
 */
@Composable
fun CreateSiteScreen(
    form: CreateSiteForm,
    actions: SitesActions,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    BackHandler(enabled = form.cancellable && !form.working) { actions.cancelNewSite() }
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(colors.background)
                .safeDrawingPadding()
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        if (form.cancellable) {
            ColdframeButton(
                label = stringResource(R.string.modal_cancel),
                onClick = actions.cancelNewSite,
                variant = ButtonVariant.Ghost,
                working = form.working,
            )
        }
        Text(
            text = stringResource(R.string.create_site_title),
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        Text(
            text = stringResource(R.string.create_site_intro),
            style = Typography.body.textStyle(),
            color = colors.textPrimary,
        )
        TextInput(
            label = stringResource(R.string.create_site_name),
            value = form.name,
            onValueChange = actions.setName,
            helper = stringResource(R.string.create_site_name_helper),
            error = form.nameError?.let { stringResource(it.message()) },
        )
        TimeZonePanel(form.timeZone, actions)
        form.notice?.let { notice ->
            InlineNotice(
                message = stringResource(notice.createMessage()),
                action =
                    if (notice.tryAgain) {
                        NoticeActionUi(stringResource(R.string.notice_try_again), actions.submit)
                    } else {
                        null
                    },
                announcement = Announcement.Assertive,
            )
        }
        PrimaryButton(
            label = stringResource(R.string.create_site_action),
            onClick = actions.submit,
            working = form.working,
            workingLabel = stringResource(R.string.create_site_working),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/**
 * The time-zone confirm panel (UX-DR61): a 1 dp dashed `support-warning` box proposing the
 * detected zone, or naming the zone the user chose. Change shows a filter field over the IANA
 * IDs, which are never translated or reformatted.
 */
@Composable
fun TimeZonePanel(
    proposal: TimeZoneProposal,
    actions: SitesActions,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .dashedBorder(colors.supportWarning)
                .padding(Spacing.STEP_5.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Text(
            text = stringResource(R.string.time_zone_legend),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        Text(
            text =
                stringResource(
                    if (proposal.confirmed) R.string.time_zone_chosen else R.string.time_zone_question,
                    proposal.shown,
                ),
            style = Typography.bodyLg.textStyle(),
            color = colors.textPrimary,
        )
        Text(
            text = stringResource(R.string.time_zone_helper),
            style = Typography.helper.textStyle(),
            color = colors.textHelper,
        )
        if (!proposal.confirmed) {
            ColdframeButton(
                label = stringResource(R.string.time_zone_confirm),
                onClick = actions.confirmTimeZone,
                variant = ButtonVariant.Secondary,
            )
        }
        if (!proposal.changing) {
            ColdframeButton(
                label = stringResource(R.string.time_zone_change),
                onClick = actions.changeTimeZone,
                variant = ButtonVariant.Ghost,
            )
        } else {
            TimeZoneList(chosen = proposal.chosen, actions = actions)
        }
    }
}

@Composable
private fun TimeZoneList(
    chosen: String?,
    actions: SitesActions,
) {
    val colors = Coldframe.colors
    var filter by rememberSaveable { mutableStateOf("") }
    val zones = remember(actions) { actions.timeZones() }
    val needle = filter.trim().replace(' ', '_')
    val shown = zones.filter { it.contains(needle, ignoreCase = true) }
    TextInput(
        label = stringResource(R.string.time_zone_filter),
        value = filter,
        onValueChange = { filter = it },
        helper = stringResource(R.string.time_zone_filter_helper),
    )
    if (shown.isEmpty()) {
        Text(
            text = stringResource(R.string.time_zone_none),
            style = Typography.body.textStyle(),
            color = colors.textPrimary,
        )
    } else {
        // Bounded, so the list scrolls inside the panel while the form scrolls around it.
        LazyColumn(modifier = Modifier.fillMaxWidth().heightIn(max = 320.dp)) {
            items(shown, key = { it }) { zone ->
                val selected = zone == chosen
                Row(
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                            .clickable(role = Role.Button) { actions.pickTimeZone(zone) }
                            .semantics { this.selected = selected }
                            .padding(horizontal = Spacing.STEP_4.dp, vertical = Spacing.STEP_4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
                ) {
                    Text(
                        text = zone,
                        style = Typography.bodyLg.textStyle(),
                        color = colors.textPrimary,
                        modifier = Modifier.weight(1f),
                    )
                    if (selected) {
                        Icon(
                            ColdframeIcons.checkmark,
                            contentDescription = null,
                            tint = colors.primaryText,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
                HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
            }
        }
    }
}
