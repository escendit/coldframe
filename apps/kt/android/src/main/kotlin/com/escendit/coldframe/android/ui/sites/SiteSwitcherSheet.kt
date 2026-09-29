package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.styledText
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * Site switcher (UX-DR23): a Material `ModalBottomSheet` listing every Site in the Server's order
 * with the user's Role, the current Site marked with `checkmark` in `primary-text` and exposed as
 * selected, and "New Site" last, even with a single Site.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SiteSwitcherSheet(
    sites: List<SiteSummary>,
    currentId: String,
    onSelect: (String) -> Unit,
    onNewSite: () -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Coldframe.colors
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        shape = RectangleShape,
        containerColor = colors.background,
        contentColor = colors.textPrimary,
        tonalElevation = 0.dp,
        scrimColor = colors.overlay,
        dragHandle = null,
    ) {
        SiteSwitcherList(sites, currentId, onSelect, onNewSite)
    }
}

/** The switcher's content, also rendered on its own in tests and snapshots. */
@Composable
fun SiteSwitcherList(
    sites: List<SiteSummary>,
    currentId: String,
    onSelect: (String) -> Unit,
    onNewSite: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    Column(modifier = modifier.fillMaxWidth().verticalScroll(rememberScrollState()).navigationBarsPadding()) {
        Text(
            text = stringResource(R.string.sites_title),
            style = Typography.section.textStyle(),
            color = colors.textPrimary,
            modifier = Modifier.padding(Spacing.GUTTER_MOBILE.dp).semantics { heading() },
        )
        HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        sites.forEach { site ->
            val current = site.id == currentId
            Row(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                        .clickable(role = Role.Button) { onSelect(site.id) }
                        .semantics { selected = current }
                        .padding(horizontal = Spacing.GUTTER_MOBILE.dp, vertical = Spacing.STEP_4.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
            ) {
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Spacing.STEP_1.dp)) {
                    Text(text = site.name, style = Typography.bodyLg.textStyle(), color = colors.textPrimary)
                    Text(
                        text = styledText(stringResource(site.role.label()), Typography.statusLabel),
                        style = Typography.statusLabel.textStyle(),
                        color = colors.textSecondary,
                    )
                }
                if (current) {
                    Icon(
                        ColdframeIcons.checkmark,
                        contentDescription = null,
                        tint = colors.primaryText,
                        modifier = Modifier.size(24.dp),
                    )
                }
            }
            HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
        }
        Row(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                    .clickable(role = Role.Button, onClick = onNewSite)
                    .padding(horizontal = Spacing.GUTTER_MOBILE.dp, vertical = Spacing.STEP_4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.sites_new),
                style = Typography.bodyLg.textStyle(),
                color = colors.primaryText,
            )
        }
    }
}
