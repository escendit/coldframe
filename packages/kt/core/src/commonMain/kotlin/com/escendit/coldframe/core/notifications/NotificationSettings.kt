package com.escendit.coldframe.core.notifications

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.NotificationSettingsDto
import com.escendit.coldframe.core.api.SetSiteNotificationSettingsRequestDto
import com.escendit.coldframe.core.api.SiteNotificationSettingsDto
import com.escendit.coldframe.core.api.SiteReminderCadenceDto
import com.escendit.coldframe.core.api.UpdateNotificationSettingsRequestDto
import com.escendit.coldframe.core.sites.SiteSummary

/** The calls of My notifications; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface NotificationSettingsApi {
    /** `getMyNotificationSettings`: the caller's Notification Window and time zone. */
    public suspend fun getMyNotificationSettings(): ApiResult<NotificationSettingsDto>

    /** `updateMyNotificationSettings`: the fields that are set; answers the settings in force. */
    public suspend fun updateMyNotificationSettings(
        request: UpdateNotificationSettingsRequestDto,
    ): ApiResult<NotificationSettingsDto>

    /** `getSiteNotificationSettings` (Member): the caller's mute and Reminder cadence for a Site, and the Site's cadence. */
    public suspend fun getSiteNotificationSettings(siteId: String): ApiResult<SiteNotificationSettingsDto>

    /** `setSiteNotificationSettings` (Member): the mute and the cadence, both every time. */
    public suspend fun setSiteNotificationSettings(
        siteId: String,
        request: SetSiteNotificationSettingsRequestDto,
    ): ApiResult<SiteNotificationSettingsDto>
}

/** The Site's Reminder cadence, which Site settings shows; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface SiteReminderCadenceApi {
    /** `getSiteReminderCadence` (Member). */
    public suspend fun getSiteReminderCadence(siteId: String): ApiResult<SiteReminderCadenceDto>

    /** `setSiteReminderCadence` (Admin+): [cadence] is a [ReminderCadence.key]. */
    public suspend fun setSiteReminderCadence(
        siteId: String,
        cadence: String,
    ): ApiResult<SiteReminderCadenceDto>
}

/** How often a Reminder repeats while a Threshold Alert stays open (UX-DR50). There is no "never". */
public enum class ReminderCadence {
    Daily,
    Every2Days,
    ;

    /** The contract value: `daily`, `every2Days`. */
    public val key: String get() = name.replaceFirstChar { it.lowercase() }

    public companion object {
        /** The Server's value, or `null` for none or one this app does not know. */
        public fun fromServer(value: String?): ReminderCadence? = entries.firstOrNull { it.key == value }
    }
}

/**
 * The daily Notification Window (UX-DR47): wall-clock `"HH:mm"` (24 h) times in the User's time zone, within one
 * day. The Server stays the only validator; [inOrder] only gates Save.
 */
public data class NotificationWindow(
    val from: String,
    val to: String,
) {
    /** Minutes since midnight, for the 24 h bar; 0 for a text that is no time. */
    val fromMinutes: Int get() = minutesOf(from) ?: 0

    val toMinutes: Int get() = minutesOf(to) ?: 0

    /** Both are times and the window starts before it ends. */
    val inOrder: Boolean
        get() {
            val start = minutesOf(from) ?: return false
            val end = minutesOf(to) ?: return false
            return start < end
        }

    public companion object {
        public const val DEFAULT_FROM: String = "07:00"
        public const val DEFAULT_TO: String = "22:00"
        public const val MINUTES_PER_DAY: Int = 1440
        private const val MINUTES_PER_HOUR = 60
        private const val HOURS_PER_DAY = 24
        private const val LENGTH = 5
        private const val SEPARATOR = 2
        private const val TWO_DIGITS = 10

        /** `"HH:mm"` as minutes since midnight, or `null` when [time] is not one (`"7:00"`, `"24:00"`). */
        public fun minutesOf(time: String): Int? {
            if (time.length != LENGTH || time[SEPARATOR] != ':') return null
            val digits = time.removeRange(SEPARATOR, SEPARATOR + 1)
            if (!digits.all { it in '0'..'9' }) return null
            val hour = digits.substring(0, SEPARATOR).toInt()
            val minute = digits.substring(SEPARATOR).toInt()
            if (hour >= HOURS_PER_DAY || minute >= MINUTES_PER_HOUR) return null
            return hour * MINUTES_PER_HOUR + minute
        }

        /** A picker's hour and minute as `"HH:mm"`. */
        public fun timeOf(
            hour: Int,
            minute: Int,
        ): String = "${twoDigits(hour)}:${twoDigits(minute)}"

        private fun twoDigits(value: Int): String = if (value < TWO_DIGITS) "0$value" else value.toString()
    }
}

/**
 * The time-zone confirm panel on My notifications (UX-DR48). [detected] is the proposal: the device's zone, else
 * the zone the Server stored as detected, else `null` (the User picks from the list). [chosen] is the zone the
 * User chose, as the Server holds it; the panel shows until there is one. [changing] shows the searchable list;
 * [working] while a choice is on its way.
 */
public data class NotificationTimeZone(
    val detected: String?,
    val chosen: String?,
    val changing: Boolean,
    val working: Boolean = false,
) {
    /** The zone the panel names: the User's choice, else the proposal, else none. */
    val shown: String? get() = chosen ?: detected

    val confirmed: Boolean get() = chosen != null
}

/**
 * My settings for the current Site (UX-DR49, UX-DR50): the mute, which affects only this User, and my Reminder
 * cadence, where `null` is "Use Site setting" and [siteReminderCadence] is what the Site uses. [working] while a
 * change is on its way; the values already show the change.
 */
public data class SiteNotificationSettings(
    val site: SiteSummary,
    val muted: Boolean,
    val reminderCadence: ReminderCadence?,
    val siteReminderCadence: ReminderCadence,
    val working: Boolean = false,
)

/** The control of My notifications a notice belongs to. */
public enum class NotificationControl {
    Window,
    TimeZone,
    Mute,
    ReminderCadence,
}

/** Why My notifications could not be read, or a change was not saved. Only some can be tried again. */
public enum class NotificationSettingsNotice(
    public val tryAgain: Boolean,
) {
    /** 400: the Server refused the window or does not know the time zone. Nothing changed. */
    Invalid(false),

    /** 403 or 404 on a Site: it is no longer one of the User's Sites. Nothing changed. */
    SiteRefused(false),

    /** The change did not reach the Server, or the Server failed: nothing changed; Try again sends it again. */
    NotSaved(true),

    /** The settings could not be read: no answer. */
    Unreachable(true),
    Certificate(false),

    /** The settings could not be read: another answer. */
    Unexpected(true),
}

/** My notifications (UX-DR72). */
public sealed interface NotificationSettingsState {
    /** Signed out. */
    public data object Idle : NotificationSettingsState

    public data object Loading : NotificationSettingsState

    public data class Failed(
        val notice: NotificationSettingsNotice,
    ) : NotificationSettingsState

    /**
     * [window] is what the Server holds, [draft] what the control shows; Save sends the draft. [windowSaved] after
     * a save, until the next edit. [site] is `null` without a current Site: then only the window and the time zone
     * show. [notice] says why the last change was not saved, with [noticeControl] the control it belongs to, which
     * is back at the Server's value.
     */
    public data class Ready(
        val window: NotificationWindow,
        val draft: NotificationWindow,
        val windowWorking: Boolean,
        val windowSaved: Boolean,
        val timeZone: NotificationTimeZone,
        val site: SiteNotificationSettings?,
        val notice: NotificationSettingsNotice?,
        val noticeControl: NotificationControl?,
    ) : NotificationSettingsState {
        val windowDirty: Boolean get() = draft != window

        /** The draft does not start before it ends: said inline, and Save sends nothing. */
        val windowOutOfOrder: Boolean get() = !draft.inOrder

        val canSaveWindow: Boolean get() = windowDirty && draft.inOrder && !windowWorking
    }
}
