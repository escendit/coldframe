package com.escendit.coldframe.android.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import com.escendit.coldframe.R
import com.escendit.coldframe.designtokens.TypeRole
import com.escendit.coldframe.designtokens.Typography as Roles

private val ubuntu =
    FontFamily(
        Font(R.font.ubuntu_light, FontWeight.Light),
        Font(R.font.ubuntu_regular, FontWeight.Normal),
    )

// DW-9: Ubuntu Condensed ships in weight 400 only; 300 falls back to it.
private val ubuntuCondensed = FontFamily(Font(R.font.ubuntu_condensed_regular, FontWeight.Normal))

private val ubuntuMono = FontFamily(Font(R.font.ubuntu_mono_regular, FontWeight.Normal))

private fun familyOf(name: String): FontFamily =
    when (name) {
        "Ubuntu Condensed" -> ubuntuCondensed
        "Ubuntu Mono" -> ubuntuMono
        else -> ubuntu
    }

/**
 * The text style of a generated role at the current font scale. Sizes are `sp`, so they scale;
 * roles with a `maxFontScale` stop growing at that scale (DESIGN.md: values ≥ 36 cap at 2×).
 */
@Composable
fun TypeRole.textStyle(): TextStyle {
    val fontScale = LocalDensity.current.fontScale
    val size = (scaledSizeSp(fontScale) / fontScale).sp
    return TextStyle(
        fontFamily = familyOf(fontFamily),
        fontWeight = FontWeight(fontWeight),
        fontSize = size,
        lineHeight = lineHeight.em,
        letterSpacing = letterSpacingEm.em,
    )
}

/** Material's slots, mapped to the nearest generated roles for components that read them. */
@Composable
internal fun coldframeMaterialTypography(): Typography {
    val body = Roles.body.textStyle()
    val bodyLg = Roles.bodyLg.textStyle()
    val section = Roles.section.textStyle()
    val title = Roles.title.textStyle()
    val headline = Roles.headline.textStyle()
    val helper = Roles.helper.textStyle()
    val button = Roles.button.textStyle()
    return Typography(
        displayLarge = headline,
        displayMedium = headline,
        displaySmall = headline,
        headlineLarge = headline,
        headlineMedium = title,
        headlineSmall = section,
        titleLarge = section,
        titleMedium = bodyLg,
        titleSmall = body,
        bodyLarge = bodyLg,
        bodyMedium = body,
        bodySmall = helper,
        labelLarge = button,
        labelMedium = helper,
        labelSmall = helper,
    )
}
