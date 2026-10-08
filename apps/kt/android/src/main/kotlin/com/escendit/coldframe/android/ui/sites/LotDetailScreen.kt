package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.horizontalDrag
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.Segment
import com.escendit.coldframe.android.ui.components.SegmentedChoice
import com.escendit.coldframe.android.ui.components.plate
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.lots.ChartBand
import com.escendit.coldframe.core.lots.ChartBar
import com.escendit.coldframe.core.lots.DeviceCells
import com.escendit.coldframe.core.lots.HeroValueKind
import com.escendit.coldframe.core.lots.HistoryChart
import com.escendit.coldframe.core.lots.LotDetail
import com.escendit.coldframe.core.lots.LotDetailHero
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotTileVariant
import com.escendit.coldframe.core.lots.LotsEvent
import com.escendit.coldframe.core.lots.SensorCell
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow
import java.time.Instant
import java.time.ZoneId

/** Marks the History chart for tests. */
const val HISTORY_CHART_TAG = "history-chart"

/** Marks the Set Thresholds / View Thresholds control for tests. */
const val THRESHOLDS_ACTION_TAG = "thresholds-action"

/** The chart's plot height. */
private const val CHART_HEIGHT_DP = 120

/** One minute: the stale age is told in minutes. */
private const val TICK_MILLIS = 60_000L

/**
 * Lot detail (UX-DR63, Story 4.8): the full-width hero, a 3-up row of Sensor cells, the 30-day
 * History chart with its Sensor picker, a 2-up row of Device cells, in that order; the rows wrap
 * to one per row from font scale 1.5. The core's [LotDetail] decides everything that is shown:
 * which note, value, cells and bars apply, every number and the stale state. This screen draws it
 * with the words of [LotDetailCopy]. Calibrate and Set Thresholds (read-only "View Thresholds" for a
 * Member) lead to their own screens; Pause has no destination before Epic 8.
 *
 * - Stale mode (UX-DR79): the stale header on top, the hero without a value, and no Sensor or
 *   Device cell: no live value is drawn.
 * - A *no Node* Lot: the empty detail, with Add a Node for Administrators and Owners.
 * - Loading a Lot with nothing stored: its name and an outline, no value.
 * - Pulling down refreshes (UX-DR112); the minute tick moves the stale age and is never announced.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LotDetailScreen(
    state: LotDetailState,
    actions: LotDetailActions,
    modifier: Modifier = Modifier,
    now: () -> Instant = Instant::now,
    zone: ZoneId = ZoneId.systemDefault(),
    events: Flow<LotsEvent> = emptyFlow(),
    onAddNode: (lotId: String) -> Unit = {},
    onCalibrate: (lotId: String, name: String) -> Unit = { _, _ -> },
    onThresholds: (lotId: String, name: String, sensorId: String?) -> Unit = { _, _, _ -> },
    onOpenDevices: () -> Unit = {},
) {
    val colors = Coldframe.colors
    var minute by remember { mutableIntStateOf(0) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(TICK_MILLIS)
            minute++
        }
    }
    val clock = remember(state, minute) { now() }
    val detail =
        remember(state, clock) { (state as? LotDetailState.Ready)?.let { LotDetail.of(it, clock.toEpochMilli()) } }
    val copy = rememberLotDetailCopy(clock, zone)
    val overviewCopy = rememberOverviewCopy(clock, zone)
    val refreshing = detail?.refreshing == true
    val siteId = (state as? LotDetailState.Ready)?.site?.id.orEmpty()
    PullToRefreshBox(
        isRefreshing = refreshing,
        onRefresh = actions.refresh,
        modifier = modifier.fillMaxSize().background(colors.background),
    ) {
        StaleAnnouncer(events = events, siteId = siteId, copy = overviewCopy)
        Column(
            modifier =
                Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(Spacing.GUTTER_MOBILE.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
        ) {
            when (state) {
                is LotDetailState.Ready -> {
                    if (detail != null) {
                        ReadyDetail(
                            state = state,
                            detail = detail,
                            copy = copy,
                            overviewCopy = overviewCopy,
                            actions = actions,
                            onAddNode = onAddNode,
                            onCalibrate = onCalibrate,
                            onThresholds = onThresholds,
                            onOpenDevices = onOpenDevices,
                        )
                    }
                }

                is LotDetailState.Loading -> {
                    LoadingDetail(state.name, stringResource(R.string.garden_loading, state.name))
                }

                is LotDetailState.Failed -> {
                    Text(
                        text = state.name,
                        style = Typography.title.textStyle(),
                        color = colors.textPrimary,
                        modifier = Modifier.semantics { heading() },
                    )
                    InlineNotice(
                        message = stringResource(state.notice.message(), state.site.name),
                        action =
                            if (state.notice.tryAgain) {
                                NoticeActionUi(stringResource(R.string.notice_try_again), actions.refresh)
                            } else {
                                null
                            },
                        announcement = Announcement.Polite,
                    )
                }

                LotDetailState.Idle -> {
                    Unit
                }
            }
        }
    }
}

@Composable
private fun LoadingDetail(
    name: String,
    loading: String,
) {
    val colors = Coldframe.colors
    Text(
        text = name,
        style = Typography.title.textStyle(),
        color = colors.textPrimary,
        modifier = Modifier.semantics { heading() },
    )
    Box(
        modifier =
            Modifier
                .fillMaxWidth()
                .heightIn(
                    min = 160.dp,
                ).border(1.dp, colors.borderSubtle)
                .padding(Spacing.TILE_PADDING.dp),
    ) {
        Text(text = loading, style = Typography.body.textStyle(), color = colors.textSecondary)
    }
}

@Composable
private fun ReadyDetail(
    state: LotDetailState.Ready,
    detail: LotDetail,
    copy: LotDetailCopy,
    overviewCopy: OverviewCopy,
    actions: LotDetailActions,
    onAddNode: (lotId: String) -> Unit,
    onCalibrate: (lotId: String, name: String) -> Unit,
    onThresholds: (lotId: String, name: String, sensorId: String?) -> Unit,
    onOpenDevices: () -> Unit,
) {
    val oneColumn = LocalDensity.current.fontScale >= ONE_COLUMN_FONT_SCALE
    if (detail.stale) {
        StaleDetailHeader(
            title = overviewCopy.staleTitle(state.site.name),
            age = detail.staleAge?.let { overviewCopy.staleAge(it) },
            body = overviewCopy.staleDetail(detail.fetchedAtEpochMs),
        )
    }
    Hero(detail.hero, copy, overviewCopy)
    // Calibrate is the core's answer: Admin+ on a live Lot with a calibratable Sensor; hidden, never disabled.
    if (detail.canCalibrate) {
        ColdframeButton(
            label = stringResource(R.string.calibrate_action),
            onClick = { onCalibrate(state.lot.id, state.lot.name) },
            variant = ButtonVariant.Secondary,
        )
    }
    // Set Thresholds is the core's answer: Admin+ on a live Lot with a Sensor, hidden, never disabled (UX-DR84). A Member
    // gets "View Thresholds": the same screen, read-only, with no edit control.
    if (detail.canViewThresholds) {
        ColdframeButton(
            label =
                stringResource(
                    if (detail.canSetThresholds) R.string.thresholds_action_set else R.string.thresholds_action_view,
                ),
            onClick = { onThresholds(state.lot.id, state.lot.name, null) },
            variant = ButtonVariant.Secondary,
            modifier = Modifier.testTag(THRESHOLDS_ACTION_TAG),
        )
    }
    if (detail.noNode) {
        NoNode(detail, state.lot.id, onAddNode)
        return
    }
    detail.sensors?.let { cells ->
        SensorCells(
            cells,
            copy,
            oneColumn,
            onOpen =
                if (detail.canViewThresholds) {
                    { sensorId ->
                        onThresholds(state.lot.id, state.lot.name, sensorId)
                    }
                } else {
                    null
                },
        )
    }
    HistorySection(detail, copy, actions)
    detail.device?.let { DeviceSection(it, copy, oneColumn, onOpenDevices) }
}

/** Stale header of Lot detail (UX-DR24): `cloud--offline`, the title, the age as the heading, one line. */
@Composable
private fun StaleDetailHeader(
    title: String,
    age: String?,
    body: String,
) {
    val colors = Coldframe.colors
    val titleStyle = Typography.bodyLg.textStyle()
    val iconSize = maxOf(20.dp, with(LocalDensity.current) { titleStyle.fontSize.toDp() } + Spacing.STEP_2.dp)
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        Row(
            modifier = Modifier.semantics(mergeDescendants = true) {},
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Icon(ColdframeIcons.cloudOffline, null, tint = colors.textPrimary, modifier = Modifier.size(iconSize))
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
        Text(text = body, style = Typography.body.textStyle(), color = colors.textSecondary)
    }
}

/**
 * The hero (UX-DR27, UX-DR78): the Lot name, the status icon and label with "since ‹time›", the
 * big value, the reading and low Threshold when the Server sent them, and the status note. It has
 * the tile's treatment of the status; only needs water is orange. One accessibility element.
 */
@Composable
private fun Hero(
    hero: LotDetailHero,
    copy: LotDetailCopy,
    overviewCopy: OverviewCopy,
) {
    val colors = Coldframe.colors
    val paint = colors.paint(hero.variant)
    val shape = hero.variant.shape()
    val plated = shape.fill == TileFill.Hatch
    val plate = Modifier.plate(plated, colors.statusHatchGround)
    val label = overviewCopy.label(hero.variant, hero.label)
    val since = hero.statusSinceEpochMs?.let { copy.since(it) }
    val value = copy.heroValue(hero)
    val reading = copy.heroReading(hero)
    val low = copy.heroLow(hero)
    val note = copy.note(hero)
    val silence = copy.silence(hero)
    val labelStyle = Typography.statusLabel.textStyle()
    val iconSize = maxOf(16.dp, with(LocalDensity.current) { labelStyle.fontSize.toDp() } + Spacing.STEP_1.dp)
    val spoken =
        listOfNotNull(hero.name, label, since, value.takeIf { hero.valueKind != HeroValueKind.None }, note)
            .joinToString(", ")
    Column(
        modifier =
            Modifier
                .fillMaxWidth()
                .tileSurface(shape, paint, hero.soilPercent, hero.lowPercent)
                .padding(Spacing.TILE_PADDING.dp)
                .semantics(mergeDescendants = true) { contentDescription = spoken },
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Text(
            text = hero.name,
            style = Typography.title.textStyle(),
            color = paint.ink,
            modifier = plate.semantics { heading() },
        )
        Row(
            modifier = plate,
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            Icon(
                ColdframeIcons.of(hero.variant.carbonIcon()),
                contentDescription = null,
                tint = paint.labelInk,
                modifier = Modifier.size(iconSize),
            )
            Text(
                text = styledText(label, Typography.statusLabel),
                style = labelStyle,
                color = paint.labelInk,
            )
            if (since != null) {
                Text(text = since, style = Typography.metaMono.textStyle(), color = paint.footInk)
            }
        }
        if (hero.variant != LotTileVariant.Stale) {
            Text(
                text = silence ?: value,
                style = Typography.heroValue.textStyle(),
                color = paint.ink,
                modifier = plate,
            )
        }
        for (line in listOfNotNull(reading, low)) {
            Text(text = line, style = Typography.metaMono.textStyle(), color = paint.footInk, modifier = plate)
        }
        if (note != null) {
            Text(text = note, style = Typography.body.textStyle(), color = paint.ink, modifier = plate)
        }
        if (hero.resumeSiteHint) {
            Text(text = copy.resumeSite(), style = Typography.body.textStyle(), color = paint.ink, modifier = plate)
        }
    }
}

@Composable
private fun NoNode(
    detail: LotDetail,
    lotId: String,
    onAddNode: (lotId: String) -> Unit,
) {
    Text(
        text = stringResource(R.string.lot_detail_no_node),
        style = Typography.bodyLg.textStyle(),
        color = Coldframe.colors.textSecondary,
    )
    if (detail.canAddNode) {
        ColdframeButton(
            label = stringResource(R.string.devices_add_node),
            onClick = { onAddNode(lotId) },
            variant = ButtonVariant.Secondary,
        )
    }
}

@Composable
private fun SectionHeading(text: String) {
    Text(
        text = text,
        style = Typography.section.textStyle(),
        color = Coldframe.colors.textPrimary,
        modifier = Modifier.semantics { heading() },
    )
}

/** A block of `layer-01` with a 1 dp `border-subtle` outline: helper label, title value, helper meta. */
@Composable
private fun Cell(
    label: String,
    value: String,
    meta: String?,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    onClick: (() -> Unit)? = null,
) {
    val colors = Coldframe.colors
    Column(
        modifier =
            modifier
                .background(colors.layer01)
                .border(1.dp, colors.borderSubtle)
                .then(if (onClick != null) Modifier.clickable(role = Role.Button, onClick = onClick) else Modifier)
                .semantics(mergeDescendants = true) {}
                .padding(Spacing.TILE_PADDING.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
    ) {
        Text(text = label, style = Typography.helper.textStyle(), color = colors.textSecondary)
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_2.dp),
        ) {
            if (icon != null) {
                Icon(icon, contentDescription = null, tint = colors.textPrimary, modifier = Modifier.size(24.dp))
            }
            Text(text = value, style = Typography.title.textStyle(), color = colors.textPrimary)
        }
        if (meta != null) {
            Text(text = meta, style = Typography.helper.textStyle(), color = colors.textSecondary)
        }
    }
}

/** Rows of [columns] equal cells; a short last row keeps the cell width. */
@Composable
private fun <T> CellRows(
    items: List<T>,
    columns: Int,
    cell: @Composable (T, Modifier) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        items.chunked(columns).forEach { row ->
            Row(
                modifier = Modifier.fillMaxWidth().height(IntrinsicSize.Min),
                horizontalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp),
            ) {
                row.forEach { cell(it, Modifier.weight(1f).fillMaxHeight()) }
                repeat(columns - row.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

/** The Sensor cells (UX-DR28): 3-up, one per row at large text. Watched Sensors show their value only. */
@Composable
private fun SensorCells(
    cells: List<SensorCell>,
    copy: LotDetailCopy,
    oneColumn: Boolean,
    onOpen: ((sensorId: String) -> Unit)? = null,
) {
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        SectionHeading(stringResource(R.string.lot_detail_sensors))
        if (cells.isEmpty()) {
            Text(
                text = stringResource(R.string.lot_detail_no_readings),
                style = Typography.body.textStyle(),
                color = Coldframe.colors.textSecondary,
            )
        } else {
            CellRows(cells, if (oneColumn) 1 else 3) { cell, modifier ->
                Cell(
                    label = copy.quantity(cell.quantity),
                    value = copy.value(cell.number, cell.unit),
                    meta = copy.reading(cell.measuredAtEpochMs),
                    modifier = modifier,
                    // A tap on a Sensor cell opens that Sensor's Threshold column.
                    onClick = cell.sensorId?.let { id -> onOpen?.let { open -> { open(id) } } },
                )
            }
        }
    }
}

/** The Device cells (UX-DR29): battery with charging, and last seen; 2-up, one per row at large text. */
@Composable
private fun DeviceSection(
    device: DeviceCells,
    copy: LotDetailCopy,
    oneColumn: Boolean,
    onOpenDevices: () -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.TILE_GAP.dp)) {
        Text(
            text = stringResource(R.string.lot_detail_node, device.nodeId),
            style = Typography.metaMono.textStyle(),
            color = Coldframe.colors.textSecondary,
            modifier = Modifier.semantics { heading() },
        )
        CellRows(listOf(0, 1), if (oneColumn) 1 else 2) { index, modifier ->
            if (index == 0) {
                Cell(
                    label = stringResource(R.string.lot_detail_battery),
                    value = copy.battery(device.batteryPercent),
                    meta = copy.charging(device.charging),
                    modifier = modifier,
                    icon = if (device.batteryLow) ColdframeIcons.batteryLow else null,
                    onClick = onOpenDevices,
                )
            } else {
                Cell(
                    label = stringResource(R.string.lot_detail_last_seen),
                    value = copy.lastSeen(device.lastSeenAtEpochMs),
                    meta = stringResource(R.string.lot_detail_every_fifteen),
                    modifier = modifier,
                    onClick = onOpenDevices,
                )
            }
        }
    }
}

@Composable
private fun HistorySection(
    detail: LotDetail,
    copy: LotDetailCopy,
    actions: LotDetailActions,
) {
    val colors = Coldframe.colors
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp)) {
        SectionHeading(stringResource(R.string.lot_detail_history))
        val picked = detail.picked
        if (detail.quantities.size > 1 && picked != null) {
            SegmentedChoice(
                label = stringResource(R.string.lot_detail_picker),
                segments = detail.quantities.map { Segment(it, copy.quantity(it)) },
                selected = picked,
                onSelect = actions.pick,
            )
        }
        val chart = detail.chart
        when {
            chart != null -> {
                HistoryChartView(chart, copy)
            }

            detail.historyUnavailable -> {
                Text(
                    text = stringResource(R.string.lot_detail_chart_unavailable),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }

            else -> {
                Text(
                    text = stringResource(R.string.lot_detail_chart_no_readings),
                    style = Typography.body.textStyle(),
                    color = colors.textSecondary,
                )
            }
        }
    }
}

/**
 * The History chart (UX-DR32, UX-DR33): one 1 dp outlined bar per day for 30 days, the day's low
 * scaled by the core; a day without Readings is a gap, never zero. With a Threshold band (soil
 * moisture in %, UX-DR5) it draws the `chart-band` zone, a 2 dp low line and a 1 dp dashed
 * `chart-high-line`, and a day whose low is under the low Threshold is a solid `chart-bar-below-low`
 * bar, with the legend "solid bar = below N %". Tapping or dragging selects a day and shows its readout (the low,
 * and for temperature, humidity and air also the range) as text; the whole chart is one element
 * whose label is the text summary (UX-DR98). Nothing animates.
 */
@Composable
fun HistoryChartView(
    chart: HistoryChart,
    copy: LotDetailCopy,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    var selected by rememberSaveable(chart.quantity) { mutableIntStateOf(-1) }
    var width by remember { mutableIntStateOf(1) }
    val summary = copy.chartSummary(chart)
    val bars = chart.bars

    fun indexAt(x: Float): Int = ((x / width) * bars.size).toInt().coerceIn(0, bars.size - 1)
    val barColor = colors.chartBar
    val axis = colors.borderSubtle
    val band = chart.band
    val legend = copy.chartLegend(chart)
    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp)) {
        Box(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .height(CHART_HEIGHT_DP.dp)
                    .testTag(HISTORY_CHART_TAG)
                    .onSizeChanged { width = it.width.coerceAtLeast(1) }
                    .pointerInput(bars) {
                        // A press selects the day under the finger, and a drag along the chart follows it.
                        awaitEachGesture {
                            val down = awaitFirstDown()
                            selected = indexAt(down.position.x)
                            horizontalDrag(down.id) { change ->
                                selected = indexAt(change.position.x)
                                change.consume()
                            }
                        }
                    }.semantics {
                        contentDescription = summary
                        role = Role.Image
                    }.drawBehind {
                        drawChart(
                            bars,
                            selected,
                            barColor,
                            axis,
                            band,
                            Bands(colors.chartBand, colors.primaryText, colors.chartHighLine, colors.chartBarBelowLow),
                        )
                    },
        )
        if (legend != null) {
            Text(text = legend, style = Typography.helper.textStyle(), color = colors.textSecondary)
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text(
                text = copy.chartDay(bars.first().dayEpochMs),
                style = Typography.metaMono.textStyle(),
                color = colors.textSecondary,
            )
            Text(
                text = copy.chartDay(bars.last().dayEpochMs),
                style = Typography.metaMono.textStyle(),
                color = colors.textSecondary,
            )
        }
        val shown = bars.getOrNull(selected)
        if (shown != null) {
            Text(
                text = copy.readout(chart, shown),
                style = Typography.metaMono.textStyle().copy(fontWeight = FontWeight.Normal),
                color = colors.textPrimary,
            )
        }
    }
}

/** Draws the baseline and one outlined bar per present day; the selected one is 2 dp. */
private fun DrawScope.drawChart(
    bars: List<ChartBar>,
    selected: Int,
    barColor: Color,
    axis: Color,
    band: ChartBand?,
    inks: Bands,
) {
    val slot = size.width / bars.size
    val gap = slot * BAR_GAP
    val plot = size.height - 2.dp.toPx()

    // The Threshold band (UX-DR5): from the low line up to the high line, or to the top without a high.
    if (band != null) {
        val bottom = size.height - plot * band.lowFraction.toFloat()
        val top = band.highFraction?.let { size.height - plot * it.toFloat() } ?: 0f
        drawRect(inks.band, topLeft = Offset(0f, top), size = Size(size.width, (bottom - top).coerceAtLeast(0f)))
    }
    drawLine(axis, Offset(0f, size.height), Offset(size.width, size.height), 1.dp.toPx())
    bars.forEachIndexed { index, bar ->
        if (!bar.present) return@forEachIndexed
        val height = plot * bar.fraction.toFloat()
        val stroke = (if (index == selected) 2.dp else 1.dp).toPx()
        val topLeft = Offset(index * slot + gap / 2 + stroke / 2, size.height - height + stroke / 2)
        val barSize = Size((slot - gap - stroke).coerceAtLeast(1f), (height - stroke).coerceAtLeast(1f))
        if (bar.belowLow) {
            // A day whose low is under the low Threshold is a solid bar: a cue that is not colour alone (UX-DR32).
            drawRect(inks.belowLow, topLeft, barSize, style = Fill)
            if (index == selected) drawRect(inks.low, topLeft, barSize, style = Stroke(stroke))
        } else {
            drawRect(color = barColor, topLeft = topLeft, size = barSize, style = Stroke(stroke))
        }
    }
    if (band != null) {
        val lowY = size.height - plot * band.lowFraction.toFloat()
        drawLine(inks.low, Offset(0f, lowY), Offset(size.width, lowY), 2.dp.toPx())
        band.highFraction?.let { fraction ->
            val highY = size.height - plot * fraction.toFloat()
            val dash = PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 4.dp.toPx()))
            drawLine(inks.high, Offset(0f, highY), Offset(size.width, highY), 1.dp.toPx(), pathEffect = dash)
        }
    }
}

/** The colours the band is drawn with, resolved once per frame from the theme's tokens. */
private class Bands(
    val band: Color,
    val low: Color,
    val high: Color,
    val belowLow: Color,
)

/** The share of a day's slot that stays empty between two bars. */
private const val BAR_GAP = 0.2f
