package com.escendit.coldframe.core.sites

import com.russhwolf.settings.Settings

/**
 * Choices kept on this device: the current Site, and the time zone the User confirmed or picked
 * on Create Site until the Server has it. The notification settings engine sends [timeZone] once
 * as the User's choice and removes it when the Server answered; it is also removed at sign-out
 * (Story 6.3, DW-23). Afterwards the zone is read from the Server.
 *
 * Two values belong to the installation and outlive every session (Story 6.5): [installationId], which names this
 * installation in its push registration, and [notificationPermissionAsked], set once the OS prompt was answered.
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

    /** The ID of this installation in `/me/push-registrations/{installationId}`; `null` until the first registration. */
    public var installationId: String?
        get() = settings.getStringOrNull(INSTALLATION_ID)
        set(value) {
            if (value == null) settings.remove(INSTALLATION_ID) else settings.putString(INSTALLATION_ID, value)
        }

    /** Whether the OS notification prompt was answered on this device; it is shown once (UX-DR122). */
    public var notificationPermissionAsked: Boolean
        get() = settings.getBoolean(PERMISSION_ASKED, false)
        set(value) {
            settings.putBoolean(PERMISSION_ASKED, value)
        }

    public companion object {
        public const val CURRENT_SITE: String = "sites.current"
        public const val TIME_ZONE: String = "timeZone.chosen"
        public const val INSTALLATION_ID: String = "push.installationId"
        public const val PERMISSION_ASKED: String = "push.permissionAsked"
    }
}
