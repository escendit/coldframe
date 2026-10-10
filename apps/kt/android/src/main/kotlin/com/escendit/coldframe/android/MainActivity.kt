package com.escendit.coldframe.android

import android.Manifest
import android.content.Intent
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.escendit.coldframe.android.push.PushIntents
import com.escendit.coldframe.android.push.notificationPermission
import com.escendit.coldframe.android.push.openNotificationSettings
import com.escendit.coldframe.android.ui.alerts.AlertsActions
import com.escendit.coldframe.android.ui.calibrate.CalibrateActions
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.notifications.NotificationSettingsActions
import com.escendit.coldframe.android.ui.notifications.PushActions
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.thresholds.ThresholdsActions

/**
 * Registers the Custom Tabs flow, renders the root, and on every foreground resumes the session,
 * reads the Lots and the Alerts again (stale mode is left by the first read that succeeds) and
 * tells the core what Android says about notifications (UX-DR88).
 * Add a Hub and Add a Node keep the screen on through their own view while a flow is open.
 *
 * Push (Story 6.5): the activity holds the `POST_NOTIFICATIONS` prompt and the system settings, and hands the
 * routing keys of a tapped notification to the core, on a cold start ([onCreate]) and while it runs
 * ([onNewIntent], `singleTop`). Where the tap leads is the core's answer.
 */
class MainActivity : ComponentActivity() {
    private val push get() = (application as ColdframeApplication).signIn.push

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        openedFrom(intent)
    }

    /** A tapped notification started or resumed the activity: its routing keys go to the core. */
    private fun openedFrom(intent: Intent?) {
        PushIntents.routing(intent)?.let { push.opened(it) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        val app = application as ColdframeApplication
        app.signIn.registerActivity(this)
        super.onCreate(savedInstanceState)
        // Not again after a configuration change: the tap was handed over when the activity was first made.
        if (savedInstanceState == null) openedFrom(intent)
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
            val pushState by push.state.collectAsStateWithLifecycle()
            // The OS prompt of UX-DR122; however it ends, it was answered.
            val askNotifications =
                rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
                    push.promptAnswered(notificationPermission())
                }
            val pushActions =
                remember(askNotifications) {
                    PushActions(
                        ask = {
                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                                askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
                            } else {
                                push.promptAnswered(notificationPermission())
                            }
                        },
                        openSettings = { openNotificationSettings() },
                        routeHandled = push::routeHandled,
                    )
                }
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
                    // Granted or revoked in the system settings meanwhile: the notice follows (UX-DR88).
                    push.reportPermission(notificationPermission())
                },
                alerts = alertsState,
                alertsActions = alertsActions,
                notifications = notificationsState,
                notificationsActions = notificationsActions,
                push = pushState,
                pushActions = pushActions,
            )
        }
    }
}
