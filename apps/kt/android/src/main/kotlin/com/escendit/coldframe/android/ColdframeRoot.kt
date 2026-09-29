package com.escendit.coldframe.android

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.shell.AppShell
import com.escendit.coldframe.android.ui.signin.SignInScreen
import com.escendit.coldframe.android.ui.sites.CreateSiteScreen
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.sites.loadMessage
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.designtokens.Spacing

/**
 * Maps the core's state to a surface. No branching on errors, URLs or tokens here: the shell
 * only renders [SignInState], [SitesState] and [ThemePreference]. Signed in, the Sites decide:
 * Create Site replaces the tab shell without a Membership, and covers it from "New Site".
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
) {
    ColdframeTheme(isDark = theme.isDark(systemIsDark)) {
        when (state) {
            SignInState.Restoring -> Background()
            SignInState.Working -> SignInScreen(notice = null, working = true, onSignIn = onSignIn)
            is SignInState.SignedOut -> SignInScreen(notice = state.notice, working = false, onSignIn = onSignIn)
            is SignInState.SignedIn -> SignedIn(sites, theme, onSignOut, onSelectTheme, sitesActions, lots, lotsActions)
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
            // Create Site replaces the shell while open, so no hidden tab stays reachable.
            val creating = sites.creating
            if (creating != null) {
                CreateSiteScreen(form = creating, actions = actions)
            } else {
                AppShell(
                    sites = sites,
                    theme = theme,
                    onSelectTheme = onSelectTheme,
                    onSignOut = onSignOut,
                    actions = actions,
                    lots = lots,
                    lotsActions = lotsActions,
                )
            }
        }
    }
}

@Composable
private fun Background() {
    Box(Modifier.fillMaxSize().background(Coldframe.colors.background))
}
