package com.escendit.coldframe.android.ui.pause

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.sites.PauseEngine
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope

/**
 * What a surface can ask of the core about Pause (Story 8.2): open the sheet, fill it, save it, and
 * the one-tap Resume. The shell never decides whether something is paused or who may pause; it
 * forwards taps to its surface's [PauseEngine] and renders what comes back (AD-14).
 */
class PauseActions(
    /** Pause in the Site menu: the sheet with Whole Site only. */
    val openForSite: () -> Unit = {},
    /** Pause on Lot detail or a Devices row: the sheet with This Device selected. */
    val openForDevice: (deviceId: String) -> Unit = {},
    val setScope: (PauseScope) -> Unit = {},
    /** The picked calendar date as `yyyy-MM-dd`, or `null` for no end. */
    val setUntil: (String?) -> Unit = {},
    val cancel: () -> Unit = {},
    /** The primary button, and Try again inside the sheet. */
    val save: () -> Unit = {},
    val resumeSite: () -> Unit = {},
    val resumeDevice: (deviceId: String) -> Unit = {},
    /** Try again after a Resume that did not happen. */
    val retry: () -> Unit = {},
) {
    companion object {
        val None = PauseActions()

        fun of(engine: PauseEngine): PauseActions =
            PauseActions(
                openForSite = engine::openForSite,
                openForDevice = engine::openForDevice,
                setScope = engine::setScope,
                setUntil = engine::setUntil,
                cancel = engine::cancel,
                save = engine::save,
                resumeSite = engine::resumeSite,
                resumeDevice = engine::resumeDevice,
                retry = engine::retry,
            )
    }
}

/**
 * Why a Pause or a Resume did not happen. [unreachable] is the surface's own "can't reach" copy.
 * [PauseNotice.Forbidden] and [PauseNotice.SiteGone] take the Site's name; [PauseNotice.DateRefused] is the date field's line.
 */
@StringRes
fun PauseNotice.message(
    @StringRes unreachable: Int,
): Int =
    when (this) {
        PauseNotice.DateRefused -> R.string.pause_until_refused
        PauseNotice.Forbidden -> R.string.site_settings_forbidden
        PauseNotice.NotFound -> R.string.pause_not_found
        PauseNotice.SiteGone -> R.string.notifications_notice_site_gone
        PauseNotice.Unreachable -> unreachable
        PauseNotice.Certificate -> R.string.notice_certificate
        PauseNotice.Unexpected -> R.string.pause_unexpected
    }
