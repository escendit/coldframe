package com.escendit.coldframe.core.appearance

import android.content.Context
import com.russhwolf.settings.SharedPreferencesSettings

/** The theme store of this device (not synced, not the token store). */
public object AndroidAppearance {
    private const val PREFERENCES = "com.escendit.coldframe.appearance"

    public fun create(context: Context): AppearanceStore {
        val preferences = context.applicationContext.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        return AppearanceStore(SharedPreferencesSettings(preferences))
    }
}
