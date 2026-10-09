package com.escendit.coldframe.core.sites

import com.russhwolf.settings.Settings

/**
 * Choices kept on this device: the current Site, and the time zone the User confirmed or picked
 * on Create Site until the Server has it. The notification settings engine sends [timeZone] once
 * as the User's choice and removes it when the Server answered; it is also removed at sign-out
 * (Story 6.3, DW-23). Afterwards the zone is read from the Server.
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
