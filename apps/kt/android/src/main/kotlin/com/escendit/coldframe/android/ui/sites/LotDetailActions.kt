package com.escendit.coldframe.android.ui.sites

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.lots.LotDetailEngine
import com.escendit.coldframe.core.lots.LotDetailNotice
import com.escendit.coldframe.core.lots.SensorQuantity

/**
 * What Lot detail can ask of the core. The shell never computes a status, a unit or an aggregate;
 * it forwards taps to [LotDetailEngine] and renders what comes back (AD-14).
 */
class LotDetailActions(
    val open: (lotId: String, name: String) -> Unit = { _, _ -> },
    val close: () -> Unit = {},
    /** Pull-to-refresh, the app coming back to the foreground, and Try again. */
    val refresh: () -> Unit = {},
    val pick: (SensorQuantity) -> Unit = {},
) {
    companion object {
        val None = LotDetailActions()

        fun of(engine: LotDetailEngine): LotDetailActions =
            LotDetailActions(
                open = engine::open,
                close = engine::close,
                refresh = engine::refresh,
                pick = engine::pick,
            )
    }
}

/** Lot detail could not be read and there is nothing to show; `%1$s` is the Site name where the copy has one. */
@StringRes
fun LotDetailNotice.message(): Int =
    when (this) {
        LotDetailNotice.NotFound -> R.string.lot_detail_not_found
        LotDetailNotice.Forbidden -> R.string.lot_detail_forbidden
        LotDetailNotice.Unreachable -> R.string.notice_unreachable
        LotDetailNotice.Certificate -> R.string.notice_certificate
        LotDetailNotice.Unexpected -> R.string.lot_detail_unexpected
    }
