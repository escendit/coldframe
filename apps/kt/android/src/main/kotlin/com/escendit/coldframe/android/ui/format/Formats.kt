package com.escendit.coldframe.android.ui.format

import android.content.res.Resources
import android.text.format.DateFormat
import com.escendit.coldframe.R
import com.escendit.coldframe.core.devices.SilenceWindowLength
import com.escendit.coldframe.core.devices.SilenceWindowUnit
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.LotDurationUnit
import java.text.NumberFormat
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.time.temporal.ChronoUnit
import java.util.Locale
import kotlin.time.Duration
import kotlin.time.Duration.Companion.days
import kotlin.time.Duration.Companion.hours

/**
 * Locale-aware formats by the Voice rules (UX-DR127). Every word comes from `strings.xml`; the
 * numbers, clock and calendar come from the locale.
 */
object Formats {
    /** "min" under 1 h, "h" under 24 h ("h min" with [withMinutes], for the stale header), "d" from 24 h. */
    fun duration(
        resources: Resources,
        duration: Duration,
        withMinutes: Boolean = false,
    ): String {
        val value = if (duration.isNegative()) Duration.ZERO else duration
        return when {
            value < 1.hours -> {
                resources.getString(R.string.duration_minutes, value.inWholeMinutes.toInt())
            }

            value < 1.days -> {
                val hours = value.inWholeHours.toInt()
                val minutes = (value.inWholeMinutes % 60).toInt()
                if (withMinutes && minutes > 0) {
                    resources.getString(R.string.duration_hours_minutes, hours, minutes)
                } else {
                    resources.getString(R.string.duration_hours, hours)
                }
            }

            else -> {
                resources.getString(R.string.duration_days, value.inWholeDays.toInt())
            }
        }
    }

    /** A silence as the core measured it, rounded down: "12 min", "6 h", "2 d". The unit is the core's choice. */
    fun duration(
        resources: Resources,
        duration: LotDuration,
    ): String =
        resources.getString(
            when (duration.unit) {
                LotDurationUnit.Minutes -> R.string.duration_minutes
                LotDurationUnit.Hours -> R.string.duration_hours
                LotDurationUnit.Days -> R.string.duration_days
            },
            duration.value,
        )

    /** A Silence Window as a number with its unit: "5 min", "8 h", "30 d". */
    fun silenceWindow(
        resources: Resources,
        window: SilenceWindowLength,
    ): String =
        resources.getString(
            when (window.unit) {
                SilenceWindowUnit.Minutes -> R.string.duration_minutes
                SilenceWindowUnit.Hours -> R.string.duration_hours
                SilenceWindowUnit.Days -> R.string.duration_days
            },
            window.value,
        )

    /** Clock time in the locale's 12 or 24 hour format. */
    fun time(
        instant: Instant,
        zone: ZoneId,
        locale: Locale,
    ): String =
        DateTimeFormatter
            .ofLocalizedTime(FormatStyle.SHORT)
            .withLocale(locale)
            .withZone(zone)
            .format(instant)

    /** Today → clock time; 1 to 7 calendar days ago → weekday; otherwise (or later than today) → date. */
    fun whenText(
        instant: Instant,
        now: Instant,
        zone: ZoneId,
        locale: Locale,
    ): String {
        val day = instant.atZone(zone).toLocalDate()
        val today = now.atZone(zone).toLocalDate()
        val daysAgo = ChronoUnit.DAYS.between(day, today)
        return when {
            daysAgo == 0L -> {
                time(instant, zone, locale)
            }

            daysAgo in 1..7 -> {
                DateTimeFormatter.ofPattern("EEE", locale).withZone(zone).format(instant)
            }

            else -> {
                DateTimeFormatter
                    .ofPattern(
                        DateFormat.getBestDateTimePattern(locale, "dMMM"),
                        locale,
                    ).withZone(zone)
                    .format(instant)
            }
        }
    }

    /** A calendar day without its year: "1 Nov", or "1 November" when [long] (spoken labels). */
    fun day(
        instant: Instant,
        zone: ZoneId,
        locale: Locale,
        long: Boolean = false,
    ): String =
        DateTimeFormatter
            .ofPattern(DateFormat.getBestDateTimePattern(locale, if (long) "dMMMM" else "dMMM"), locale)
            .withZone(zone)
            .format(instant)

    /** A calendar date with its year, as the User picked it: "1 Mar 2027". No time zone is involved. */
    fun date(
        date: LocalDate,
        locale: Locale,
    ): String = DateTimeFormatter.ofPattern(DateFormat.getBestDateTimePattern(locale, "dMMMy"), locale).format(date)

    /** A whole number in the locale's format. */
    fun number(
        value: Number,
        locale: Locale,
    ): String = NumberFormat.getIntegerInstance(locale).format(value)

    /** A percentage (0–100) with the locale's spacing, "20%" in en, "20 %" in de. */
    fun percent(
        value: Number,
        locale: Locale,
    ): String = NumberFormat.getPercentInstance(locale).format(value.toDouble() / 100)
}
