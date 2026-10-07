package com.escendit.coldframe.core.devices

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
) {
    public companion object {
        public const val ONLINE: String = "online"
        public const val OFFLINE: String = "offline"
    }
}

/** The snapshot of [state]. */
public fun snapshotOf(state: DevicesState): DevicesSnapshot {
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
    )
}
