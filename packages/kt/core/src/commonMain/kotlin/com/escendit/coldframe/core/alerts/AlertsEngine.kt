package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.api.AlertDto
import com.escendit.coldframe.core.api.AlertListDto
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.lots.SensorQuantity
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

/** The Alerts call; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface AlertsApi {
    /** One page of the Site's Alerts; [cursor] is the `nextCursor` of the page before, unchanged. */
    public suspend fun listAlerts(
        siteId: String,
        cursor: String?,
    ): ApiResult<AlertListDto>
}

/**
 * The Alerts of the current Site (UX-DR25, UX-DR98). It follows [sites]: a Site that becomes
 * current is read at once, so the Alerts tab carries its count before the tab is opened; no
 * current Site is [AlertsState.Idle]. The shells call [load] when the Alerts surface is entered
 * and [refresh] for pull-to-refresh and whenever the app comes to the front; there is no polling.
 *
 * Every read follows `nextCursor` to the end, at most [MAX_PAGES] pages; a read that stops there
 * with a cursor still left shows the Alerts of the pages it read, without a sign that more follow.
 * Shown rows stay while a read runs. There is no stale mode: a read that fails, on any page, leaves [AlertsState.Failed]
 * without rows and without a count. Answers for an earlier Site, session or read are dropped.
 */
public class AlertsEngine(
    private val api: AlertsApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
) {
    private val mutableState = MutableStateFlow<AlertsState>(AlertsState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<AlertsState> = mutableState.asStateFlow()

    /** Bumped by every read and when the current Site changes or goes away, so older answers are dropped. */
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
            mutableState.value = AlertsState.Idle
            return
        }
        if (site.id != current.site?.id) {
            mutableState.value = AlertsState.Loading(site)
            read()
            return
        }
        // Same Site with a new name or Role: keep what is shown.
        mutableState.value =
            when (current) {
                is AlertsState.Ready -> current.copy(site = site)
                is AlertsState.Loading -> current.copy(site = site)
                is AlertsState.Failed -> current.copy(site = site)
                AlertsState.Idle -> current
            }
    }

    /**
     * Reads the Alerts again: pull-to-refresh, and every time the app comes to the front. Shown
     * rows and the count stay while it runs ([AlertsState.Ready.refreshing]); after a failed load
     * it tries again from [AlertsState.Loading]. A first load already under way is left to finish.
     */
    public fun refresh() {
        when (val current = mutableState.value) {
            is AlertsState.Ready -> {
                mutableState.value = current.copy(refreshing = true)
                read()
            }

            is AlertsState.Failed -> {
                mutableState.value = AlertsState.Loading(current.site)
                read()
            }

            is AlertsState.Loading, AlertsState.Idle -> {
                Unit
            }
        }
    }

    /** The same as [refresh]: every entry of the Alerts surface, and Try again after a failed load. */
    public fun load() {
        refresh()
    }

    private fun read() {
        val siteId = mutableState.value.site?.id ?: return
        val started = ++generation
        scope.launch {
            val alerts = mutableListOf<AlertDto>()
            var openCount = 0
            var cursor: String? = null
            var pages = 0
            do {
                val result = api.listAlerts(siteId, cursor)
                if (started != generation) return@launch
                when (result) {
                    is ApiResult.Ok -> {
                        alerts += result.value.alerts
                        // The same on every page; the last page's is the newest.
                        openCount = result.value.openCount
                        cursor = result.value.nextCursor
                    }

                    is ApiResult.Failed -> {
                        val latest = mutableState.value.site ?: return@launch
                        generation++
                        mutableState.value = failed(latest, result.failure)
                        return@launch
                    }
                }
            } while (cursor != null && ++pages < MAX_PAGES)
            val latest = mutableState.value.site ?: return@launch
            mutableState.value = AlertsState.Ready(latest, alerts.toSummaries(), openCount)
        }
    }

    /** A 401 ended the session in the API, so the surface goes idle; anything else is a notice without rows. */
    private fun failed(
        site: SiteSummary,
        failure: ApiFailure,
    ): AlertsState =
        when (failure) {
            ApiFailure.Unauthorized -> AlertsState.Idle
            ApiFailure.Certificate -> AlertsState.Failed(site, AlertsNotice.Certificate)
            else -> AlertsState.Failed(site, AlertsNotice.Unreachable)
        }

    public companion object {
        /** The most pages one read follows: 20 pages of the Server's default 50 Alerts. */
        public const val MAX_PAGES: Int = 20

        /**
         * The Server's list in its order, each Alert once. An Alert without a readable `openedAt`
         * cannot say when it started and is left out; so is one whose `closedAt` is present but
         * unreadable, which would otherwise be shown as open.
         */
        internal fun List<AlertDto>.toSummaries(): List<AlertSummary> =
            distinctBy { it.id }.mapNotNull { alert ->
                val openedAt = epochMsOf(alert.openedAt) ?: return@mapNotNull null
                val closedAt = alert.closedAt?.let { epochMsOf(it) ?: return@mapNotNull null }
                AlertSummary(
                    id = alert.id,
                    kind = alert.kind,
                    side = AlertSide.fromServer(alert.side),
                    quantity = SensorQuantity.fromServer(alert.quantity),
                    lotId = alert.lotId,
                    lotName = alert.lotName,
                    deviceId = alert.deviceId,
                    openedAtEpochMs = openedAt,
                    closedAtEpochMs = closedAt,
                )
            }

        /** An ISO-8601 instant as Unix milliseconds, or `null` when it is not one. */
        internal fun epochMsOf(instant: String): Long? = Instant.parseOrNull(instant)?.toEpochMilliseconds()
    }
}
