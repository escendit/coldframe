package com.escendit.coldframe.android.ui.sites

import android.content.res.Resources
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalResources
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.format.Formats
import com.escendit.coldframe.core.lots.LotCount
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.LotDurationUnit
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotTile
import com.escendit.coldframe.core.lots.LotTileFoot
import com.escendit.coldframe.core.lots.LotTileLabel
import com.escendit.coldframe.core.lots.LotTileSpoken
import com.escendit.coldframe.core.lots.LotTileValue
import com.escendit.coldframe.core.lots.LotTileVariant
import com.escendit.coldframe.core.lots.LotsHeadline
import com.escendit.coldframe.core.lots.LotsHeadlineKind
import com.escendit.coldframe.core.lots.LotsOverview
import com.escendit.coldframe.core.lots.StaleAge
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import kotlin.time.Duration.Companion.days
import kotlin.time.Duration.Companion.hours
import kotlin.time.Duration.Companion.minutes
import androidx.compose.ui.text.intl.Locale as ComposeLocale

/**
 * The words of the Site overview (UX-DR18, UX-DR19, UX-DR24, UX-DR98, UX-DR129): the core's
 * structured [LotsOverview] in, sentences from `strings.xml` out. It decides nothing: which
 * headline, label, value, foot and spoken label apply, the rounding, the durations and the stale
 * age all come from the core (AD-14). Only the locale's clock, calendar and number formats are
 * added here. [now] is the clock "today" is told against for times of day.
 */
class OverviewCopy(
    private val resources: Resources,
    private val now: Instant,
    private val zone: ZoneId,
    private val locale: Locale,
) {
    /** A sentence without arguments is taken as written: "no % until calibrated" is not a format. */
    private fun text(
        @StringRes id: Int,
        vararg arguments: Any,
    ): String = if (arguments.isEmpty()) resources.getString(id) else resources.getString(id, *arguments)

    /** Clock time today, a weekday within a week, else a date. */
    private fun time(epochMs: Long): String = Formats.whenText(Instant.ofEpochMilli(epochMs), now, zone, locale)

    private fun day(
        epochMs: Long,
        long: Boolean = false,
    ): String = Formats.day(Instant.ofEpochMilli(epochMs), zone, locale, long)

    private fun joined(
        @StringRes pair: Int,
        parts: List<String?>,
    ): String? = parts.filterNotNull().reduceOrNull { all, next -> text(pair, all, next) }

    // The header

    /** The one-sentence Site state (UX-DR129). */
    fun headline(headline: LotsHeadline): String {
        val until = headline.pausedUntilEpochMs
        val name = headline.lotName
        return when (headline.kind) {
            LotsHeadlineKind.NoReadings -> {
                text(R.string.garden_no_readings)
            }

            LotsHeadlineKind.NeedsWater -> {
                if (name != null) {
                    text(R.string.garden_headline_lot_needs_water, name)
                } else {
                    resources.getQuantityString(R.plurals.count_lots_need_water, headline.count, headline.count)
                }
            }

            LotsHeadlineKind.CantBeRead -> {
                resources.getQuantityString(R.plurals.garden_headline_cant_read, headline.count, headline.count)
            }

            LotsHeadlineKind.Paused -> {
                if (until != null) {
                    text(R.string.garden_headline_paused_until, day(until))
                } else {
                    text(R.string.garden_headline_paused)
                }
            }

            LotsHeadlineKind.NothingNeedsWater -> {
                text(R.string.garden_headline_nothing)
            }
        }
    }

    /** The counts ("2 unknown · 2 OK"), or the detail line of "No Readings yet"; `null` with nothing to count. */
    fun subline(overview: LotsOverview): String? =
        if (overview.headline.kind == LotsHeadlineKind.NoReadings) {
            text(R.string.garden_no_readings_detail)
        } else {
            joined(R.string.list_dot, overview.counts.map { count(it) })
        }

    private fun count(count: LotCount): String =
        when (count.status) {
            LotStatus.NeedsWater -> {
                resources.getQuantityString(R.plurals.count_lots_need_water, count.count, count.count)
            }

            LotStatus.NeedsCalibration -> {
                resources.getQuantityString(R.plurals.garden_count_needs_calibration, count.count, count.count)
            }

            LotStatus.Unknown -> {
                text(R.string.garden_count_unknown, count.count)
            }

            LotStatus.Ok -> {
                text(R.string.garden_count_ok, count.count)
            }

            LotStatus.Paused -> {
                text(R.string.garden_count_paused, count.count)
            }

            LotStatus.NoNode -> {
                text(R.string.garden_count_no_node, count.count)
            }
        }

    // The stale header and its announcements

    /** "Home garden · can't reach your Server". */
    fun staleTitle(siteName: String): String = text(R.string.stale_title, siteName)

    /** "2 h 12 min old": "min" under 1 h, "h min" under 24 h, "d" from 24 h. */
    fun staleAge(age: StaleAge): String =
        text(
            R.string.stale_age,
            Formats.duration(resources, age.days.days + age.hours.hours + age.minutes.minutes, withMinutes = true),
        )

    /** "Last data 07:02. You may be away from home, …" */
    fun staleDetail(fetchedAtEpochMs: Long): String = text(R.string.stale_detail, time(fetchedAtEpochMs))

    /** The polite announcement on entering stale mode (UX-DR106). */
    fun enteredStale(fetchedAtEpochMs: Long): String = text(R.string.stale_entered, time(fetchedAtEpochMs))

    /** The polite announcement on leaving it. */
    fun leftStale(): String = text(R.string.stale_left)

    // A tile

    /** The status label in sentence case; a stale tile says "Was …". Uppercase comes from style. */
    fun label(tile: LotTile): String = label(tile.variant, tile.label)

    /** The label of [label] in [variant]: the stale variant says "Was …". */
    fun label(
        variant: LotTileVariant,
        label: LotTileLabel,
    ): String {
        val stale = variant == LotTileVariant.Stale
        return text(
            when (label) {
                LotTileLabel.NeedsWater -> {
                    if (stale) R.string.lot_tile_was_needs_water else R.string.lot_tile_label_needs_water
                }

                LotTileLabel.Ok -> {
                    if (stale) R.string.lot_tile_was_ok else R.string.lot_tile_label_ok
                }

                LotTileLabel.Silent -> {
                    if (stale) R.string.lot_tile_was_unknown_node else R.string.lot_tile_label_unknown_node
                }

                LotTileLabel.HubSilent -> {
                    if (stale) R.string.lot_tile_was_unknown_hub else R.string.lot_tile_label_unknown_hub
                }

                LotTileLabel.NeedsCalibration -> {
                    if (stale) R.string.lot_tile_was_needs_calibration else R.string.lot_tile_label_needs_calibration
                }

                LotTileLabel.Paused -> {
                    if (stale) R.string.lot_tile_was_paused else R.string.lot_tile_label_paused
                }

                LotTileLabel.PausedBySite -> {
                    if (stale) R.string.lot_tile_was_paused_by_site else R.string.lot_tile_label_paused_by_site
                }

                LotTileLabel.NoNode -> {
                    if (stale) R.string.lot_tile_was_no_node else R.string.lot_tile_no_node
                }
            },
        )
    }

    /** The big value, or `null` when the tile has none (stale, or the Server sent no percentage). */
    fun value(tile: LotTile): String? =
        when (tile.value) {
            LotTileValue.Soil -> tile.soilPercent?.let { text(R.string.soil_approx, Formats.number(it, locale)) }
            LotTileValue.Duration -> tile.duration?.let { duration(it) }
            LotTileValue.Raw -> text(R.string.lot_tile_value_raw)
            LotTileValue.Dash -> text(R.string.lot_tile_value_paused)
            LotTileValue.Plus -> text(R.string.lot_tile_no_node_value)
            LotTileValue.None -> null
        }

    /** The foot line, or `null` when the tile has none. */
    fun foot(tile: LotTile): String? {
        val reading = tile.readingAtEpochMs
        val soil = tile.soilPercent
        return when (tile.foot) {
            LotTileFoot.Reading -> {
                joined(
                    R.string.list_dot,
                    listOf(
                        reading?.let { time(it) },
                        tile.lowPercent?.let { text(R.string.lot_tile_foot_low, Formats.percent(it, locale)) },
                    ),
                )
            }

            LotTileFoot.WasPercentAt -> {
                if (soil != null && reading != null) {
                    val percent = text(R.string.soil_approx, Formats.percent(soil, locale))
                    text(R.string.lot_tile_foot_was, percent, time(reading))
                } else {
                    reading?.let { text(R.string.lot_tile_foot_last_reading, time(it)) }
                }
            }

            LotTileFoot.LastReading -> {
                reading?.let { text(R.string.lot_tile_foot_last_reading, time(it)) }
            }

            LotTileFoot.NoReadingsYet -> {
                text(R.string.lot_tile_foot_no_readings)
            }

            LotTileFoot.NoPercentUntilCalibrated -> {
                text(R.string.lot_tile_foot_uncalibrated)
            }

            LotTileFoot.PausedUntil -> {
                tile.pausedUntilEpochMs?.let { text(R.string.lot_tile_foot_until, day(it)) }
            }

            LotTileFoot.Paused -> {
                text(R.string.lot_tile_foot_paused)
            }

            LotTileFoot.AddNode -> {
                text(R.string.lot_tile_add_node)
            }

            LotTileFoot.AsOf -> {
                tile.asOfEpochMs?.let { text(R.string.lot_tile_as_of, time(it)) }
            }

            LotTileFoot.None -> {
                null
            }
        }
    }

    /** The one label TalkBack speaks for the whole tile (UX-DR98); absent parts are left out. */
    fun spoken(tile: LotTile): String {
        val parts: List<String?> =
            when (tile.spoken) {
                LotTileSpoken.NeedsWater -> {
                    listOf(
                        text(R.string.lot_tile_spoken_needs_water),
                        aboutPercent(tile),
                        lowPercent(tile),
                        tile.readingAtEpochMs?.let { text(R.string.lot_tile_spoken_reading, time(it)) },
                    )
                }

                LotTileSpoken.Ok -> {
                    listOf(text(R.string.lot_tile_spoken_ok), aboutPercent(tile), lowPercent(tile))
                }

                LotTileSpoken.NodeSilent -> {
                    silent(tile, R.string.lot_tile_spoken_node_silent)
                }

                LotTileSpoken.HubSilent -> {
                    silent(tile, R.string.lot_tile_spoken_hub_silent)
                }

                LotTileSpoken.NeedsCalibration -> {
                    listOf(
                        text(R.string.lot_tile_spoken_needs_calibration),
                        text(R.string.lot_tile_spoken_uncalibrated),
                    )
                }

                LotTileSpoken.PausedUntil -> {
                    listOf(
                        tile.pausedUntilEpochMs?.let {
                            text(
                                R.string.lot_tile_spoken_paused_until,
                                day(it, long = true),
                            )
                        }
                            ?: text(R.string.lot_tile_spoken_paused),
                    )
                }

                LotTileSpoken.PausedWithSite -> {
                    listOf(text(R.string.lot_tile_spoken_paused_site))
                }

                LotTileSpoken.Paused -> {
                    listOf(text(R.string.lot_tile_spoken_paused))
                }

                LotTileSpoken.NoNode -> {
                    return text(R.string.lot_tile_description_no_node, tile.name)
                }

                LotTileSpoken.Stale -> {
                    listOf(
                        text(spokenWas(tile.status)),
                        text(R.string.lot_tile_spoken_not_live),
                        tile.asOfEpochMs?.let { text(R.string.lot_tile_as_of, time(it)) },
                    )
                }
            }
        return joined(R.string.list_comma, listOf(tile.name) + parts) ?: tile.name
    }

    private fun aboutPercent(tile: LotTile): String? =
        tile.soilPercent?.let { text(R.string.lot_tile_spoken_percent, it) }

    private fun lowPercent(tile: LotTile): String? = tile.lowPercent?.let { text(R.string.lot_tile_spoken_low, it) }

    /** "unknown, Node silent for 6 hours, last about 40 percent at 01:05". */
    private fun silent(
        tile: LotTile,
        @StringRes silentFor: Int,
    ): List<String?> {
        val reading = tile.readingAtEpochMs
        val soil = tile.soilPercent
        val last =
            when {
                reading == null -> text(R.string.lot_tile_spoken_no_readings)
                soil != null -> text(R.string.lot_tile_spoken_last_percent, soil, time(reading))
                else -> text(R.string.lot_tile_spoken_last_reading, time(reading))
            }
        return listOf(
            text(R.string.lot_tile_spoken_unknown),
            tile.duration?.let { text(silentFor, spokenDuration(it)) },
            last,
        )
    }

    @StringRes
    private fun spokenWas(status: LotStatus): Int =
        when (status) {
            LotStatus.NeedsWater -> R.string.lot_tile_spoken_was_needs_water
            LotStatus.NeedsCalibration -> R.string.lot_tile_spoken_was_needs_calibration
            LotStatus.Unknown -> R.string.lot_tile_spoken_was_unknown
            LotStatus.Ok -> R.string.lot_tile_spoken_was_ok
            LotStatus.Paused -> R.string.lot_tile_spoken_was_paused
            LotStatus.NoNode -> R.string.lot_tile_spoken_was_no_node
        }

    /** "6 h": the unit is the core's choice. */
    private fun duration(duration: LotDuration): String =
        text(
            when (duration.unit) {
                LotDurationUnit.Minutes -> R.string.duration_minutes
                LotDurationUnit.Hours -> R.string.duration_hours
                LotDurationUnit.Days -> R.string.duration_days
            },
            duration.value,
        )

    /** "6 hours", by the locale's plural rule. */
    private fun spokenDuration(duration: LotDuration): String =
        resources.getQuantityString(
            when (duration.unit) {
                LotDurationUnit.Minutes -> R.plurals.duration_spoken_minutes
                LotDurationUnit.Hours -> R.plurals.duration_spoken_hours
                LotDurationUnit.Days -> R.plurals.duration_spoken_days
            },
            duration.value,
            duration.value,
        )
}

/** The overview's words for the phone's locale, told against [now] in [zone]. */
@Composable
fun rememberOverviewCopy(
    now: Instant,
    zone: ZoneId,
): OverviewCopy {
    val resources = LocalResources.current
    val locale = ComposeLocale.current.platformLocale
    return remember(resources, now, zone, locale) { OverviewCopy(resources, now, zone, locale) }
}
