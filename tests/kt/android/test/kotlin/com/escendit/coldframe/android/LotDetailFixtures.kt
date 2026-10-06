package com.escendit.coldframe.android

import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotHistory
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotUnknownCause
import com.escendit.coldframe.core.lots.NodeStatus
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.lots.SensorReading
import com.escendit.coldframe.core.lots.SensorUnit
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.sites.SiteRole
import java.time.Instant

/**
 * Lot detail as the Server would send it (Story 4.8), told against [LotFixtures.now]. The shell
 * tests only render these: every value, unit and day is fixed here, as it is the Server's.
 */
object LotDetailFixtures {
    private fun at(text: String): Long = Instant.parse(text).toEpochMilli()

    private val node = NodeStatus("7c19a1b2c3d4e5f6", 62, ChargeState.Charging, at("2026-10-06T07:02:00Z"))

    val sensors =
        listOf(
            SensorReading(SensorQuantity.SoilMoisture, 1840.0, SensorUnit.Raw, at("2026-10-06T07:02:00Z")),
            SensorReading(SensorQuantity.AirTemperature, 14.4, SensorUnit.Celsius, at("2026-10-06T07:02:00Z")),
            SensorReading(SensorQuantity.RelativeHumidity, 77.6, SensorUnit.Percent, at("2026-10-06T07:02:00Z")),
            SensorReading(SensorQuantity.GasResistance, 142.37, SensorUnit.KiloOhm, at("2026-10-06T07:02:00Z")),
        )

    /** Thirty days with gaps: no Reading on every seventh day; soil lows between 1500 and 1900. */
    private fun days(
        scale: (Int) -> Double,
        spread: Double,
    ): List<LotHistoryDayDto> =
        (0 until 30)
            .filter { it % 7 != 3 }
            .map { index ->
                val day =
                    java.time.LocalDate
                        .of(2026, 9, 7)
                        .plusDays(index.toLong())
                        .toString()
                LotHistoryDayDto(day, scale(index), scale(index) + spread, 96)
            }

    private val soilHistory =
        LotHistory(SensorQuantity.SoilMoisture, SensorUnit.Raw, days({ 1500.0 + (it * 37 % 400) }, 220.0))

    val temperatureHistory =
        LotHistory(SensorQuantity.AirTemperature, SensorUnit.Celsius, days({ 6.0 + (it % 9) }, 9.0))

    private fun lot(
        status: LotStatus,
        name: String = "Tomatoes",
        node: NodeStatus? = this.node,
        sensors: List<SensorReading> = this.sensors,
        unknownCause: LotUnknownCause? = null,
        pausedBy: List<LotPauseSource> = emptyList(),
        pausedUntil: Long? = null,
        moisture: Double? = null,
        low: Double? = null,
    ) = LotSummary(
        id = "lot-tomatoes",
        name = name,
        status = status,
        statusSinceEpochMs = at("2026-10-06T05:45:00Z"),
        lastReadingAtEpochMs = if (node != null) at("2026-10-06T07:02:00Z") else null,
        unknownCause = unknownCause,
        pausedBy = pausedBy,
        pausedUntilEpochMs = pausedUntil,
        moisturePercent = moisture,
        lowThresholdPercent = low,
        node = node,
        sensors = sensors,
    )

    val needsCalibration = lot(LotStatus.NeedsCalibration)
    val ok = lot(LotStatus.Ok, moisture = 36.0, low = 25.0)
    val needsWater = lot(LotStatus.NeedsWater, moisture = 21.0, low = 30.0)

    /** A silent Node, 6 h since its last Reading. */
    val unknown =
        lot(LotStatus.Unknown, unknownCause = LotUnknownCause.Node).copy(
            lastReadingAtEpochMs = at("2026-10-06T01:04:00Z"),
            statusSinceEpochMs = at("2026-10-06T02:00:00Z"),
        )
    val paused =
        lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Device), pausedUntil = at("2026-11-01T00:00:00Z"))
    val pausedBySite = lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site))
    val noNode = lot(LotStatus.NoNode, name = "Carrots", node = null, sensors = emptyList())

    /** A Node with a flat battery and no report of charging. */
    val lowBattery =
        needsCalibration.copy(
            node = NodeStatus("7c19a1b2c3d4e5f6", 14, ChargeState.NotCharging, at("2026-10-06T07:02:00Z")),
        )

    fun ready(
        lot: LotSummary = needsCalibration,
        role: SiteRole = SiteRole.Owner,
        staleReason: StaleReason? = null,
        picked: SensorQuantity? = lot.sensors.firstOrNull()?.quantity,
        history: Map<SensorQuantity, LotHistory> =
            mapOf(SensorQuantity.SoilMoisture to soilHistory, SensorQuantity.AirTemperature to temperatureHistory),
    ): LotDetailState.Ready =
        LotDetailState.Ready(
            site = homeSite(role),
            lot = lot,
            picked = picked,
            history = if (lot.sensors.isEmpty()) emptyMap() else history,
            historyUnavailable = false,
            fetchedAtEpochMs = (if (staleReason == null) LotFixtures.now else LotFixtures.fetchedAt).toEpochMilli(),
            staleReason = staleReason,
            refreshing = false,
        )
}
