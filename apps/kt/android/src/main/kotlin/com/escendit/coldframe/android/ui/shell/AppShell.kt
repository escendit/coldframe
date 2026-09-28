package com.escendit.coldframe.android.ui.shell

import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Box
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.settings.AppearanceScreen
import com.escendit.coldframe.android.ui.settings.SettingsScreen
import com.escendit.coldframe.android.ui.sites.GardenScreen
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

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
 * Alerts and Devices show their heading only; Settings leads to Appearance, and
 * system/predictive back returns. The Site menu's "Site settings" opens the Settings index.
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
) {
    val colors = Coldframe.colors
    var tab by rememberSaveable { mutableStateOf(Tab.Garden) }
    var appearanceOpen by rememberSaveable { mutableStateOf(false) }
    val showingAppearance = tab == Tab.Settings && appearanceOpen

    BackHandler(enabled = showingAppearance) { appearanceOpen = false }

    // The bar grows with the heading's line height, so a scaled title never clips (UX-DR96).
    val titleStyle = Typography.title.textStyle()
    val barHeight =
        with(LocalDensity.current) {
            maxOf(
                TopAppBarDefaults.TopAppBarExpandedHeight,
                titleStyle.fontSize.toDp() * Typography.title.lineHeight + Spacing.STEP_6.dp,
            )
        }

    Scaffold(
        modifier = modifier.fillMaxSize(),
        containerColor = colors.background,
        topBar = {
            Box {
                TopAppBar(
                    title = {
                        Text(
                            text = stringResource(if (showingAppearance) R.string.appearance_title else tab.title),
                            style = titleStyle,
                            modifier = Modifier.semantics { heading() },
                        )
                    },
                    navigationIcon = {
                        if (showingAppearance) {
                            IconButton(
                                onClick = { appearanceOpen = false },
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
                                if (tab == item && item == Tab.Settings) appearanceOpen = false
                                tab = item
                            },
                            icon = { Icon(item.icon(), contentDescription = null, modifier = Modifier.size(24.dp)) },
                            label = { Text(stringResource(item.label), style = Typography.helper.textStyle()) },
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
            // Alerts and Devices carry their heading only until their stories.
            if (tab == Tab.Garden) {
                GardenScreen(
                    sites = sites,
                    actions = actions,
                    onOpenSiteSettings = {
                        appearanceOpen = false
                        tab = Tab.Settings
                    },
                )
            } else if (showingAppearance) {
                AppearanceScreen(theme = theme, onSelectTheme = onSelectTheme)
            } else if (tab == Tab.Settings) {
                SettingsScreen(onOpenAppearance = { appearanceOpen = true }, onSignOut = onSignOut)
            }
        }
    }
}
