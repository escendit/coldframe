package com.escendit.coldframe.android

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.escendit.coldframe.android.ui.shell.AppShell
import com.escendit.coldframe.android.ui.signin.SignInScreen
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState

/**
 * Maps the core's state to a surface. No branching on errors, URLs or tokens here: the shell
 * only renders [SignInState] and [ThemePreference].
 */
@Composable
fun ColdframeRoot(
    state: SignInState,
    theme: ThemePreference,
    onSignIn: () -> Unit,
    onSignOut: () -> Unit,
    onSelectTheme: (ThemePreference) -> Unit,
    systemIsDark: Boolean = isSystemInDarkTheme(),
) {
    ColdframeTheme(isDark = theme.isDark(systemIsDark)) {
        when (state) {
            SignInState.Restoring -> Box(Modifier.fillMaxSize().background(Coldframe.colors.background))
            SignInState.Working -> SignInScreen(notice = null, working = true, onSignIn = onSignIn)
            is SignInState.SignedOut -> SignInScreen(notice = state.notice, working = false, onSignIn = onSignIn)
            is SignInState.SignedIn -> AppShell(theme = theme, onSelectTheme = onSelectTheme, onSignOut = onSignOut)
        }
    }
}
