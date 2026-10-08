package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.api.LotHistoryDto
import com.escendit.coldframe.core.api.SensorThresholdsDto
import com.escendit.coldframe.core.lots.LotsEngine.Companion.toSummary
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import com.russhwolf.settings.Settings
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlin.math.roundToInt
import kotlin.time.Clock

/** The Lot detail calls; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface LotDetailApi {
    /** `getLot`: the Lot, with `node` and `sensors` while it holds a Node. */
    public suspend fun getLot(
        siteId: String,
        lotId: String,
    ): ApiResult<LotDto>

    /** `getLotHistory`: one page of daily history of [quantity] (a contract value), from [cursor] on. */
    public suspend fun getLotHistory(
        siteId: String,
        lotId: String,
        quantity: String,
        cursor: String?,
    ): ApiResult<LotHistoryDto>

    /** `getSensorThresholds` (Member): the soil Sensor's Thresholds for the chart band; a failed read draws no high line. */
    public suspend fun getSensorThresholds(
        siteId: String,
        sensorId: String,
    ): ApiResult<SensorThresholdsDto> = ApiResult.Failed(ApiFailure.Unexpected)
}

/** The history of one quantity as read: [unit] and the [days] of all pages, ascending. */
public data class LotHistory(
    val quantity: SensorQuantity,
    val unit: SensorUnit,
    val days: List<LotHistoryDayDto>,
)

/**
 * The soil Sensor's Thresholds in whole percent, read with the Member-readable `getSensorThresholds` for the chart band
 * (Story 5.4); [lowPercent] and [highPercent] are `null` for a side with none.
 */
public data class SoilThresholds(
    val sensorId: String,
    val lowPercent: Int?,
    val highPercent: Int?,
)

/** Why Lot detail could not be read and there is nothing to show. */
public enum class LotDetailNotice(
    public val tryAgain: Boolean,
) {
    /** 404: the Lot was removed meanwhile, or is not on this Site. */
    NotFound(false),

    /** 403. */
    Forbidden(false),
    Unreachable(true),
    Certificate(false),
    Unexpected(true),
}

/** Lot detail of the current Site. */
public sealed interface LotDetailState {
    /** No Lot is open. */
    public data object Idle : LotDetailState

    /** The first read of [lotId] with no last good data: the name and an outline, no values. */
    public data class Loading(
        val site: SiteSummary,
        val lotId: String,
        val name: String,
    ) : LotDetailState

    public data class Failed(
        val site: SiteSummary,
        val lotId: String,
        val name: String,
        val notice: LotDetailNotice,
    ) : LotDetailState

    /**
     * [lot] is the Server's Lot with its Node and Sensors; [history] the daily history of each
     * quantity read so far in this visit, [picked] the one the chart shows. [fetchedAtEpochMs] is
     * the last successful read; [staleReason] is set while the data is not live (transport only).
     */
    public data class Ready(
        val site: SiteSummary,
        val lot: LotSummary,
        val picked: SensorQuantity?,
        val history: Map<SensorQuantity, LotHistory>,
        val historyUnavailable: Boolean,
        val fetchedAtEpochMs: Long,
        val staleReason: StaleReason?,
        val refreshing: Boolean,
        /** The soil Sensor's Thresholds for the chart band; `null` until read, and when the read failed. */
        val soilThresholds: SoilThresholds? = null,
    ) : LotDetailState {
        val stale: Boolean get() = staleReason != null
    }
}

/**
 * Lot detail (UX-DR63): one Lot of the current Site, its Sensors and Node, and the history of the
 * picked quantity. The shells call [open] on entering and [refresh] on foreground or pull; there
 * is no polling. Stale mode follows [LotsEngine]: a read that fails for a transport reason is
 * tried once more, then the last good detail stays, stale, with no live value drawn; the first
 * success makes it live. The last good detail of each Lot is kept in settings
 * (`lotDetail.lastGood.‹siteId›.‹lotId›`) for a cold start, dropped on 403/404 and cleared when
 * the session ends. Nothing here converts, aggregates or derives a status.
 */
public class LotDetailEngine(
    private val api: LotDetailApi,
    private val sites: SitesEngine,
    settings: Settings,
    private val scope: CoroutineScope,
    internal val now: () -> Long = { Clock.System.now().toEpochMilliseconds() },
) {
    private val cache = LotDetailCache(settings)
    private val mutableState = MutableStateFlow<LotDetailState>(LotDetailState.Idle)
    private val mutableEvents =
        MutableSharedFlow<LotsEvent>(
            extraBufferCapacity = LotsEngine.EVENT_BUFFER,
            onBufferOverflow = BufferOverflow.DROP_OLDEST,
        )

    public val state: StateFlow<LotDetailState> = mutableState.asStateFlow()

    /** Entering and leaving stale mode, as [LotsEngine.events]. */
    public val events: SharedFlow<LotsEvent> = mutableEvents.asSharedFlow()

    private var generation = 0
    private var reads = 0

    init {
        sites.onSessionEnded {
            generation++
            mutableState.value = LotDetailState.Idle
            cache.clear()
        }
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current?.id }
                .distinctUntilChanged()
                .collect {
                    generation++
                    mutableState.value = LotDetailState.Idle
                }
        }
    }

    /** Opens [lotId], shown under [name] until the Server's answer arrives. */
    public fun open(
        lotId: String,
        name: String,
    ) {
        val site = (sites.state.value as? SitesState.Ready)?.current ?: return
        generation++
        val cached = cache.read(site.id, lotId)
        mutableState.value =
            if (cached == null) {
                LotDetailState.Loading(site, lotId, name)
            } else {
                val lot = cached.lot.toSummary()
                val histories = cached.history.mapNotNull { it.toLotHistory() }.associateBy { it.quantity }
                LotDetailState.Ready(
                    site = site,
                    lot = lot,
                    picked = defaultQuantity(lot),
                    history = histories,
                    historyUnavailable = false,
                    fetchedAtEpochMs = cached.fetchedAt,
                    staleReason = StaleReason.Cached,
                    refreshing = false,
                )
            }
        read()
    }

    /** Leaves Lot detail. */
    public fun close() {
        generation++
        mutableState.value = LotDetailState.Idle
    }

    /** Pull-to-refresh and every foreground; after a failed first read it starts again. */
    public fun refresh() {
        when (val current = mutableState.value) {
            is LotDetailState.Failed -> {
                mutableState.value = LotDetailState.Loading(current.site, current.lotId, current.name)
                read()
            }

            is LotDetailState.Ready -> {
                read()
            }

            is LotDetailState.Loading, LotDetailState.Idle -> {
                Unit
            }
        }
    }

    /** Shows the history of [quantity], reading it once per visit. */
    public fun pick(quantity: SensorQuantity) {
        val ready = mutableState.value as? LotDetailState.Ready ?: return
        if (quantity !in ready.lot.sensors.map { it.quantity }) return
        mutableState.value = ready.copy(picked = quantity, historyUnavailable = false)
        if (quantity !in ready.history) loadHistory(ready.site.id, ready.lot.id, quantity, generation)
    }

    private fun lotIdOf(state: LotDetailState): Pair<String, String>? =
        when (state) {
            is LotDetailState.Loading -> state.site.id to state.lotId
            is LotDetailState.Failed -> state.site.id to state.lotId
            is LotDetailState.Ready -> state.site.id to state.lot.id
            LotDetailState.Idle -> null
        }

    private fun read() {
        val (siteId, lotId) = lotIdOf(mutableState.value) ?: return
        val started = generation
        val turn = ++reads
        (mutableState.value as? LotDetailState.Ready)?.let { mutableState.value = it.copy(refreshing = true) }
        scope.launch {
            var result = api.getLot(siteId, lotId)
            if (started != generation || turn != reads) return@launch
            if (result is ApiResult.Failed && result.failure.transport) {
                result = api.getLot(siteId, lotId)
                if (started != generation || turn != reads) return@launch
            }
            when (result) {
                is ApiResult.Ok -> landed(siteId, result.value, started)
                is ApiResult.Failed -> missed(siteId, lotId, result.failure)
            }
        }
    }

    private fun landed(
        siteId: String,
        dto: LotDto,
        started: Int,
    ) {
        val current = mutableState.value
        val site = (current as? LotDetailState.Ready)?.site ?: lotSite(current) ?: return
        val lot = dto.toSummary()
        val fetchedAt = now()
        val kept = (current as? LotDetailState.Ready)?.takeIf { it.lot.id == lot.id }
        val quantities = lot.sensors.map { it.quantity }
        val picked = kept?.picked?.takeIf { it in quantities } ?: defaultQuantity(lot)
        // Only the picked quantity is kept: the others are outdated after a refresh, so the next pick reads them again.
        val history = kept?.history.orEmpty().filterKeys { it == picked }
        mutableState.value =
            LotDetailState.Ready(
                site = site,
                lot = lot,
                picked = picked,
                history = history,
                historyUnavailable = false,
                fetchedAtEpochMs = fetchedAt,
                staleReason = null,
                refreshing = false,
                soilThresholds = kept?.soilThresholds,
            )
        cache.store(siteId, dto, history.values.map { it.toDto() }, fetchedAt)
        if (kept?.staleReason == StaleReason.Unreachable) mutableEvents.tryEmit(LotsEvent.LeftStale(siteId))
        // History is a daily aggregate that moves on every Reading, so it is read again with the Lot.
        if (picked != null) loadHistory(siteId, lot.id, picked, started)
        lot.sensors
            .firstOrNull { it.quantity == SensorQuantity.SoilMoisture && it.sensorId != null }
            ?.sensorId
            ?.let { loadThresholds(siteId, it, started) }
    }

    // The band's high line needs the Thresholds; a failed read leaves the chart without it, never the page.
    private fun loadThresholds(
        siteId: String,
        sensorId: String,
        started: Int,
    ) {
        scope.launch {
            val result = api.getSensorThresholds(siteId, sensorId)
            if (started != generation) return@launch
            val current = mutableState.value as? LotDetailState.Ready ?: return@launch
            val dto = (result as? ApiResult.Ok)?.value ?: return@launch
            if (SensorUnit.fromServer(dto.unit) != SensorUnit.Percent) return@launch
            mutableState.value =
                current.copy(
                    soilThresholds =
                        SoilThresholds(
                            sensorId,
                            dto.low.value?.roundToInt(),
                            dto.high.value?.roundToInt(),
                        ),
                )
        }
    }

    private fun lotSite(state: LotDetailState): SiteSummary? =
        when (state) {
            is LotDetailState.Loading -> state.site
            is LotDetailState.Failed -> state.site
            is LotDetailState.Ready -> state.site
            LotDetailState.Idle -> null
        }

    private fun missed(
        siteId: String,
        lotId: String,
        failure: ApiFailure,
    ) {
        val current = mutableState.value
        when {
            failure == ApiFailure.Unauthorized -> {
                generation++
                mutableState.value = LotDetailState.Idle
                sites.forget()
            }

            failure.transport -> {
                unreachable(current, failure)
            }

            else -> {
                cache.remove(siteId, lotId)
                val site = lotSite(current) ?: return
                val name = (current as? LotDetailState.Ready)?.lot?.name ?: nameOf(current)
                mutableState.value = LotDetailState.Failed(site, lotId, name, noticeOf(failure))
            }
        }
    }

    private fun nameOf(state: LotDetailState): String =
        when (state) {
            is LotDetailState.Loading -> state.name
            is LotDetailState.Failed -> state.name
            is LotDetailState.Ready -> state.lot.name
            LotDetailState.Idle -> ""
        }

    private fun unreachable(
        current: LotDetailState,
        failure: ApiFailure,
    ) {
        when (current) {
            is LotDetailState.Ready -> {
                mutableState.value = current.copy(staleReason = StaleReason.Unreachable, refreshing = false)
                if (current.staleReason != StaleReason.Unreachable) {
                    mutableEvents.tryEmit(LotsEvent.EnteredStale(current.site.id, current.fetchedAtEpochMs))
                }
            }

            is LotDetailState.Loading -> {
                mutableState.value =
                    LotDetailState.Failed(current.site, current.lotId, current.name, noticeOf(failure))
            }

            is LotDetailState.Failed, LotDetailState.Idle -> {
                Unit
            }
        }
    }

    private fun loadHistory(
        siteId: String,
        lotId: String,
        quantity: SensorQuantity,
        started: Int,
    ) {
        scope.launch {
            val days = mutableListOf<LotHistoryDayDto>()
            var unit = ""
            var cursor: String? = null
            var pages = 0
            do {
                val result = api.getLotHistory(siteId, lotId, quantity.key, cursor)
                if (started != generation) return@launch
                when (result) {
                    is ApiResult.Ok -> {
                        unit = result.value.unit
                        days += result.value.days
                        cursor = result.value.nextCursor
                    }

                    is ApiResult.Failed -> {
                        val current = mutableState.value as? LotDetailState.Ready ?: return@launch
                        if (current.picked == quantity) mutableState.value = current.copy(historyUnavailable = true)
                        return@launch
                    }
                }
            } while (cursor != null && ++pages < MAX_PAGES)
            val current = mutableState.value as? LotDetailState.Ready ?: return@launch
            val knownUnit = SensorUnit.fromServer(unit) ?: return@launch
            val history = current.history + (quantity to LotHistory(quantity, knownUnit, days))
            mutableState.value = current.copy(history = history, historyUnavailable = false)
            // Keep the cached copy in step, so a cold start has the chart too.
            cache.updateHistory(siteId, lotId, history.values.map { it.toDto() })
        }
    }

    private companion object {
        const val MAX_PAGES = 4

        /** Soil moisture first when the Lot has it, else the Server's first Sensor. */
        fun defaultQuantity(lot: LotSummary): SensorQuantity? {
            val quantities = lot.sensors.map { it.quantity }
            return if (SensorQuantity.SoilMoisture in
                quantities
            ) {
                SensorQuantity.SoilMoisture
            } else {
                quantities.firstOrNull()
            }
        }

        fun noticeOf(failure: ApiFailure): LotDetailNotice =
            when (failure) {
                ApiFailure.NotFound -> LotDetailNotice.NotFound
                ApiFailure.Forbidden -> LotDetailNotice.Forbidden
                ApiFailure.Certificate -> LotDetailNotice.Certificate
                ApiFailure.Unreachable -> LotDetailNotice.Unreachable
                else -> LotDetailNotice.Unexpected
            }

        fun LotHistoryDto.toLotHistory(): LotHistory? {
            val q = SensorQuantity.fromServer(quantity) ?: return null
            val u = SensorUnit.fromServer(unit) ?: return null
            return LotHistory(q, u, days)
        }

        fun LotHistory.toDto(): LotHistoryDto = LotHistoryDto(quantity.key, unit.key, days)
    }
}

/** The last good Lot detail per Lot, on this device only, with the histories read for it. */
@Serializable
internal data class CachedLotDetail(
    val fetchedAt: Long,
    val lot: LotDto,
    val history: List<LotHistoryDto>,
)

internal class LotDetailCache(
    private val settings: Settings,
) {
    fun read(
        siteId: String,
        lotId: String,
    ): CachedLotDetail? {
        val key = keyOf(siteId, lotId)
        val stored = settings.getStringOrNull(key) ?: return null
        return try {
            JSON.decodeFromString(CachedLotDetail.serializer(), stored)
        } catch (_: SerializationException) {
            settings.remove(key)
            null
        } catch (_: IllegalArgumentException) {
            settings.remove(key)
            null
        }
    }

    fun store(
        siteId: String,
        lot: LotDto,
        history: List<LotHistoryDto>,
        fetchedAtEpochMs: Long,
    ) {
        settings.putString(
            keyOf(siteId, lot.id),
            JSON.encodeToString(CachedLotDetail.serializer(), CachedLotDetail(fetchedAtEpochMs, lot, history)),
        )
    }

    fun updateHistory(
        siteId: String,
        lotId: String,
        history: List<LotHistoryDto>,
    ) {
        val cached = read(siteId, lotId) ?: return
        store(siteId, cached.lot, history, cached.fetchedAt)
    }

    fun remove(
        siteId: String,
        lotId: String,
    ) {
        settings.remove(keyOf(siteId, lotId))
    }

    fun clear() {
        settings.keys.filter { it.startsWith(KEY_PREFIX) }.forEach { settings.remove(it) }
    }

    companion object {
        const val KEY_PREFIX: String = "lotDetail.lastGood."

        private val JSON = Json { ignoreUnknownKeys = true }

        fun keyOf(
            siteId: String,
            lotId: String,
        ): String = "$KEY_PREFIX$siteId.$lotId"
    }
}
