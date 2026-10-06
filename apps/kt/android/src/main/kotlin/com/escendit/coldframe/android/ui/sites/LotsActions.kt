package com.escendit.coldframe.android.ui.sites

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.lots.LotsNoticeKind
import com.escendit.coldframe.core.sites.NameError

/**
 * What Site settings and the Garden Lot tiles can ask of the core. The shell never computes Lot
 * state, order or status; it forwards taps to [LotsEngine] and renders what comes back (AD-14).
 */
class LotsActions(
    val load: () -> Unit = {},
    /** Pull-to-refresh, and the app coming back to the foreground. */
    val refresh: () -> Unit = {},
    val setSiteName: (String) -> Unit = {},
    val renameSite: () -> Unit = {},
    val setNewLotName: (String) -> Unit = {},
    val createLot: () -> Unit = {},
    val startRename: (String) -> Unit = {},
    val setRename: (String) -> Unit = {},
    val rename: () -> Unit = {},
    val cancelRename: () -> Unit = {},
    val askRemove: (String) -> Unit = {},
    val confirmRemove: () -> Unit = {},
    val cancelRemove: () -> Unit = {},
) {
    companion object {
        val None = LotsActions()

        fun of(engine: LotsEngine): LotsActions =
            LotsActions(
                load = engine::load,
                refresh = engine::refresh,
                setSiteName = engine::setSiteName,
                renameSite = engine::renameSite,
                setNewLotName = engine::setNewLotName,
                createLot = engine::createLot,
                startRename = engine::startRename,
                setRename = engine::setRename,
                rename = engine::rename,
                cancelRename = engine::cancelRename,
                askRemove = engine::askRemove,
                confirmRemove = engine::confirmRemove,
                cancelRemove = engine::cancelRemove,
            )
    }
}

/** A change in Site settings did not happen; `%1$s` is the Site (Forbidden) or the Lot (LotClaimed). */
@StringRes
fun LotsNoticeKind.message(): Int =
    when (this) {
        LotsNoticeKind.Forbidden -> R.string.site_settings_forbidden
        LotsNoticeKind.LotClaimed -> R.string.site_settings_lot_claimed
        LotsNoticeKind.LotNotFound -> R.string.site_settings_lot_not_found
        LotsNoticeKind.RenameSiteUnavailable -> R.string.site_settings_rename_site_unavailable
        LotsNoticeKind.KeyReused -> R.string.site_settings_key_reused
        LotsNoticeKind.Unreachable -> R.string.notice_unreachable
        LotsNoticeKind.Certificate -> R.string.notice_certificate
        LotsNoticeKind.Unexpected -> R.string.site_settings_unexpected
    }

/** A Lot name's reason: Blank names the Lot; TooLong is the shared 100-character copy. */
@StringRes
fun NameError.lotMessage(): Int =
    when (this) {
        NameError.Blank -> R.string.site_settings_lot_name_blank
        NameError.TooLong -> R.string.create_site_name_too_long
    }
