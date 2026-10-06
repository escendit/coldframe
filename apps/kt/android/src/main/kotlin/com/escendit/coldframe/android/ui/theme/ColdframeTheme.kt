package com.escendit.coldframe.android.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.escendit.coldframe.designtokens.ColorTokens
import com.escendit.coldframe.designtokens.Radius
import com.escendit.coldframe.designtokens.ThemedColor

/** The colour tokens this story draws with, resolved for one theme. */
@Immutable
data class ColdframeColors(
    val isDark: Boolean,
    val background: Color,
    val layer01: Color,
    val field01: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val textHelper: Color,
    val textOnColor: Color,
    val borderSubtle: Color,
    val borderStrong: Color,
    val focus: Color,
    val focusGap: Color,
    val primary: Color,
    val primaryActive: Color,
    val primaryText: Color,
    val secondary: Color,
    val buttonSecondary: Color,
    val inkOnBright: Color,
    val supportError: Color,
    val supportErrorText: Color,
    val supportWarning: Color,
    val overlay: Color,
    val statusNoNodeBorder: Color,
    val statusNoNodeInk: Color,
    val supportSuccess: Color,
    val setupDone: Color,
    val statusHatchGround: Color,
    val statusHatchLine: Color,
    val statusUnknownBorder: Color,
    val statusWaterFill: Color,
    val statusWaterInk: Color,
    val statusWaterLevel: Color,
    val statusOkFill: Color,
    val statusOkLevel: Color,
    val statusOkBorder: Color,
    val statusLevelEdge: Color,
    val statusLowMarker: Color,
    val statusCalibrationBorder: Color,
    val statusCalibrationInk: Color,
    val statusPausedFill: Color,
    val statusPausedBorder: Color,
    val statusPausedInk: Color,
    val staleBorder: Color,
    val staleInk: Color,
    val chartBar: Color,
) {
    companion object {
        /** Resolves every token for [isDark]; nothing else in the app names a colour. */
        fun of(isDark: Boolean): ColdframeColors {
            fun ThemedColor.color(): Color = Color(resolve(isDark).toInt())
            return ColdframeColors(
                isDark = isDark,
                background = ColorTokens.background.color(),
                layer01 = ColorTokens.layer01.color(),
                field01 = ColorTokens.field01.color(),
                textPrimary = ColorTokens.textPrimary.color(),
                textSecondary = ColorTokens.textSecondary.color(),
                textHelper = ColorTokens.textHelper.color(),
                textOnColor = ColorTokens.textOnColor.color(),
                borderSubtle = ColorTokens.borderSubtle.color(),
                borderStrong = ColorTokens.borderStrong.color(),
                focus = ColorTokens.focus.color(),
                focusGap = ColorTokens.focusGap.color(),
                primary = ColorTokens.primary.color(),
                primaryActive = ColorTokens.primaryActive.color(),
                primaryText = ColorTokens.primaryText.color(),
                secondary = ColorTokens.secondary.color(),
                buttonSecondary = ColorTokens.buttonSecondary.color(),
                inkOnBright = ColorTokens.inkOnBright.color(),
                supportError = ColorTokens.supportError.color(),
                supportErrorText = ColorTokens.supportErrorText.color(),
                supportWarning = ColorTokens.supportWarning.color(),
                overlay = ColorTokens.overlay.color(),
                statusNoNodeBorder = ColorTokens.statusNoNodeBorder.color(),
                statusNoNodeInk = ColorTokens.statusNoNodeInk.color(),
                supportSuccess = ColorTokens.supportSuccess.color(),
                setupDone = ColorTokens.setupDone.color(),
                statusHatchGround = ColorTokens.statusHatchGround.color(),
                statusHatchLine = ColorTokens.statusHatchLine.color(),
                statusUnknownBorder = ColorTokens.statusUnknownBorder.color(),
                statusWaterFill = ColorTokens.statusWaterFill.color(),
                statusWaterInk = ColorTokens.statusWaterInk.color(),
                statusWaterLevel = ColorTokens.statusWaterLevel.color(),
                statusOkFill = ColorTokens.statusOkFill.color(),
                statusOkLevel = ColorTokens.statusOkLevel.color(),
                statusOkBorder = ColorTokens.statusOkBorder.color(),
                statusLevelEdge = ColorTokens.statusLevelEdge.color(),
                statusLowMarker = ColorTokens.statusLowMarker.color(),
                statusCalibrationBorder = ColorTokens.statusCalibrationBorder.color(),
                statusCalibrationInk = ColorTokens.statusCalibrationInk.color(),
                statusPausedFill = ColorTokens.statusPausedFill.color(),
                statusPausedBorder = ColorTokens.statusPausedBorder.color(),
                statusPausedInk = ColorTokens.statusPausedInk.color(),
                staleBorder = ColorTokens.staleBorder.color(),
                staleInk = ColorTokens.staleInk.color(),
                chartBar = ColorTokens.chartBar.color(),
            )
        }
    }
}

val LocalColdframeColors = staticCompositionLocalOf { ColdframeColors.of(isDark = false) }

/** Square everywhere (DESIGN.md Shapes): the token radius is 0. */
private val squareShapes =
    RoundedCornerShape(Radius.NONE.dp).let { square ->
        Shapes(
            extraSmall = square,
            small = square,
            medium = square,
            large = square,
            extraLarge = square,
        )
    }

/**
 * Material 3 structure with the Escendit brand layer: colours from [ColorTokens], type from the
 * generated roles, 0 radius, no shadows (tonal elevation stays 0 through surface = background).
 */
@Composable
fun ColdframeTheme(
    isDark: Boolean,
    content: @Composable () -> Unit,
) {
    val colors = ColdframeColors.of(isDark)
    val base = if (isDark) darkColorScheme() else lightColorScheme()
    val scheme =
        base.copy(
            primary = colors.primary,
            onPrimary = colors.inkOnBright,
            primaryContainer = colors.primary,
            onPrimaryContainer = colors.inkOnBright,
            secondary = colors.buttonSecondary,
            onSecondary = colors.textOnColor,
            secondaryContainer = colors.layer01,
            onSecondaryContainer = colors.primaryText,
            background = colors.background,
            onBackground = colors.textPrimary,
            surface = colors.background,
            onSurface = colors.textPrimary,
            surfaceVariant = colors.layer01,
            onSurfaceVariant = colors.textSecondary,
            surfaceContainer = colors.background,
            surfaceContainerHigh = colors.background,
            surfaceContainerHighest = colors.layer01,
            surfaceContainerLow = colors.background,
            surfaceContainerLowest = colors.background,
            surfaceTint = Color.Transparent,
            outline = colors.borderStrong,
            outlineVariant = colors.borderSubtle,
            error = colors.supportErrorText,
            scrim = colors.overlay,
        )
    CompositionLocalProvider(LocalColdframeColors provides colors) {
        MaterialTheme(
            colorScheme = scheme,
            shapes = squareShapes,
            typography = coldframeMaterialTypography(),
            content = content,
        )
    }
}

/** Shorthand for the resolved tokens. */
object Coldframe {
    val colors: ColdframeColors
        @Composable get() = LocalColdframeColors.current
}
