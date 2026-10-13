package com.escendit.coldframe.android

import com.escendit.coldframe.core.alerts.AlertDeviceKind
import com.escendit.coldframe.core.alerts.AlertSide
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.sites.SiteRole
import java.time.Instant

/**
 * Alerts of every row variant as the Server would list them (Story 6.2), told against [now]. The
 * shell tests only render them.
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

    // Story 7.2: Health Alerts as `healthAlerts` lists them, about a Device and with its silent-since.

    private fun silentDevice(
        id: String,
        deviceId: String,
        deviceKind: AlertDeviceKind,
        silentSince: String?,
        openedAt: String,
        lotName: String? = null,
        closedAt: String? = null,
    ) = AlertSummary(
        id = id,
        kind = AlertSummary.KIND_SILENT,
        side = null,
        quantity = null,
        lotId = lotName?.let { "lot-$id" },
        lotName = lotName,
        deviceId = deviceId,
        openedAtEpochMs = at(openedAt),
        closedAtEpochMs = closedAt?.let(::at),
        deviceKind = deviceKind,
        silentSinceEpochMs = silentSince?.let(::at),
    )

    /** A Node on Beans, last heard 6 h 4 min before [now]. */
    val silentNode =
        silentDevice(
            "node",
            "9c0d1e4b5a673f2a",
            AlertDeviceKind.Node,
            "2026-10-09T01:00:00Z",
            "2026-10-09T07:00:00Z",
            lotName = "Beans",
        )

    /** Hub 3F2A, last heard 5 min before [now]. */
    val silentHub =
        silentDevice("hub", "3f2a9c0d1e4b5a67", AlertDeviceKind.Hub, "2026-10-09T06:59:00Z", "2026-10-09T07:04:00Z")

    /** A Node on no Lot, named by its short name. */
    val silentSpareNode =
        silentDevice(
            "spare",
            "9c0d000000000000",
            AlertDeviceKind.Node,
            "2026-10-09T00:30:00Z",
            "2026-10-09T06:30:00Z",
        )

    /** A Hub's Alert that closed: the row drops " for …". */
    val closedSilentHub =
        silentDevice(
            "was-hub",
            "1b00aa11bb22cc33",
            AlertDeviceKind.Hub,
            "2026-10-08T20:00:00Z",
            "2026-10-08T20:05:00Z",
            closedAt = "2026-10-08T21:00:00Z",
        )

    /** A silent Alert opened before the Server recorded a silent-since. */
    val silentHubNoSince =
        silentDevice("old-hub", "3f2a9c0d1e4b5a67", AlertDeviceKind.Hub, null, "2026-10-09T07:00:00Z")

    /** A kind this app does not know on a Hub: the neutral row without a Lot. */
    val unknownKindOnHub = silentHub.copy(id = "flood-hub", kind = "flooded")

    /** The Alerts surface with Node and Hub Health rows: a Threshold Alert, the open silent Devices, a closed one. */
    val silentDevices: List<AlertSummary> = listOf(needsWater, silentHub, silentNode, silentSpareNode, closedSilentHub)

    // Story 7.3: low batteries as `healthAlerts` lists them, with the level the Alert opened with.

    private fun lowBatteryOf(
        id: String,
        deviceId: String,
        openedAt: String,
        batteryPercent: Int?,
        lotName: String? = null,
        closedAt: String? = null,
    ) = AlertSummary(
        id = id,
        kind = AlertSummary.KIND_BATTERY,
        side = null,
        quantity = null,
        lotId = lotName?.let { "lot-$id" },
        lotName = lotName,
        deviceId = deviceId,
        openedAtEpochMs = at(openedAt),
        closedAtEpochMs = closedAt?.let(::at),
        deviceKind = AlertDeviceKind.Node,
        batteryPercent = batteryPercent,
    )

    /** A Node on Lettuce whose Alert opened at 14 %, not charging. */
    val lowBattery = lowBatteryOf("low", "9c0d1e2f3a4b5c68", "2026-10-09T06:45:00Z", 14, lotName = "Lettuce")

    /** A low Node on no Lot, named by its short name. */
    val lowBatterySpareNode = lowBatteryOf("low-spare", "7c19000000000004", "2026-10-09T06:15:00Z", 9)

    /** A low battery whose Alert states no level. */
    val lowBatteryNoLevel = lowBatteryOf("low-old", "4a5b6c7d8e9f0a1b", "2026-10-09T05:55:00Z", null, lotName = "Herbs")

    /** A low battery that recovered: the row names no level. */
    val closedLowBattery =
        lowBatteryOf(
            "was-low",
            "9c0d1e2f3a4b5c68",
            "2026-10-08T18:00:00Z",
            12,
            lotName = "Lettuce",
            closedAt = "2026-10-08T21:00:00Z",
        )

    /** The Alerts surface with low-battery Health rows: a Threshold Alert, the open ones, a closed one. */
    val lowBatteries: List<AlertSummary> =
        listOf(needsWater, lowBattery, lowBatterySpareNode, lowBatteryNoLevel, closedLowBattery)

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
