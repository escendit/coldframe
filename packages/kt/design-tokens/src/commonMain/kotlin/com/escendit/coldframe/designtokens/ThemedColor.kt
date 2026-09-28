package com.escendit.coldframe.designtokens

/**
 * A colour token with one value per theme, each an Android colour int `0xAARRGGBB`.
 *
 * Tokens without a dark twin in DESIGN.md hold the same value twice.
 */
public data class ThemedColor(
    val light: Long,
    val dark: Long,
) {
    /** The value for the current theme. */
    public fun resolve(isDark: Boolean): Long = if (isDark) dark else light
}
