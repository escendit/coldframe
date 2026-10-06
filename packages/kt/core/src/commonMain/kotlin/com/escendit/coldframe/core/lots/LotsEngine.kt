package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
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
import kotlin.time.Clock

/** The Lot calls of Site settings and Garden; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface LotsApi {
    public suspend fun listLots(siteId: String): ApiResult<LotListDto>

    public suspend fun createLot(
        siteId: String,
        name: String,
        idempotencyKey: String,
    ): ApiResult<LotDto>

    public suspend fun renameLot(
        siteId: String,
        lotId: String,
        name: String,
    ): ApiResult<LotDto>

    public suspend fun removeLot(
        siteId: String,
        lotId: String,
    ): ApiResult<Unit>
}

/**
 * The Lots of the current Site and the Site settings surface (UX-DR74, UX-DR84, UX-DR20). It
 * follows [sites]: a new current Site reloads, no current Site is [LotsState.Idle]. Every
 * change reloads the list afterwards, so the shells show the Server's order and status only.
 * Answers for an earlier Site or session are dropped.
 *
 * Stale mode (UX-DR79, UX-DR80) is decided here and is transport-only. A read that fails for a
 * transport reason ([ApiFailure.transport]) is tried once more. When both fail over shown Lots,
 * they stay with [StaleReason.Unreachable]; the first read that succeeds makes them live again.
 * The last good Lots of each Site are kept in [settings] and shown with [StaleReason.Cached]
 * until the first read for the Site lands; without them the Site is [LotsState.Loading], then
 * [LotsState.Failed] if the Server does not answer. There is no age threshold and no timer: the
 * shells call [refresh].
 *
 * Nothing else is served stale. A 403 or 404 drops the Site's kept Lots, and the load notice
 * replaces the Lots that were showing. A certificate failure always shows its notice. The kept
 * Lots of every Site are cleared when the session ends (sign-out, or a 401).
 */
public class LotsEngine(
    private val api: LotsApi,
    private val sites: SitesEngine,
    settings: Settings,
    private val scope: CoroutineScope,
    private val newKey: () -> String = SitesEngine::randomKey,
    /** Epoch milliseconds; stamps [LotsState.Ready.fetchedAtEpochMs]. */
    internal val now: () -> Long = { Clock.System.now().toEpochMilliseconds() },
) {
    private val cache = LotsCache(settings)
    private val mutableState = MutableStateFlow<LotsState>(LotsState.Idle)
    private val mutableEvents =
        MutableSharedFlow<LotsEvent>(extraBufferCapacity = EVENT_BUFFER, onBufferOverflow = BufferOverflow.DROP_OLDEST)

    /** One observable state for the shells. */
    public val state: StateFlow<LotsState> = mutableState.asStateFlow()

    /**
     * Entering and leaving stale mode, for a polite announcement (UX-DR106). Not replayed: an
     * event with no collector is dropped. A first load, a refresh that changes nothing and a
     * second failed refresh send nothing.
     */
    public val events: SharedFlow<LotsEvent> = mutableEvents.asSharedFlow()

    /** Bumped when the current Site changes or goes away, so older answers are dropped. */
    private var generation = 0

    /** Bumped by every read of the list; only the newest read's answer is applied. */
    private var reads = 0

    init {
        // In one step, so a read of the ended session that lands afterwards stores nothing.
        sites.onSessionEnded {
            generation++
            cache.clear()
        }
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current }
                .distinctUntilChanged()
                .collect { follow(it) }
        }
    }

    private fun follow(site: SiteSummary?) {
        val current = mutableState.value
        val shownId = current.site?.id
        if (site == null) {
            generation++
            mutableState.value = LotsState.Idle
            return
        }
        if (site.id != shownId) {
            generation++
            open(site)
            return
        }
        // Same Site with a new name or Role (a rename, or a reconciled Role): keep the list.
        mutableState.value =
            when (current) {
                is LotsState.Ready -> {
                    val draft = current.siteName
                    val renamedDraft =
                        if (!draft.working && draft.draft == current.site.name) draft.copy(draft = site.name) else draft
                    current.copy(site = site, siteName = renamedDraft)
                }

                is LotsState.Loading -> {
                    current.copy(site = site)
                }

                is LotsState.Failed -> {
                    current.copy(site = site)
                }

                LotsState.Idle -> {
                    current
                }
            }
    }

    /**
     * Reads the Lots again: pull-to-refresh, and every time the overview comes to the front.
     * Shown Lots stay while it runs ([LotsState.Ready.refreshing]); after a failed load it tries
     * again from [LotsState.Loading]. A read that fails twice puts shown Lots in stale mode.
     * Sites that came from this device are read again with it.
     */
    public fun refresh() {
        if ((sites.state.value as? SitesState.Ready)?.fromCache == true) sites.load()
        when (val current = mutableState.value) {
            is LotsState.Ready -> {
                read()
            }

            is LotsState.Failed -> {
                mutableState.value = LotsState.Loading(current.site)
                read()
            }

            is LotsState.Loading, LotsState.Idle -> {
                Unit
            }
        }
    }

    /** The same as [refresh]: Try again after a failed load, and the reload after a Node is assigned. */
    public fun load() {
        refresh()
    }

    /** A Site becomes current: its last good Lots in stale mode if there are any, then a read. */
    private fun open(site: SiteSummary) {
        val cached = cache.read(site.id)
        mutableState.value =
            if (cached == null) {
                LotsState.Loading(site)
            } else {
                ready(site, cached.lots.toSummaries(), cached.fetchedAt, StaleReason.Cached)
            }
        read()
    }

    /**
     * Reads the list, once more after a transport failure, and applies the answer unless a newer
     * read, another Site or a sign-out came meanwhile.
     */
    private fun read() {
        val siteId = mutableState.value.site?.id ?: return
        val started = generation
        val turn = ++reads
        updateReady { it.copy(refreshing = true) }
        scope.launch {
            var result = api.listLots(siteId)
            if (started != generation || turn != reads) return@launch
            if (result is ApiResult.Failed && result.failure.transport) {
                result = api.listLots(siteId)
                if (started != generation || turn != reads) return@launch
            }
            when (result) {
                is ApiResult.Ok -> {
                    landed(result.value.lots.filter { it.removed != true })
                }

                is ApiResult.Failed -> {
                    if (result.failure ==
                        ApiFailure.Unauthorized
                    ) {
                        signedOut()
                    } else {
                        missed(result.failure)
                    }
                }
            }
        }
    }

    /** A read succeeded: the Lots are live, and they are the new last good Lots of the Site. */
    private fun landed(lots: List<LotDto>) {
        val current = mutableState.value
        val site = current.site ?: return
        val fetchedAt = now()
        cache.store(site.id, lots, fetchedAt)
        mutableState.value =
            if (current is LotsState.Ready) {
                current.copy(
                    lots = lots.toSummaries(),
                    fetchedAtEpochMs = fetchedAt,
                    staleReason = null,
                    refreshing = false,
                )
            } else {
                ready(site, lots.toSummaries(), fetchedAt, staleReason = null)
            }
        if ((current as? LotsState.Ready)?.staleReason == StaleReason.Unreachable) {
            mutableEvents.tryEmit(LotsEvent.LeftStale(site.id))
        }
    }

    /** A read did not succeed. Only a transport failure leaves last good Lots on screen. */
    private fun missed(failure: ApiFailure) {
        val current = mutableState.value
        val site = current.site ?: return
        when {
            failure.transport -> unreachable(current, failure)

            // Never served stale, and never retried insecurely.
            failure == ApiFailure.Certificate -> mutableState.value = LotsState.Failed(site, SitesNotice.Certificate)

            else -> refused(site, failure)
        }
    }

    /** A read and its retry failed: shown Lots go stale; with nothing to show, the load notice. */
    private fun unreachable(
        current: LotsState,
        failure: ApiFailure,
    ) {
        when (current) {
            is LotsState.Ready -> {
                mutableState.value = current.copy(staleReason = StaleReason.Unreachable, refreshing = false)
                if (current.staleReason != StaleReason.Unreachable) {
                    mutableEvents.tryEmit(LotsEvent.EnteredStale(current.siteId, current.fetchedAtEpochMs))
                }
            }

            is LotsState.Loading -> {
                mutableState.value = LotsState.Failed(current.site, loadNoticeOf(failure))
            }

            is LotsState.Failed, LotsState.Idle -> {
                Unit
            }
        }
    }

    /**
     * The Server answered that the Site is not the caller's to read (403, 404): its kept Lots are
     * dropped and the load notice replaces whatever was shown, live or not.
     */
    private fun refused(
        site: SiteSummary,
        failure: ApiFailure,
    ) {
        cache.remove(site.id)
        mutableState.value = LotsState.Failed(site, loadNoticeOf(failure))
    }

    private fun ready(
        site: SiteSummary,
        lots: List<LotSummary>,
        fetchedAtEpochMs: Long,
        staleReason: StaleReason?,
    ): LotsState.Ready =
        LotsState.Ready(
            site = site,
            lots = lots,
            siteName = SiteNameForm(site.name, error = null, working = false),
            create = CreateLotForm("", error = null, working = false, idempotencyKey = newKey()),
            renaming = null,
            removing = null,
            notice = null,
            fetchedAtEpochMs = fetchedAtEpochMs,
            staleReason = staleReason,
            refreshing = false,
        )

    /** Reads the list again after a change, unless the Site changed meanwhile. */
    private fun refresh(started: Int) {
        if (started == generation) read()
    }

    // Rename Site (Owner)

    public fun setSiteName(name: String) {
        updateReady {
            if (it.siteName.working) {
                it
            } else {
                it.copy(
                    siteName = it.siteName.copy(draft = name, error = null),
                )
            }
        }
    }

    /** Rename Site. An invalid name shows its reason and sends nothing. Owner only. */
    public fun renameSite() {
        val ready = mutableState.value as? LotsState.Ready ?: return
        if (!ready.settings.canRenameSite || ready.siteName.working) return
        val error = CreateSiteForm.validate(ready.siteName.draft)
        if (error != null) {
            updateReady { it.copy(siteName = it.siteName.copy(error = error)) }
            return
        }
        val name = ready.siteName.draft.trim()
        updateReady { it.copy(siteName = it.siteName.copy(error = null, working = true), notice = null) }
        val started = generation
        scope.launch {
            val result = sites.renameSite(name)
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    updateReady { it.copy(siteName = SiteNameForm(result.value.name, error = null, working = false)) }
                }

                is ApiResult.Failed -> {
                    failed(
                        result.failure,
                        ready.site.name,
                        lotName = null,
                        sent = name,
                        renamingSite = true,
                    ) { state, error ->
                        state.copy(
                            siteName = state.siteName.copy(working = false, error = error ?: state.siteName.error),
                        )
                    }
                }
            }
        }
    }

    // Create Lot (Admin+)

    public fun setNewLotName(name: String) {
        updateReady { if (it.create.working) it else it.copy(create = it.create.copy(name = name, error = null)) }
    }

    /** Create Lot with the attempt's key. An invalid name shows its reason and sends nothing. */
    public fun createLot() {
        val ready = mutableState.value as? LotsState.Ready ?: return
        if (!ready.settings.canEditLots || ready.create.working) return
        val error = CreateSiteForm.validate(ready.create.name)
        if (error != null) {
            updateReady { it.copy(create = it.create.copy(error = error)) }
            return
        }
        val form = ready.create
        updateReady { it.copy(create = it.create.copy(error = null, working = true), notice = null) }
        val started = generation
        scope.launch {
            val result = api.createLot(ready.siteId, form.name.trim(), form.idempotencyKey)
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    updateReady {
                        it.copy(
                            create = CreateLotForm("", null, working = false, idempotencyKey = newKey()),
                        )
                    }
                    refresh(started)
                }

                is ApiResult.Failed -> {
                    failed(result.failure, ready.site.name, lotName = null, sent = form.name) { state, error ->
                        val key = if (result.failure == ApiFailure.KeyReused) newKey() else state.create.idempotencyKey
                        state.copy(
                            create =
                                state.create.copy(
                                    working = false,
                                    error = error ?: state.create.error,
                                    idempotencyKey = key,
                                ),
                        )
                    }
                }
            }
        }
    }

    // Rename Lot (Admin+)

    /** Opens Rename Lot with the Lot's current name. */
    public fun startRename(lotId: String) {
        val ready = mutableState.value as? LotsState.Ready ?: return
        if (!ready.settings.canEditLots || ready.renaming?.working == true) return
        val lot = ready.lots.firstOrNull { it.id == lotId } ?: return
        mutableState.value =
            ready.copy(renaming = RenameLotForm(lot.id, lot.name, error = null, working = false), notice = null)
    }

    public fun setRename(name: String) {
        updateReady { state ->
            val form = state.renaming
            if (form == null || form.working) state else state.copy(renaming = form.copy(draft = name, error = null))
        }
    }

    public fun cancelRename() {
        updateReady { if (it.renaming?.working == true) it else it.copy(renaming = null) }
    }

    /** Rename Lot. An invalid name shows its reason and sends nothing. */
    public fun rename() {
        val ready = mutableState.value as? LotsState.Ready ?: return
        val form = ready.renaming ?: return
        if (form.working) return
        val error = CreateSiteForm.validate(form.draft)
        if (error != null) {
            updateReady { it.copy(renaming = form.copy(error = error)) }
            return
        }
        updateReady { it.copy(renaming = form.copy(error = null, working = true), notice = null) }
        val started = generation
        scope.launch {
            val result = api.renameLot(ready.siteId, form.lotId, form.draft.trim())
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    updateReady { it.copy(renaming = null) }
                    refresh(started)
                }

                is ApiResult.Failed -> {
                    failed(result.failure, ready.site.name, lotName = null, sent = form.draft) { state, error ->
                        // A validation error stays in the dialog; any other outcome closes it.
                        val open = state.renaming
                        state.copy(
                            renaming =
                                if (error != null &&
                                    open != null
                                ) {
                                    open.copy(working = false, error = error)
                                } else {
                                    null
                                },
                        )
                    }
                    if (result.failure == ApiFailure.NotFound) refresh(started)
                }
            }
        }
    }

    // Remove Lot (Admin+)

    /** Asks "Remove Lot {lot}?"; nothing is sent until [confirmRemove]. */
    public fun askRemove(lotId: String) {
        val ready = mutableState.value as? LotsState.Ready ?: return
        if (!ready.settings.canEditLots || ready.removing?.working == true) return
        val lot = ready.lots.firstOrNull { it.id == lotId } ?: return
        mutableState.value =
            ready.copy(removing = RemoveLotConfirmation(lot.id, lot.name, working = false), notice = null)
    }

    public fun cancelRemove() {
        updateReady { if (it.removing?.working == true) it else it.copy(removing = null) }
    }

    /** Remove Lot. A Lot that holds a Node is refused by the Server and stays (409). */
    public fun confirmRemove() {
        val ready = mutableState.value as? LotsState.Ready ?: return
        val confirmation = ready.removing ?: return
        if (confirmation.working) return
        updateReady { it.copy(removing = confirmation.copy(working = true), notice = null) }
        val started = generation
        scope.launch {
            val result = api.removeLot(ready.siteId, confirmation.lotId)
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    updateReady { it.copy(removing = null) }
                    refresh(started)
                }

                is ApiResult.Failed -> {
                    failed(result.failure, ready.site.name, lotName = confirmation.lotName) { state, _ ->
                        state.copy(removing = null)
                    }
                    if (result.failure == ApiFailure.NotFound) refresh(started)
                }
            }
        }
    }

    /**
     * Applies a failed change: a 401 ends the session; a 400 becomes the field's reason (passed
     * to [reset]); anything else becomes one notice.
     */
    private fun failed(
        failure: ApiFailure,
        siteName: String,
        lotName: String?,
        sent: String? = null,
        renamingSite: Boolean = false,
        reset: (LotsState.Ready, NameError?) -> LotsState.Ready,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            signedOut()
            return
        }
        if (failure == ApiFailure.Validation) {
            val tooLong = (sent?.trim()?.length ?: 0) > CreateSiteForm.MAX_NAME_LENGTH
            updateReady { reset(it, if (tooLong) NameError.TooLong else NameError.Blank) }
            return
        }
        val notice =
            when (failure) {
                ApiFailure.Forbidden -> {
                    LotsNotice(LotsNoticeKind.Forbidden, siteName)
                }

                ApiFailure.LotClaimed -> {
                    LotsNotice(LotsNoticeKind.LotClaimed, lotName)
                }

                ApiFailure.NotFound -> {
                    LotsNotice(LotsNoticeKind.LotNotFound)
                }

                // Only renaming the Site waits on Keycloak; a 503 on a Lot action is unexpected.
                ApiFailure.IdentityProviderUnavailable -> {
                    LotsNotice(if (renamingSite) LotsNoticeKind.RenameSiteUnavailable else LotsNoticeKind.Unexpected)
                }

                ApiFailure.KeyReused -> {
                    LotsNotice(LotsNoticeKind.KeyReused)
                }

                ApiFailure.Unreachable -> {
                    LotsNotice(LotsNoticeKind.Unreachable)
                }

                ApiFailure.Certificate -> {
                    LotsNotice(LotsNoticeKind.Certificate)
                }

                ApiFailure.Validation,
                ApiFailure.Unauthorized,
                ApiFailure.DeviceOnAnotherSite,
                ApiFailure.DeviceAssigned,
                ApiFailure.Unexpected,
                -> {
                    LotsNotice(
                        LotsNoticeKind.Unexpected,
                    )
                }
            }
        updateReady { reset(it, null).copy(notice = notice) }
    }

    /** A 401: the session is over, and nothing kept on this device outlives it. */
    private fun signedOut() {
        generation++
        mutableState.value = LotsState.Idle
        sites.forget()
    }

    private fun updateReady(change: (LotsState.Ready) -> LotsState.Ready) {
        val ready = mutableState.value as? LotsState.Ready ?: return
        mutableState.value = change(ready)
    }

    private companion object {
        const val EVENT_BUFFER = 8

        fun List<LotDto>.toSummaries(): List<LotSummary> =
            // The Server's order, never re-sorted (UX-DR20); removed Lots are not listed.
            filter { it.removed != true }.map { it.toSummary() }

        /** Maps the Server's fields one to one; a value this client does not know is left out. */
        fun LotDto.toSummary(): LotSummary =
            LotSummary(
                id = id,
                name = name,
                status = LotStatus.fromServer(status),
                statusSinceEpochMs = statusSince?.let(DevicesEngine::epochMsOf),
                lastReadingAtEpochMs = lastReadingAt?.let(DevicesEngine::epochMsOf),
                unknownCause = LotUnknownCause.fromServer(unknownCause),
                pausedBy = pausedBy.orEmpty().mapNotNull(LotPauseSource::fromServer),
                pausedUntilEpochMs = pausedUntil?.let(DevicesEngine::epochMsOf),
                moisturePercent = moisturePercent,
                lowThresholdPercent = lowThresholdPercent,
            )

        fun loadNoticeOf(failure: ApiFailure): SitesNotice =
            when (failure) {
                ApiFailure.Unreachable -> SitesNotice.Unreachable
                ApiFailure.Certificate -> SitesNotice.Certificate
                else -> SitesNotice.Unexpected
            }
    }
}
