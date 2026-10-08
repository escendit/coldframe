package com.escendit.coldframe.android

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.calibrate.CalibrateActions
import com.escendit.coldframe.android.ui.calibrate.CalibrateScreen
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.setup.AddHubFlow
import com.escendit.coldframe.android.ui.setup.AddNodeFlow
import com.escendit.coldframe.android.ui.setup.HubSetupActions
import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.android.ui.shell.AppShell
import com.escendit.coldframe.android.ui.shell.Tab
import com.escendit.coldframe.android.ui.signin.SignInScreen
import com.escendit.coldframe.android.ui.sites.CreateSiteScreen
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.sites.loadMessage
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.android.ui.thresholds.ThresholdsActions
import com.escendit.coldframe.android.ui.thresholds.ThresholdsScreen
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.calibrate.CalibrateState
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotsEvent
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.setup.NodeSetupState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.thresholds.ThresholdsState
import com.escendit.coldframe.designtokens.Spacing
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow
import java.time.Instant

/**
 * Maps the core's state to a surface. No branching on errors, URLs or tokens here: the shell
 * only renders [SignInState], [SitesState] and [ThemePreference]. Signed in, the Sites decide:
 * Create Site replaces the tab shell without a Membership, and covers it from "New Site"; Add a
 * Hub and Add a Node cover it while their flow is open, and closing one returns to the tab it was
 * opened from.
 *
 * [now] is the clock of the shell: last-seen times, the stale age and the Lots' durations are told
 * against it, and the Garden reads it again on its minute tick. [lotsEvents] are the core's
 * stale-mode events for the Garden's polite announcements. [onForeground] runs on every start of
 * the activity: the app resumes its session and reads the Lots again (UX-DR112).
 */
@Composable
fun ColdframeRoot(
    state: SignInState,
    sites: SitesState,
    theme: ThemePreference,
    onSignIn: () -> Unit,
    onSignOut: () -> Unit,
    onSelectTheme: (ThemePreference) -> Unit,
    sitesActions: SitesActions = SitesActions.None,
    systemIsDark: Boolean = isSystemInDarkTheme(),
    lots: LotsState = LotsState.Idle,
    lotsActions: LotsActions = LotsActions.None,
    hubSetup: HubSetupState = HubSetupState.CLOSED,
    hubSetupActions: HubSetupActions = HubSetupActions.None,
    devices: DevicesState = DevicesState.Idle,
    devicesActions: DevicesActions = DevicesActions.None,
    now: () -> Instant = Instant::now,
    nodeSetup: NodeSetupState = NodeSetupState.CLOSED,
    nodeSetupActions: NodeSetupActions = NodeSetupActions.None,
    lotsEvents: Flow<LotsEvent> = emptyFlow(),
    lotDetail: LotDetailState = LotDetailState.Idle,
    lotDetailActions: LotDetailActions = LotDetailActions.None,
    lotDetailEvents: Flow<LotsEvent> = emptyFlow(),
    calibrate: CalibrateState = CalibrateState.Idle,
    calibrateActions: CalibrateActions = CalibrateActions.None,
    thresholds: ThresholdsState = ThresholdsState.Idle,
    thresholdsActions: ThresholdsActions = ThresholdsActions.None,
    onForeground: () -> Unit = {},
) {
    // Every start of the activity, the first one and each return to the foreground.
    LifecycleEventEffect(Lifecycle.Event.ON_START) { onForeground() }
    ColdframeTheme(isDark = theme.isDark(systemIsDark)) {
        when (state) {
            SignInState.Restoring -> {
                Background()
            }

            SignInState.Working -> {
                SignInScreen(notice = null, working = true, onSignIn = onSignIn)
            }

            is SignInState.SignedOut -> {
                SignInScreen(notice = state.notice, working = false, onSignIn = onSignIn)
            }

            is SignInState.SignedIn -> {
                SignedIn(
                    sites,
                    theme,
                    onSignOut,
                    onSelectTheme,
                    sitesActions,
                    lots,
                    lotsActions,
                    hubSetup,
                    hubSetupActions,
                    devices,
                    devicesActions,
                    now,
                    nodeSetup,
                    nodeSetupActions,
                    lotsEvents,
                    lotDetail,
                    lotDetailActions,
                    lotDetailEvents,
                    calibrate,
                    calibrateActions,
                    thresholds,
                    thresholdsActions,
                )
            }
        }
    }
}

@Composable
private fun SignedIn(
    sites: SitesState,
    theme: ThemePreference,
    onSignOut: () -> Unit,
    onSelectTheme: (ThemePreference) -> Unit,
    actions: SitesActions,
    lots: LotsState,
    lotsActions: LotsActions,
    hubSetup: HubSetupState,
    hubSetupActions: HubSetupActions,
    devices: DevicesState,
    devicesActions: DevicesActions,
    now: () -> Instant,
    nodeSetup: NodeSetupState,
    nodeSetupActions: NodeSetupActions,
    lotsEvents: Flow<LotsEvent>,
    lotDetail: LotDetailState,
    lotDetailActions: LotDetailActions,
    lotDetailEvents: Flow<LotsEvent>,
    calibrate: CalibrateState,
    calibrateActions: CalibrateActions,
    thresholds: ThresholdsState,
    thresholdsActions: ThresholdsActions,
) {
    when (sites) {
        SitesState.Idle, SitesState.Loading -> {
            Background()
        }

        is SitesState.Failed -> {
            Box(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .background(Coldframe.colors.background)
                        .safeDrawingPadding()
                        .padding(Spacing.GUTTER_MOBILE.dp),
                contentAlignment = Alignment.Center,
            ) {
                InlineNotice(
                    message = stringResource(sites.notice.loadMessage()),
                    action =
                        if (sites.notice.tryAgain) {
                            NoticeActionUi(stringResource(R.string.notice_try_again), actions.load)
                        } else {
                            null
                        },
                    announcement = Announcement.Assertive,
                )
            }
        }

        is SitesState.NeedsSite -> {
            CreateSiteScreen(form = sites.form, actions = actions)
        }

        is SitesState.Ready -> {
            // The selected tab outlives the shell: Create Site, Add a Hub and Add a Node replace it
            // while open, and closing them returns to the tab they were opened from.
            val tab = rememberSaveable { mutableStateOf(Tab.Garden) }
            // Create Site replaces the shell while open, so no hidden tab stays reachable.
            val creating = sites.creating
            if (creating != null) {
                CreateSiteScreen(form = creating, actions = actions)
            } else if (thresholds !is ThresholdsState.Idle) {
                ThresholdsScreen(state = thresholds, actions = thresholdsActions, now = now)
            } else if (calibrate !is CalibrateState.Idle) {
                CalibrateScreen(state = calibrate, actions = calibrateActions, now = now)
            } else if (hubSetup.open) {
                AddHubFlow(state = hubSetup, actions = hubSetupActions)
            } else if (nodeSetup.open) {
                AddNodeFlow(state = nodeSetup, actions = nodeSetupActions)
            } else {
                AppShell(
                    sites = sites,
                    theme = theme,
                    onSelectTheme = onSelectTheme,
                    onSignOut = onSignOut,
                    actions = actions,
                    lots = lots,
                    lotsActions = lotsActions,
                    onAddHub = hubSetupActions.open,
                    onAddNode = nodeSetupActions.open,
                    devices = devices,
                    devicesActions = devicesActions,
                    tabState = tab,
                    now = now,
                    lotsEvents = lotsEvents,
                    lotDetail = lotDetail,
                    lotDetailActions = lotDetailActions,
                    lotDetailEvents = lotDetailEvents,
                    onCalibrate = calibrateActions.open,
                    onThresholds = thresholdsActions.open,
                )
            }
        }
    }
}

@Composable
private fun Background() {
    Box(Modifier.fillMaxSize().background(Coldframe.colors.background))
}
