package com.escendit.coldframe.android.ui.sites

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MenuDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.sites.SiteMenuAction
import com.escendit.coldframe.core.sites.SiteMenuItem
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import com.escendit.coldframe.core.sites.SiteMenu as SiteMenuModel

/**
 * Site menu (UX-DR22): the `overflow-menu--vertical` trigger at the right of the Site summary
 * header opens a Material dropdown with the core's items for the Role. For now that is only
 * "Site settings", which opens Site settings (UX-DR74); Pause arrives with its sheet. [items] are
 * the core's: in stale mode it hands them over disabled, and a disabled item says "Needs your
 * Server" (UX-DR79).
 */
@Composable
fun SiteMenu(
    site: SiteSummary,
    onOpenSiteSettings: () -> Unit,
    modifier: Modifier = Modifier,
    items: List<SiteMenuItem> = SiteMenuModel.items(site.role),
) {
    val colors = Coldframe.colors
    var open by rememberSaveable { mutableStateOf(false) }
    Box(modifier = modifier) {
        IconButton(
            onClick = { open = true },
            modifier = Modifier.sizeIn(minWidth = Spacing.BUTTON_HEIGHT.dp, minHeight = Spacing.BUTTON_HEIGHT.dp),
        ) {
            Icon(
                ColdframeIcons.overflowMenuVertical,
                contentDescription = stringResource(R.string.site_menu_open, site.name),
                tint = colors.textPrimary,
                modifier = Modifier.size(24.dp),
            )
        }
        DropdownMenu(
            expanded = open,
            onDismissRequest = { open = false },
            shape = RectangleShape,
            containerColor = colors.background,
            tonalElevation = 0.dp,
            shadowElevation = 0.dp,
        ) {
            items.forEach { item ->
                DropdownMenuItem(
                    text = {
                        Column {
                            Text(
                                text = stringResource(item.action.label(), site.name),
                                style = Typography.bodyLg.textStyle(),
                            )
                            if (!item.enabled) {
                                Text(
                                    text = stringResource(R.string.site_menu_needs_server),
                                    style = Typography.helper.textStyle(),
                                )
                            }
                        }
                    },
                    colors =
                        MenuDefaults.itemColors(
                            textColor = colors.textPrimary,
                            disabledTextColor = colors.textHelper,
                        ),
                    enabled = item.enabled,
                    onClick = {
                        open = false
                        when (item.action) {
                            SiteMenuAction.SiteSettings -> onOpenSiteSettings()

                            // Pause and Resume stay hidden until the Pause sheet exists.
                            SiteMenuAction.Pause, SiteMenuAction.Resume -> Unit
                        }
                    },
                    modifier = Modifier.heightIn(min = Spacing.BUTTON_HEIGHT.dp),
                )
            }
        }
    }
}
