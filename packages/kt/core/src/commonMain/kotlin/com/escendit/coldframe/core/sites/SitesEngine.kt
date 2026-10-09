package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.escendit.coldframe.core.signin.SignInState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlin.uuid.ExperimentalUuidApi
import kotlin.uuid.Uuid

/** The Server calls the Sites surfaces need; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface SitesApi {
    public suspend fun listSites(): ApiResult<SiteListDto>

    public suspend fun createSite(
        name: String,
        idempotencyKey: String,
    ): ApiResult<SiteDto>

    public suspend fun renameSite(
        siteId: String,
        name: String,
    ): ApiResult<SiteDto>
}

/**
 * The Sites state of the signed-in user: which Sites they hold, the current one, and Create Site
 * (UX-DR61, UX-DR23). Loads when the session becomes [SignInState.SignedIn] and goes back
 * to [SitesState.Idle] when it ends. Shells only render [state] and call the actions.
 *
 * The last good Sites are kept on this device (UX-DR80). On sign-in they show at once with
 * [SitesState.Ready.fromCache] while the read runs, and they stay when the read and its one
 * retry fail for a transport reason, so the overview can show its last good Lots in stale mode.
 * A certificate failure or any other answer shows its notice instead. When the session ends
 * (sign-out, or a 401) the kept Sites are cleared and every listener of [onSessionEnded] is run.
 */
public class SitesEngine(
    private val api: SitesApi,
    private val choices: DeviceChoices,
    private val scope: CoroutineScope,
    signIn: StateFlow<SignInState>,
    private val detectTimeZone: () -> String = ::detectedTimeZone,
    private val newKey: () -> String = ::randomKey,
    private val zones: () -> List<String> = ::availableTimeZones,
) {
    private val mutableState = MutableStateFlow<SitesState>(SitesState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<SitesState> = mutableState.asStateFlow()

    /** Bumped when the session ends, so answers to an earlier session are dropped. */
    private var session = 0

    /** The session a read of the Sites is running for, so a second one does not start. */
    private var loadingSession = NOT_LOADING

    private val cache = SitesCache(choices.settings)
    private val sessionEnded = mutableListOf<() -> Unit>()

    /** The zone the User chose, as the Server holds it; Create Site names it instead of asking again. */
    private var serverTimeZone: String? = null
    private var timeZoneChosen: ((String) -> Unit)? = null

    init {
        scope.launch {
            // A sign-out clears what this device kept; a start that is still restoring does not.
            launch { signIn.collect { if (it is SignInState.SignedOut) forget() } }
            signIn.map { it is SignInState.SignedIn }.distinctUntilChanged().collect { signedIn ->
                session++
                loadingSession = NOT_LOADING
                if (signedIn) {
                    cachedState()?.let { mutableState.value = it }
                    load()
                } else {
                    mutableState.value = SitesState.Idle
                }
            }
        }
    }

    /**
     * Reads the Sites; also Try again after [SitesState.Failed]. Sites shown from this device
     * stay while it runs. A transport failure is tried once more.
     */
    public fun load() {
        if (loadingSession == session) return
        val started = session
        loadingSession = started
        if ((mutableState.value as? SitesState.Ready)?.fromCache != true) mutableState.value = SitesState.Loading
        scope.launch {
            var result = api.listSites()
            if (started != session) return@launch
            if (result is ApiResult.Failed && result.failure.transport) {
                result = api.listSites()
                if (started != session) return@launch
            }
            loadingSession = NOT_LOADING
            when (result) {
                is ApiResult.Ok -> {
                    cache.store(result.value.sites)
                    val shown = mutableState.value as? SitesState.Ready
                    val next = stateFor(result.value.sites.map { it.toSummary() }, preferred = choices.currentSiteId)
                    // Create Site opened over the kept Sites stays open.
                    mutableState.value =
                        if (next is SitesState.Ready && shown != null) next.copy(creating = shown.creating) else next
                }

                is ApiResult.Failed -> {
                    missed(result.failure)
                }
            }
        }
    }

    private fun missed(failure: ApiFailure) {
        if (failure == ApiFailure.Unauthorized) {
            forget()
            mutableState.value = SitesState.Idle
            return
        }
        val shown = mutableState.value as? SitesState.Ready
        mutableState.value =
            when {
                !failure.transport -> SitesState.Failed(noticeOf(failure))
                shown?.fromCache == true -> shown
                else -> cachedState() ?: SitesState.Failed(noticeOf(failure))
            }
    }

    /** The last good Sites of this device as the shown state, or `null` without any. */
    private fun cachedState(): SitesState.Ready? {
        val sites = cache.read() ?: return null
        val state = stateFor(sites.map { it.toSummary() }, preferred = choices.currentSiteId) as? SitesState.Ready
        return state?.copy(fromCache = true)
    }

    /**
     * Runs [action] whenever the session ends (sign-out, or a 401), after the kept Sites are
     * cleared. The Lots engine clears its last good Lots with it, so nothing one user saw is
     * shown to the next on the same device.
     */
    internal fun onSessionEnded(action: () -> Unit) {
        sessionEnded += action
    }

    /**
     * Runs [action] with every zone confirmed or picked on Create Site. The notification settings
     * engine sends it to the Server as the User's choice (Story 6.3, DW-23).
     */
    internal fun onTimeZoneChosen(action: (String) -> Unit) {
        timeZoneChosen = action
    }

    /**
     * The zone the User chose, as the Server holds it (`null` while none is chosen). Create Site
     * shows it as chosen from now on; the Server's zone replaces what an open form shows.
     */
    internal fun timeZoneFromServer(zoneId: String?) {
        serverTimeZone = zoneId
        if (zoneId != null) {
            updateForm { if (it.working) it else it.copy(timeZone = it.timeZone.copy(chosen = zoneId)) }
        }
    }

    /**
     * The session is over: nothing kept on this device outlives it, the zone waiting for the
     * Server included. The session is bumped in the same step as the clear, so an answer of the
     * ended session that lands afterwards stores nothing.
     */
    internal fun forget() {
        session++
        loadingSession = NOT_LOADING
        cache.clear()
        choices.timeZone = null
        serverTimeZone = null
        sessionEnded.forEach { it() }
    }

    /** The Site switcher's pick: the whole app shows [siteId]; the choice persists per device. */
    public fun select(siteId: String) {
        val ready = mutableState.value as? SitesState.Ready ?: return
        val site = ready.sites.firstOrNull { it.id == siteId } ?: return
        choices.currentSiteId = site.id
        mutableState.value = ready.copy(current = site)
    }

    /** "New Site" in the switcher: Create Site over the shell, with a fresh key. */
    public fun newSite() {
        val ready = mutableState.value as? SitesState.Ready ?: return
        if (ready.creating != null) return
        mutableState.value = ready.copy(creating = newForm(cancellable = true))
    }

    /** Cancel on Create Site from "New Site": back to the Garden. Ignored while working. */
    public fun cancelNewSite() {
        val ready = mutableState.value as? SitesState.Ready ?: return
        if (ready.creating?.working != false) return
        mutableState.value = ready.copy(creating = null)
    }

    public fun setName(name: String) {
        updateForm { if (it.working) it else it.copy(name = name, nameError = null) }
    }

    /**
     * Confirm on the panel: the shown zone becomes the User's choice. It is kept on this device
     * until the Server has it ([onTimeZoneChosen]).
     */
    public fun confirmTimeZone() {
        val zone = currentForm()?.timeZone?.shown ?: return
        choose(zone)
    }

    private fun choose(zoneId: String) {
        updateForm { form ->
            choices.timeZone = zoneId
            form.copy(timeZone = form.timeZone.copy(chosen = zoneId, changing = false))
        }
        timeZoneChosen?.invoke(zoneId)
    }

    /** Change on the panel: shows the searchable list. */
    public fun changeTimeZone() {
        updateForm { it.copy(timeZone = it.timeZone.copy(changing = true)) }
    }

    /** A zone picked from the list; unknown IDs are ignored. */
    public fun pickTimeZone(zoneId: String) {
        if (zoneId !in availableZones || currentForm() == null) return
        choose(zoneId)
    }

    /** Create Site. An invalid name shows its reason and sends nothing. Ignored while working. */
    public fun submit() {
        val form = currentForm() ?: return
        if (form.working) return
        val nameError = CreateSiteForm.validate(form.name)
        if (nameError != null) {
            updateForm { it.copy(nameError = nameError, notice = null) }
            return
        }
        updateForm { it.copy(working = true, nameError = null, notice = null) }
        val started = session
        scope.launch {
            val result = api.createSite(form.name.trim(), form.idempotencyKey)
            if (started != session) return@launch
            when (result) {
                is ApiResult.Ok -> created(result.value.toSummary(), started)
                is ApiResult.Failed -> createFailed(result.failure, form)
            }
        }
    }

    /**
     * Renames the current Site (Owner; the caller gates the control, the Server enforces it).
     * On success the Sites are listed again, so the switcher and the Garden header show the new
     * name; the current Site stays current. Nothing is sent when no Site is current.
     */
    public suspend fun renameSite(name: String): ApiResult<SiteDto> {
        val ready = mutableState.value as? SitesState.Ready ?: return ApiResult.Failed(ApiFailure.Unexpected)
        val started = session
        val result = api.renameSite(ready.current.id, name)
        if (started != session) return result
        if (result is ApiResult.Ok) {
            val renamed = result.value.toSummary().copy(role = ready.current.role)
            // The Server's order after the write; the projection is updated before 200.
            val listed = api.listSites()
            if (started != session) return result
            val latest = mutableState.value as? SitesState.Ready ?: return result
            val sites =
                when (listed) {
                    is ApiResult.Ok -> {
                        cache.store(listed.value.sites)
                        listed.value.sites.map { it.toSummary() }
                    }

                    is ApiResult.Failed -> {
                        latest.sites.map {
                            if (it.id ==
                                renamed.id
                            ) {
                                it.copy(name = renamed.name)
                            } else {
                                it
                            }
                        }
                    }
                }
            val current =
                sites.firstOrNull { it.id == latest.current.id }
                    ?: latest.current.let { if (it.id == renamed.id) it.copy(name = renamed.name) else it }
            mutableState.value = latest.copy(sites = sites, current = current)
        }
        return result
    }

    private suspend fun created(
        site: SiteSummary,
        started: Int,
    ) {
        choices.currentSiteId = site.id
        val known = (mutableState.value as? SitesState.Ready)?.sites.orEmpty()
        // The Server's order after the write; the projection is updated before 201 (read-your-writes).
        val listed = api.listSites()
        if (started != session) return
        if (listed is ApiResult.Ok) cache.store(listed.value.sites)
        val sites =
            when (listed) {
                is ApiResult.Ok -> listed.value.sites.map { it.toSummary() }
                is ApiResult.Failed -> known
            }.let { if (it.any { s -> s.id == site.id }) it else it + site }
        mutableState.value = SitesState.Ready(sites, sites.first { it.id == site.id }, creating = null)
    }

    private fun createFailed(
        failure: ApiFailure,
        sent: CreateSiteForm,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            forget()
            mutableState.value = SitesState.Idle
            return
        }
        updateForm { form ->
            val idle = form.copy(working = false)
            when (failure) {
                ApiFailure.Validation -> {
                    val tooLong = sent.name.trim().length > CreateSiteForm.MAX_NAME_LENGTH
                    idle.copy(nameError = if (tooLong) NameError.TooLong else NameError.Blank)
                }

                ApiFailure.KeyReused -> {
                    idle.copy(notice = SitesNotice.KeyReused, idempotencyKey = newKey())
                }

                else -> {
                    idle.copy(notice = noticeOf(failure))
                }
            }
        }
    }

    private fun stateFor(
        sites: List<SiteSummary>,
        preferred: String?,
    ): SitesState {
        if (sites.isEmpty()) return SitesState.NeedsSite(newForm(cancellable = false))
        // An unknown or stale current id falls back to the first listed Site.
        val current = sites.firstOrNull { it.id == preferred } ?: sites.first()
        return SitesState.Ready(sites, current, creating = null)
    }

    private fun newForm(cancellable: Boolean): CreateSiteForm =
        CreateSiteForm(
            name = "",
            nameError = null,
            working = false,
            notice = null,
            idempotencyKey = newKey(),
            timeZone =
                TimeZoneProposal(
                    detected = detectTimeZone(),
                    chosen = serverTimeZone ?: choices.timeZone,
                    changing = false,
                ),
            cancellable = cancellable,
        )

    private fun currentForm(): CreateSiteForm? =
        when (val current = mutableState.value) {
            is SitesState.NeedsSite -> current.form
            is SitesState.Ready -> current.creating
            else -> null
        }

    private fun updateForm(change: (CreateSiteForm) -> CreateSiteForm) {
        when (val current = mutableState.value) {
            is SitesState.NeedsSite -> mutableState.value = current.copy(form = change(current.form))
            is SitesState.Ready -> current.creating?.let { mutableState.value = current.copy(creating = change(it)) }
            else -> Unit
        }
    }

    private val availableZones: Set<String> by lazy { zones().toSet() }

    public companion object {
        private const val NOT_LOADING = -1

        private fun noticeOf(failure: ApiFailure): SitesNotice =
            when (failure) {
                ApiFailure.Unreachable -> SitesNotice.Unreachable

                ApiFailure.Certificate -> SitesNotice.Certificate

                ApiFailure.IdentityProviderUnavailable -> SitesNotice.IdentityProviderUnavailable

                ApiFailure.KeyReused -> SitesNotice.KeyReused

                ApiFailure.Validation,
                ApiFailure.Unauthorized,
                ApiFailure.Forbidden,
                ApiFailure.NotFound,
                ApiFailure.LotClaimed,
                ApiFailure.DeviceOnAnotherSite,
                ApiFailure.DeviceAssigned,
                ApiFailure.CalibrationNotDelivered,
                ApiFailure.ReminderCadenceNotDelivered,
                ApiFailure.Unexpected,
                -> SitesNotice.Unexpected
            }

        private fun SiteDto.toSummary(): SiteSummary = SiteSummary(id, name, SiteRole.fromServer(role))

        /** A new Idempotency-Key: a random UUID. */
        @OptIn(ExperimentalUuidApi::class)
        public fun randomKey(): String = Uuid.random().toString()
    }
}
