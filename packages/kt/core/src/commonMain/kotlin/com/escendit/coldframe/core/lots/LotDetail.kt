package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.api.NodeStatusDto
import com.escendit.coldframe.core.api.SensorReadingDto
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.sites.SiteRole
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.log10
import kotlin.math.pow
import kotlin.math.roundToInt
import kotlin.math.roundToLong

/*
 * Lot detail as both shells draw it (Story 4.8): hero, Sensor cells, the History chart and the
 * Device cells. Everything here is structured data over the Server's fields; the shells own the
 * string catalogues. Nothing computes a status, a unit conversion or an aggregation (AD-14): the
 * Server converts and aggregates, this file only formats and lays out what it sent.
 */

/** What a Sensor measures, as the Server names it. */
public enum class SensorQuantity(
    /** The contract value. */
    public val key: String,
) {
    SoilMoisture("soil_moisture"),
    AirTemperature("air_temperature"),
    RelativeHumidity("relative_humidity"),
    GasResistance("gas_resistance"),
    ;

    /** Soil moisture is drawn as a daily low only; the others also show the day's min and max. */
    public val showsRange: Boolean get() = this != SoilMoisture

    public companion object {
        public fun fromServer(value: String): SensorQuantity? = entries.firstOrNull { it.key == value }
    }
}

/** The unit of a converted value: `raw` is the uncalibrated soil count, never a percentage. */
public enum class SensorUnit(
    public val key: String,
) {
    Raw("raw"),
    Celsius("°C"),
    Percent("%"),
    KiloOhm("kΩ"),
    ;

    public companion object {
        public fun fromServer(value: String): SensorUnit? = entries.firstOrNull { it.key == value }
    }
}

/** The charger state of a Node at its last report. */
public enum class ChargeState {
    Charging,
    NotCharging,
    ;

    /** The contract value: `charging`, `notCharging`. */
    public val key: String get() = name.replaceFirstChar { it.lowercase() }

    public companion object {
        public fun fromServer(value: String?): ChargeState? = entries.firstOrNull { it.key == value }
    }
}

/** A Lot's Node with its battery, charging and last seen; every optional field is the Server's or `null`. */
public data class NodeStatus(
    val deviceId: String,
    val batteryPercent: Int? = null,
    val charging: ChargeState? = null,
    val lastSeenAtEpochMs: Long? = null,
)

/** The newest Reading of a Sensor as the Server converted it. */
public data class SensorReading(
    val quantity: SensorQuantity,
    val value: Double,
    val unit: SensorUnit,
    val measuredAtEpochMs: Long?,
    /** The Sensor ID Calibration names; `null` from a Server that does not send it. */
    val sensorId: String? = null,
    /** The Server's: the Sensor's Specification says `calibration: true`. */
    val calibratable: Boolean = false,
)

internal fun NodeStatusDto.toNodeStatus(): NodeStatus =
    NodeStatus(
        deviceId = deviceId,
        batteryPercent = batteryPercent,
        charging = ChargeState.fromServer(charging),
        lastSeenAtEpochMs = lastSeenAt?.let(DevicesEngine::epochMsOf),
    )

/** `null` for a quantity or unit this client does not know: it is left out, never guessed. */
internal fun SensorReadingDto.toSensorReading(): SensorReading? {
    val known = SensorQuantity.fromServer(quantity) ?: return null
    val knownUnit = SensorUnit.fromServer(unit) ?: return null
    return SensorReading(known, value, knownUnit, DevicesEngine.epochMsOf(measuredAt), sensorId, calibratable == true)
}

/** Formatting of converted values: soil `raw N`, whole °C and %RH, kΩ to 3 significant digits. */
public object SensorFormat {
    private const val SIGNIFICANT = 3
    private const val MIN_SIGNIFICANT = 0.001
    private const val LOW_BATTERY = 20

    /** The number only, with `.` as the decimal separator; the shells add the unit from their catalogues. */
    public fun number(
        value: Double,
        unit: SensorUnit,
    ): String =
        when (unit) {
            SensorUnit.KiloOhm -> significant(value)
            SensorUnit.Raw, SensorUnit.Celsius, SensorUnit.Percent -> whole(value)
        }

    private fun whole(value: Double): String = if (value.isNaN()) "0" else value.roundToLong().toString()

    /** [value] to 3 significant digits without exponent: `142`, `14.2`, `1.42`, `1420`. */
    internal fun significant(value: Double): String {
        if (value.isNaN() || value.isInfinite() || abs(value) < MIN_SIGNIFICANT) return "0"
        val magnitude = floor(log10(abs(value))).toInt()
        val decimals = (SIGNIFICANT - 1 - magnitude).coerceAtLeast(0)
        val scale = 10.0.pow(SIGNIFICANT - 1 - magnitude)
        val rounded = (value * scale).roundToLong() / scale
        if (decimals == 0) return rounded.roundToLong().toString()
        val text = rounded.toString()
        val dot = text.indexOf('.')
        if (dot < 0) return text + "." + "0".repeat(decimals)
        return text.padEnd(dot + 1 + decimals, '0').take(dot + 1 + decimals)
    }

    /** Battery below 20 % shows `battery--low`. */
    public fun batteryLow(percent: Int?): Boolean = percent != null && percent < LOW_BATTERY
}

/** One Sensor cell: [number] and [unit] for the shell to compose; [measuredAtEpochMs] for its time. */
public data class SensorCell(
    val quantity: SensorQuantity,
    val number: String,
    val unit: SensorUnit,
    val measuredAtEpochMs: Long?,
)

/** What the hero's big value is. */
public enum class HeroValueKind {
    /** `raw N` from [LotDetailHero.rawNumber]: needs calibration. */
    Raw,

    /** `~20` from [LotDetailHero.soilPercent]: the Server sent `moisturePercent`. */
    Percent,

    /** `—`: no value to show (including every stale hero). */
    None,
}

/** The status-specific text under the hero (UX-DR78). */
public enum class HeroNote {
    None,

    /** "no % until calibrated". */
    NoPercentUntilCalibrated,

    /** "Check power or range." */
    CheckPowerOrRange,

    /** "Hub ‹id› is silent; Lots behind it can't be read." (the Server names no Hub, so the copy omits the ID). */
    HubSilent,

    /** "paused until ‹date›": [LotDetailHero.pausedUntilEpochMs]. */
    PausedUntil,

    /** "paused": a Pause with no end. */
    Paused,

    /** "Paused with the Site". */
    PausedWithSite,
}

/**
 * The hero. [variant], [label] and [spoken] are the Lot tile's (the matching tile treatment); in
 * stale mode they are the stale ones and no value shows. [needsWaterFill] is true only for
 * needs water. [resumeSiteHint] is "Resume the Site to resume this Node", for Admin+ on a Lot
 * paused by the Site. [duration] is the silence of an unknown Lot ([LotTile.duration]).
 */
public data class LotDetailHero(
    val name: String,
    val status: LotStatus,
    val variant: LotTileVariant,
    val label: LotTileLabel,
    val spoken: LotTileSpoken,
    val statusSinceEpochMs: Long?,
    val valueKind: HeroValueKind,
    val rawNumber: String?,
    val soilPercent: Int?,
    val lowPercent: Int?,
    val readingAtEpochMs: Long?,
    val note: HeroNote,
    val pausedUntilEpochMs: Long?,
    val resumeSiteHint: Boolean,
    val duration: LotDuration?,
) {
    val needsWaterFill: Boolean get() = status == LotStatus.NeedsWater && variant != LotTileVariant.Stale
}

/** The two Device cells: battery with charging, and last seen with its cadence. */
public data class DeviceCells(
    val nodeId: String,
    val batteryPercent: Int?,
    val batteryLow: Boolean,
    val charging: ChargeState?,
    val lastSeenAtEpochMs: Long?,
)

/** One of the chart's 30 days. A day without Readings is a gap: [present] is false, never zero. */
public data class ChartBar(
    val dayEpochMs: Long,
    val day: String,
    val present: Boolean,
    val lowNumber: String?,
    val highNumber: String?,
    val readingCount: Int,
    /** 0..1 of the axis; 0 for a gap. */
    val fraction: Double,
)

/**
 * The History chart for [quantity]: 30 UTC days ending on the day of `now`, ascending.
 * [lowestNumber] / [lowestDay] and [highestNumber] / [highestDay] are over the days with Readings,
 * for the text summary; [daysWithReadings] counts them. The bars are daily lows scaled to the
 * axis, which runs from 0 (or the lowest value below 0) to the largest daily low.
 */
public data class HistoryChart(
    val quantity: SensorQuantity,
    val unit: SensorUnit,
    val bars: List<ChartBar>,
    val daysWithReadings: Int,
    val lowestNumber: String?,
    val lowestDay: String?,
    val highestNumber: String?,
    val highestDay: String?,
) {
    public companion object {
        public const val DAYS: Int = 30
        private const val MS_PER_DAY = 86_400_000L
        private const val MIN_FRACTION = 0.03

        /** [days] as the Server sent them (ascending, only days with Readings), laid out against `now`. */
        public fun of(
            quantity: SensorQuantity,
            unit: SensorUnit,
            days: List<LotHistoryDayDto>,
            nowEpochMs: Long,
        ): HistoryChart {
            val today = nowEpochMs.floorDiv(MS_PER_DAY)
            val byDay = days.associateBy { it.day }
            val slots = (today - DAYS + 1..today).map { epochDay -> epochDay to byDay[dayText(epochDay)] }
            val present = slots.mapNotNull { it.second }
            val axisLow = minOf(0.0, present.minOfOrNull { it.low } ?: 0.0)
            val axisHigh = maxOf(axisLow + 1.0, present.maxOfOrNull { it.low } ?: 0.0)
            val bars =
                slots.map { (epochDay, entry) ->
                    ChartBar(
                        dayEpochMs = epochDay * MS_PER_DAY,
                        day = dayText(epochDay),
                        present = entry != null,
                        lowNumber = entry?.let { SensorFormat.number(it.low, unit) },
                        highNumber = entry?.let { SensorFormat.number(it.high, unit) },
                        readingCount = entry?.readingCount ?: 0,
                        fraction =
                            if (entry == null) {
                                0.0
                            } else {
                                ((entry.low - axisLow) / (axisHigh - axisLow)).coerceIn(MIN_FRACTION, 1.0)
                            },
                    )
                }
            val lowest = present.minByOrNull { it.low }
            val highest = present.maxByOrNull { it.high }
            return HistoryChart(
                quantity = quantity,
                unit = unit,
                bars = bars,
                daysWithReadings = present.size,
                lowestNumber = lowest?.let { SensorFormat.number(it.low, unit) },
                lowestDay = lowest?.day,
                highestNumber = highest?.let { SensorFormat.number(it.high, unit) },
                highestDay = highest?.day,
            )
        }

        /** `yyyy-MM-dd` of a day count since 1970-01-01 (civil-from-days). */
        internal fun dayText(epochDay: Long): String {
            val z = epochDay + 719_468
            val era = z.floorDiv(146_097L)
            val doe = z - era * 146_097
            val yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365
            val y = yoe + era * 400
            val doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
            val mp = (5 * doy + 2) / 153
            val d = doy - (153 * mp + 2) / 5 + 1
            val m = if (mp < 10) mp + 3 else mp - 9
            val year = if (m <= 2) y + 1 else y
            return "${year.toString().padStart(
                4,
                '0',
            )}-${m.toString().padStart(2, '0')}-${d.toString().padStart(2, '0')}"
        }
    }
}

/**
 * Lot detail of a [LotDetailState.Ready], built for one `now`. [sensors] and [device] are `null`
 * while [stale] (no live value is shown) and for a Lot without a Node; [noNode] is the empty
 * detail (Add a Node on mobile for Admin+, else "Add a Node from the mobile app" on web).
 * [quantities] are the Sensors the Lot has, in the Server's order, for the picker; the picker
 * shows with more than one. [chart] is `null` until its history is read, [historyUnavailable]
 * when that read failed.
 */
public data class LotDetail(
    val stale: Boolean,
    val fetchedAtEpochMs: Long,
    val staleAge: StaleAge?,
    val refreshing: Boolean,
    val hero: LotDetailHero,
    val noNode: Boolean,
    val canAddNode: Boolean,
    val sensors: List<SensorCell>?,
    val device: DeviceCells?,
    val quantities: List<SensorQuantity>,
    val picked: SensorQuantity?,
    val chart: HistoryChart?,
    val historyUnavailable: Boolean,
    /** Calibrate shows: Admin+ on a live Lot with a Sensor whose Specification calls for Calibration; hidden, never disabled. */
    val canCalibrate: Boolean = false,
) {
    public companion object {
        public fun of(
            ready: LotDetailState.Ready,
            nowEpochMs: Long,
        ): LotDetail {
            val lot = ready.lot
            val stale = ready.stale
            val role = ready.site.role
            val tile = LotTile.of(lot, stale, ready.fetchedAtEpochMs, nowEpochMs, canAddNode = false)
            val soil = lot.sensors.firstOrNull { it.quantity == SensorQuantity.SoilMoisture }
            val bySite = tile.pausedBySite
            val calibrating = lot.status == LotStatus.NeedsCalibration
            val percent = lot.moisturePercent?.let(SoilMoisture::rounded)
            val valueKind =
                when {
                    stale -> HeroValueKind.None
                    calibrating && soil != null -> HeroValueKind.Raw
                    !calibrating && percent != null -> HeroValueKind.Percent
                    else -> HeroValueKind.None
                }
            val hero =
                LotDetailHero(
                    name = lot.name,
                    status = lot.status,
                    variant = tile.variant,
                    label = tile.label,
                    spoken = tile.spoken,
                    statusSinceEpochMs = lot.statusSinceEpochMs,
                    valueKind = valueKind,
                    rawNumber =
                        soil?.takeIf { valueKind == HeroValueKind.Raw }?.let {
                            SensorFormat.number(
                                it.value,
                                SensorUnit.Raw,
                            )
                        },
                    soilPercent = percent.takeIf { valueKind == HeroValueKind.Percent },
                    lowPercent = if (stale) null else lot.lowThresholdPercent?.takeUnless { it.isNaN() }?.roundToInt(),
                    readingAtEpochMs = if (stale) null else lot.lastReadingAtEpochMs,
                    note = if (stale) HeroNote.None else noteOf(lot, tile, bySite),
                    pausedUntilEpochMs = lot.pausedUntilEpochMs.takeIf { !stale && lot.status == LotStatus.Paused },
                    resumeSiteHint = !stale && bySite && role >= SiteRole.Administrator,
                    duration = tile.duration,
                )
            val node = lot.node
            val liveNode = if (stale) null else node
            return LotDetail(
                stale = stale,
                fetchedAtEpochMs = ready.fetchedAtEpochMs,
                staleAge = if (stale) StaleAge.between(ready.fetchedAtEpochMs, nowEpochMs) else null,
                refreshing = ready.refreshing,
                hero = hero,
                noNode = lot.status == LotStatus.NoNode,
                canAddNode = DevicesState.canAddNode(role),
                sensors =
                    if (stale || node == null) {
                        null
                    } else {
                        lot.sensors.map {
                            SensorCell(
                                it.quantity,
                                SensorFormat.number(it.value, it.unit),
                                it.unit,
                                it.measuredAtEpochMs,
                            )
                        }
                    },
                device =
                    liveNode?.let {
                        DeviceCells(
                            nodeId = it.deviceId,
                            batteryPercent = it.batteryPercent,
                            batteryLow = SensorFormat.batteryLow(it.batteryPercent),
                            charging = it.charging,
                            lastSeenAtEpochMs = it.lastSeenAtEpochMs,
                        )
                    },
                quantities = lot.sensors.map { it.quantity }.distinct(),
                picked = ready.picked,
                chart =
                    ready.picked?.let { ready.history[it] }?.let {
                        HistoryChart.of(
                            it.quantity,
                            it.unit,
                            it.days,
                            nowEpochMs,
                        )
                    },
                historyUnavailable = ready.historyUnavailable,
                canCalibrate =
                    !stale && role >= SiteRole.Administrator &&
                        lot.sensors.any { it.calibratable && it.sensorId != null },
            )
        }

        private fun noteOf(
            lot: LotSummary,
            tile: LotTile,
            bySite: Boolean,
        ): HeroNote =
            when (lot.status) {
                LotStatus.NeedsCalibration -> {
                    HeroNote.NoPercentUntilCalibrated
                }

                LotStatus.Unknown -> {
                    if (lot.unknownCause == LotUnknownCause.Hub) HeroNote.HubSilent else HeroNote.CheckPowerOrRange
                }

                LotStatus.Paused -> {
                    when {
                        bySite -> HeroNote.PausedWithSite
                        tile.pausedUntilEpochMs != null -> HeroNote.PausedUntil
                        else -> HeroNote.Paused
                    }
                }

                else -> {
                    HeroNote.None
                }
            }
    }
}
