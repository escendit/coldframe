package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.lots.SensorQuantity
import com.escendit.coldframe.core.sites.SiteSummary

/*
 * The Alerts surface as both shells draw it (Story 6.2): the Alert row in its four variants, in
 * the groups Threshold, Health and Closed. Everything here is structured data over the Server's
 * fields; the shells own the string catalogues. Nothing decides whether an Alert is open, and
 * nothing re-sorts: the Server lists open Alerts newest first, then the closed ones.
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

    /** The Node is silent. */
    Silent("silent"),

    /** The Node's battery is low. */
    Battery("battery"),

    /** The soil Sensor needs Calibration. */
    Uncalibrated("uncalibrated"),

    /** A kind, side or quantity this app does not know: a Health row that names only the Lot. */
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

/**
 * An Alert of the current Site, as the Server listed it. [kind] is the contract's extensible
 * `AlertKind`, kept as sent; [side] and [quantity] are `null` when absent or not known to this app.
 * [closedAtEpochMs] is `null` while the Alert is open. The Alert carries no value and no Threshold.
 */
public data class AlertSummary(
    val id: String,
    val kind: String,
    val side: AlertSide?,
    val quantity: SensorQuantity?,
    val lotId: String,
    val lotName: String,
    val deviceId: String,
    val openedAtEpochMs: Long,
    val closedAtEpochMs: Long?,
) {
    val open: Boolean get() = closedAtEpochMs == null

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

    /** Threshold and uncalibrated rows open Lot detail of [lotId]; silent, battery and unknown rows open Devices. */
    val target: AlertTarget
        get() =
            when (condition) {
                AlertCondition.Silent, AlertCondition.Battery, AlertCondition.Unknown -> AlertTarget.Devices
                else -> AlertTarget.Lot
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
     * [alerts] in the Server's order: open newest first, then closed newest first. [openCount] is
     * the Server's count of open Alerts of the Site, shown on the Alerts tab. [refreshing] while a
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
