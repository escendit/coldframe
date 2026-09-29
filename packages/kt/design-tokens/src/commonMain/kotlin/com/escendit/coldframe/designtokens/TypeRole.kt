package com.escendit.coldframe.designtokens

/**
 * A typography role of DESIGN.md.
 *
 * [sizeSp] scales with the Android font scale. Roles of 36 sp and more carry [maxFontScale] = 2:
 * they stop growing at twice their base size. [uppercase] is applied by style; strings are never
 * upper-cased.
 */
public data class TypeRole(
    val fontFamily: String,
    val sizeSp: Float,
    val fontWeight: Int,
    /** Line height as a multiple of the font size. */
    val lineHeight: Float,
    val letterSpacingEm: Float,
    val uppercase: Boolean,
    val maxFontScale: Float?,
) {
    /** The size in sp at [fontScale], after the cap. */
    public fun scaledSizeSp(fontScale: Float): Float =
        sizeSp * (maxFontScale?.let { minOf(fontScale, it) } ?: fontScale)
}
