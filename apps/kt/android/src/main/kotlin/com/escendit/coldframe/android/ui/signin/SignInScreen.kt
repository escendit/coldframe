package com.escendit.coldframe.android.ui.signin

import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.Announcement
import com.escendit.coldframe.android.ui.components.InlineNotice
import com.escendit.coldframe.android.ui.components.NoticeActionUi
import com.escendit.coldframe.android.ui.components.PrimaryButton
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.signin.Notice
import com.escendit.coldframe.core.signin.NoticeAction
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlin.math.hypot

/** The catalogue copy of each notice (UX-DR92, UX-DR93). */
@StringRes
fun Notice.message(): Int =
    when (this) {
        Notice.Unreachable -> R.string.notice_unreachable
        Notice.Certificate -> R.string.notice_certificate
        Notice.Keycloak -> R.string.notice_keycloak
        Notice.SignedOut -> R.string.notice_signed_out
    }

@StringRes
fun NoticeAction.label(): Int =
    when (this) {
        NoticeAction.TryAgain -> R.string.notice_try_again
        NoticeAction.SignIn -> R.string.notice_sign_in
    }

/**
 * The Sign-in surface (UX-DR59, UX-DR60): the signature radial gradient, the only one in the
 * product, behind a card with the Coldframe mark and SIGN IN. No Server field (AD-23). A notice
 * sits inside the card with its one action or none; both actions start a new sign-in.
 */
@Composable
fun SignInScreen(
    notice: Notice?,
    working: Boolean,
    onSignIn: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Coldframe.colors
    val primary = colors.primary
    val secondary = colors.secondary
    Box(
        modifier =
            modifier
                .fillMaxSize()
                .drawBehind {
                    val center = Offset(size.width * 0.05f, size.height * 0.05f)
                    val radius = hypot(size.width * 0.95f, size.height * 0.95f)
                    drawRect(Brush.radialGradient(listOf(primary, secondary), center = center, radius = radius))
                }.safeDrawingPadding()
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .widthIn(max = 416.dp)
                    .background(colors.background)
                    .border(1.dp, colors.borderSubtle)
                    .padding(Spacing.STEP_7.dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
        ) {
            Text(
                text = stringResource(R.string.app_name),
                style = Typography.headline.textStyle(),
                color = colors.textPrimary,
                modifier = Modifier.semantics { heading() },
            )
            if (notice != null) {
                InlineNotice(
                    message = stringResource(notice.message()),
                    action = notice.action?.let { NoticeActionUi(stringResource(it.label()), onSignIn) },
                    announcement = if (notice == Notice.SignedOut) Announcement.Polite else Announcement.Assertive,
                )
            }
            PrimaryButton(
                label = stringResource(R.string.signin_action),
                onClick = onSignIn,
                working = working,
                workingLabel = stringResource(R.string.signin_working),
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
