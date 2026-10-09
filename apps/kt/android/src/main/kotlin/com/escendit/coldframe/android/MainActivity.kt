package com.escendit.coldframe.android

import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.escendit.coldframe.android.ui.alerts.AlertsActions
import com.escendit.coldframe.android.ui.calibrate.CalibrateActions
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.notifications.NotificationSettingsActions
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.thresholds.ThresholdsActions

/**
 * Registers the Custom Tabs flow, renders the root, and on every foreground resumes the session
 * and reads the Lots and the Alerts again (stale mode is left by the first read that succeeds).
 * Add a Hub and Add a Node keep the screen on through their own view while a flow is open.
 */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        val app = application as ColdframeApplication
        app.signIn.registerActivity(this)
        super.onCreate(savedInstanceState)
        val engine = app.signIn.engine
        val appearance = app.appearance
        val sites = app.signIn.sites
        val sitesActions = SitesActions.of(sites)
        val lots = app.signIn.lots
        val lotsActions = LotsActions.of(lots)
        val lotDetail = app.signIn.lotDetail
        val lotDetailActions = LotDetailActions.of(lotDetail)
        val calibrate = app.signIn.calibrate
        val thresholds = app.signIn.thresholds
        val thresholdsActions = ThresholdsActions.of(thresholds)
        // After Calibration, the confirmation leads on to Thresholds for the same Lot (Story 5.4).
        val calibrateActions =
            CalibrateActions.of(calibrate) { lotId, name ->
                calibrate.close()
                thresholds.open(lotId, name)
            }
        val devices = app.signIn.devices
        val devicesActions = DevicesActions.of(devices)
        val alerts = app.signIn.alerts
        val alertsActions = AlertsActions.of(alerts)
        // The core reads these when the session starts; the shell reads them again on every entry of the surface.
        val notifications = app.signIn.notifications
        val notificationsActions = NotificationSettingsActions.of(notifications)
        val hubSetup = app.signIn.hubSetup
        val hubSetupActions = HubSetupActions.of(hubSetup)
        val nodeSetup = app.signIn.nodeSetup
        val nodeSetupActions = NodeSetupActions.of(nodeSetup)
        setContent {
            val state by engine.state.collectAsStateWithLifecycle()
            val theme by appearance.theme.collectAsStateWithLifecycle()
            val sitesState by sites.state.collectAsStateWithLifecycle()
            val lotsState by lots.state.collectAsStateWithLifecycle()
            val lotDetailState by lotDetail.state.collectAsStateWithLifecycle()
            val calibrateState by calibrate.state.collectAsStateWithLifecycle()
            val thresholdsState by thresholds.state.collectAsStateWithLifecycle()
            val devicesState by devices.state.collectAsStateWithLifecycle()
            val alertsState by alerts.state.collectAsStateWithLifecycle()
            val notificationsState by notifications.state.collectAsStateWithLifecycle()
            val hubSetupState by hubSetup.state.collectAsStateWithLifecycle()
            val nodeSetupState by nodeSetup.state.collectAsStateWithLifecycle()
            val systemIsDark = isSystemInDarkTheme()
            val isDark = theme.isDark(systemIsDark)
            // System bar icons follow the app's theme, which may differ from the OS appearance.
            DisposableEffect(isDark) {
                val style =
                    if (isDark) {
                        SystemBarStyle.dark(Color.TRANSPARENT)
                    } else {
                        SystemBarStyle.light(Color.TRANSPARENT, Color.TRANSPARENT)
                    }
                enableEdgeToEdge(statusBarStyle = style, navigationBarStyle = style)
                onDispose {}
            }
            ColdframeRoot(
                state = state,
                sites = sitesState,
                theme = theme,
                onSignIn = engine::signIn,
                onSignOut = engine::signOut,
                onSelectTheme = appearance::select,
                sitesActions = sitesActions,
                systemIsDark = systemIsDark,
                lots = lotsState,
                lotsActions = lotsActions,
                hubSetup = hubSetupState,
                hubSetupActions = hubSetupActions,
                devices = devicesState,
                devicesActions = devicesActions,
                nodeSetup = nodeSetupState,
                nodeSetupActions = nodeSetupActions,
                lotsEvents = lots.events,
                lotDetail = lotDetailState,
                lotDetailActions = lotDetailActions,
                lotDetailEvents = lotDetail.events,
                calibrate = calibrateState,
                calibrateActions = calibrateActions,
                thresholds = thresholdsState,
                thresholdsActions = thresholdsActions,
                // Reload on start: a no-op until a Site is current, else the Lots are read again.
                onForeground = {
                    engine.resume()
                    lots.refresh()
                    lotDetail.refresh()
                    // The Alerts tab carries its count on every tab, so it is read on every foreground too.
                    alerts.refresh()
                },
                alerts = alertsState,
                alertsActions = alertsActions,
                notifications = notificationsState,
                notificationsActions = notificationsActions,
            )
        }
    }
}
