package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.lots.LotDuration

/**
 * [DevicesState] flattened for Swift. Enum values cross as catalogue key suffixes
 * (`unreachable`, `certificate`) and the Hubs as parallel string lists by Device ID: [hubStatuses]
 * holds `online` or `offline` exactly as the Server said, [hubLastSeen] the last heartbeat as
 * Unix milliseconds in decimal, or an empty string for a Hub that never sent one. No token or
 * URL crosses this boundary.
 *
 * Nodes, one entry per Node in the Server's order (Lot name, unassigned last): [nodeIds],
 * [nodeLotNames] (empty for an unassigned Node), [nodeBatteries] (percent in decimal, empty when
 * unknown), [nodeBatteryLow] (below 20 %), [nodeCharging] (`charging`, `notCharging` or empty) and
 * [nodeLastSeen] (Unix milliseconds in decimal, empty when unknown).
 *
 * Move and Unassign (UX-DR31): [canManageNodes] is the Role rule (Administrator and up). [nodeLotIds]
 * (empty for an unassigned Node) and the Lot picker as parallel lists in the Server's order, [lotIds],
 * [lotNames] and [lotHasNode] (another Node holds it). A Node's own Lot, and a Lot with a Node, cannot
 * be picked. [workingNodeId] is the Node being moved or unassigned (empty when none); [failedNodeId]
 * and [actionNotice] (`forbidden`, `lotTaken`, `notFound`, `unreachable`, `certificate` or
 * `unexpected`) the last action that did not happen (both empty when none).
 *
 * Silence (Story 7.2), measured at the `now` the snapshot was taken at. Per Hub ([hubShortNames] is
 * "3F2A" of "Hub 3F2A") and per Node ([nodeShortNames]), parallel to the ID lists: `…SilentSince`
 * (Unix milliseconds in decimal, empty unless the Server says the Device is silent),
 * `…SilenceValues` with `…SilenceUnits` (the silence rounded down, "6" with `hours`; units are
 * `minutes`, `hours` or `days`; both empty unless silent), `…WindowSeconds` (the Silence Window in
 * force in decimal, empty when the Server sent none), `…WindowValues` with `…WindowUnits` (the same
 * as a number with a unit, "8" with `hours`) and `…WindowOverridden` (the Device has its own window,
 * so "Use default" is offered). [canSetSilenceWindow] is the Role rule (Administrator and up): the
 * control does not exist for a Member. [silenceWindowWorkingId] is the Device whose window is being
 * saved (empty when none); [silenceWindowFailedId] and [silenceWindowNotice] (`forbidden`,
 * `notFound`, `outOfRange`, `unreachable`, `certificate` or `unexpected`) the last change that did
 * not happen (both empty when none). [silenceWindowMinSeconds] and [silenceWindowMaxSeconds] are the
 * range, 1 min to 30 days.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`.
 */
public data class DevicesSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val siteName: String?,
    val canAddHub: Boolean,
    val canAddNode: Boolean,
    val hubIds: List<String>,
    val hubStatuses: List<String>,
    val hubLastSeen: List<String>,
    val nodeIds: List<String> = emptyList(),
    val nodeLotNames: List<String> = emptyList(),
    val nodeBatteries: List<String> = emptyList(),
    val nodeBatteryLow: List<Boolean> = emptyList(),
    val nodeCharging: List<String> = emptyList(),
    val nodeLastSeen: List<String> = emptyList(),
    val canManageNodes: Boolean = false,
    val nodeLotIds: List<String> = emptyList(),
    val lotIds: List<String> = emptyList(),
    val lotNames: List<String> = emptyList(),
    val lotHasNode: List<Boolean> = emptyList(),
    val workingNodeId: String = "",
    val failedNodeId: String = "",
    val actionNotice: String = "",
    val hubShortNames: List<String> = emptyList(),
    val hubSilentSince: List<String> = emptyList(),
    val hubSilenceValues: List<String> = emptyList(),
    val hubSilenceUnits: List<String> = emptyList(),
    val hubWindowSeconds: List<String> = emptyList(),
    val hubWindowValues: List<String> = emptyList(),
    val hubWindowUnits: List<String> = emptyList(),
    val hubWindowOverridden: List<Boolean> = emptyList(),
    val nodeShortNames: List<String> = emptyList(),
    val nodeSilentSince: List<String> = emptyList(),
    val nodeSilenceValues: List<String> = emptyList(),
    val nodeSilenceUnits: List<String> = emptyList(),
    val nodeWindowSeconds: List<String> = emptyList(),
    val nodeWindowValues: List<String> = emptyList(),
    val nodeWindowUnits: List<String> = emptyList(),
    val nodeWindowOverridden: List<Boolean> = emptyList(),
    val canSetSilenceWindow: Boolean = false,
    val silenceWindowWorkingId: String = "",
    val silenceWindowFailedId: String = "",
    val silenceWindowNotice: String = "",
    val silenceWindowMinSeconds: Int = SilenceWindow.MIN_SECONDS,
    val silenceWindowMaxSeconds: Int = SilenceWindow.MAX_SECONDS,
) {
    public companion object {
        public const val ONLINE: String = "online"
        public const val OFFLINE: String = "offline"
    }
}

private fun LotDuration?.valueText(): String = this?.value?.toString().orEmpty()

private fun LotDuration?.unitText(): String =
    this
        ?.unit
        ?.name
        ?.lowercase()
        .orEmpty()

/** The snapshot of [state], with the silence of silent Devices measured at [nowEpochMs]. */
public fun snapshotOf(
    state: DevicesState,
    nowEpochMs: Long,
): DevicesSnapshot {
    val surface =
        when (state) {
            DevicesState.Idle -> "idle"
            is DevicesState.Loading -> "loading"
            is DevicesState.Failed -> "failed"
            is DevicesState.Ready -> "ready"
        }
    val failed = state as? DevicesState.Failed
    val hubs = (state as? DevicesState.Ready)?.hubs.orEmpty()
    val nodes = (state as? DevicesState.Ready)?.nodes.orEmpty()
    val lots = (state as? DevicesState.Ready)?.lots.orEmpty()
    val failure = (state as? DevicesState.Ready)?.failure
    val windowFailure = (state as? DevicesState.Ready)?.silenceWindowFailure
    return DevicesSnapshot(
        surface = surface,
        notice = failed?.notice?.name?.replaceFirstChar { it.lowercase() },
        noticeTryAgain = failed?.notice?.tryAgain == true,
        siteId = state.site?.id,
        siteName = state.site?.name,
        canAddHub = state.canAddHub,
        canAddNode = state.canAddNode,
        hubIds = hubs.map { it.id },
        hubStatuses = hubs.map { if (it.online) DevicesSnapshot.ONLINE else DevicesSnapshot.OFFLINE },
        hubLastSeen = hubs.map { it.lastSeenAtEpochMs?.toString().orEmpty() },
        nodeIds = nodes.map { it.id },
        nodeLotNames = nodes.map { it.lotName.orEmpty() },
        nodeBatteries = nodes.map { it.batteryPercent?.toString().orEmpty() },
        nodeBatteryLow = nodes.map { it.batteryLow },
        nodeCharging = nodes.map { it.charging?.key.orEmpty() },
        nodeLastSeen = nodes.map { it.lastSeenAtEpochMs?.toString().orEmpty() },
        canManageNodes = state.canManageNodes,
        nodeLotIds = nodes.map { it.lotId.orEmpty() },
        lotIds = lots.map { it.id },
        lotNames = lots.map { it.name },
        lotHasNode = lots.map { it.hasNode },
        workingNodeId = (state as? DevicesState.Ready)?.workingNodeId.orEmpty(),
        failedNodeId = failure?.nodeId.orEmpty(),
        actionNotice =
            failure
                ?.notice
                ?.name
                ?.replaceFirstChar { it.lowercase() }
                .orEmpty(),
        hubShortNames = hubs.map { it.shortName },
        hubSilentSince = hubs.map { it.silentSinceEpochMs?.toString().orEmpty() },
        hubSilenceValues = hubs.map { it.silence(nowEpochMs).valueText() },
        hubSilenceUnits = hubs.map { it.silence(nowEpochMs).unitText() },
        hubWindowSeconds = hubs.map { it.silenceWindowSeconds?.toString().orEmpty() },
        hubWindowValues =
            hubs.map {
                it.silenceWindow
                    ?.value
                    ?.toString()
                    .orEmpty()
            },
        hubWindowUnits =
            hubs.map {
                it.silenceWindow
                    ?.unit
                    ?.key
                    .orEmpty()
            },
        hubWindowOverridden = hubs.map { it.silenceWindowOverridden },
        nodeShortNames = nodes.map { it.shortName },
        nodeSilentSince = nodes.map { it.silentSinceEpochMs?.toString().orEmpty() },
        nodeSilenceValues = nodes.map { it.silence(nowEpochMs).valueText() },
        nodeSilenceUnits = nodes.map { it.silence(nowEpochMs).unitText() },
        nodeWindowSeconds = nodes.map { it.silenceWindowSeconds?.toString().orEmpty() },
        nodeWindowValues =
            nodes.map {
                it.silenceWindow
                    ?.value
                    ?.toString()
                    .orEmpty()
            },
        nodeWindowUnits =
            nodes.map {
                it.silenceWindow
                    ?.unit
                    ?.key
                    .orEmpty()
            },
        nodeWindowOverridden = nodes.map { it.silenceWindowOverridden },
        canSetSilenceWindow = state.canSetSilenceWindow,
        silenceWindowWorkingId = (state as? DevicesState.Ready)?.silenceWindowWorkingId.orEmpty(),
        silenceWindowFailedId = windowFailure?.deviceId.orEmpty(),
        silenceWindowNotice =
            windowFailure
                ?.notice
                ?.name
                ?.replaceFirstChar { it.lowercase() }
                .orEmpty(),
    )
}
