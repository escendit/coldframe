package com.escendit.coldframe.android.ui.sites

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
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
import com.escendit.coldframe.android.ui.components.TimeZoneActions
import com.escendit.coldframe.android.ui.components.TimeZonePanel
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Create Site (UX-DR61): the Site name, the shared time-zone confirm panel (UX-DR48) and "Create Site",
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
        TimeZonePanel(
            shown = form.timeZone.shown,
            chosen = form.timeZone.chosen,
            changing = form.timeZone.changing,
            actions =
                TimeZoneActions(
                    confirm = actions.confirmTimeZone,
                    change = actions.changeTimeZone,
                    pick = actions.pickTimeZone,
                    zones = actions.timeZones,
                ),
        )
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
