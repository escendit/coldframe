package com.escendit.coldframe.android.ui.devices

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.devices.DevicesNotice
import com.escendit.coldframe.core.devices.NodeActionNotice
import com.escendit.coldframe.core.devices.SilenceWindowNotice

/**
 * What the Devices tab can ask of the core. The shell never computes whether a Device is online;
 * it asks [DevicesEngine] to read the list again and renders what comes back (AD-14).
 */
class DevicesActions(
    val load: () -> Unit = {},
    val moveNode: (nodeId: String, lotId: String) -> Unit = { _, _ -> },
    val unassignNode: (nodeId: String) -> Unit = {},
    /** The Silence Window of a Hub or Node in whole seconds; `null` is "Use default" (Story 7.2). */
    val setSilenceWindow: (deviceId: String, windowSeconds: Int?) -> Unit = { _, _ -> },
) {
    companion object {
        val None = DevicesActions()

        fun of(engine: DevicesEngine): DevicesActions =
            DevicesActions(
                load = engine::load,
                moveNode = engine::moveNode,
                unassignNode = engine::unassignNode,
                setSilenceWindow = engine::setSilenceWindow,
            )
    }
}

/** Why a move or unassign did not happen, as the inline copy under the Node's row. */
@StringRes
fun NodeActionNotice.message(): Int =
    when (this) {
        NodeActionNotice.Forbidden -> R.string.devices_action_forbidden
        NodeActionNotice.LotTaken -> R.string.devices_action_lot_taken
        NodeActionNotice.NotFound -> R.string.devices_action_not_found
        NodeActionNotice.Unreachable -> R.string.devices_unreachable
        NodeActionNotice.Certificate -> R.string.notice_certificate
        NodeActionNotice.Unexpected -> R.string.devices_action_unexpected
    }

/**
 * Why a Silence Window was not changed, as the inline copy under the Device's row. A 403 is the
 * Site-wide "You can't change this on ‹Site›…" (UX-DR84) and takes the Site's name.
 */
@StringRes
fun SilenceWindowNotice.message(): Int =
    when (this) {
        SilenceWindowNotice.Forbidden -> R.string.site_settings_forbidden
        SilenceWindowNotice.NotFound -> R.string.devices_silence_window_not_found
        SilenceWindowNotice.OutOfRange -> R.string.devices_silence_window_range
        SilenceWindowNotice.Unreachable -> R.string.devices_unreachable
        SilenceWindowNotice.Certificate -> R.string.notice_certificate
        SilenceWindowNotice.Unexpected -> R.string.devices_action_unexpected
    }

/** The Devices could not be read: no rows are shown with it. */
@StringRes
fun DevicesNotice.message(): Int =
    when (this) {
        DevicesNotice.Unreachable -> R.string.devices_unreachable
        DevicesNotice.Certificate -> R.string.notice_certificate
    }
