package com.escendit.coldframe.android.ui.alerts

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.alerts.AlertTarget
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import java.time.Instant
import java.time.ZoneId

/**
 * The Alerts tab (UX-DR64): open Alerts as "Threshold Alerts" then "Health Alerts", newest first in
 * each as the Server listed them, then "Closed" with the Alerts closed in the last 7 days. Without
 * an open Alert it says "No open Alerts.", followed by Closed when it has rows. A failed load shows
 * the notice and no rows; there is no stale mode. Pull-to-refresh reads the list again.
 *
 * A row opens Lot detail of its Lot ([onOpenLot]) or the Devices tab ([onOpenDevices]); which one is
 * the core's answer. [now] is the clock the times are told against, read once per shown list.
 * With [fill] false the surface takes only the height of its content (snapshots of the whole list).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AlertsScreen(
    alerts: AlertsState,
    actions: AlertsActions,
    onOpenLot: (lotId: String, name: String) -> Unit,
    onOpenDevices: () -> Unit,
    modifier: Modifier = Modifier,
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
    fill: Boolean = true,
) {
    val colors = Coldframe.colors
    val pull = rememberPullToRefreshState()
    val refreshing = (alerts as? AlertsState.Ready)?.refreshing == true
    val clock = remember(alerts) { now() }
    val copy = rememberAlertsCopy(clock, zone)
    val open: (AlertSummary) -> Unit = { alert ->
        when (alert.target) {
            AlertTarget.Lot -> onOpenLot(alert.lotId, alert.lotName)
            AlertTarget.Devices -> onOpenDevices()
        }
    }
    val size = if (fill) Modifier.fillMaxSize() else Modifier.fillMaxWidth()

    PullToRefreshBox(
        isRefreshing = refreshing,
        onRefresh = actions.refresh,
        modifier = modifier.then(size).background(colors.background),
        state = pull,
        indicator = {
            // A bar instead of a spinner (UX-DR114), as on the Site overview.
            if (refreshing || pull.distanceFraction >= 1f) {
                Box(
                    Modifier
                        .align(Alignment.TopCenter)
                        .fillMaxWidth()
                        .height(Spacing.STEP_2.dp)
                        .background(colors.primary),
                )
            }
        },
    ) {
        Column(
            modifier =
                size
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = Spacing.GUTTER_MOBILE.dp, vertical = Spacing.STEP_5.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
        ) {
            when (alerts) {
                is AlertsState.Ready -> {
                    val threshold = alerts.threshold
                    val health = alerts.health
                    val closed = alerts.closed
                    if (threshold.isEmpty() && health.isEmpty()) {
                        // Never "All good": the app cannot vouch for that (UX-DR82).
                        Text(
                            text = stringResource(R.string.alerts_empty),
                            style = Typography.body.textStyle(),
                            color = colors.textSecondary,
                        )
                    }
                    AlertGroupSection(R.string.alerts_group_threshold, threshold, copy, open)
                    AlertGroupSection(R.string.alerts_group_health, health, copy, open)
                    AlertGroupSection(R.string.alerts_group_closed, closed, copy, open)
                }

                is AlertsState.Failed -> {
                    InlineNotice(
                        message = stringResource(alerts.notice.message()),
                        action =
                            if (alerts.notice.tryAgain) {
                                NoticeActionUi(stringResource(R.string.notice_try_again), actions.load)
                            } else {
                                null
                            },
                        announcement = Announcement.Polite,
                    )
                }

                AlertsState.Idle, is AlertsState.Loading -> {
                    Unit
                }
            }
        }
    }
}

/** One group: its heading over its rows in the Server's order. A group without rows is not shown. */
@Composable
private fun AlertGroupSection(
    heading: Int,
    rows: List<AlertSummary>,
    copy: AlertsCopy,
    onOpen: (AlertSummary) -> Unit,
) {
    if (rows.isEmpty()) return
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        Text(
            text = stringResource(heading),
            style = Typography.section.textStyle(),
            color = Coldframe.colors.textPrimary,
            modifier = Modifier.padding(bottom = Spacing.STEP_2.dp).semantics { heading() },
        )
        rows.forEach { alert -> AlertRow(alert, copy, onOpen = { onOpen(alert) }) }
    }
}
