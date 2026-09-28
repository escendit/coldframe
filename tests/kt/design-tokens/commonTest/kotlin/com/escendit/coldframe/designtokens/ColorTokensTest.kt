package com.escendit.coldframe.designtokens

import kotlin.test.Test
import kotlin.test.assertEquals

class ColorTokensTest {
    @Test
    fun holdsEveryDesignColour() {
        assertEquals(53, ColorTokens.all.size)
    }

    @Test
    fun holdsLightAndDarkValuesAsAarrggbb() {
        assertEquals(ThemedColor(light = 0xFFFEFEFE, dark = 0xFF262626), ColorTokens.background)
        assertEquals(ThemedColor(light = 0xFFB84A00, dark = 0xFFFF8A2E), ColorTokens.primaryText)
        assertEquals(0xFF161616, ColorTokens.focus.resolve(isDark = false))
        assertEquals(0xFFF4F4F4, ColorTokens.focus.resolve(isDark = true))
    }

    @Test
    fun keepsAlpha() {
        assertEquals(0x80161616, ColorTokens.overlay.light)
        assertEquals(0xB3161616, ColorTokens.overlay.dark)
    }

    @Test
    fun usesTheLightValueInBothThemesWithoutADarkTwin() {
        assertEquals(ColorTokens.primary.light, ColorTokens.primary.dark)
        assertEquals(0xFF0A0A0A, ColorTokens.inkOnBright.dark)
    }

    @Test
    fun keysEveryTokenByItsDesignName() {
        assertEquals(ColorTokens.statusWaterFill, ColorTokens.all["status-water-fill"])
        assertEquals(ColorTokens.layer01, ColorTokens.all["layer-01"])
    }

    @Test
    fun holdsSpacingAndSquareRadii() {
        assertEquals(2f, Spacing.STEP_1)
        assertEquals(160f, Spacing.STEP_13)
        assertEquals(48f, Spacing.BUTTON_HEIGHT)
        assertEquals(0f, Radius.NONE)
        assertEquals(0f, Radius.DEFAULT)
    }
}
