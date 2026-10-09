package com.escendit.coldframe.android.ui.alerts

import android.content.res.Resources
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalResources
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.core.alerts.AlertCondition
import com.escendit.coldframe.core.alerts.AlertEyebrow
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.lots.SensorQuantity
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * The words of an Alert row (UX-DR25, UX-DR98): the core's structured [AlertSummary] in, sentences
 * from `strings.xml` out. It decides nothing: the condition, the eyebrow and whether the Alert is
 * closed come from the core. An Alert carries no value and no Threshold, so the row names the Lot
 * and the side only. [now] is the clock "today" is told against.
 */
class AlertsCopy(
    private val resources: Resources,
    private val now: Instant,
    private val zone: ZoneId,
    private val locale: Locale,
) {
    /** Clock time today, a weekday within a week, else a date. */
    private fun time(epochMs: Long): String = Formats.whenText(Instant.ofEpochMilli(epochMs), now, zone, locale)

    @StringRes
    private fun quantity(quantity: SensorQuantity?): Int =
        when (quantity) {
            SensorQuantity.AirTemperature -> R.string.alert_quantity_air_temperature
            SensorQuantity.RelativeHumidity -> R.string.alert_quantity_relative_humidity
            SensorQuantity.GasResistance -> R.string.alert_quantity_gas_resistance
            SensorQuantity.SoilMoisture, null -> R.string.alert_quantity_soil_moisture
        }

    /** What is wrong, naming the Lot: "Tomatoes needs water". */
    fun title(alert: AlertSummary): String {
        val lot = alert.lotName
        return when (alert.condition) {
            AlertCondition.NeedsWater -> {
                resources.getString(R.string.alert_title_needs_water, lot)
            }

            AlertCondition.TooWet -> {
                resources.getString(R.string.alert_title_too_wet, lot)
            }

            AlertCondition.TooLow -> {
                resources.getString(R.string.alert_title_too_low, lot, resources.getString(quantity(alert.quantity)))
            }

            AlertCondition.TooHigh -> {
                resources.getString(R.string.alert_title_too_high, lot, resources.getString(quantity(alert.quantity)))
            }

            AlertCondition.Silent -> {
                resources.getString(R.string.alert_title_silent, lot)
            }

            AlertCondition.Battery -> {
                resources.getString(R.string.alert_title_battery, lot)
            }

            AlertCondition.Uncalibrated -> {
                resources.getString(R.string.alert_title_uncalibrated, lot)
            }

            AlertCondition.Unknown -> {
                resources.getString(R.string.alert_title_unknown, lot)
            }
        }
    }

    /**
     * The line over the title, in sentence case (uppercase comes from style): the kind of Alert with
     * when it opened, or, once closed, when it closed. An open Health Alert only names itself.
     */
    fun eyebrow(alert: AlertSummary): String {
        val kind =
            resources.getString(
                when (alert.eyebrow) {
                    AlertEyebrow.NeedsWater -> R.string.alert_eyebrow_needs_water
                    AlertEyebrow.BelowLow -> R.string.alert_eyebrow_below_low
                    AlertEyebrow.AboveHigh -> R.string.alert_eyebrow_above_high
                    AlertEyebrow.Health -> R.string.alert_eyebrow_health
                },
            )
        val closedAt = alert.closedAtEpochMs
        return when {
            closedAt != null -> resources.getString(R.string.alert_eyebrow_closed, kind, time(closedAt))
            alert.eyebrow == AlertEyebrow.Health -> kind
            else -> resources.getString(R.string.alert_eyebrow_open, kind, time(alert.openedAtEpochMs))
        }
    }

    /** The row's one screen-reader label: the condition and when it started, and when it closed. */
    fun spoken(alert: AlertSummary): String {
        val closedAt = alert.closedAtEpochMs
        val since = time(alert.openedAtEpochMs)
        return if (closedAt == null) {
            resources.getString(R.string.alert_spoken_open, title(alert), since)
        } else {
            resources.getString(R.string.alert_spoken_closed, title(alert), since, time(closedAt))
        }
    }
}

/** The Alerts copy for the current resources and locale, told against [now] in [zone]. */
@Composable
fun rememberAlertsCopy(
    now: Instant,
    zone: ZoneId,
): AlertsCopy {
    val resources = LocalResources.current
    val locale = ComposeLocale.current.platformLocale
    return remember(resources, now, zone, locale) { AlertsCopy(resources, now, zone, locale) }
}
