package com.escendit.coldframe.android.ui.devices

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.devices.DevicesNotice
import com.escendit.coldframe.core.devices.NodeActionNotice

/**
 * What the Devices tab can ask of the core. The shell never computes whether a Device is online;
 * it asks [DevicesEngine] to read the list again and renders what comes back (AD-14).
 */
class DevicesActions(
    val load: () -> Unit = {},
    val moveNode: (nodeId: String, lotId: String) -> Unit = { _, _ -> },
    val unassignNode: (nodeId: String) -> Unit = {},
) {
    companion object {
        val None = DevicesActions()

        fun of(engine: DevicesEngine): DevicesActions =
            DevicesActions(load = engine::load, moveNode = engine::moveNode, unassignNode = engine::unassignNode)
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

/** The Devices could not be read: no rows are shown with it. */
@StringRes
fun DevicesNotice.message(): Int =
    when (this) {
        DevicesNotice.Unreachable -> R.string.devices_unreachable
        DevicesNotice.Certificate -> R.string.notice_certificate
    }
