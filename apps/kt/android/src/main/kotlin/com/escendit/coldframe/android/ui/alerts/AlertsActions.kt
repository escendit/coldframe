package com.escendit.coldframe.android.ui.alerts

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.alerts.AlertsEngine
import com.escendit.coldframe.core.alerts.AlertsNotice

/**
 * What the Alerts tab can ask of the core: read the list again. An Alert has no action of its own
 * (nothing to mark, nothing to put off); the shell only renders what [AlertsEngine] read (AD-14).
 */
class AlertsActions(
    /** Every entry of the Alerts tab, and Try again. */
    val load: () -> Unit = {},
    /** Pull-to-refresh, and the app coming back to the foreground. */
    val refresh: () -> Unit = {},
) {
    companion object {
        val None = AlertsActions()

        fun of(engine: AlertsEngine): AlertsActions = AlertsActions(load = engine::load, refresh = engine::refresh)
    }
}

/** The Alerts could not be read: no rows are shown with it. */
@StringRes
fun AlertsNotice.message(): Int =
    when (this) {
        AlertsNotice.Unreachable -> R.string.alerts_unreachable
        AlertsNotice.Certificate -> R.string.notice_certificate
    }
