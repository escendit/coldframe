package com.escendit.coldframe.android.ui.devices

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.devices.DevicesNotice

/**
 * What the Devices tab can ask of the core. The shell never computes whether a Device is online;
 * it asks [DevicesEngine] to read the list again and renders what comes back (AD-14).
 */
class DevicesActions(
    val load: () -> Unit = {},
) {
    companion object {
        val None = DevicesActions()

        fun of(engine: DevicesEngine): DevicesActions = DevicesActions(load = engine::load)
    }
}

/** The Devices could not be read: no rows are shown with it. */
@StringRes
fun DevicesNotice.message(): Int =
    when (this) {
        DevicesNotice.Unreachable -> R.string.devices_unreachable
        DevicesNotice.Certificate -> R.string.notice_certificate
    }
