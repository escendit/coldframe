package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary

/**
 * A Hub of the current Site, as the Server listed it. [online] is the Server's, computed when it
 * answered; the client never computes it and never keeps it past a failed reload.
 * [lastSeenAtEpochMs] is the last accepted heartbeat, `null` for a Hub that never sent one.
 */
public data class HubSummary(
    val id: String,
    val online: Boolean,
    val lastSeenAtEpochMs: Long?,
)

/**
 * A Node of the current Site, as the Server listed it, in the Server's order (Lot name, unassigned
 * last, then Device ID). [lotName] is `null` for an unassigned Node; [batteryPercent], [charging]
 * and [lastSeenAtEpochMs] come from its newest device report and are `null` when unknown.
 */
public data class NodeSummary(
    val id: String,
    val lotId: String?,
    val lotName: String?,
    val batteryPercent: Int?,
    val batteryLow: Boolean,
    val charging: ChargeState?,
    val lastSeenAtEpochMs: Long?,
)

/**
 * A Lot of the Move picker (UX-DR31): a Lot that has another Node, or that the Node is on already,
 * is listed but cannot be picked. [hasNode] is the Server's `noNode` status turned around; the
 * Server still decides, and answers 409 for a Lot that was taken meanwhile.
 */
public data class MoveLotChoice(
    val id: String,
    val name: String,
    val hasNode: Boolean,
    val current: Boolean,
) {
    /** Only a free Lot, other than the Node's own, can be picked. */
    val selectable: Boolean get() = !hasNode && !current
}

/** Why a move or unassign did not happen. Each is inline copy on the Devices surface, naming the Node. */
public enum class NodeActionNotice {
    /** 403: the Role is too low (it may have changed since the list loaded); nothing changed. */
    Forbidden,

    /** 409 `lot-claimed`: the Lot got a Node meanwhile. Pick another Lot. */
    LotTaken,

    /** 404: the Node or the Lot is gone. */
    NotFound,

    /** No answer: nothing changed. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** Any other answer. */
    Unexpected,
}

/** The last move or unassign of [nodeId] that did not happen. */
public data class NodeActionFailure(
    val nodeId: String,
    val notice: NodeActionNotice,
)

/** Why the Devices could not be read. No row is shown with it, so nothing stays "Online". */
public enum class DevicesNotice(
    public val tryAgain: Boolean,
) {
    /** No answer, or an answer that is not the list: "Can't reach your Server." */
    Unreachable(true),

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate(false),
}

/** The Devices surface of the current Site. Follows the current Site of the Sites engine. */
public sealed interface DevicesState {
    /** Signed out, or no current Site. */
    public data object Idle : DevicesState

    /** The list is being read. No row is shown meanwhile: an earlier answer's "Online" is not kept. */
    public data class Loading(
        val site: SiteSummary,
    ) : DevicesState

    /** The list could not be read; no rows, [notice] in their place. */
    public data class Failed(
        val site: SiteSummary,
        val notice: DevicesNotice,
    ) : DevicesState

    /**
     * [hubs] are the Site's Hubs by Device ID, [nodes] its Nodes in the Server's order, never re-sorted.
     * [lots] are the Site's Lots in the Server's order, read only for a Role that may move Nodes.
     * [workingNodeId] is the Node whose move or unassign is under way; [failure] the last one that
     * did not happen.
     */
    public data class Ready(
        val site: SiteSummary,
        val hubs: List<HubSummary>,
        val nodes: List<NodeSummary> = emptyList(),
        val lots: List<LotOption> = emptyList(),
        val workingNodeId: String? = null,
        val failure: NodeActionFailure? = null,
    ) : DevicesState {
        /** The picker of [node]: every Lot in the Server's order, the ones that cannot be picked marked. */
        public fun choicesFor(node: NodeSummary): List<MoveLotChoice> =
            lots.map {
                MoveLotChoice(
                    it.id,
                    it.name,
                    hasNode = it.hasNode && it.id != node.lotId,
                    current =
                        it.id == node.lotId,
                )
            }
    }

    public companion object {
        /**
         * Add a Hub is for Owners and Administrators (UX-DR84): hidden for a Member, never disabled.
         */
        public fun canAddHub(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /**
         * Move a Node to another Lot and Unassign are for Owners and Administrators (UX-DR31) and need
         * no Bluetooth: hidden for a Member, never disabled.
         */
        public fun canManageNodes(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /** Add a Node follows the same rule: Owners and Administrators only, hidden for a Member. */
        public fun canAddNode(role: SiteRole): Boolean = canAddHub(role)
    }
}

/** A Lot of the Site as the Move picker reads it: [hasNode] is true unless the Server's status is `noNode`. */
public data class LotOption(
    val id: String,
    val name: String,
    val hasNode: Boolean,
)

/** The Site the surface is on, or `null` while [DevicesState.Idle]. */
public val DevicesState.site: SiteSummary?
    get() =
        when (this) {
            DevicesState.Idle -> null
            is DevicesState.Loading -> site
            is DevicesState.Failed -> site
            is DevicesState.Ready -> site
        }

/** Whether the Add a Hub header action shows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canAddHub: Boolean
    get() = site?.let { DevicesState.canAddHub(it.role) } == true

/** Whether the Add a Node header action shows: the same rule as [canAddHub]. */
public val DevicesState.canAddNode: Boolean
    get() = site?.let { DevicesState.canAddNode(it.role) } == true

/** Whether Move and Unassign show on Node rows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canManageNodes: Boolean
    get() = site?.let { DevicesState.canManageNodes(it.role) } == true
