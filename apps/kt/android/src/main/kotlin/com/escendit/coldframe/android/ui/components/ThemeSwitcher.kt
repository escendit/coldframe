package com.escendit.coldframe.android.ui.components

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import com.escendit.coldframe.R
import com.escendit.coldframe.core.appearance.ThemePreference

/** Theme switcher (UX-DR53): System (default) / Light / Dark, applied at once, no Save. */
@Composable
fun ThemeSwitcher(
    selected: ThemePreference,
    onSelect: (ThemePreference) -> Unit,
    modifier: Modifier = Modifier,
) {
    SegmentedChoice(
        label = stringResource(R.string.appearance_theme),
        segments =
            listOf(
                Segment(ThemePreference.System, stringResource(R.string.theme_system)),
                Segment(ThemePreference.Light, stringResource(R.string.theme_light)),
                Segment(ThemePreference.Dark, stringResource(R.string.theme_dark)),
            ),
        selected = selected,
        onSelect = onSelect,
        helper = stringResource(R.string.appearance_theme_helper),
        modifier = modifier,
    )
}
