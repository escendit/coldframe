package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DeviceListItemDto
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
                        DevicesState.Ready(latest, result.value.devices.toHubs())
                    }

                    is ApiResult.Failed -> {
                        failed(latest, result.failure)
                    }
                }
        }
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

        /** An ISO-8601 instant as Unix milliseconds, or `null` when it is not one. */
        internal fun epochMsOf(instant: String): Long? = Instant.parseOrNull(instant)?.toEpochMilliseconds()
    }
}
