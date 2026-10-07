package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.sites.SiteMenu
import com.escendit.coldframe.core.sites.SiteMenuItem
import com.escendit.coldframe.core.sites.SiteRole
import kotlin.math.roundToInt

/*
 * The Site overview as both shells draw it: headline, counts, one tile per Lot. Everything here
 * is structured data; the shells own the string catalogues. Nothing computes or re-sorts a
 * status (AD-14): the models count the Server's statuses, pick the fields a variant shows and
 * measure durations from the Server's timestamps.
 */

/** Approximate soil moisture (UX-DR128): `~` and the nearest 5 %, never a decimal. */
public object SoilMoisture {
    private const val STEP = 5

    /** [percent] to the nearest 5, halves up, within 0 to 100. */
    public fun rounded(percent: Double): Int {
        if (percent.isNaN()) return 0
        return ((percent / STEP).roundToInt() * STEP).coerceIn(0, 100)
    }

    /** The tile value: `~20`. The unit and "about" belong to the shells' catalogues. */
    public fun format(percent: Double): String = "~${rounded(percent)}"
}

public enum class LotDurationUnit {
    Minutes,
    Hours,
    Days,
}

/** A duration as the tiles show it: "min" under 1 h, "h" under 24 h, "d" from 24 h, rounded down. */
public data class LotDuration(
    val value: Int,
    val unit: LotDurationUnit,
) {
    public companion object {
        /** From [fromEpochMs] to [nowEpochMs]; a time in the future reads as 0 min. */
        public fun between(
            fromEpochMs: Long,
            nowEpochMs: Long,
        ): LotDuration {
            val minutes = ((nowEpochMs - fromEpochMs).coerceAtLeast(0) / MS_PER_MINUTE)
            return when {
                minutes < MINUTES_PER_HOUR -> LotDuration(minutes.toInt(), LotDurationUnit.Minutes)
                minutes < MINUTES_PER_DAY -> LotDuration((minutes / MINUTES_PER_HOUR).toInt(), LotDurationUnit.Hours)
                else -> LotDuration((minutes / MINUTES_PER_DAY).toInt(), LotDurationUnit.Days)
            }
        }
    }
}

/**
 * The age of the shown data for the stale header ("2 h 12 min old"), rounded down to the minute.
 * The shells show "min" alone under 1 h, "h min" under 24 h and "d" from 24 h. The shells tick
 * it every minute and never announce a tick.
 */
public data class StaleAge(
    val days: Int,
    val hours: Int,
    val minutes: Int,
) {
    public companion object {
        public fun between(
            fetchedAtEpochMs: Long,
            nowEpochMs: Long,
        ): StaleAge {
            val minutes = (nowEpochMs - fetchedAtEpochMs).coerceAtLeast(0) / MS_PER_MINUTE
            return StaleAge(
                days = (minutes / MINUTES_PER_DAY).toInt(),
                hours = (minutes % MINUTES_PER_DAY / MINUTES_PER_HOUR).toInt(),
                minutes = (minutes % MINUTES_PER_HOUR).toInt(),
            )
        }
    }
}

private const val MS_PER_MINUTE = 60_000L
private const val MINUTES_PER_HOUR = 60L
private const val MINUTES_PER_DAY = 24L * MINUTES_PER_HOUR

/** Which headline sentence the Site gets (UX-DR129); the first that applies, in this order. */
public enum class LotsHeadlineKind {
    /** No Lot has a Node: "No Readings yet", with the first-run detail line. */
    NoReadings,

    /** "‹Lot› needs water" for one Lot, "N Lots need water" for more. */
    NeedsWater,

    /** Unknown or needs calibration: "1 Lot can't be read" / "N Lots can't be read". */
    CantBeRead,

    /** Every Lot with a Node is paused by the Site: "Paused until ‹date›" or "Paused", in paused ink. */
    Paused,

    NothingNeedsWater,
}

/**
 * The headline. [count] is the number of Lots the sentence is about ([LotsHeadlineKind.NeedsWater],
 * [LotsHeadlineKind.CantBeRead]), else 0. [lotName] is set only for exactly one Lot that needs
 * water. [pausedUntilEpochMs] is set only when every Site-paused Lot ends at the same time.
 */
public data class LotsHeadline(
    val kind: LotsHeadlineKind,
    val count: Int = 0,
    val lotName: String? = null,
    val pausedUntilEpochMs: Long? = null,
) {
    /** The headline is drawn in paused ink. */
    val pausedInk: Boolean get() = kind == LotsHeadlineKind.Paused

    public companion object {
        public fun of(lots: List<LotSummary>): LotsHeadline {
            val withNode = lots.filter { it.status != LotStatus.NoNode }
            val needWater = withNode.filter { it.status == LotStatus.NeedsWater }
            val unread =
                withNode.count { it.status == LotStatus.Unknown || it.status == LotStatus.NeedsCalibration }
            return when {
                withNode.isEmpty() -> {
                    LotsHeadline(LotsHeadlineKind.NoReadings)
                }

                needWater.isNotEmpty() -> {
                    LotsHeadline(
                        LotsHeadlineKind.NeedsWater,
                        count = needWater.size,
                        lotName = needWater.singleOrNull()?.name,
                    )
                }

                unread > 0 -> {
                    LotsHeadline(LotsHeadlineKind.CantBeRead, count = unread)
                }

                withNode.all { it.status == LotStatus.Paused && LotPauseSource.Site in it.pausedBy } -> {
                    val ends = withNode.map { it.pausedUntilEpochMs }.distinct()
                    LotsHeadline(LotsHeadlineKind.Paused, pausedUntilEpochMs = ends.singleOrNull())
                }

                else -> {
                    LotsHeadline(LotsHeadlineKind.NothingNeedsWater)
                }
            }
        }
    }
}

/** One part of the counts subline: "2 unknown", "1 without Node". */
public data class LotCount(
    val status: LotStatus,
    val count: Int,
)

/** The counts subline (UX-DR21): the non-zero counts in the Server's order, without needs water. */
public object LotCounts {
    private val order =
        listOf(LotStatus.NeedsCalibration, LotStatus.Unknown, LotStatus.Ok, LotStatus.Paused, LotStatus.NoNode)

    /** Empty when no Lot has a Node: the first-run view has no subline. */
    public fun of(lots: List<LotSummary>): List<LotCount> {
        if (lots.none { it.status != LotStatus.NoNode }) return emptyList()
        return order.mapNotNull { status ->
            val count = lots.count { it.status == status }
            if (count > 0) LotCount(status, count) else null
        }
    }
}

/** How a tile is drawn: shape, border and icon (UX-DR18, UX-DR19). */
public enum class LotTileVariant {
    NeedsWater,
    Ok,
    Unknown,
    NeedsCalibration,
    Paused,
    NoNode,

    /** Any status in stale mode: no fill, `cloud--offline`, "Was ‹status label›". */
    Stale,
}

/** The status label under the Lot name. A stale tile says "Was ‹label›". */
public enum class LotTileLabel {
    NeedsWater,
    Ok,

    /** "Silent · unknown": the Node is silent, or the Server gave no cause. */
    Silent,

    /** "Hub silent · unknown". */
    HubSilent,
    NeedsCalibration,
    Paused,

    /** "Paused by Site": `pausedBy` includes the Site. */
    PausedBySite,
    NoNode,
}

/** The big value of a tile. */
public enum class LotTileValue {
    /** `~20`, from [LotTile.soilPercent]. */
    Soil,

    /** The silence, from [LotTile.duration]. */
    Duration,

    /** `raw`. */
    Raw,

    /** `—`. */
    Dash,

    /** `+`. */
    Plus,

    /** No value: a stale tile, or a status whose value the Server did not send. */
    None,
}

/** The foot line of a tile. */
public enum class LotTileFoot {
    /**
     * "‹t› · low 30 %": [LotTile.readingAtEpochMs] and [LotTile.lowPercent], each only when
     * present. At least one is.
     */
    Reading,

    /** "was ~40 % at ‹t›": [LotTile.soilPercent] and [LotTile.readingAtEpochMs]. */
    WasPercentAt,

    /** "last Reading ‹t›": [LotTile.readingAtEpochMs]. */
    LastReading,

    /** "no Readings yet". */
    NoReadingsYet,

    /** "no % until calibrated". */
    NoPercentUntilCalibrated,

    /** "until 1 Nov": [LotTile.pausedUntilEpochMs]. */
    PausedUntil,

    /** "paused": a Pause with no end. */
    Paused,

    /** "add a Node". */
    AddNode,

    /** "as of ‹t›": [LotTile.asOfEpochMs]. */
    AsOf,

    /** No foot: neither a Reading time nor a low Threshold. */
    None,
}

/** Which spoken label a tile gets (UX-DR98); the parts come from the tile's fields. */
public enum class LotTileSpoken {
    /** "‹Lot›, needs water, about 20 percent, low 30 percent, Reading ‹t›"; absent parts are left out. */
    NeedsWater,

    /** "‹Lot›, OK, about 35 percent, low 25 percent"; absent parts are left out. */
    Ok,

    /** "‹Lot›, unknown, Node silent for 6 hours, last about 40 percent at ‹t›"; absent parts are left out. */
    NodeSilent,

    /** The same with "Hub silent for …". */
    HubSilent,

    /** "‹Lot›, needs calibration, no percentage until calibrated". */
    NeedsCalibration,

    /** "‹Lot›, paused until 1 November". */
    PausedUntil,

    /** "‹Lot›, paused with the Site". */
    PausedWithSite,

    /** "‹Lot›, paused". */
    Paused,

    /** "‹Lot›, no Node, add a Node", unchanged. */
    NoNode,

    /** "‹Lot›, was ‹status›, not live, as of ‹t›". */
    Stale,
}

/**
 * One Lot tile. [status] is the Server's; everything else says how to draw it. A field that the
 * variant does not show is `null`, so a shell cannot show a value on a stale tile by accident.
 *
 * [soilPercent] is already rounded to 5 ([SoilMoisture]); [lowPercent] is the low Threshold as a
 * whole number. [durationSinceEpochMs] is what the silence is measured from: the last Reading
 * for a silent Node, `statusSince` for a silent Hub or a Lot without a Reading. [duration] is
 * that silence at the `now` the overview was built with. [opensAddNode] is the one tap target a
 * tile has: a live *no Node* tile, for Administrators and Owners.
 */
public data class LotTile(
    val id: String,
    val name: String,
    val status: LotStatus,
    val variant: LotTileVariant,
    val label: LotTileLabel,
    val value: LotTileValue,
    val foot: LotTileFoot,
    val spoken: LotTileSpoken,
    val soilPercent: Int? = null,
    val lowPercent: Int? = null,
    val readingAtEpochMs: Long? = null,
    val durationSinceEpochMs: Long? = null,
    val duration: LotDuration? = null,
    val pausedBySite: Boolean = false,
    val pausedUntilEpochMs: Long? = null,
    val asOfEpochMs: Long? = null,
    val opensAddNode: Boolean = false,
    /** A live *needs calibration* tile, for Administrators and Owners: the tile's Calibrate control. */
    val opensCalibrate: Boolean = false,
) {
    public companion object {
        /**
         * The tile of [lot]. In [stale] mode every Lot gets the stale variant with
         * [fetchedAtEpochMs] as its "as of" time.
         */
        public fun of(
            lot: LotSummary,
            stale: Boolean,
            fetchedAtEpochMs: Long,
            nowEpochMs: Long,
            canAddNode: Boolean,
            canCalibrate: Boolean = false,
        ): LotTile {
            val pausedBySite = lot.status == LotStatus.Paused && LotPauseSource.Site in lot.pausedBy
            val label = labelOf(lot, pausedBySite)
            if (stale) {
                return LotTile(
                    id = lot.id,
                    name = lot.name,
                    status = lot.status,
                    variant = LotTileVariant.Stale,
                    label = label,
                    value = LotTileValue.None,
                    foot = LotTileFoot.AsOf,
                    spoken = LotTileSpoken.Stale,
                    pausedBySite = pausedBySite,
                    asOfEpochMs = fetchedAtEpochMs,
                )
            }
            val base =
                LotTile(
                    id = lot.id,
                    name = lot.name,
                    status = lot.status,
                    variant = LotTileVariant.Unknown,
                    label = label,
                    value = LotTileValue.None,
                    foot = LotTileFoot.None,
                    spoken = LotTileSpoken.NodeSilent,
                    pausedBySite = pausedBySite,
                )
            return when (lot.status) {
                LotStatus.NeedsWater -> {
                    measured(base, lot, LotTileVariant.NeedsWater, LotTileSpoken.NeedsWater)
                }

                LotStatus.Ok -> {
                    measured(base, lot, LotTileVariant.Ok, LotTileSpoken.Ok)
                }

                LotStatus.Unknown -> {
                    unknown(base, lot, nowEpochMs)
                }

                LotStatus.NeedsCalibration -> {
                    base.copy(
                        variant = LotTileVariant.NeedsCalibration,
                        value = LotTileValue.Raw,
                        foot = LotTileFoot.NoPercentUntilCalibrated,
                        spoken = LotTileSpoken.NeedsCalibration,
                        opensCalibrate = canCalibrate,
                    )
                }

                LotStatus.Paused -> {
                    paused(base, lot)
                }

                LotStatus.NoNode -> {
                    base.copy(
                        variant = LotTileVariant.NoNode,
                        value = LotTileValue.Plus,
                        foot = LotTileFoot.AddNode,
                        spoken = LotTileSpoken.NoNode,
                        opensAddNode = canAddNode,
                    )
                }
            }
        }

        private fun labelOf(
            lot: LotSummary,
            pausedBySite: Boolean,
        ): LotTileLabel =
            when (lot.status) {
                LotStatus.NeedsWater -> {
                    LotTileLabel.NeedsWater
                }

                LotStatus.Ok -> {
                    LotTileLabel.Ok
                }

                LotStatus.Unknown -> {
                    if (lot.unknownCause ==
                        LotUnknownCause.Hub
                    ) {
                        LotTileLabel.HubSilent
                    } else {
                        LotTileLabel.Silent
                    }
                }

                LotStatus.NeedsCalibration -> {
                    LotTileLabel.NeedsCalibration
                }

                LotStatus.Paused -> {
                    if (pausedBySite) LotTileLabel.PausedBySite else LotTileLabel.Paused
                }

                LotStatus.NoNode -> {
                    LotTileLabel.NoNode
                }
            }

        /** Needs water and OK: the percentage, then the Reading time and the low Threshold. */
        private fun measured(
            base: LotTile,
            lot: LotSummary,
            variant: LotTileVariant,
            spoken: LotTileSpoken,
        ): LotTile {
            val soil = lot.moisturePercent?.let(SoilMoisture::rounded)
            val low = lot.lowThresholdPercent?.takeUnless { it.isNaN() }?.roundToInt()
            return base.copy(
                variant = variant,
                value = if (soil != null) LotTileValue.Soil else LotTileValue.None,
                foot = if (lot.lastReadingAtEpochMs != null || low != null) LotTileFoot.Reading else LotTileFoot.None,
                spoken = spoken,
                soilPercent = soil,
                lowPercent = low,
                readingAtEpochMs = lot.lastReadingAtEpochMs,
            )
        }

        private fun unknown(
            base: LotTile,
            lot: LotSummary,
            nowEpochMs: Long,
        ): LotTile {
            val hub = lot.unknownCause == LotUnknownCause.Hub
            val reading = lot.lastReadingAtEpochMs
            val since = if (hub) lot.statusSinceEpochMs else reading ?: lot.statusSinceEpochMs
            val duration = since?.let { LotDuration.between(it, nowEpochMs) }
            // A percentage without the time it was read at is never shown.
            val soil = if (reading != null) lot.moisturePercent?.let(SoilMoisture::rounded) else null
            return base.copy(
                variant = LotTileVariant.Unknown,
                value = if (duration != null) LotTileValue.Duration else LotTileValue.None,
                foot =
                    when {
                        reading == null -> LotTileFoot.NoReadingsYet
                        soil != null -> LotTileFoot.WasPercentAt
                        else -> LotTileFoot.LastReading
                    },
                spoken = if (hub) LotTileSpoken.HubSilent else LotTileSpoken.NodeSilent,
                soilPercent = soil,
                readingAtEpochMs = reading,
                durationSinceEpochMs = since,
                duration = duration,
            )
        }

        private fun paused(
            base: LotTile,
            lot: LotSummary,
        ): LotTile {
            val until = lot.pausedUntilEpochMs
            return base.copy(
                variant = LotTileVariant.Paused,
                value = LotTileValue.Dash,
                foot = if (until != null) LotTileFoot.PausedUntil else LotTileFoot.Paused,
                spoken =
                    when {
                        until != null -> LotTileSpoken.PausedUntil
                        base.pausedBySite -> LotTileSpoken.PausedWithSite
                        else -> LotTileSpoken.Paused
                    },
                pausedUntilEpochMs = until,
            )
        }
    }
}

/**
 * The Site overview of a [LotsState.Ready], built for one `now`. The shells build it again on
 * their minute tick, so [staleAge] and the tiles' durations move; nothing here is announced.
 *
 * In [stale] mode the stale header replaces the summary ([fetchedAtEpochMs] is "Last data ‹t›",
 * [staleAge] its age), every tile is [LotTileVariant.Stale] and every [menu] item is disabled
 * with "Needs your Server". [headline] and [counts] are still filled, for the shells that keep
 * them; they describe the last good data.
 */
public data class LotsOverview(
    val stale: Boolean,
    val fetchedAtEpochMs: Long,
    val staleAge: StaleAge?,
    val refreshing: Boolean,
    val headline: LotsHeadline,
    val counts: List<LotCount>,
    val tiles: List<LotTile>,
    val menu: List<SiteMenuItem>,
) {
    public companion object {
        public fun of(
            ready: LotsState.Ready,
            nowEpochMs: Long,
        ): LotsOverview {
            val canAddNode = DevicesState.canAddNode(ready.site.role)
            val canCalibrate = ready.site.role >= SiteRole.Administrator
            return LotsOverview(
                stale = ready.stale,
                fetchedAtEpochMs = ready.fetchedAtEpochMs,
                staleAge = if (ready.stale) StaleAge.between(ready.fetchedAtEpochMs, nowEpochMs) else null,
                refreshing = ready.refreshing,
                headline = LotsHeadline.of(ready.lots),
                counts = LotCounts.of(ready.lots),
                tiles =
                    ready.lots.map {
                        LotTile.of(it, ready.stale, ready.fetchedAtEpochMs, nowEpochMs, canAddNode, canCalibrate)
                    },
                menu = ready.siteMenu(),
            )
        }
    }
}

/**
 * The Site menu for the Lots' Site (UX-DR22): in stale mode every item is disabled with "Needs
 * your Server". Empty while there is no Site.
 */
public fun LotsState.siteMenu(): List<SiteMenuItem> = site?.let { SiteMenu.items(it.role, stale = stale) }.orEmpty()
