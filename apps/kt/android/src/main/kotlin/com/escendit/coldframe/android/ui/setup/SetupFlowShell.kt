package com.escendit.coldframe.android.ui.setup

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.view.accessibility.AccessibilityManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.components.ButtonVariant
import com.escendit.coldframe.android.ui.components.ColdframeButton
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.textStyle
import com.escendit.coldframe.core.setup.AndroidRadioState
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.designtokens.Spacing
import com.escendit.coldframe.designtokens.Typography
import kotlinx.coroutines.delay

/**
 * Setup flow shell (UX-DR39): Cancel (step 1) or Back top-left, the big "01 / 05" counter
 * (current in `primary-text`, total in `text-helper`), the step title in `headline`, then one
 * question per screen with its primary action directly below, not pinned. On every step change
 * focus moves to the title, and the pane title makes TalkBack read it.
 */
@Composable
fun SetupFlowShell(
    step: Int,
    total: Int,
    title: String,
    showsCancel: Boolean,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    val colors = Coldframe.colors
    val titleFocus = remember { FocusRequester() }
    LaunchedEffect(step, title) { titleFocus.requestFocus() }
    val counter = stringResource(R.string.setup_step_description, step, total)
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .background(colors.background)
                .safeDrawingPadding()
                .semantics { paneTitle = title }
                .verticalScroll(rememberScrollState())
                .padding(Spacing.GUTTER_MOBILE.dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.STEP_6.dp),
    ) {
        ColdframeButton(
            label = stringResource(if (showsCancel) R.string.modal_cancel else R.string.nav_back),
            onClick = onBack,
            variant = ButtonVariant.Ghost,
        )
        Row(
            modifier = Modifier.clearAndSetSemantics { contentDescription = counter },
            verticalAlignment = Alignment.Bottom,
            horizontalArrangement = Arrangement.spacedBy(Spacing.STEP_3.dp),
        ) {
            Text(
                text = twoDigits(step),
                style = Typography.stepCounter.textStyle(),
                color = colors.primaryText,
            )
            Text(
                text = stringResource(R.string.setup_step_of, twoDigits(total)),
                style = Typography.stepCounter.textStyle(),
                color = colors.textHelper,
            )
        }
        Text(
            text = title,
            style = Typography.headline.textStyle(),
            color = colors.textPrimary,
            modifier =
                Modifier
                    .fillMaxWidth()
                    .focusRequester(titleFocus)
                    .focusable()
                    .semantics { heading() },
        )
        content()
    }
}

/** "01" to "05": numbers, not copy. */
fun twoDigits(value: Int): String = value.toString().padStart(2, '0')

/**
 * What every setup flow does around its steps: the screen stays on while the flow is open, system
 * back goes to [onSystemBack], the radio is read again on every resume, and the Bluetooth
 * permissions are asked for once the radio says they are missing.
 */
@Composable
fun SetupFlowEffects(
    open: Boolean,
    keepAwake: Boolean,
    radio: RadioState,
    onSystemBack: () -> Unit,
    onRecheckRadio: () -> Unit,
) {
    val view = LocalView.current
    DisposableEffect(keepAwake) {
        view.keepScreenOn = keepAwake
        onDispose { view.keepScreenOn = false }
    }
    BackHandler(enabled = open) { onSystemBack() }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { onRecheckRadio() }
    val permissions =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
            onRecheckRadio()
        }
    LaunchedEffect(radio == RadioState.Unauthorized) {
        if (radio ==
            RadioState.Unauthorized
        ) {
            permissions.launch(AndroidRadioState.REQUIRED_PERMISSIONS.toTypedArray())
        }
    }
}

/** The leave confirmation (UX-DR39): a native dialog naming the Device; Keep setting up changes nothing. */
@Composable
fun SetupLeaveDialog(
    question: String,
    detail: String,
    onConfirm: () -> Unit,
    onStay: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onStay,
        title = { Text(question, style = Typography.section.textStyle()) },
        text = { Text(detail, style = Typography.body.textStyle()) },
        confirmButton = {
            ColdframeButton(
                label = stringResource(R.string.setup_leave_confirm),
                onClick = onConfirm,
                variant = ButtonVariant.Secondary,
            )
        },
        dismissButton = {
            ColdframeButton(
                label = stringResource(R.string.setup_leave_stay),
                onClick = onStay,
                variant = ButtonVariant.Ghost,
            )
        },
        containerColor = Coldframe.colors.background,
    )
}

/**
 * One announcement as a live region that exists before it is filled (UX-DR105): polite for
 * progress and candidates, [assertive] for errors. [id] is the announcement's id, null while
 * there is none; [message] is its text. While TalkBack is on, the core hears that an announcement
 * is being read for an estimated reading time, so a timeout waits (UX-DR103); Android reports no
 * "finished" event.
 */
@Composable
fun SetupAnnouncementRegion(
    id: Int?,
    message: String,
    assertive: Boolean,
    onAnnouncing: (Boolean) -> Unit,
) {
    val context = LocalContext.current
    val announcing by rememberUpdatedState(onAnnouncing)
    val latest by rememberUpdatedState(message)
    // The region always exists; every announcement id empties it, then fills it on the next
    // frame, so the same words twice are still a change TalkBack reads.
    var shown by remember { mutableStateOf("") }
    LaunchedEffect(id) {
        shown = ""
        if (id == null) return@LaunchedEffect
        withFrameNanos { }
        shown = latest
        val manager = context.getSystemService(AccessibilityManager::class.java)
        if (manager?.isEnabled == true && manager.isTouchExplorationEnabled) {
            announcing(true)
            try {
                delay(readingTimeMillis(latest))
            } finally {
                announcing(false)
            }
        }
    }
    Box(
        modifier =
            Modifier.size(1.dp).semantics {
                contentDescription = shown
                liveRegion = if (assertive) LiveRegionMode.Assertive else LiveRegionMode.Polite
            },
    )
}

/** At least 1 s plus 60 ms per character: a screen reader's pace, generously. */
fun readingTimeMillis(message: String): Long = 1_000L + 60L * message.length

/** Bluetooth settings when it is off, the app's own settings when its permission is missing. */
fun Context.openBluetoothSettings(bluetoothOff: Boolean) {
    val intent =
        if (bluetoothOff) {
            Intent(Settings.ACTION_BLUETOOTH_SETTINGS)
        } else {
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", packageName, null))
        }
    startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
}
