package com.escendit.coldframe.core.appearance

import com.russhwolf.settings.Settings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Theme choice of Settings → Appearance (UX-DR15). System follows the OS appearance. */
public enum class ThemePreference(
    /** The value persisted on the device. */
    public val storedValue: String,
) {
    System("system"),
    Light("light"),
    Dark("dark"),
    ;

    /** Whether the dark tokens apply, given the OS appearance. */
    public fun isDark(systemIsDark: Boolean): Boolean =
        when (this) {
            System -> systemIsDark
            Light -> false
            Dark -> true
        }

    public companion object {
        /** Anything unknown, or nothing stored, is System. */
        public fun fromStored(value: String?): ThemePreference =
            entries.firstOrNull { it.storedValue == value } ?: System
    }
}

/**
 * Persists the theme per device, never synced. A choice applies at once: [theme] changes in the
 * same call, with no Save.
 */
public class AppearanceStore(
    private val settings: Settings,
) {
    private val mutableTheme = MutableStateFlow(ThemePreference.fromStored(settings.getStringOrNull(KEY)))

    public val theme: StateFlow<ThemePreference> = mutableTheme.asStateFlow()

    public fun select(preference: ThemePreference) {
        if (preference == ThemePreference.System) {
            settings.remove(KEY)
        } else {
            settings.putString(KEY, preference.storedValue)
        }
        mutableTheme.value = preference
    }

    public companion object {
        public const val KEY: String = "appearance.theme"
    }
}
