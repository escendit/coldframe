package com.escendit.coldframe.android.ui.sites

import android.content.res.Resources
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalResources
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.ChartBar
import com.escendit.coldframe.core.lots.HeroNote
import com.escendit.coldframe.core.lots.HeroValueKind
import com.escendit.coldframe.core.lots.HistoryChart
import com.escendit.coldframe.core.lots.LotDetailHero
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorUnit
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.Locale
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * The words of Lot detail (UX-DR27 to UX-DR33, UX-DR78, UX-DR98): the core's structured
 * [com.escendit.coldframe.core.lots.LotDetail] in, sentences from `strings.xml` out. It decides
 * nothing: which note, value, cells and bars apply, and every number, come from the core. Only the
 * locale's clock and calendar are added here. History days are UTC dates and are written as such.
 */
class LotDetailCopy(
    private val resources: Resources,
    private val now: Instant,
    private val zone: ZoneId,
    private val locale: Locale,
) {
    private fun text(
        @StringRes id: Int,
        vararg arguments: Any,
    ): String = if (arguments.isEmpty()) resources.getString(id) else resources.getString(id, *arguments)

    private fun time(epochMs: Long): String = Formats.whenText(Instant.ofEpochMilli(epochMs), now, zone, locale)

    /** "since 05:45". */
    fun since(epochMs: Long): String = text(R.string.lot_detail_since, time(epochMs))

    /** The hero's big value: `raw 1840`, `~35`, or `—`. */
    fun heroValue(hero: LotDetailHero): String =
        when (hero.valueKind) {
            HeroValueKind.Raw -> text(R.string.lot_detail_value_raw, hero.rawNumber.orEmpty())
            HeroValueKind.Percent -> text(R.string.soil_approx, Formats.number(hero.soilPercent ?: 0, locale))
            HeroValueKind.None -> text(R.string.lot_tile_value_paused)
        }

    /** "±5 % · 07:02" with a percentage, else "last Reading 07:02"; `null` without a Reading time. */
    fun heroReading(hero: LotDetailHero): String? {
        val at = hero.readingAtEpochMs ?: return null
        return if (hero.valueKind == HeroValueKind.Percent) {
            text(R.string.lot_detail_reading, time(at))
        } else {
            text(R.string.lot_tile_foot_last_reading, time(at))
        }
    }

    /** "low 30 %" when the Server sent a low Threshold. */
    fun heroLow(hero: LotDetailHero): String? =
        hero.lowPercent?.let { text(R.string.lot_tile_foot_low, Formats.percent(it, locale)) }

    /** The status-specific line under the hero (UX-DR78); `null` for none. */
    fun note(hero: LotDetailHero): String? =
        when (hero.note) {
            HeroNote.None -> null
            HeroNote.NoPercentUntilCalibrated -> text(R.string.lot_tile_foot_uncalibrated)
            HeroNote.CheckPowerOrRange -> text(R.string.lot_detail_node_silent)
            HeroNote.HubSilent -> text(R.string.lot_detail_hub_silent)
            HeroNote.PausedUntil -> hero.pausedUntilEpochMs?.let { text(R.string.lot_detail_paused_until, day(it)) }
            HeroNote.Paused -> text(R.string.lot_detail_paused)
            HeroNote.PausedWithSite -> text(R.string.lot_detail_paused_with_site)
        }

    /** "Resume the Site to resume this Node". */
    fun resumeSite(): String = text(R.string.lot_detail_resume_site)

    /** "6 h" and the like for the silence of an unknown Lot. */
    fun silence(hero: LotDetailHero): String? =
        hero.duration?.let {
            text(
                when (it.unit) {
                    com.escendit.coldframe.core.lots.LotDurationUnit.Minutes -> R.string.duration_minutes
                    com.escendit.coldframe.core.lots.LotDurationUnit.Hours -> R.string.duration_hours
                    com.escendit.coldframe.core.lots.LotDurationUnit.Days -> R.string.duration_days
                },
                it.value,
            )
        }

    private fun day(epochMs: Long): String = Formats.day(Instant.ofEpochMilli(epochMs), zone, locale)

    // Sensor and Device cells

    fun quantity(quantity: SensorQuantity): String =
        text(
            when (quantity) {
                SensorQuantity.SoilMoisture -> R.string.lot_detail_quantity_soil_moisture
                SensorQuantity.AirTemperature -> R.string.lot_detail_quantity_air_temperature
                SensorQuantity.RelativeHumidity -> R.string.lot_detail_quantity_relative_humidity
                SensorQuantity.GasResistance -> R.string.lot_detail_quantity_gas_resistance
            },
        )

    /** `raw 1840`, `14 °C`, `78 %`, `142 kΩ`: the Server's converted number with its unit. */
    fun value(
        number: String,
        unit: SensorUnit,
    ): String =
        text(
            when (unit) {
                SensorUnit.Raw -> R.string.lot_detail_value_raw
                SensorUnit.Celsius -> R.string.lot_detail_value_celsius
                SensorUnit.Percent -> R.string.lot_detail_value_percent
                SensorUnit.KiloOhm -> R.string.lot_detail_value_kiloohm
            },
            number,
        )

    fun reading(epochMs: Long?): String? = epochMs?.let(::time)

    fun battery(percent: Int?): String =
        percent?.let { text(R.string.lot_detail_value_percent, it.toString()) } ?: text(R.string.lot_tile_value_paused)

    fun charging(state: ChargeState?): String? =
        when (state) {
            ChargeState.Charging -> text(R.string.devices_charging)
            ChargeState.NotCharging -> text(R.string.devices_not_charging)
            null -> null
        }

    fun lastSeen(epochMs: Long?): String = epochMs?.let(::time) ?: text(R.string.lot_tile_value_paused)

    // Chart

    /** A UTC day as "6 Oct". */
    fun chartDay(dayEpochMs: Long): String = Formats.day(Instant.ofEpochMilli(dayEpochMs), ZoneOffset.UTC, locale)

    /** The selected day: "5 Oct: lowest 1790" for soil, "5 Oct: 6 °C to 22 °C" for the others. */
    fun readout(
        chart: HistoryChart,
        bar: ChartBar,
    ): String {
        val day = chartDay(bar.dayEpochMs)
        val low = bar.lowNumber ?: return day
        return if (chart.quantity.showsRange) {
            text(
                R.string.lot_detail_chart_readout_range,
                day,
                value(low, chart.unit),
                value(bar.highNumber.orEmpty(), chart.unit),
            )
        } else {
            text(R.string.lot_detail_chart_readout_low, day, value(low, chart.unit))
        }
    }

    /** The chart's text alternative (UX-DR98): the quantity, the lowest day and how many days have Readings. */
    fun chartSummary(chart: HistoryChart): String {
        val name = quantity(chart.quantity)
        val lowest = chart.lowestNumber
        val lowestDay = chart.bars.firstOrNull { it.day == chart.lowestDay }
        if (lowest == null || lowestDay == null) return text(R.string.lot_detail_chart_summary_empty, name)
        return resources.getQuantityString(
            R.plurals.lot_detail_chart_summary,
            chart.daysWithReadings,
            name,
            value(lowest, chart.unit),
            chartDay(lowestDay.dayEpochMs),
            chart.daysWithReadings,
        )
    }
}

/** Lot detail's words for the phone's locale, told against [now] in [zone]. */
@Composable
fun rememberLotDetailCopy(
    now: Instant,
    zone: ZoneId,
): LotDetailCopy {
    val resources = LocalResources.current
    val locale = ComposeLocale.current.platformLocale
    return remember(resources, now, zone, locale) { LotDetailCopy(resources, now, zone, locale) }
}
