package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesNotice
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

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
 */
public class LotsEngine(
    private val api: LotsApi,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
    private val newKey: () -> String = SitesEngine::randomKey,
) {
    private val mutableState = MutableStateFlow<LotsState>(LotsState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<LotsState> = mutableState.asStateFlow()

    /** Bumped when the current Site changes or goes away, so older answers are dropped. */
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
        val shownId = current.site()?.id
        if (site == null) {
            generation++
            mutableState.value = LotsState.Idle
            return
        }
        if (site.id != shownId) {
            generation++
            fetch(site)
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

    /** Try again after a failed load; on a shown list, reads it again in place. */
    public fun load() {
        when (val current = mutableState.value) {
            is LotsState.Ready -> refresh(generation)
            is LotsState.Failed -> fetch(current.site)
            is LotsState.Loading, LotsState.Idle -> Unit
        }
    }

    private fun fetch(site: SiteSummary) {
        val started = generation
        mutableState.value = LotsState.Loading(site)
        scope.launch {
            val result = api.listLots(site.id)
            if (started != generation) return@launch
            val latest = mutableState.value.site() ?: return@launch
            mutableState.value =
                when (result) {
                    is ApiResult.Ok -> {
                        LotsState.Ready(
                            site = latest,
                            lots = result.value.lots.toSummaries(),
                            siteName = SiteNameForm(latest.name, error = null, working = false),
                            create = CreateLotForm("", error = null, working = false, idempotencyKey = newKey()),
                            renaming = null,
                            removing = null,
                            notice = null,
                        )
                    }

                    is ApiResult.Failed -> {
                        if (result.failure == ApiFailure.Unauthorized) {
                            LotsState.Idle
                        } else {
                            LotsState.Failed(latest, loadNoticeOf(result.failure))
                        }
                    }
                }
        }
    }

    /** Reads the list again after a change; a failed read keeps the list that is shown. */
    private fun refresh(started: Int) {
        val ready = mutableState.value as? LotsState.Ready ?: return
        scope.launch {
            val result = api.listLots(ready.siteId)
            if (started != generation) return@launch
            when (result) {
                is ApiResult.Ok -> updateReady { it.copy(lots = result.value.lots.toSummaries()) }
                is ApiResult.Failed -> if (result.failure == ApiFailure.Unauthorized) signedOut()
            }
        }
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

    private fun signedOut() {
        generation++
        mutableState.value = LotsState.Idle
    }

    private fun updateReady(change: (LotsState.Ready) -> LotsState.Ready) {
        val ready = mutableState.value as? LotsState.Ready ?: return
        mutableState.value = change(ready)
    }

    private companion object {
        fun LotsState.site(): SiteSummary? =
            when (this) {
                is LotsState.Ready -> site
                is LotsState.Loading -> site
                is LotsState.Failed -> site
                LotsState.Idle -> null
            }

        fun List<LotDto>.toSummaries(): List<LotSummary> =
            // The Server's order, never re-sorted (UX-DR20); removed Lots are not listed.
            filter { it.removed != true }.map { LotSummary(it.id, it.name, LotStatus.fromServer(it.status)) }

        fun loadNoticeOf(failure: ApiFailure): SitesNotice =
            when (failure) {
                ApiFailure.Unreachable -> SitesNotice.Unreachable
                ApiFailure.Certificate -> SitesNotice.Certificate
                else -> SitesNotice.Unexpected
            }
    }
}
