package com.escendit.coldframe.android.ui.calibrate

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.calibrate.CalibrateEngine
import com.escendit.coldframe.core.calibrate.CalibrateNotice

/**
 * What Calibrate can ask of the core. The shell never decides when a Reading is fresh, which
 * point is recorded or whether the flow may open; it forwards taps to [CalibrateEngine] and
 * renders its state (AD-14).
 */
class CalibrateActions(
    val open: (lotId: String, name: String) -> Unit = { _, _ -> },
    val close: () -> Unit = {},
    val retry: () -> Unit = {},
    val pick: (readingSeq: Long) -> Unit = {},
    val record: () -> Unit = {},
    /** After Calibration: Set Thresholds for the same Lot (Story 5.4); the shell closes Calibrate and opens Thresholds. */
    val setThresholds: (lotId: String, name: String) -> Unit = { _, _ -> },
) {
    companion object {
        val None = CalibrateActions()

        fun of(
            engine: CalibrateEngine,
            setThresholds: (lotId: String, name: String) -> Unit = { _, _ -> },
        ): CalibrateActions =
            CalibrateActions(
                open = engine::open,
                close = engine::close,
                retry = engine::retry,
                pick = engine::pick,
                record = engine::record,
                setThresholds = setThresholds,
            )
    }
}

/**
 * What happened, what did not change and what to do next; `%1$s` is the Site name where the copy
 * has one.
 */
@StringRes
fun CalibrateNotice.message(): Int =
    when (this) {
        CalibrateNotice.Indistinct -> R.string.calibrate_notice_indistinct
        CalibrateNotice.NotDelivered -> R.string.calibrate_notice_not_delivered
        CalibrateNotice.Forbidden -> R.string.calibrate_notice_forbidden
        CalibrateNotice.NotFound -> R.string.calibrate_notice_not_found
        CalibrateNotice.NoSensor -> R.string.calibrate_no_sensor
        CalibrateNotice.Unreachable -> R.string.calibrate_notice_unreachable
        CalibrateNotice.Certificate -> R.string.notice_certificate
        CalibrateNotice.Unexpected -> R.string.calibrate_notice_unexpected
    }
