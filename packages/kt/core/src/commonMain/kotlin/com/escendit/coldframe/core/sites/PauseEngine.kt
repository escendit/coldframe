package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SitePauseDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Pause and Resume of one surface (UX-DR46, UX-DR70): the Pause sheet and the one-tap Resume, for
 * the current Site of [sites] or one of its Devices. The Lots, Lot detail and Devices engines each
 * own one (`pause`), so a sheet and a notice belong to the surface they were opened on.
 *
 * Only an Administrator or Owner does anything here; for a Member every call is ignored, as the
 * controls do not exist. One request runs at a time.
 *
 * - **The sheet.** [openForSite] (the Site menu) offers Whole Site only; [openForDevice] (Lot detail,
 *   a Devices row) offers This Device, selected, and Whole Site. [save] sends the picked calendar
 *   date as it is, or no date. Accepted, the sheet closes. Refused, it stays open with what was
 *   entered and says why ([PauseForm.notice]): a 400 on a Pause that carried a date marks the date
 *   field; a 400 on anything else is [PauseNotice.Unexpected].
 * - **Resume** is one call, without a sheet: [resumeSite], [resumeDevice]. One that did not happen
 *   is [PauseState.failure]; [retry] sends it again.
 * - **Afterwards** nothing is guessed: the Server's answer about the Site is taken over, the Sites
 *   are read again, and the Lots, Lot detail and Devices read their lists again
 *   ([SitesEngine.onPauseChanged]). A 404 reads them again too, for the scope that was asked: the
 *   Device is gone ([PauseNotice.NotFound]), or the Site is ([PauseNotice.SiteGone]).
 *
 * A change of the current Site or the end of the session closes the sheet and drops what was under
 * way; a 401 ends the session in the API.
 */
public class PauseEngine internal constructor(
    private val api: PauseApi?,
    private val sites: SitesEngine,
    private val scope: CoroutineScope,
) {
    private val mutableState = MutableStateFlow(PauseState())

    /** One observable state for the shells. */
    public val state: StateFlow<PauseState> = mutableState.asStateFlow()

    /** Bumped when the current Site changes or the session ends, so older answers are dropped. */
    private var generation = 0

    init {
        sites.onSessionEnded { reset() }
        scope.launch {
            sites.state
                .map { (it as? SitesState.Ready)?.current?.id }
                .distinctUntilChanged()
                .collect { reset() }
        }
    }

    /** Drops the sheet, a Resume under way and the last failure; an answer still on its way is ignored. */
    internal fun reset() {
        generation++
        mutableState.value = PauseState()
    }

    /** The current Site, when the caller may pause on it. */
    private fun site(): SiteSummary? =
        (sites.state.value as? SitesState.Ready)?.current?.takeIf { api != null && DevicePause.allowed(it.role) }

    /** Pause from the Site menu: the sheet with Whole Site as its only scope. */
    public fun openForSite() {
        val site = site() ?: return
        if (mutableState.value.working) return
        mutableState.value = PauseState(sheet = PauseForm(site.name, deviceId = null, scope = PauseScope.Site))
    }

    /** Pause from Lot detail or a Devices row: the sheet with This Device selected, and Whole Site. */
    public fun openForDevice(deviceId: String) {
        val site = site() ?: return
        if (mutableState.value.working || deviceId.isBlank()) return
        mutableState.value = PauseState(sheet = PauseForm(site.name, deviceId, scope = PauseScope.Device))
    }

    /** Picks a scope the sheet offers. Ignored while working. */
    public fun setScope(picked: PauseScope) {
        editSheet { if (picked in it.scopes) it.copy(scope = picked) else it }
    }

    /**
     * The "Until" date as the User picked it, `yyyy-MM-dd`; `null` or an empty text clears it. A text
     * that is no calendar date is ignored. Editing the date takes its mark away.
     */
    public fun setUntil(date: String?) {
        if (date.isNullOrBlank()) {
            editSheet { it.copy(until = null, notice = it.notice.takeUnless { n -> n == PauseNotice.DateRefused }) }
            return
        }
        val parsed = PauseDate.parse(date.trim()) ?: return
        editSheet { it.copy(until = parsed.iso, notice = it.notice.takeUnless { n -> n == PauseNotice.DateRefused }) }
    }

    /** The "Until" date as numbers, for a native date picker. A date the calendar does not have is ignored. */
    public fun setUntil(
        year: Int,
        month: Int,
        day: Int,
    ) {
        PauseDate.of(year, month, day)?.let { setUntil(it.iso) }
    }

    /** Cancel: closes the sheet and sends nothing. Ignored while working. */
    public fun cancel() {
        val current = mutableState.value
        if (current.sheet == null || current.sheet.working) return
        mutableState.value = current.copy(sheet = null)
    }

    private fun editSheet(change: (PauseForm) -> PauseForm) {
        val current = mutableState.value
        val sheet = current.sheet ?: return
        if (sheet.working) return
        mutableState.value = current.copy(sheet = change(sheet))
    }

    /** The primary button, and Try again in the sheet: sends the Pause the sheet describes. */
    public fun save() {
        val api = api ?: return
        val site = site() ?: return
        val current = mutableState.value
        val sheet = current.sheet ?: return
        if (current.working) return
        val deviceId = sheet.deviceId
        mutableState.value = current.copy(sheet = sheet.copy(working = true, notice = null))
        val started = generation
        scope.launch {
            val failure =
                if (sheet.scope == PauseScope.Device && deviceId != null) {
                    failureOf(api.pauseDevice(site.id, deviceId, sheet.until))
                } else {
                    sitePaused(api.pauseSite(site.id, sheet.until), site.id, started)
                }
            if (started != generation) return@launch
            val now = mutableState.value
            when (failure) {
                null -> {
                    mutableState.value = now.copy(sheet = null)
                    changed(sheet.scope, started)
                }

                ApiFailure.Unauthorized -> {
                    reset()
                }

                else -> {
                    val notice = noticeOf(failure, sheet.scope, dated = sheet.until != null)
                    mutableState.value = now.copy(sheet = now.sheet?.copy(working = false, notice = notice))
                    if (failure == ApiFailure.NotFound) changed(sheet.scope, started)
                }
            }
        }
    }

    /** Resume from the Site menu: one tap, no sheet. */
    public fun resumeSite() {
        resume(PauseTarget(PauseScope.Site))
    }

    /** Resume from Lot detail or a Devices row: lifts the Device's own Pause. */
    public fun resumeDevice(deviceId: String) {
        if (deviceId.isBlank()) return
        resume(PauseTarget(PauseScope.Device, deviceId))
    }

    /** Try again after a Resume that did not happen, when its notice offers it. */
    public fun retry() {
        val failure = mutableState.value.failure ?: return
        if (failure.notice.tryAgain) resume(failure.target)
    }

    private fun resume(target: PauseTarget) {
        val api = api ?: return
        val site = site() ?: return
        val current = mutableState.value
        if (current.working) return
        val deviceId = target.deviceId
        mutableState.value = current.copy(resuming = target, failure = null)
        val started = generation
        scope.launch {
            val failure =
                if (target.scope == PauseScope.Device && deviceId != null) {
                    failureOf(api.resumeDevice(site.id, deviceId))
                } else {
                    sitePaused(api.resumeSite(site.id), site.id, started)
                }
            if (started != generation) return@launch
            val now = mutableState.value
            when (failure) {
                null -> {
                    mutableState.value = now.copy(resuming = null, failure = null)
                    changed(target.scope, started)
                }

                ApiFailure.Unauthorized -> {
                    reset()
                }

                else -> {
                    val notice = noticeOf(failure, target.scope, dated = false)
                    mutableState.value = now.copy(resuming = null, failure = PauseFailure(target, notice, site.name))
                    if (failure == ApiFailure.NotFound) changed(target.scope, started)
                }
            }
        }
    }

    /** The Server's answer about the Site is what the Sites show from now on; `null` when it accepted. */
    private fun sitePaused(
        result: ApiResult<SitePauseDto>,
        siteId: String,
        started: Int,
    ): ApiFailure? =
        when (result) {
            is ApiResult.Ok -> {
                if (started == generation) sites.pauseAnswered(siteId, result.value)
                null
            }

            is ApiResult.Failed -> {
                result.failure
            }
        }

    private fun failureOf(result: ApiResult<*>): ApiFailure? = (result as? ApiResult.Failed)?.failure

    /** The lists read again what the Server holds now. */
    private fun changed(
        changed: PauseScope,
        started: Int,
    ) {
        if (started != generation) return
        if (changed == PauseScope.Site) sites.refresh()
        sites.pauseChanged()
    }

    private companion object {
        /**
         * The only thing a pause request can get wrong is its date: a 400 is the date's only when the
         * request was [dated], which a Resume never is. A 404 names what was asked for.
         */
        fun noticeOf(
            failure: ApiFailure,
            asked: PauseScope,
            dated: Boolean,
        ): PauseNotice =
            when (failure) {
                ApiFailure.Validation -> if (dated) PauseNotice.DateRefused else PauseNotice.Unexpected
                ApiFailure.Forbidden -> PauseNotice.Forbidden
                ApiFailure.NotFound -> if (asked == PauseScope.Site) PauseNotice.SiteGone else PauseNotice.NotFound
                ApiFailure.Unreachable -> PauseNotice.Unreachable
                ApiFailure.Certificate -> PauseNotice.Certificate
                else -> PauseNotice.Unexpected
            }
    }
}
