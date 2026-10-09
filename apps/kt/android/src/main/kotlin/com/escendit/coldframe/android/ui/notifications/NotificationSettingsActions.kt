package com.escendit.coldframe.android.ui.notifications

import androidx.annotation.StringRes
import com.escendit.coldframe.R
import com.escendit.coldframe.core.notifications.NotificationControl
import com.escendit.coldframe.core.notifications.NotificationSettingsEngine
import com.escendit.coldframe.core.notifications.NotificationSettingsNotice
import com.escendit.coldframe.core.notifications.ReminderCadence

/**
 * What My notifications can ask of the core. The shell never validates a window, picks a zone to
 * propose or decides what a failed change leaves behind; it forwards taps to
 * [NotificationSettingsEngine] and renders its state (AD-14).
 */
class NotificationSettingsActions(
    /** Every entry of the surface reads the settings again. */
    val load: () -> Unit = {},
    /** Try again: after a failed load, or for the change that was not saved. */
    val retry: () -> Unit = {},
    val setWindowFrom: (String) -> Unit = {},
    val setWindowTo: (String) -> Unit = {},
    val saveWindow: () -> Unit = {},
    val confirmTimeZone: () -> Unit = {},
    val changeTimeZone: () -> Unit = {},
    val pickTimeZone: (String) -> Unit = {},
    val timeZones: () -> List<String> = { emptyList() },
    val setMuted: (Boolean) -> Unit = {},
    /** `null` is "Use Site setting". */
    val setReminderCadence: (ReminderCadence?) -> Unit = {},
) {
    companion object {
        val None = NotificationSettingsActions()

        fun of(engine: NotificationSettingsEngine): NotificationSettingsActions =
            NotificationSettingsActions(
                load = engine::load,
                retry = engine::retry,
                setWindowFrom = engine::setWindowFrom,
                setWindowTo = engine::setWindowTo,
                saveWindow = engine::saveWindow,
                confirmTimeZone = engine::confirmTimeZone,
                changeTimeZone = engine::changeTimeZone,
                pickTimeZone = engine::pickTimeZone,
                timeZones = engine::timeZones,
                setMuted = engine::setMuted,
                setReminderCadence = engine::setReminderCadence,
            )
    }
}

/** "Daily" or "Every 2 days": the same words in My notifications and in Site settings. */
@StringRes
fun ReminderCadence.label(): Int =
    when (this) {
        ReminderCadence.Daily -> R.string.reminders_daily
        ReminderCadence.Every2Days -> R.string.reminders_every_2_days
    }

/**
 * Why the settings could not be read, or why the change to [control] was not saved; `%1$s` is the
 * Site name where the copy has one.
 */
@StringRes
fun NotificationSettingsNotice.message(control: NotificationControl? = null): Int =
    when (this) {
        NotificationSettingsNotice.Invalid -> {
            when (control) {
                NotificationControl.Window -> R.string.notifications_notice_invalid_window
                NotificationControl.TimeZone -> R.string.notifications_notice_invalid_time_zone
                else -> R.string.notifications_notice_not_saved
            }
        }

        NotificationSettingsNotice.SiteRefused -> {
            R.string.notifications_notice_site_gone
        }

        NotificationSettingsNotice.NotSaved -> {
            R.string.notifications_notice_not_saved
        }

        NotificationSettingsNotice.Unreachable -> {
            R.string.notice_unreachable
        }

        NotificationSettingsNotice.Certificate -> {
            R.string.notice_certificate
        }

        NotificationSettingsNotice.Unexpected -> {
            R.string.notifications_unavailable
        }
    }
