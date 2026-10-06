package com.escendit.coldframe.core.sites

import com.russhwolf.settings.Settings

/**
 * Choices kept on this device only: the current Site and the time zone the user confirmed or
 * picked. The time zone waits here until the Notification Window story sends it to the User.
 */
public class DeviceChoices(
    internal val settings: Settings,
) {
    public var currentSiteId: String?
        get() = settings.getStringOrNull(CURRENT_SITE)
        set(value) {
            if (value == null) settings.remove(CURRENT_SITE) else settings.putString(CURRENT_SITE, value)
        }

    public var timeZone: String?
        get() = settings.getStringOrNull(TIME_ZONE)
        set(value) {
            if (value == null) settings.remove(TIME_ZONE) else settings.putString(TIME_ZONE, value)
        }

    public companion object {
        public const val CURRENT_SITE: String = "sites.current"
        public const val TIME_ZONE: String = "timeZone.chosen"
    }
}
