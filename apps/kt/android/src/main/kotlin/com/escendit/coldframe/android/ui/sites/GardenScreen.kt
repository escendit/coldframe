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
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
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
import com.escendit.coldframe.core.lots.LotsEvent
import com.escendit.coldframe.core.lots.LotsOverview
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.site
import com.escendit.coldframe.core.sites.FirstRunStep
import com.escendit.coldframe.core.sites.FirstRunSteps
import com.escendit.coldframe.core.sites.FirstRunTile
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.StepState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow
import java.time.Instant
import java.time.ZoneId
import com.escendit.coldframe.core.sites.SiteMenu as SiteMenuModel

/**
 * The Site overview (UX-DR62, UX-DR82, Story 4.7). The core's [LotsOverview] decides everything
 * that is shown about the Lots: headline, counts, one tile per Lot in the Server's order, and
 * whether the data is live. This screen only draws it, with the words of [OverviewCopy].
 *
 * - Live: the Site summary header with the headline as a heading and the counts (UX-DR21, UX-DR129).
 * - Stale mode (UX-DR79): the stale header replaces the summary, every tile is a stale tile and
 *   the Site menu items are disabled with "Needs your Server".
 * - A cold start without stored Lots (UX-DR80): "Loading ‹Site›" and outline tiles.
 * - A failed load with nothing to show: its notice in place of the grid.
 *
 * The overview is built again when the Lots change and once a minute, so the stale age and the
 * silence of unknown Lots move; that tick is the only timer here and it is never announced
 * (UX-DR106). Pulling down refreshes (UX-DR112). Entering and leaving stale mode ([events]) are
 * announced politely. The first-run step tiles stay as they are; for Administrators and Owners
 * the Add a Hub tile starts Add a Hub (UX-DR66) and Members see the read-only notice instead. A
 * *no Node* tile starts Add a Node with its Lot when the core says so (UX-DR67).
 *
 * [now] is the clock ages and durations are told against; [zone] is the phone's.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GardenScreen(
    sites: SitesState.Ready,
    actions: SitesActions,
    onOpenSiteSettings: () -> Unit,
    modifier: Modifier = Modifier,
    lots: LotsState = LotsState.Idle,
    lotsActions: LotsActions = LotsActions.None,
    onAddHub: () -> Unit = {},
    onAddNode: (lotId: String) -> Unit = {},
    onOpenLot: (lotId: String, name: String) -> Unit = { _, _ -> },
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
    events: Flow<LotsEvent> = emptyFlow(),
) {
    val colors = Coldframe.colors
    var switcherOpen by rememberSaveable { mutableStateOf(false) }
    val site = sites.current
    val steps = FirstRunSteps.of(site.role)

    // The minute tick: the only timer of the overview. It moves the age and the durations.
    var minute by remember { mutableIntStateOf(0) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(TICK_MILLIS)
            minute++
        }
    }
    // Lots of another Site (a switch in flight) are not this Site's overview.
    val shown = if (lots.site?.id == site.id) lots else LotsState.Idle
    val clock = remember(shown, minute) { now() }
    val copy = rememberOverviewCopy(clock, zone)
    val overview =
        remember(shown, clock) { (shown as? LotsState.Ready)?.let { LotsOverview.of(it, clock.toEpochMilli()) } }
    val menuItems = overview?.menu ?: SiteMenuModel.items(site.role)
    val menu: @Composable () -> Unit = {
        SiteMenu(site = site, onOpenSiteSettings = onOpenSiteSettings, items = menuItems)
    }
    val pull = rememberPullToRefreshState()
    val refreshing = overview?.refreshing == true

    PullToRefreshBox(
        isRefreshing = refreshing,
        onRefresh = lotsActions.refresh,
        modifier = modifier.fillMaxSize().background(colors.background),
        state = pull,
        indicator = {
            RefreshBar(
                armed = refreshing || pull.distanceFraction >= 1f,
                Modifier.align(Alignment.TopCenter),
            )
        },
    ) {
        // Outside the scrolling content, so the region takes no room in it.
        StaleAnnouncer(events = events, siteId = site.id, copy = copy)
        Column(
            modifier =
                Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(Spacing.GUTTER_MOBILE.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
        ) {
            if (overview != null && overview.stale) {
                StaleHeader(
                    name = site.name,
                    title = copy.staleTitle(site.name),
                    age = overview.staleAge?.let { copy.staleAge(it) },
                    detail = copy.staleDetail(overview.fetchedAtEpochMs),
                    onOpenSwitcher = { switcherOpen = true },
                    menu = menu,
                )
            } else {
                SiteSummaryHeader(
                    name = site.name,
                    headline =
                        when {
                            overview != null -> copy.headline(overview.headline)
                            shown is LotsState.Loading -> stringResource(R.string.garden_loading, site.name)
                            else -> stringResource(R.string.garden_no_readings)
                        },
                    subline =
                        when {
                            overview != null -> copy.subline(overview)
                            shown is LotsState.Loading -> null
                            else -> stringResource(R.string.garden_no_readings_detail)
                        },
                    pausedInk = overview?.headline?.pausedInk == true,
                    onOpenSwitcher = { switcherOpen = true },
                    menu = menu,
                )
            }
            FirstRunTiles(steps, onAddHub = onAddHub)
            if (steps.memberNotice) {
                InlineNotice(message = stringResource(R.string.garden_member_notice))
            }
            when (shown) {
                is LotsState.Ready -> {
                    // Which tile starts Add a Node is the core's answer (`opensAddNode`).
                    if (overview != null && overview.tiles.isNotEmpty()) {
                        LotTiles(overview.tiles, copy, onAddNode = onAddNode, onOpenLot = onOpenLot)
                    }
                }

                is LotsState.Loading -> {
                    SkeletonLotTiles()
                }

                is LotsState.Failed -> {
                    InlineNotice(
                        message = stringResource(shown.notice.loadMessage()),
                        action =
                            if (shown.notice.tryAgain) {
                                NoticeActionUi(stringResource(R.string.notice_try_again), lotsActions.load)
                            } else {
                                null
                            },
                        announcement = Announcement.Polite,
                    )
                }

                LotsState.Idle -> {
                    Unit
                }
            }
        }
    }
    if (switcherOpen) {
        SiteSwitcherSheet(
            sites = sites.sites,
            currentId = site.id,
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

/** One minute: the stale age and the durations are told in minutes. */
private const val TICK_MILLIS = 60_000L

/**
 * What pull-to-refresh shows instead of a spinner (UX-DR114): a `primary` bar across the top
 * while the pull is far enough to refresh on release and while the read runs. It appears and
 * goes at once.
 */
@Composable
private fun RefreshBar(
    armed: Boolean,
    modifier: Modifier = Modifier,
) {
    if (armed) {
        Box(modifier = modifier.fillMaxWidth().height(Spacing.STEP_2.dp).background(Coldframe.colors.primary))
    }
}

/**
 * Entering and leaving stale mode, announced politely (UX-DR106): a live region that exists
 * before it is filled. Each event of this Site empties it and fills it on the next frame, so the
 * same words twice are still read. Nothing else writes to it: not the age tick, not a refresh
 * that changes nothing.
 */
@Composable
internal fun StaleAnnouncer(
    events: Flow<LotsEvent>,
    siteId: String,
    copy: OverviewCopy,
) {
    val words by rememberUpdatedState(copy)
    var shown by remember { mutableStateOf("") }
    LaunchedEffect(events, siteId) {
        shown = ""
        events.collect { event ->
            if (event.siteId != siteId) return@collect
            shown = ""
            withFrameNanos { }
            shown =
                when (event) {
                    is LotsEvent.EnteredStale -> words.enteredStale(event.fetchedAtEpochMs)
                    is LotsEvent.LeftStale -> words.leftStale()
                }
        }
    }
    Box(
        modifier =
            Modifier.size(1.dp).semantics {
                contentDescription = shown
                liveRegion = LiveRegionMode.Polite
            },
    )
}

/** The Site name, tappable with `chevron--down` to open the Site switcher, and the Site menu trigger. */
@Composable
private fun SiteBar(
    name: String,
    onOpenSwitcher: () -> Unit,
    menu: @Composable () -> Unit,
) {
    val colors = Coldframe.colors
    val switchLabel = stringResource(R.string.sites_open_switcher, name)
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
}

/**
 * Site summary header (UX-DR21): the Site name, tappable with `chevron--down` to open the Site
 * switcher, the Site menu trigger at the right, then the [headline] as a heading and the
 * [subline] (the counts, or the detail of "No Readings yet"). A Site paused as a whole has its
 * headline in `status-paused-ink` ([pausedInk]). There is no Site clock yet.
 */
@Composable
fun SiteSummaryHeader(
    name: String,
    headline: String,
    subline: String?,
    onOpenSwitcher: () -> Unit,
    menu: @Composable () -> Unit,
    modifier: Modifier = Modifier,
    pausedInk: Boolean = false,
) {
    val colors = Coldframe.colors
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        SiteBar(name, onOpenSwitcher, menu)
        Text(
            text = headline,
            style = Typography.headline.textStyle(),
            color = if (pausedInk) colors.statusPausedInk else colors.textPrimary,
            modifier = Modifier.semantics { heading() },
        )
        if (subline != null) {
            Text(text = subline, style = Typography.body.textStyle(), color = colors.textSecondary)
        }
    }
}

/**
 * Stale header (UX-DR24): replaces the summary while the Server cannot be reached. The Site bar
 * stays, so the Site switcher and the (disabled) Site menu remain reachable; then `cloud--offline`
 * with [title], the [age] of the data in `headline` and `stale-ink` as the heading, and one line
 * of [detail]. The age is plain text: it changes with the minute tick and is never announced.
 */
@Composable
fun StaleHeader(
    name: String,
    title: String,
    age: String?,
    detail: String,
    onOpenSwitcher: () -> Unit,
    menu: @Composable () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val titleStyle = Typography.bodyLg.textStyle()
    val iconSize = maxOf(20.dp, with(LocalDensity.current) { titleStyle.fontSize.toDp() } + Spacing.STEP_2.dp)
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        SiteBar(name, onOpenSwitcher, menu)
        Row(
            modifier = Modifier.semantics(mergeDescendants = true) {},
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Icon(
                ColdframeIcons.cloudOffline,
                contentDescription = null,
                tint = colors.textPrimary,
                modifier = Modifier.size(iconSize),
            )
            Text(text = title, style = titleStyle, color = colors.textPrimary, modifier = Modifier.weight(1f))
        }
        if (age != null) {
            Text(
                text = age,
                style = Typography.headline.textStyle(),
                color = colors.staleInk,
                modifier = Modifier.semantics { heading() },
            )
        }
        Text(text = detail, style = Typography.body.textStyle(), color = colors.textSecondary)
    }
}

/**
 * First-run step tiles (UX-DR54): 2×2, STEP 1–4. The next step is solid `primary` with
 * `ink-on-bright`; later steps have a 1 dp dashed `border-strong` outline; done steps a
 * checkmark. Each tile is one accessibility element; when [FirstRunSteps.actionable], the next
 * step (Add a Hub) is a button that starts its flow.
 */
@Composable
fun FirstRunTiles(
    steps: FirstRunSteps,
    modifier: Modifier = Modifier,
    onAddHub: () -> Unit = {},
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
                row.forEach { tile ->
                    val acts = steps.actionable && tile.state == StepState.Next && tile.step == FirstRunStep.AddHub
                    StepTile(tile, Modifier.weight(1f).fillMaxHeight(), onClick = if (acts) onAddHub else null)
                }
            }
        }
    }
}

@Composable
private fun StepTile(
    tile: FirstRunTile,
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
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
                .then(if (onClick != null) Modifier.clickable(role = Role.Button, onClick = onClick) else Modifier)
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
