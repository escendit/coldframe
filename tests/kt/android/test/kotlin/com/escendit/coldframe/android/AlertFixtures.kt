package com.escendit.coldframe.android

import com.escendit.coldframe.core.alerts.AlertSide
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.sites.SiteRole
import java.time.Instant

/**
 * Alerts of every row variant as the Server would list them (Story 6.2), told against [now]. Health
 * Alerts have no producer before Epic 7, so they exist only here. The shell tests only render them.
 */
object AlertFixtures {
    val now: Instant = Instant.parse("2026-10-09T07:04:00Z")

    private fun at(text: String): Long = Instant.parse(text).toEpochMilli()

    private fun alert(
        id: String,
        lotName: String,
        openedAt: String,
        kind: String = AlertSummary.KIND_THRESHOLD,
        side: AlertSide? = null,
        quantity: SensorQuantity = SensorQuantity.SoilMoisture,
        closedAt: String? = null,
    ) = AlertSummary(
        id = id,
        kind = kind,
        side = side,
        quantity = quantity,
        lotId = "lot-$id",
        lotName = lotName,
        deviceId = "7c19000000000001",
        openedAtEpochMs = at(openedAt),
        closedAtEpochMs = closedAt?.let(::at),
    )

    /** The only orange row: open, low side, soil moisture. */
    val needsWater = alert("water", "Tomatoes", "2026-10-09T05:45:00Z", side = AlertSide.Low)

    /** Open, high side, soil moisture: neutral, never orange. */
    val tooWet = alert("wet", "Herbs", "2026-10-09T04:10:00Z", side = AlertSide.High)

    val tooCold =
        alert("cold", "Peppers", "2026-10-08T22:30:00Z", side = AlertSide.Low, quantity = SensorQuantity.AirTemperature)

    val tooHumid =
        alert(
            "humid",
            "Basil",
            "2026-10-01T09:00:00Z",
            side = AlertSide.High,
            quantity = SensorQuantity.RelativeHumidity,
        )

    val silent = alert("silent", "Beans", "2026-10-09T01:05:00Z", kind = AlertSummary.KIND_SILENT)
    val battery = alert("battery", "Salad", "2026-10-08T12:00:00Z", kind = AlertSummary.KIND_BATTERY)
    val uncalibrated = alert("uncal", "Carrots", "2026-10-07T08:00:00Z", kind = AlertSummary.KIND_UNCALIBRATED)

    /** A kind this app does not know: a Health row with `help`. */
    val unknownKind = alert("flood", "Leeks", "2026-10-06T08:00:00Z", kind = "flooded")

    val closedWater =
        alert("was-water", "Tomatoes", "2026-10-09T05:45:00Z", side = AlertSide.Low, closedAt = "2026-10-09T06:40:00Z")

    val closedWet =
        alert("was-wet", "Herbs", "2026-10-07T10:00:00Z", side = AlertSide.High, closedAt = "2026-10-07T12:30:00Z")

    val threshold = listOf(needsWater, tooWet, tooCold, tooHumid)
    val health = listOf(silent, battery, uncalibrated, unknownKind)
    val closed = listOf(closedWater, closedWet)

    /** Every variant in the Server's order: open newest first, then closed newest first. */
    val everyVariant: List<AlertSummary> = (threshold + health).sortedByDescending { it.openedAtEpochMs } + closed

    fun ready(
        alerts: List<AlertSummary> = everyVariant,
        role: SiteRole = SiteRole.Member,
        refreshing: Boolean = false,
    ) = AlertsState.Ready(homeSite(role), alerts, alerts.count { it.open }, refreshing)
}
