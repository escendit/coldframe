package com.escendit.coldframe.core.appearance

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import com.russhwolf.settings.NSUserDefaultsSettings
import kotlinx.coroutines.MainScope
import platform.Foundation.NSUserDefaults

/** The theme store of this device for Swift, which passes and receives the stored values. */
public class IosAppearance {
    private val scope = MainScope()
    private val store = AppearanceStore(NSUserDefaultsSettings(NSUserDefaults.standardUserDefaults))

    /** Calls [onEach] with `system`, `light` or `dark`, now and on every change. */
    public fun watch(onEach: (String) -> Unit): Watch = store.theme.watch(scope) { onEach(it.storedValue) }

    /** Anything unknown selects System. */
    public fun select(storedValue: String) {
        store.select(ThemePreference.fromStored(storedValue))
    }
}
