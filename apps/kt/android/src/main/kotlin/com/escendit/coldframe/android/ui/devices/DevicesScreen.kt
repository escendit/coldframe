package com.escendit.coldframe.android.ui.devices

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * The Devices tab (UX-DR30, UX-DR65): a "Hubs" section with one row per Hub, by Device ID. A row
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
                if (devices.hubs.isEmpty()) {
                    Text(
                        text = stringResource(R.string.devices_empty),
                        style = Typography.body.textStyle(),
                        color = Coldframe.colors.textSecondary,
                    )
                } else {
                    val locale = ComposeLocale.current.platformLocale
                    Hubs(devices.hubs, remember(devices) { now() }, zone, locale)
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
