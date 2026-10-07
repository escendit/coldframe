package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.devices.DevicesState

/**
 * [LotDetailState] flattened for Swift: enum values cross as catalogue key suffixes
 * (`needsWater`, `soilMoisture`, `raw`, `notCharging`), times as Unix milliseconds, and an
 * optional number as a decimal string that is empty when absent. No token or URL crosses.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`; [notice] the [LotDetailNotice] key with
 * [noticeTryAgain]. The rest is [LotDetail] for the `now` the snapshot was taken at, empty
 * unless `ready`:
 * - Stale: [stale], [staleReason] (`cached`, `unreachable`), [fetchedAtEpochMs], the age
 *   [staleAgeDays] / [staleAgeHours] / [staleAgeMinutes], [refreshing].
 * - Hero: [heroVariant] / [heroLabel] / [heroSpoken] (the tile's keys), [heroStatus] (contract
 *   value), [heroStatusSince], [heroValue] (`raw`, `percent`, `none`) with [heroRawNumber] or
 *   [heroSoilPercent], [heroLowPercent], [heroReadingAt], [heroNote] (`none`,
 *   `noPercentUntilCalibrated`, `checkPowerOrRange`, `hubSilent`, `pausedUntil`, `paused`,
 *   `pausedWithSite`), [heroPausedUntil], [heroResumeSiteHint], [heroNeedsWaterFill] and the
 *   silence [heroDurationValue] with [heroDurationUnit].
 * - [noNode] and [canAddNode] (Admin+, mobile).
 * - Sensor cells, one entry per Sensor in the Server's order ([hasSensors] false while stale or
 *   without a Node): [sensorQuantities], [sensorNumbers], [sensorUnits] (`raw`, `celsius`,
 *   `percent`, `kiloOhm`), [sensorMeasuredAts].
 * - Device cells ([hasDevice]): [deviceNodeId], [deviceBattery] (decimal or empty),
 *   [deviceBatteryLow], [deviceCharging] (`charging`, `notCharging` or empty), [deviceLastSeen].
 * - Chart: [quantities] (the picker, shown with more than one), [picked], [historyUnavailable],
 *   [hasChart] with [chartQuantity], [chartUnit], 30 bars in the parallel lists [barDays]
 *   (`yyyy-MM-dd`), [barDayEpochMs], [barPresent], [barLows], [barHighs] (numbers, empty for a
 *   gap), [barCounts], [barFractions] (0..1), and the summary parts [chartDaysWithReadings],
 *   [chartLowest] with [chartLowestDay], [chartHighest] with [chartHighestDay].
 */
public data class LotDetailSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val lotId: String?,
    val lotName: String?,
    val stale: Boolean,
    val staleReason: String?,
    val refreshing: Boolean,
    val fetchedAtEpochMs: Long,
    val staleAgeDays: Int,
    val staleAgeHours: Int,
    val staleAgeMinutes: Int,
    val heroStatus: String?,
    val heroVariant: String?,
    val heroLabel: String?,
    val heroSpoken: String?,
    val heroStatusSince: String,
    val heroValue: String?,
    val heroRawNumber: String,
    val heroSoilPercent: String,
    val heroLowPercent: String,
    val heroReadingAt: String,
    val heroNote: String?,
    val heroPausedUntil: String,
    val heroResumeSiteHint: Boolean,
    val heroNeedsWaterFill: Boolean,
    val heroDurationValue: String,
    val heroDurationUnit: String,
    val noNode: Boolean,
    val canAddNode: Boolean,
    val hasSensors: Boolean,
    val sensorQuantities: List<String>,
    val sensorNumbers: List<String>,
    val sensorUnits: List<String>,
    val sensorMeasuredAts: List<String>,
    val hasDevice: Boolean,
    val deviceNodeId: String,
    val deviceBattery: String,
    val deviceBatteryLow: Boolean,
    val deviceCharging: String,
    val deviceLastSeen: String,
    val quantities: List<String>,
    val picked: String?,
    val historyUnavailable: Boolean,
    val hasChart: Boolean,
    val chartQuantity: String?,
    val chartUnit: String?,
    val barDays: List<String>,
    val barDayEpochMs: List<Long>,
    val barPresent: List<Boolean>,
    val barLows: List<String>,
    val barHighs: List<String>,
    val barCounts: List<Int>,
    val barFractions: List<Double>,
    val chartDaysWithReadings: Int,
    val chartLowest: String,
    val chartLowestDay: String,
    val chartHighest: String,
    val chartHighestDay: String,
)

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

private fun Number?.text(): String = this?.toString().orEmpty()

private fun SensorQuantity.camel(): String = name.replaceFirstChar { it.lowercase() }

/** The snapshot of [state], with ages measured at [nowEpochMs]. */
public fun snapshotOf(
    state: LotDetailState,
    nowEpochMs: Long,
): LotDetailSnapshot {
    val ready = state as? LotDetailState.Ready
    val detail = ready?.let { LotDetail.of(it, nowEpochMs) }
    val failed = state as? LotDetailState.Failed
    val hero = detail?.hero
    val chart = detail?.chart
    val bars = chart?.bars.orEmpty()
    val site =
        when (state) {
            is LotDetailState.Loading -> state.site
            is LotDetailState.Failed -> state.site
            is LotDetailState.Ready -> state.site
            LotDetailState.Idle -> null
        }
    return LotDetailSnapshot(
        surface =
            when (state) {
                LotDetailState.Idle -> "idle"
                is LotDetailState.Loading -> "loading"
                is LotDetailState.Failed -> "failed"
                is LotDetailState.Ready -> "ready"
            },
        notice = failed?.notice?.key(),
        noticeTryAgain = failed?.notice?.tryAgain == true,
        siteId = site?.id,
        lotId =
            when (state) {
                is LotDetailState.Loading -> state.lotId
                is LotDetailState.Failed -> state.lotId
                is LotDetailState.Ready -> state.lot.id
                LotDetailState.Idle -> null
            },
        lotName =
            when (state) {
                is LotDetailState.Loading -> state.name
                is LotDetailState.Failed -> state.name
                is LotDetailState.Ready -> state.lot.name
                LotDetailState.Idle -> null
            },
        stale = detail?.stale == true,
        staleReason = ready?.staleReason?.key(),
        refreshing = detail?.refreshing == true,
        fetchedAtEpochMs = detail?.fetchedAtEpochMs ?: 0,
        staleAgeDays = detail?.staleAge?.days ?: 0,
        staleAgeHours = detail?.staleAge?.hours ?: 0,
        staleAgeMinutes = detail?.staleAge?.minutes ?: 0,
        heroStatus = hero?.status?.key,
        heroVariant = hero?.variant?.key(),
        heroLabel = hero?.label?.key(),
        heroSpoken = hero?.spoken?.key(),
        heroStatusSince = hero?.statusSinceEpochMs.text(),
        heroValue = hero?.valueKind?.key(),
        heroRawNumber = hero?.rawNumber.orEmpty(),
        heroSoilPercent = hero?.soilPercent.text(),
        heroLowPercent = hero?.lowPercent.text(),
        heroReadingAt = hero?.readingAtEpochMs.text(),
        heroNote = hero?.note?.key(),
        heroPausedUntil = hero?.pausedUntilEpochMs.text(),
        heroResumeSiteHint = hero?.resumeSiteHint == true,
        heroNeedsWaterFill = hero?.needsWaterFill == true,
        heroDurationValue = hero?.duration?.value.text(),
        heroDurationUnit =
            hero
                ?.duration
                ?.unit
                ?.key()
                .orEmpty(),
        noNode = detail?.noNode == true,
        canAddNode = site?.let { DevicesState.canAddNode(it.role) } == true,
        hasSensors = detail?.sensors != null,
        sensorQuantities = detail?.sensors?.map { it.quantity.camel() }.orEmpty(),
        sensorNumbers = detail?.sensors?.map { it.number }.orEmpty(),
        sensorUnits = detail?.sensors?.map { it.unit.key() }.orEmpty(),
        sensorMeasuredAts = detail?.sensors?.map { it.measuredAtEpochMs.text() }.orEmpty(),
        hasDevice = detail?.device != null,
        deviceNodeId = detail?.device?.nodeId.orEmpty(),
        deviceBattery = detail?.device?.batteryPercent.text(),
        deviceBatteryLow = detail?.device?.batteryLow == true,
        deviceCharging =
            detail
                ?.device
                ?.charging
                ?.key
                .orEmpty(),
        deviceLastSeen = detail?.device?.lastSeenAtEpochMs.text(),
        quantities = detail?.quantities?.map { it.camel() }.orEmpty(),
        picked = detail?.picked?.camel(),
        historyUnavailable = detail?.historyUnavailable == true,
        hasChart = chart != null,
        chartQuantity = chart?.quantity?.camel(),
        chartUnit = chart?.unit?.key(),
        barDays = bars.map { it.day },
        barDayEpochMs = bars.map { it.dayEpochMs },
        barPresent = bars.map { it.present },
        barLows = bars.map { it.lowNumber.orEmpty() },
        barHighs = bars.map { it.highNumber.orEmpty() },
        barCounts = bars.map { it.readingCount },
        barFractions = bars.map { it.fraction },
        chartDaysWithReadings = chart?.daysWithReadings ?: 0,
        chartLowest = chart?.lowestNumber.orEmpty(),
        chartLowestDay = chart?.lowestDay.orEmpty(),
        chartHighest = chart?.highestNumber.orEmpty(),
        chartHighestDay = chart?.highestDay.orEmpty(),
    )
}
