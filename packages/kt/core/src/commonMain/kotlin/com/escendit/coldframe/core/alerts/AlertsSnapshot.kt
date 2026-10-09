package com.escendit.coldframe.core.alerts

/**
 * [AlertsState] flattened for Swift. [surface] is `idle`, `loading`, `failed` or `ready`; [notice]
 * is `unreachable` or `certificate`. [openCount] is the Server's count of open Alerts for the tab
 * label, 0 unless `ready`. No token or URL crosses this boundary.
 *
 * The Alerts are parallel lists, one entry per Alert in the Server's order (open newest first, then
 * closed newest first): [alertIds], [alertGroups] (`threshold`, `health` or `closed`),
 * [alertVariants] (`needsWater`, `threshold`, `health` or `closed`), [alertConditions]
 * (`needsWater`, `tooWet`, `tooLow`, `tooHigh`, `silent`, `battery`, `uncalibrated` or `unknown`),
 * [alertEyebrows] (`needsWater`, `belowLow`, `aboveHigh` or `health`), [alertIcons] (the Carbon icon
 * name: `rain-drop`, `arrow--down`, `arrow--up`, `help`, `battery--low` or `tools`),
 * [alertQuantities] (the contract's `SensorQuantity`, empty when this app does not know it),
 * [alertLotIds], [alertLotNames], [alertDeviceIds], [alertOpenedAt] (Unix milliseconds in decimal),
 * [alertClosedAt] (the same, empty while the Alert is open) and [alertTargets] (`lot` opens Lot
 * detail of the Alert's Lot, `devices` the Devices surface).
 */
public data class AlertsSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val siteName: String?,
    val openCount: Int,
    val refreshing: Boolean,
    val alertIds: List<String> = emptyList(),
    val alertGroups: List<String> = emptyList(),
    val alertVariants: List<String> = emptyList(),
    val alertConditions: List<String> = emptyList(),
    val alertEyebrows: List<String> = emptyList(),
    val alertIcons: List<String> = emptyList(),
    val alertQuantities: List<String> = emptyList(),
    val alertLotIds: List<String> = emptyList(),
    val alertLotNames: List<String> = emptyList(),
    val alertDeviceIds: List<String> = emptyList(),
    val alertOpenedAt: List<String> = emptyList(),
    val alertClosedAt: List<String> = emptyList(),
    val alertTargets: List<String> = emptyList(),
)

/** The snapshot of [state]. */
public fun snapshotOf(state: AlertsState): AlertsSnapshot {
    val surface =
        when (state) {
            AlertsState.Idle -> "idle"
            is AlertsState.Loading -> "loading"
            is AlertsState.Failed -> "failed"
            is AlertsState.Ready -> "ready"
        }
    val failed = state as? AlertsState.Failed
    val ready = state as? AlertsState.Ready
    val alerts = ready?.alerts.orEmpty()
    return AlertsSnapshot(
        surface = surface,
        notice = failed?.notice?.name?.replaceFirstChar { it.lowercase() },
        noticeTryAgain = failed?.notice?.tryAgain == true,
        siteId = state.site?.id,
        siteName = state.site?.name,
        openCount = state.openCount,
        refreshing = ready?.refreshing == true,
        alertIds = alerts.map { it.id },
        alertGroups = alerts.map { it.group.key },
        alertVariants = alerts.map { it.variant.key },
        alertConditions = alerts.map { it.condition.key },
        alertEyebrows = alerts.map { it.eyebrow.key },
        alertIcons = alerts.map { it.icon.key },
        alertQuantities = alerts.map { it.quantity?.key.orEmpty() },
        alertLotIds = alerts.map { it.lotId },
        alertLotNames = alerts.map { it.lotName },
        alertDeviceIds = alerts.map { it.deviceId },
        alertOpenedAt = alerts.map { it.openedAtEpochMs.toString() },
        alertClosedAt = alerts.map { it.closedAtEpochMs?.toString().orEmpty() },
        alertTargets = alerts.map { it.target.key },
    )
}
