package com.escendit.coldframe.android.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.components.ThemeSwitcher
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.designtokens.Spacing

/** Appearance (UX-DR75): the Theme switcher only. */
@Composable
fun AppearanceScreen(
    theme: ThemePreference,
    onSelectTheme: (ThemePreference) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
    ) {
        ThemeSwitcher(selected = theme, onSelect = onSelectTheme)
    }
}
