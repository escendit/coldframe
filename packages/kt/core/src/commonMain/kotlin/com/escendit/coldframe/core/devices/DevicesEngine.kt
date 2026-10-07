package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DeviceListItemDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.SensorFormat
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlin.time.Instant

/** The Devices call; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface DevicesApi {
    public suspend fun listDevices(siteId: String): ApiResult<DeviceListDto>

    /** The Site's Lots, for the Move picker. */
    public suspend fun listLots(siteId: String): ApiResult<LotListDto>

    /** Moves a Node to another Lot (Admin+); 409 when the Lot has a Node. */
    public suspend fun moveDevice(
        siteId: String,
        deviceId: String,
        lotId: String,
    ): ApiResult<DeviceDto>

    /** Unassigns a Node from its Lot (Admin+). */
    public suspend fun unassignDevice(
        siteId: String,
        deviceId: String,
    ): ApiResult<DeviceDto>
}

/**
 * The Devices of the current Site (UX-DR30, UX-DR65). It follows [sites]: a new current Site
 * loads, no current Site is [DevicesState.Idle]. The shells call [load] every time the Devices
 * surface is entered; there is no polling. Every load drops the rows first and a failed one
 * leaves [DevicesState.Failed], so a Hub is never shown online from an earlier answer. Answers
 * for an earlier Site, session or load are dropped.
 */
public class DevicesEngine(
    private val api: DevicesApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
) {
    private val mutableState = MutableStateFlow<DevicesState>(DevicesState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<DevicesState> = mutableState.asStateFlow()

    /** Bumped by every load and when the current Site changes or goes away, so older answers are dropped. */
    private var generation = 0

    init {
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current }
                .distinctUntilChanged()
                .collect { follow(it) }
        }
    }

    private fun follow(site: SiteSummary?) {
        val current = mutableState.value
        if (site == null) {
            generation++
            mutableState.value = DevicesState.Idle
            return
        }
        if (site.id != current.site?.id) {
            fetch(site)
            return
        }
        // Same Site with a new name or Role: keep what is shown, with the Role's actions.
        mutableState.value =
            when (current) {
                is DevicesState.Ready -> current.copy(site = site)
                is DevicesState.Loading -> current.copy(site = site)
                is DevicesState.Failed -> current.copy(site = site)
                DevicesState.Idle -> current
            }
    }

    /**
     * Reads the list again: on every entry of the Devices surface, after Add a Hub closes, and for
     * Try again. A load already under way is left to finish.
     */
    public fun load() {
        when (val current = mutableState.value) {
            is DevicesState.Ready -> fetch(current.site)
            is DevicesState.Failed -> fetch(current.site)
            is DevicesState.Loading, DevicesState.Idle -> Unit
        }
    }

    private fun fetch(site: SiteSummary) {
        val started = ++generation
        mutableState.value = DevicesState.Loading(site)
        scope.launch {
            val result = api.listDevices(site.id)
            if (started != generation) return@launch
            val latest = mutableState.value.site ?: return@launch
            mutableState.value =
                when (result) {
                    is ApiResult.Ok -> {
                        ready(latest, result.value.devices, lotsFor(latest, result.value.devices))
                    }

                    is ApiResult.Failed -> {
                        failed(latest, result.failure)
                    }
                }
        }
    }

    /**
     * The Lots for the Move picker, read only for a Role that may move Nodes and only when the Site has
     * a Node. A Lot list that cannot be read leaves the picker empty; the Server decides every move anyway.
     */
    private suspend fun lotsFor(
        site: SiteSummary,
        devices: List<DeviceListItemDto>,
    ): List<LotOption> {
        if (!DevicesState.canManageNodes(site.role) || devices.none { it.kind == KIND_NODE }) return emptyList()
        return when (val result = api.listLots(site.id)) {
            is ApiResult.Ok -> {
                // The Server's order, never re-sorted (UX-DR20); removed Lots are not listed.
                result.value.lots
                    .filter { it.removed != true }
                    .map { LotOption(it.id, it.name, hasNode = LotStatus.fromServer(it.status) != LotStatus.NoNode) }
            }

            is ApiResult.Failed -> {
                emptyList()
            }
        }
    }

    private fun ready(
        site: SiteSummary,
        devices: List<DeviceListItemDto>,
        lots: List<LotOption>,
    ): DevicesState.Ready = DevicesState.Ready(site, devices.toHubs(), devices.toNodes(), lots)

    /**
     * Moves [nodeId] to [lotId] (UX-DR31). Only a [DevicesState.Ready] surface of a Role that may move
     * Nodes does anything: a Lot that cannot be picked is never sent. The list is read again after
     * the Server accepted, so the shells show its answer, not a guess; a refusal is a [NodeActionFailure]
     * and nothing else changes.
     */
    public fun moveNode(
        nodeId: String,
        lotId: String,
    ) {
        val current = mutableState.value as? DevicesState.Ready ?: return
        val node = current.nodes.firstOrNull { it.id == nodeId } ?: return
        if (!DevicesState.canManageNodes(current.site.role) || current.workingNodeId != null) return
        if (current.choicesFor(node).none { it.id == lotId && it.selectable }) {
            // Taken, or gone from the list, since the picker opened: say so instead of closing silently.
            mutableState.value = current.copy(failure = NodeActionFailure(nodeId, NodeActionNotice.LotTaken))
            return
        }
        act(current, nodeId) { api.moveDevice(current.site.id, nodeId, lotId) }
    }

    /** Unassigns [nodeId] from its Lot (UX-DR31); the shells confirm first, naming the Node. */
    public fun unassignNode(nodeId: String) {
        val current = mutableState.value as? DevicesState.Ready ?: return
        val node = current.nodes.firstOrNull { it.id == nodeId } ?: return
        if (!DevicesState.canManageNodes(current.site.role) || current.workingNodeId != null ||
            node.lotId == null
        ) {
            return
        }
        act(current, nodeId) { api.unassignDevice(current.site.id, nodeId) }
    }

    private fun act(
        before: DevicesState.Ready,
        nodeId: String,
        call: suspend () -> ApiResult<DeviceDto>,
    ) {
        val started = generation
        mutableState.value = before.copy(workingNodeId = nodeId, failure = null)
        scope.launch {
            val result = call()
            if (started != generation) return@launch
            val now = mutableState.value as? DevicesState.Ready ?: return@launch
            when (result) {
                is ApiResult.Ok -> {
                    mutableState.value = now.copy(workingNodeId = null, failure = null)
                    refresh(now.site)
                }

                is ApiResult.Failed -> {
                    if (result.failure == ApiFailure.Unauthorized) {
                        generation++
                        mutableState.value = DevicesState.Idle
                    } else {
                        mutableState.value =
                            now.copy(
                                workingNodeId = null,
                                failure = NodeActionFailure(nodeId, noticeOf(result.failure)),
                            )
                    }
                }
            }
        }
    }

    /** Reads the list again without dropping the rows first: the Server just accepted a change. */
    private fun refresh(site: SiteSummary) {
        val started = ++generation
        scope.launch {
            val result = api.listDevices(site.id)
            if (started != generation) return@launch
            val latest = mutableState.value.site ?: return@launch
            mutableState.value =
                when (result) {
                    is ApiResult.Ok -> ready(latest, result.value.devices, lotsFor(latest, result.value.devices))
                    is ApiResult.Failed -> failed(latest, result.failure)
                }
        }
    }

    private fun noticeOf(failure: ApiFailure): NodeActionNotice =
        when (failure) {
            ApiFailure.Forbidden -> NodeActionNotice.Forbidden
            ApiFailure.LotClaimed -> NodeActionNotice.LotTaken
            ApiFailure.NotFound -> NodeActionNotice.NotFound
            ApiFailure.Unreachable -> NodeActionNotice.Unreachable
            ApiFailure.Certificate -> NodeActionNotice.Certificate
            else -> NodeActionNotice.Unexpected
        }

    /** A 401 ended the session in the API, so the surface goes idle; anything else is a notice without rows. */
    private fun failed(
        site: SiteSummary,
        failure: ApiFailure,
    ): DevicesState =
        when (failure) {
            ApiFailure.Unauthorized -> DevicesState.Idle
            ApiFailure.Certificate -> DevicesState.Failed(site, DevicesNotice.Certificate)
            else -> DevicesState.Failed(site, DevicesNotice.Unreachable)
        }

    public companion object {
        /** The contract's `DeviceKind` of a Hub. */
        public const val KIND_HUB: String = "hub"

        /** The Hubs of the Server's list, by Device ID. `online` is passed through, never computed. */
        internal fun List<DeviceListItemDto>.toHubs(): List<HubSummary> =
            filter { it.kind == KIND_HUB }
                .sortedBy { it.id }
                .map { HubSummary(it.id, it.online, it.lastSeenAt?.let(::epochMsOf)) }

        /** The contract's `DeviceKind` of a Node. */
        public const val KIND_NODE: String = "node"

        /** The Nodes in the Server's order (Lot name, unassigned last, Device ID); no client sort. */
        internal fun List<DeviceListItemDto>.toNodes(): List<NodeSummary> =
            filter { it.kind == KIND_NODE }
                .map {
                    NodeSummary(
                        id = it.id,
                        lotId = it.lotId,
                        lotName = it.lotName,
                        batteryPercent = it.batteryPercent,
                        batteryLow = SensorFormat.batteryLow(it.batteryPercent),
                        charging = ChargeState.fromServer(it.charging),
                        lastSeenAtEpochMs = it.lastSeenAt?.let(::epochMsOf),
                    )
                }

        /** An ISO-8601 instant as Unix milliseconds, or `null` when it is not one. */
        internal fun epochMsOf(instant: String): Long? = Instant.parseOrNull(instant)?.toEpochMilliseconds()
    }
}
