package com.escendit.coldframe.android.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography

/**
 * How urgently TalkBack announces a notice when it appears (UX-DR104). [None] is for a notice whose
 * text moves with the clock: its screen announces it once by other means, and a tick says nothing.
 */
enum class Announcement { Polite, Assertive, None }

/** The notice's one action, if any. */
data class NoticeActionUi(
    val label: String,
    val onClick: () -> Unit,
)

/**
 * Inline notice (UX-DR56): `layer-01`, 3 dp `border-strong` left edge, `body` text. Never
 * dismissable; at most one action. The message is a live region, so it is announced on arrival,
 * unless [announcement] is [Announcement.None].
 */
@Composable
fun InlineNotice(
    message: String,
    modifier: Modifier = Modifier,
    action: NoticeActionUi? = null,
    announcement: Announcement = Announcement.Polite,
) {
    val colors = Coldframe.colors
    val edge = colors.borderStrong
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .background(colors.layer01)
                .drawBehind {
                    drawRect(edge, topLeft = Offset.Zero, size = Size(3.dp.toPx(), size.height))
                }.padding(
                    start = Spacing.STEP_5.dp,
                    end = Spacing.STEP_5.dp,
                    top = Spacing.STEP_4.dp,
                    bottom = Spacing.STEP_4.dp,
                ),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
    ) {
        Text(
            text = message,
            style = Typography.body.textStyle(),
            color = colors.textPrimary,
            modifier =
                Modifier.semantics {
                    when (announcement) {
                        Announcement.Polite -> liveRegion = LiveRegionMode.Polite
                        Announcement.Assertive -> liveRegion = LiveRegionMode.Assertive
                        Announcement.None -> Unit
                    }
                },
        )
        if (action != null) {
            ColdframeButton(
                label = action.label,
                onClick = action.onClick,
                variant = ButtonVariant.Ghost,
                modifier = Modifier.padding(start = 0.dp),
            )
        }
    }
}
