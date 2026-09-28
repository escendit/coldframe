package com.escendit.coldframe.core.appearance

import com.russhwolf.settings.MapSettings
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class AppearanceStoreTest {
    @Test
    fun uxDr15SystemIsTheDefault() {
        val store = AppearanceStore(MapSettings())

        assertEquals(ThemePreference.System, store.theme.value)
    }

    @Test
    fun uxDr15AChoiceAppliesAtOnceAndSurvivesANewStoreOverTheSameSettings() {
        val settings = MapSettings()
        val store = AppearanceStore(settings)

        store.select(ThemePreference.Dark)
        assertEquals(ThemePreference.Dark, store.theme.value)
        assertEquals(ThemePreference.Dark, AppearanceStore(settings).theme.value)

        store.select(ThemePreference.Light)
        assertEquals(ThemePreference.Light, AppearanceStore(settings).theme.value)
    }

    @Test
    fun uxDr15ChoosingSystemRemovesTheStoredValue() {
        val settings = MapSettings()
        val store = AppearanceStore(settings)
        store.select(ThemePreference.Dark)

        store.select(ThemePreference.System)

        assertNull(settings.getStringOrNull(AppearanceStore.KEY))
        assertEquals(ThemePreference.System, AppearanceStore(settings).theme.value)
    }

    @Test
    fun uxDr15AnUnknownStoredValueIsSystem() {
        val settings = MapSettings(AppearanceStore.KEY to "sepia")

        assertEquals(ThemePreference.System, AppearanceStore(settings).theme.value)
        assertEquals(ThemePreference.System, ThemePreference.fromStored(null))
    }

    @Test
    fun uxDr15SystemFollowsTheOsAppearanceAndTheOthersOverrideIt() {
        assertTrue(ThemePreference.System.isDark(systemIsDark = true))
        assertFalse(ThemePreference.System.isDark(systemIsDark = false))
        assertTrue(ThemePreference.Dark.isDark(systemIsDark = false))
        assertFalse(ThemePreference.Light.isDark(systemIsDark = true))
    }
}
