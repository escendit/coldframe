package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.devices.DeviceName
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.sites.SiteSummary

/*
 * The Alerts surface as both shells draw it (Story 6.2): the Alert row in its four variants, in
 * the groups Threshold, Health and Closed. Everything here is structured data over the Server's
 * fields; the shells own the string catalogues. Nothing decides whether an Alert is open, and
 * nothing re-sorts: the Server lists open Alerts newest first, then the closed ones. Threshold
 * Alerts come from the contract's `alerts`, Health Alerts from `healthAlerts` (Story 7.2).
 */

/** The Threshold a Threshold Alert crossed, as the contract's `AlertSide` names it. */
public enum class AlertSide(
    /** The contract value. */
    public val key: String,
) {
    Low("low"),
    High("high"),
    ;

    public companion object {
        public fun fromServer(value: String?): AlertSide? = entries.firstOrNull { it.key == value }
    }
}

/** The group a row is listed in: open Threshold Alerts, open Health Alerts, then the closed ones. */
public enum class AlertGroup(
    public val key: String,
) {
    Threshold("threshold"),
    Health("health"),
    Closed("closed"),
}

/**
 * How a row is drawn (DESIGN.md `alert-row-*`). [NeedsWater] is the only orange row: an open
 * low-side soil-moisture Threshold Alert. A closed Alert is always [Closed], whatever it was.
 */
public enum class AlertVariant(
    public val key: String,
) {
    NeedsWater("needsWater"),
    Threshold("threshold"),
    Health("health"),
    Closed("closed"),
}

/** What the row says is wrong; the shells turn it into the title and the screen-reader label. */
public enum class AlertCondition(
    public val key: String,
) {
    /** Soil moisture below the low: "<Lot> needs water". */
    NeedsWater("needsWater"),

    /** Soil moisture above the high: "<Lot> too wet". */
    TooWet("tooWet"),

    /** Another quantity below its low: "<Lot> <quantity> too low". */
    TooLow("tooLow"),

    /** Another quantity above its high: "<Lot> <quantity> too high". */
    TooHigh("tooHigh"),

    /** The Device, a Node or a Hub, is silent. */
    Silent("silent"),

    /** The Node's battery is low. */
    Battery("battery"),

    /** The soil Sensor needs Calibration. */
    Uncalibrated("uncalibrated"),

    /** A kind, side or quantity this app does not know: a neutral Health row that names the Lot or the Device. */
    Unknown("unknown"),
}

/** The eyebrow over the title; a closed row adds "closed <time>" to it. */
public enum class AlertEyebrow(
    public val key: String,
) {
    NeedsWater("needsWater"),
    BelowLow("belowLow"),
    AboveHigh("aboveHigh"),
    Health("health"),
}

/** The row's Carbon icon, by its name in the icon set. */
public enum class AlertIcon(
    public val key: String,
) {
    RainDrop("rain-drop"),
    ArrowDown("arrow--down"),
    ArrowUp("arrow--up"),
    Help("help"),
    BatteryLow("battery--low"),
    Tools("tools"),
}

/** Where a tap on the row leads: Lot detail of its Lot, or the Devices surface. */
public enum class AlertTarget(
    public val key: String,
) {
    Lot("lot"),
    Devices("devices"),
}

/** The Device a Health Alert is about, as the contract's `DeviceKind` names it. */
public enum class AlertDeviceKind(
    /** The contract value. */
    public val key: String,
) {
    Node("node"),
    Hub("hub"),
    ;

    public companion object {
        public fun fromServer(value: String?): AlertDeviceKind? = entries.firstOrNull { it.key == value }
    }
}

/** Which title sentence a row gets; the shells hold one catalogue entry per kind (two for the silent ones). */
public enum class AlertTitleKind(
    public val key: String,
) {
    /** "‹Lot› needs water". */
    NeedsWater("needsWater"),

    /** "‹Lot› too wet". */
    TooWet("tooWet"),

    /** "‹Lot› ‹quantity› too low". */
    TooLow("tooLow"),

    /** "‹Lot› ‹quantity› too high". */
    TooHigh("tooHigh"),

    /** "Node on Lot '‹Lot›' silent for ‹duration›"; without a duration "Node on Lot '‹Lot›' silent". */
    NodeOnLotSilent("nodeOnLotSilent"),

    /** A Node on no Lot: "Node ‹9C0D› silent for ‹duration›"; without a duration "Node ‹9C0D› silent". */
    NodeSilent("nodeSilent"),

    /** "Hub ‹3F2A› silent for ‹duration›"; without a duration "Hub ‹3F2A› silent". */
    HubSilent("hubSilent"),

    /** "Node battery low on Lot '‹Lot›'". */
    Battery("battery"),

    /** "Soil Sensor on Lot '‹Lot›' needs Calibration". */
    Uncalibrated("uncalibrated"),

    /** The neutral Health row with a Lot: "Health Alert on Lot '‹Lot›'". */
    HealthOnLot("healthOnLot"),

    /** The neutral Health row without a Lot: "Health Alert". */
    Health("health"),
}

/**
 * The inputs of a row's title, so Android and iOS say the same (Story 7.2). [lotName] is set for
 * the kinds that name a Lot, [deviceName] (the short name, "3F2A") for the kinds that name a Device.
 * [duration] is the silence of an open silent Alert whose silent-since the Server sent, rounded
 * down; `null` drops " for …" from the title (a closed row, or an Alert opened before the Server
 * recorded it).
 */
public data class AlertTitle(
    val kind: AlertTitleKind,
    val lotName: String? = null,
    val deviceName: String? = null,
    val duration: LotDuration? = null,
)

/**
 * An Alert of the current Site, as the Server listed it. [kind] is the contract's extensible
 * `AlertKind`, kept as sent; [side] and [quantity] are `null` when absent or not known to this app.
 * [closedAtEpochMs] is `null` while the Alert is open. The Alert carries no value and no Threshold.
 *
 * A Health Alert (Story 7.2) is about a Device: [deviceKind] says whether a Node or a Hub (`null`
 * for a Threshold Alert and for a kind this app does not know), [lotId] and [lotName] are `null`
 * for a Hub and for a Node on no Lot, and [silentSinceEpochMs] is when a silent Device was last
 * heard, `null` for another kind and for an Alert opened before the Server recorded it.
 */
public data class AlertSummary(
    val id: String,
    val kind: String,
    val side: AlertSide?,
    val quantity: SensorQuantity?,
    val lotId: String?,
    val lotName: String?,
    val deviceId: String,
    val openedAtEpochMs: Long,
    val closedAtEpochMs: Long?,
    val deviceKind: AlertDeviceKind? = null,
    val silentSinceEpochMs: Long? = null,
) {
    val open: Boolean get() = closedAtEpochMs == null

    /** "3F2A": the Device's short name, as in "Hub 3F2A". */
    val deviceName: String get() = DeviceName.short(deviceId)

    /**
     * What is wrong. A Threshold Alert is read only with a side and a quantity this app knows;
     * anything it cannot read is [AlertCondition.Unknown], never guessed.
     */
    val condition: AlertCondition
        get() =
            when (kind) {
                KIND_THRESHOLD -> thresholdCondition()
                KIND_SILENT -> AlertCondition.Silent
                KIND_BATTERY -> AlertCondition.Battery
                KIND_UNCALIBRATED -> AlertCondition.Uncalibrated
                else -> AlertCondition.Unknown
            }

    private fun thresholdCondition(): AlertCondition =
        when {
            side == null || quantity == null -> AlertCondition.Unknown
            quantity == SensorQuantity.SoilMoisture && side == AlertSide.Low -> AlertCondition.NeedsWater
            quantity == SensorQuantity.SoilMoisture -> AlertCondition.TooWet
            side == AlertSide.Low -> AlertCondition.TooLow
            else -> AlertCondition.TooHigh
        }

    /** Whether the row is a Threshold row: every other row is a Health row (UX-DR25). */
    private val threshold: Boolean
        get() =
            when (condition) {
                AlertCondition.NeedsWater, AlertCondition.TooWet, AlertCondition.TooLow, AlertCondition.TooHigh -> true
                else -> false
            }

    val group: AlertGroup
        get() =
            when {
                !open -> AlertGroup.Closed
                threshold -> AlertGroup.Threshold
                else -> AlertGroup.Health
            }

    /** Orange only for an open low-side soil-moisture Threshold Alert (UX-DR26). */
    val variant: AlertVariant
        get() =
            when {
                !open -> AlertVariant.Closed
                condition == AlertCondition.NeedsWater -> AlertVariant.NeedsWater
                threshold -> AlertVariant.Threshold
                else -> AlertVariant.Health
            }

    val eyebrow: AlertEyebrow
        get() =
            when (condition) {
                AlertCondition.NeedsWater -> AlertEyebrow.NeedsWater
                AlertCondition.TooLow -> AlertEyebrow.BelowLow
                AlertCondition.TooWet, AlertCondition.TooHigh -> AlertEyebrow.AboveHigh
                else -> AlertEyebrow.Health
            }

    val icon: AlertIcon
        get() =
            when (condition) {
                AlertCondition.NeedsWater -> AlertIcon.RainDrop
                AlertCondition.TooLow -> AlertIcon.ArrowDown
                AlertCondition.TooWet, AlertCondition.TooHigh -> AlertIcon.ArrowUp
                AlertCondition.Battery -> AlertIcon.BatteryLow
                AlertCondition.Uncalibrated -> AlertIcon.Tools
                AlertCondition.Silent, AlertCondition.Unknown -> AlertIcon.Help
            }

    /**
     * Threshold and uncalibrated rows open Lot detail of [lotId]; silent, battery and unknown rows open
     * Devices, and so does any row without a Lot.
     */
    val target: AlertTarget
        get() =
            when (condition) {
                AlertCondition.Silent, AlertCondition.Battery, AlertCondition.Unknown -> AlertTarget.Devices
                else -> if (lotId == null) AlertTarget.Devices else AlertTarget.Lot
            }

    /**
     * The title of the row at [nowEpochMs]. A silent Hub is named by its short name, a silent Node by
     * its Lot, or by its short name when it is on none. Anything the row cannot name that way (a kind
     * this app does not know, a Lot-bound kind without a Lot) gets the neutral Health title.
     */
    public fun title(nowEpochMs: Long): AlertTitle =
        when (condition) {
            AlertCondition.NeedsWater -> lotTitle(AlertTitleKind.NeedsWater)
            AlertCondition.TooWet -> lotTitle(AlertTitleKind.TooWet)
            AlertCondition.TooLow -> lotTitle(AlertTitleKind.TooLow)
            AlertCondition.TooHigh -> lotTitle(AlertTitleKind.TooHigh)
            AlertCondition.Silent -> silentTitle(nowEpochMs)
            AlertCondition.Battery -> lotTitle(AlertTitleKind.Battery)
            AlertCondition.Uncalibrated -> lotTitle(AlertTitleKind.Uncalibrated)
            AlertCondition.Unknown -> neutralTitle()
        }

    private fun lotTitle(kind: AlertTitleKind): AlertTitle =
        if (lotName == null) neutralTitle() else AlertTitle(kind, lotName = lotName)

    private fun silentTitle(nowEpochMs: Long): AlertTitle {
        // A closed row, and an Alert without a silent-since, say only that the Device is silent.
        val duration = silentSinceEpochMs?.takeIf { open }?.let { LotDuration.between(it, nowEpochMs) }
        return when {
            deviceKind == AlertDeviceKind.Hub -> {
                AlertTitle(AlertTitleKind.HubSilent, deviceName = deviceName, duration = duration)
            }

            // Also an Alert from before the Server sent a Device kind: with a Lot it was a Node's.
            lotName != null -> {
                AlertTitle(AlertTitleKind.NodeOnLotSilent, lotName = lotName, duration = duration)
            }

            deviceKind == AlertDeviceKind.Node -> {
                AlertTitle(AlertTitleKind.NodeSilent, deviceName = deviceName, duration = duration)
            }

            else -> {
                neutralTitle()
            }
        }
    }

    private fun neutralTitle(): AlertTitle =
        if (lotName == null) {
            AlertTitle(AlertTitleKind.Health)
        } else {
            AlertTitle(AlertTitleKind.HealthOnLot, lotName = lotName)
        }

    public companion object {
        /** The contract's `AlertKind` values; the set is extensible. */
        public const val KIND_THRESHOLD: String = "threshold"
        public const val KIND_SILENT: String = "silent"
        public const val KIND_BATTERY: String = "battery"
        public const val KIND_UNCALIBRATED: String = "uncalibrated"
    }
}

/** Why the Alerts could not be read. No row and no count is shown with it. */
public enum class AlertsNotice(
    public val tryAgain: Boolean,
) {
    /** No answer, or an answer that is not the list: "Can't reach your Server." */
    Unreachable(true),

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate(false),
}

/** The Alerts surface of the current Site. Follows the current Site of the Sites engine. */
public sealed interface AlertsState {
    /** Signed out, or no current Site. */
    public data object Idle : AlertsState

    /** The first read for the Site is on its way. */
    public data class Loading(
        val site: SiteSummary,
    ) : AlertsState

    /** The list could not be read; no rows, [notice] in their place. There is no stale mode. */
    public data class Failed(
        val site: SiteSummary,
        val notice: AlertsNotice,
    ) : AlertsState

    /**
     * [alerts] in the Server's order: the open Threshold Alerts newest first, the open Health Alerts
     * newest first, then the closed ones of both newest first. [openCount] is the Server's count of
     * open Alerts of the Site, Threshold and Health, shown on the Alerts tab. [refreshing] while a
     * read is on its way over shown rows.
     */
    public data class Ready(
        val site: SiteSummary,
        val alerts: List<AlertSummary>,
        val openCount: Int,
        val refreshing: Boolean = false,
    ) : AlertsState {
        /** Open Threshold Alerts, newest first. */
        val threshold: List<AlertSummary> get() = alerts.filter { it.group == AlertGroup.Threshold }

        /** Open Health Alerts, newest first. */
        val health: List<AlertSummary> get() = alerts.filter { it.group == AlertGroup.Health }

        /** Alerts closed in the last 7 days, newest first. */
        val closed: List<AlertSummary> get() = alerts.filter { it.group == AlertGroup.Closed }
    }
}

/** The Site the surface is on, or `null` while [AlertsState.Idle]. */
public val AlertsState.site: SiteSummary?
    get() =
        when (this) {
            AlertsState.Idle -> null
            is AlertsState.Loading -> site
            is AlertsState.Failed -> site
            is AlertsState.Ready -> site
        }

/** The count on the Alerts tab: the Server's open count, 0 until it is known and after a failed read. */
public val AlertsState.openCount: Int
    get() = (this as? AlertsState.Ready)?.openCount ?: 0
