package com.escendit.coldframe.android.ui.shell

import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.alerts.AlertsActions
import com.escendit.coldframe.android.ui.alerts.AlertsScreen
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.devices.DevicesScreen
import com.escendit.coldframe.android.ui.notifications.MyNotificationsScreen
import com.escendit.coldframe.android.ui.notifications.NotificationSettingsActions
import com.escendit.coldframe.android.ui.notifications.PushActions
import com.escendit.coldframe.android.ui.settings.AppearanceScreen
import com.escendit.coldframe.android.ui.settings.SettingsScreen
import com.escendit.coldframe.android.ui.settings.SiteSettingsScreen
import com.escendit.coldframe.android.ui.sites.GardenScreen
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotDetailScreen
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.ONE_COLUMN_FONT_SCALE
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.core.alerts.openCount
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.canAddHub
import com.escendit.coldframe.core.devices.canAddNode
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotsEvent
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.push.PushRoute
import com.escendit.coldframe.core.push.PushState
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow
import java.time.Instant

/** The four mobile tabs (UX-DR57), in order. */
enum class Tab(
    @param:StringRes val label: Int,
    @param:StringRes val title: Int,
    val icon: () -> ImageVector,
) {
    Garden(R.string.nav_garden, R.string.garden_title, { ColdframeIcons.grid }),
    Alerts(R.string.nav_alerts, R.string.alerts_title, { ColdframeIcons.notification }),
    Devices(R.string.nav_devices, R.string.devices_title, { ColdframeIcons.box }),
    Settings(R.string.nav_settings, R.string.settings_title, { ColdframeIcons.settings }),
}

/**
 * The signed-in shell (UX-DR57, UX-DR110): Material 3 `NavigationBar` with Carbon icons and a
 * `TopAppBar` heading per screen. The selected tab uses `primary-text` plus the M3 indicator as
 * its non-colour cue and is exposed as selected. Garden shows the current Site (Story 1.8);
 * Alerts lists the Site's Alerts (Story 6.2), reads them again every time the tab is entered, and its
 * tab label carries the open count on every tab ("Alerts · 5", spoken "Alerts, 5 open"); a row opens
 * Lot detail on the Garden tab, or the Devices tab; Devices lists the Hubs (Story 3.7) and reads them again every
 * time the tab is entered, with Add a Hub and Add a Node as ghost header actions for Administrators
 * and Owners;
 * Settings leads to My notifications (Story 6.3), which reads its settings again on every entry, to
 * Site settings and to Appearance, and system/predictive back returns. The Site menu's "Site
 * settings" opens Site settings.
 *
 * [tabState] is the selected tab. The root hoists it, so closing a flow that replaced the shell
 * (Add a Hub, Add a Node) returns to the tab it was opened from. [now] is the clock last-seen times,
 * the stale age and the Lots' durations are told against; [lotsEvents] are the core's stale-mode
 * events, which the Garden announces.
 *
 * [push] is the core's push state (Story 6.5). Its route is where a tapped notification leads (UX-DR120): the core
 * has already switched to the notification's Site, and the shell shows Lot detail or the overview on the Garden tab
 * and says so with [PushActions.routeHandled]. A route that arrives while a flow covers the shell is shown when the
 * shell is back.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AppShell(
    sites: SitesState.Ready,
    theme: ThemePreference,
    onSelectTheme: (ThemePreference) -> Unit,
    onSignOut: () -> Unit,
    modifier: Modifier = Modifier,
    actions: SitesActions = SitesActions.None,
    lots: LotsState = LotsState.Idle,
    lotsActions: LotsActions = LotsActions.None,
    onAddHub: () -> Unit = {},
    onAddNode: (lotId: String?) -> Unit = {},
    devices: DevicesState = DevicesState.Idle,
    devicesActions: DevicesActions = DevicesActions.None,
    tabState: MutableState<Tab> = rememberSaveable { mutableStateOf(Tab.Garden) },
    now: () -> Instant = Instant::now,
    lotsEvents: Flow<LotsEvent> = emptyFlow(),
    lotDetail: LotDetailState = LotDetailState.Idle,
    lotDetailActions: LotDetailActions = LotDetailActions.None,
    lotDetailEvents: Flow<LotsEvent> = emptyFlow(),
    onCalibrate: (lotId: String, name: String) -> Unit = { _, _ -> },
    onThresholds: (lotId: String, name: String, sensorId: String?) -> Unit = { _, _, _ -> },
    alerts: AlertsState = AlertsState.Idle,
    alertsActions: AlertsActions = AlertsActions.None,
    notifications: NotificationSettingsState = NotificationSettingsState.Idle,
    notificationsActions: NotificationSettingsActions = NotificationSettingsActions.None,
    push: PushState = PushState.None,
    pushActions: PushActions = PushActions.None,
) {
    val colors = Coldframe.colors
    var tab by tabState
    var appearanceOpen by rememberSaveable { mutableStateOf(false) }
    var siteSettingsOpen by rememberSaveable { mutableStateOf(false) }
    var notificationsOpen by rememberSaveable { mutableStateOf(false) }
    val showingAppearance = tab == Tab.Settings && appearanceOpen
    val showingSiteSettings = tab == Tab.Settings && siteSettingsOpen && !appearanceOpen
    val showingNotifications = tab == Tab.Settings && notificationsOpen && !appearanceOpen && !siteSettingsOpen
    // Lot detail is open while the core's detail state is not idle (it goes idle with the Site).
    val showingLotDetail = tab == Tab.Garden && lotDetail !is LotDetailState.Idle
    val showingSub = showingAppearance || showingSiteSettings || showingNotifications || showingLotDetail
    val closeSub = {
        if (showingLotDetail) lotDetailActions.close()
        appearanceOpen = false
        siteSettingsOpen = false
        notificationsOpen = false
    }

    BackHandler(enabled = showingSub) { closeSub() }

    // Every entry of the Devices and the Alerts tab reads its list again, also when a closed flow returns to it.
    LaunchedEffect(tab) {
        if (tab == Tab.Devices) devicesActions.load()
        if (tab == Tab.Alerts) alertsActions.load()
    }

    // A tapped notification: Lot detail or the overview, on the Garden tab of the Site the core switched to.
    val route = push.route
    LaunchedEffect(route) {
        when (route) {
            null -> {
                return@LaunchedEffect
            }

            is PushRoute.LotDetail -> {
                lotDetailActions.open(route.lotId, route.lotName)
            }

            is PushRoute.Overview -> {
                if (lotDetail !is LotDetailState.Idle) lotDetailActions.close()
            }
        }
        tab = Tab.Garden
        pushActions.routeHandled()
    }

    // Every entry of My notifications reads the settings again from the Server.
    LaunchedEffect(showingNotifications) {
        if (showingNotifications) notificationsActions.load()
    }

    // The bar grows with the heading's line height, so a scaled title never clips (UX-DR96).
    val titleStyle = Typography.title.textStyle()
    val titleHeight =
        with(LocalDensity.current) {
            maxOf(
                TopAppBarDefaults.TopAppBarExpandedHeight,
                titleStyle.fontSize.toDp() * Typography.title.lineHeight + Spacing.STEP_6.dp,
            )
        }
    // Add a Hub and Add a Node sit side by side; from the font scale at which the Lot grid drops
    // to one column they stack, so the heading keeps its width, and the bar grows to hold both.
    val showsAddActions = tab == Tab.Devices && devices.canAddHub && devices.canAddNode
    val stackActions = LocalDensity.current.fontScale >= ONE_COLUMN_FONT_SCALE
    val barHeight =
        if (showsAddActions && stackActions) maxOf(titleHeight, Spacing.BUTTON_HEIGHT.dp * 2) else titleHeight

    Scaffold(
        modifier = modifier.fillMaxSize(),
        containerColor = colors.background,
        topBar = {
            Box {
                TopAppBar(
                    title = {
                        Text(
                            text =
                                stringResource(
                                    when {
                                        showingAppearance -> R.string.appearance_title
                                        showingSiteSettings -> R.string.site_settings_title
                                        showingNotifications -> R.string.notifications_title
                                        else -> tab.title
                                    },
                                ),
                            style = titleStyle,
                            modifier = Modifier.semantics { heading() },
                        )
                    },
                    navigationIcon = {
                        if (showingSub) {
                            IconButton(
                                onClick = closeSub,
                                modifier =
                                    Modifier.sizeIn(
                                        minWidth = Spacing.BUTTON_HEIGHT.dp,
                                        minHeight = Spacing.BUTTON_HEIGHT.dp,
                                    ),
                            ) {
                                // Carbon has no arrow-left in the vendored set; chevron--down turned a quarter.
                                Icon(
                                    ColdframeIcons.chevronDown,
                                    contentDescription = stringResource(R.string.nav_back),
                                    modifier = Modifier.size(24.dp).rotate(90f),
                                )
                            }
                        }
                    },
                    actions = {
                        // Hidden for a Member, never disabled (UX-DR84).
                        if (showsAddActions) {
                            if (stackActions) {
                                Column(horizontalAlignment = Alignment.End) { AddDeviceActions(onAddHub, onAddNode) }
                            } else {
                                AddDeviceActions(onAddHub, onAddNode)
                            }
                        }
                    },
                    expandedHeight = barHeight,
                    colors =
                        TopAppBarDefaults.topAppBarColors(
                            containerColor = colors.background,
                            scrolledContainerColor = colors.background,
                            titleContentColor = colors.textPrimary,
                            navigationIconContentColor = colors.textPrimary,
                        ),
                )
            }
        },
        bottomBar = {
            Box {
                HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
                NavigationBar(containerColor = colors.background, tonalElevation = 0.dp) {
                    Tab.entries.forEach { item ->
                        NavigationBarItem(
                            selected = tab == item,
                            onClick = {
                                if (tab == item && (item == Tab.Settings || item == Tab.Garden)) closeSub()
                                tab = item
                            },
                            icon = { Icon(item.icon(), contentDescription = null, modifier = Modifier.size(24.dp)) },
                            label = { TabLabel(item, alerts.openCount) },
                            alwaysShowLabel = true,
                            colors =
                                NavigationBarItemDefaults.colors(
                                    selectedIconColor = colors.primaryText,
                                    selectedTextColor = colors.primaryText,
                                    indicatorColor = colors.layer01,
                                    unselectedIconColor = colors.textSecondary,
                                    unselectedTextColor = colors.textSecondary,
                                ),
                        )
                    }
                }
            }
        },
    ) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            if (tab == Tab.Alerts) {
                AlertsScreen(
                    alerts = alerts,
                    actions = alertsActions,
                    // Lot detail lives on the Garden tab, so Back returns to the overview.
                    onOpenLot = { lotId, name ->
                        tab = Tab.Garden
                        lotDetailActions.open(lotId, name)
                    },
                    onOpenDevices = { tab = Tab.Devices },
                    now = now,
                )
            } else if (tab == Tab.Devices) {
                DevicesScreen(devices = devices, actions = devicesActions, now = now)
            } else if (showingLotDetail) {
                LotDetailScreen(
                    state = lotDetail,
                    actions = lotDetailActions,
                    now = now,
                    events = lotDetailEvents,
                    onAddNode = onAddNode,
                    onCalibrate = onCalibrate,
                    onThresholds = onThresholds,
                    onOpenDevices = {
                        lotDetailActions.close()
                        tab = Tab.Devices
                    },
                )
            } else if (tab == Tab.Garden) {
                GardenScreen(
                    sites = sites,
                    actions = actions,
                    onOpenSiteSettings = {
                        appearanceOpen = false
                        notificationsOpen = false
                        siteSettingsOpen = true
                        tab = Tab.Settings
                    },
                    lots = lots,
                    lotsActions = lotsActions,
                    onAddHub = onAddHub,
                    onAddNode = onAddNode,
                    onOpenLot = lotDetailActions.open,
                    onCalibrate = onCalibrate,
                    now = now,
                    events = lotsEvents,
                    push = push,
                    pushActions = pushActions,
                )
            } else if (showingAppearance) {
                AppearanceScreen(theme = theme, onSelectTheme = onSelectTheme)
            } else if (showingSiteSettings) {
                SiteSettingsScreen(lots = lots, actions = lotsActions)
            } else if (showingNotifications) {
                MyNotificationsScreen(
                    state = notifications,
                    actions = notificationsActions,
                    notificationsOff = push.noticeVisible,
                    onOpenSettings = pushActions.openSettings,
                )
            } else if (tab == Tab.Settings) {
                SettingsScreen(
                    onOpenAppearance = { appearanceOpen = true },
                    onSignOut = onSignOut,
                    siteName = sites.current.name,
                    onOpenSiteSettings = { siteSettingsOpen = true },
                    onOpenNotifications = { notificationsOpen = true },
                )
            }
        }
    }
}

/**
 * A tab's label. The Alerts tab reads "Alerts · N" while the Site has N open Alerts, spoken as
 * "Alerts, N open"; without one it is "Alerts" like any other label.
 */
@Composable
private fun TabLabel(
    item: Tab,
    openAlerts: Int,
) {
    if (item == Tab.Alerts && openAlerts > 0) {
        val spoken = pluralStringResource(R.plurals.count_open_alerts, openAlerts, openAlerts)
        Text(
            text = stringResource(R.string.nav_alerts_count, openAlerts),
            style = Typography.helper.textStyle(),
            modifier = Modifier.semantics { contentDescription = spoken },
        )
    } else {
        Text(stringResource(item.label), style = Typography.helper.textStyle())
    }
}

/** The Devices header actions (UX-DR30): Add a Hub, then Add a Node, both ghost buttons. */
@Composable
private fun AddDeviceActions(
    onAddHub: () -> Unit,
    onAddNode: (lotId: String?) -> Unit,
) {
    ColdframeButton(
        label = stringResource(R.string.devices_add_hub),
        onClick = onAddHub,
        variant = ButtonVariant.Ghost,
    )
    ColdframeButton(
        label = stringResource(R.string.devices_add_node),
        onClick = { onAddNode(null) },
        variant = ButtonVariant.Ghost,
    )
}
