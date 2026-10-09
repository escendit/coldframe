package com.escendit.coldframe.android.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeIcons
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/** What the time-zone confirm panel asks of the core, on Create Site and on My notifications alike. */
class TimeZoneActions(
    val confirm: () -> Unit = {},
    val change: () -> Unit = {},
    val pick: (String) -> Unit = {},
    val zones: () -> List<String> = { emptyList() },
)

/**
 * The time-zone confirm panel (UX-DR48, UX-DR61), on Create Site and on My notifications: a 1 dp
 * dashed `support-warning` box proposing [shown], or naming the zone the User [chosen]. Confirm
 * shows until there is a chosen zone. Change shows a filter field over the IANA IDs, which are
 * never translated or reformatted. Without a zone to propose ([shown] is `null`) it says so and
 * shows the list at once. While [working] a choice is on its way and the buttons ignore presses.
 */
@Composable
fun TimeZonePanel(
    shown: String?,
    chosen: String?,
    changing: Boolean,
    actions: TimeZoneActions,
    modifier: Modifier = Modifier,
    working: Boolean = false,
) {
    val colors = Coldframe.colors
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .dashedBorder(colors.supportWarning)
                .padding(Spacing.STEP_5.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_4.dp),
    ) {
        Text(
            text = stringResource(R.string.time_zone_legend),
            style = Typography.body.textStyle(),
            color = colors.textSecondary,
        )
        Text(
            text =
                when {
                    shown == null -> stringResource(R.string.time_zone_unknown)
                    chosen != null -> stringResource(R.string.time_zone_chosen, shown)
                    else -> stringResource(R.string.time_zone_question, shown)
                },
            style = Typography.bodyLg.textStyle(),
            color = colors.textPrimary,
        )
        Text(
            text = stringResource(R.string.time_zone_helper),
            style = Typography.helper.textStyle(),
            color = colors.textHelper,
        )
        if (chosen == null && shown != null) {
            ColdframeButton(
                label = stringResource(R.string.time_zone_confirm),
                onClick = actions.confirm,
                variant = ButtonVariant.Secondary,
                working = working,
            )
        }
        if (changing || shown == null) {
            TimeZoneList(chosen = chosen, working = working, actions = actions)
        } else {
            ColdframeButton(
                label = stringResource(R.string.time_zone_change),
                onClick = actions.change,
                variant = ButtonVariant.Ghost,
                working = working,
            )
        }
    }
}

@Composable
private fun TimeZoneList(
    chosen: String?,
    working: Boolean,
    actions: TimeZoneActions,
) {
    val colors = Coldframe.colors
    var filter by rememberSaveable { mutableStateOf("") }
    val zones = remember(actions.zones) { actions.zones() }
    val needle = filter.trim().replace(' ', '_')
    val listed = zones.filter { it.contains(needle, ignoreCase = true) }
    TextInput(
        label = stringResource(R.string.time_zone_filter),
        value = filter,
        onValueChange = { filter = it },
        helper = stringResource(R.string.time_zone_filter_helper),
    )
    if (listed.isEmpty()) {
        Text(
            text = stringResource(R.string.time_zone_none),
            style = Typography.body.textStyle(),
            color = colors.textPrimary,
        )
    } else {
        // Bounded, so the list scrolls inside the panel while the form scrolls around it.
        LazyColumn(modifier = Modifier.fillMaxWidth().heightIn(max = 320.dp)) {
            items(listed, key = { it }) { zone ->
                val selected = zone == chosen
                Row(
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .heightIn(min = Spacing.BUTTON_HEIGHT.dp)
                            .clickable(enabled = !working, role = Role.Button) { actions.pick(zone) }
                            .semantics { this.selected = selected }
                            .padding(horizontal = Spacing.STEP_4.dp, vertical = Spacing.STEP_4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
                ) {
                    Text(
                        text = zone,
                        style = Typography.bodyLg.textStyle(),
                        color = colors.textPrimary,
                        modifier = Modifier.weight(1f),
                    )
                    if (selected) {
                        Icon(
                            ColdframeIcons.checkmark,
                            contentDescription = null,
                            tint = colors.primaryText,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
                HorizontalDivider(color = colors.borderSubtle, thickness = 1.dp)
            }
        }
    }
}
