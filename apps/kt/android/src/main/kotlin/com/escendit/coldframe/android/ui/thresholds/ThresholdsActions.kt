package com.escendit.coldframe.android.ui.thresholds

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.thresholds.ThresholdsEngine
import com.escendit.coldframe.core.thresholds.ThresholdsNotice

/**
 * What Thresholds can ask of the core. The shell never validates a value, snaps a step or decides who
 * may edit; it forwards taps and drags to [ThresholdsEngine] and renders its state (AD-14).
 */
class ThresholdsActions(
    val open: (lotId: String, name: String, sensorId: String?) -> Unit = { _, _, _ -> },
    val close: () -> Unit = {},
    val retry: () -> Unit = {},
    val setLow: (sensorId: String, value: Double) -> Unit = { _, _ -> },
    val setHigh: (sensorId: String, value: Double) -> Unit = { _, _ -> },
    val setLowText: (sensorId: String, text: String) -> Unit = { _, _ -> },
    val setHighText: (sensorId: String, text: String) -> Unit = { _, _ -> },
    val addHigh: (sensorId: String) -> Unit = {},
    val clearHigh: (sensorId: String) -> Unit = {},
    val turnOnAlerts: (sensorId: String) -> Unit = {},
    val turnOffAlerts: (sensorId: String) -> Unit = {},
    val save: () -> Unit = {},
) {
    companion object {
        val None = ThresholdsActions()

        fun of(engine: ThresholdsEngine): ThresholdsActions =
            ThresholdsActions(
                open = { lotId, name, sensorId -> engine.open(lotId, name, sensorId) },
                close = engine::close,
                retry = engine::retry,
                setLow = engine::setLow,
                setHigh = engine::setHigh,
                setLowText = engine::setLowText,
                setHighText = engine::setHighText,
                addHigh = engine::addHigh,
                clearHigh = engine::clearHigh,
                turnOnAlerts = engine::turnOnAlerts,
                turnOffAlerts = engine::turnOffAlerts,
                save = engine::save,
            )
    }
}

/** What happened, what did not change and what to do next (UX-DR91); `%1$s` is the Site name where the copy has one. */
@StringRes
fun ThresholdsNotice.message(): Int =
    when (this) {
        ThresholdsNotice.Invalid -> R.string.thresholds_notice_invalid
        ThresholdsNotice.Forbidden -> R.string.thresholds_notice_forbidden
        ThresholdsNotice.NotFound -> R.string.thresholds_notice_not_found
        ThresholdsNotice.NoSensor -> R.string.thresholds_no_sensors
        ThresholdsNotice.NotSaved -> R.string.thresholds_notice_not_saved
        ThresholdsNotice.Unreachable -> R.string.notice_unreachable
        ThresholdsNotice.Certificate -> R.string.notice_certificate
        ThresholdsNotice.Unexpected -> R.string.thresholds_notice_unexpected
    }
