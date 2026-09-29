package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.dashedBorder
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.sites.FirstRunSteps
import com.escendit.coldframe.core.sites.FirstRunTile
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.StepState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * The Garden of a Site without Readings (UX-DR62, UX-DR82): the Site summary header, "No
 * Readings yet" and the four first-run step tiles; no tile starts a flow yet, and Members also
 * see the read-only notice. Below them, the Site's Lots as tiles in the Server's order
 * (UX-DR18, UX-DR20); a failed Lot load shows its notice in place of the grid. The Site
 * switcher and the Site menu hang off the header.
 */
@Composable
fun GardenScreen(
    sites: SitesState.Ready,
    actions: SitesActions,
    onOpenSiteSettings: () -> Unit,
    modifier: Modifier = Modifier,
    lots: LotsState = LotsState.Idle,
    lotsActions: LotsActions = LotsActions.None,
) {
    val colors = Coldframe.colors
    var switcherOpen by rememberSaveable { mutableStateOf(false) }
    val steps = FirstRunSteps.of(sites.current.role)
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(colors.background)
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        SiteSummaryHeader(
            name = sites.current.name,
            onOpenSwitcher = { switcherOpen = true },
            menu = { SiteMenu(site = sites.current, onOpenSiteSettings = onOpenSiteSettings) },
        )
        FirstRunTiles(steps)
        if (steps.memberNotice) {
            InlineNotice(message = stringResource(R.string.garden_member_notice))
        }
        GardenLots(lots, siteId = sites.current.id, actions = lotsActions)
    }
    if (switcherOpen) {
        SiteSwitcherSheet(
            sites = sites.sites,
            currentId = sites.current.id,
            onSelect = { id ->
                switcherOpen = false
                actions.select(id)
            },
            onNewSite = {
                switcherOpen = false
                actions.newSite()
            },
            onDismiss = { switcherOpen = false },
        )
    }
}

/**
 * Site summary header (UX-DR21): the Site name, tappable with `chevron--down` to open the Site
 * switcher, the Site menu trigger at the right, then the headline as a heading and the subline.
 */
@Composable
fun SiteSummaryHeader(
    name: String,
    onOpenSwitcher: () -> Unit,
    menu: @Composable () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val switchLabel = stringResource(R.string.sites_open_switcher, name)
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Row(
                modifier =
                    Modifier
                        .weight(1f)
                        .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                        .clickable(role = Role.Button, onClick = onOpenSwitcher)
                        .semantics(mergeDescendants = true) { contentDescription = switchLabel },
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
            ) {
                Text(
                    text = name,
                    style = Typography.section.textStyle(),
                    color = colors.textPrimary,
                    modifier = Modifier.weight(1f, fill = false),
                )
                Icon(
                    ColdframeIcons.chevronDown,
                    contentDescription = null,
                    tint = colors.textPrimary,
                    modifier = Modifier.size(20.dp),
                )
            }
            menu()
        }
        Text(
            text = stringResource(R.string.garden_no_readings),
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        Text(
            text = stringResource(R.string.garden_no_readings_detail),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
    }
}

/**
 * First-run step tiles (UX-DR54): 2×2, STEP 1–4. The next step is solid `primary` with
 * `ink-on-bright`; later steps have a 1 dp dashed `border-strong` outline; done steps a
 * checkmark. Each tile is one accessibility element; none is actionable until its flow exists.
 */
@Composable
fun FirstRunTiles(
    steps: FirstRunSteps,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        Text(
            text = stringResource(R.string.garden_steps),
            style = Typography.section.textStyle(),
            color = Coldframe.colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        steps.tiles.chunked(2).forEach { row ->
            Row(
                modifier = Modifier.fillMaxWidth().height(IntrinsicSize.Min),
                horizontalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp),
            ) {
                row.forEach { tile -> StepTile(tile, Modifier.weight(1f).fillMaxHeight()) }
            }
        }
    }
}

@Composable
private fun StepTile(
    tile: FirstRunTile,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val next = tile.state == StepState.Next
    val ink = if (next) colors.inkOnBright else colors.textPrimary
    val surface =
        when (tile.state) {
            StepState.Next -> Modifier.background(colors.primary)
            StepState.Later, StepState.Done -> Modifier.dashedBorder(colors.borderStrong)
        }
    Box(
        modifier =
            modifier
                .heightIn(min = 120.dp)
                .then(surface)
                .semantics(mergeDescendants = true) {}
                .padding(Spacing.TILE_PADDING.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
            ) {
                if (tile.state == StepState.Done) {
                    Icon(
                        ColdframeIcons.checkmark,
                        contentDescription = null,
                        tint = ink,
                        modifier = Modifier.size(16.dp),
                    )
                }
                Text(
                    text = styledText(stringResource(R.string.garden_step, tile.number), Typography.statusLabel),
                    style = Typography.statusLabel.textStyle(),
                    color = ink,
                )
            }
            Text(text = stringResource(tile.step.label()), style = Typography.tileName.textStyle(), color = ink)
            Text(
                text = styledText(stringResource(tile.state.label()), Typography.statusLabel),
                style = Typography.statusLabel.textStyle(),
                color = ink,
            )
        }
    }
}

@Composable
private fun GardenLots(
    lots: LotsState,
    siteId: String,
    actions: LotsActions,
) {
    when (lots) {
        is LotsState.Ready -> {
            if (lots.siteId == siteId && lots.lots.isNotEmpty()) LotTiles(lots.lots)
        }

        is LotsState.Failed -> {
            if (lots.site.id == siteId) {
                InlineNotice(
                    message = stringResource(lots.notice.loadMessage()),
                    action =
                        if (lots.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.load)
                        } else {
                            null
                        },
                    announcement = Announcement.Polite,
                )
            }
        }

        LotsState.Idle, is LotsState.Loading -> {
            Unit
        }
    }
}
