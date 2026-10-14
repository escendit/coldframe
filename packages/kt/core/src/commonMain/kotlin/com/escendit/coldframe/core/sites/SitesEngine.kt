package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.escendit.coldframe.core.api.SitePauseDto
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.signin.SignInState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds
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
 *
 * While Create Site shows because the user has no Membership, the Sites are read again every
 * [MEMBERSHIP_POLL] (UX-DR90): an accepted invitation reaches the Server asynchronously, and the first
 * Site that arrives opens its overview, where the notification prompt follows (UX-DR122).
 */
public class SitesEngine(
    private val api: SitesApi,
    private val choices: DeviceChoices,
    private val scope: CoroutineScope,
    signIn: StateFlow<SignInState>,
    private val detectTimeZone: () -> String = ::detectedTimeZone,
    private val newKey: () -> String = ::randomKey,
    private val zones: () -> List<String> = ::availableTimeZones,
    private val membershipPoll: Duration = MEMBERSHIP_POLL,
) {
    private val mutableState = MutableStateFlow<SitesState>(SitesState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<SitesState> = mutableState.asStateFlow()

    /** Bumped when the session ends, so answers to an earlier session are dropped. */
    private var session = 0

    /** The session a read of the Sites is running for, so a second one does not start. */
    private var loadingSession = NOT_LOADING

    /** The session a quiet re-read of the Sites is running for ([refresh]). */
    private var refreshingSession = NOT_LOADING

    /** [refresh] was asked while one was in flight: its answer may be older than the ask, so one more read follows. */
    private var refreshAgain = false

    private val cache = SitesCache(choices.settings)
    private val sessionEnded = mutableListOf<() -> Unit>()
    private val pauseChanged = mutableListOf<() -> Unit>()

    /** The zone the User chose, as the Server holds it; Create Site names it instead of asking again. */
    private var serverTimeZone: String? = null
    private var timeZoneChosen: ((String) -> Unit)? = null

    init {
        scope.launch {
            // A sign-out clears what this device kept; a start that is still restoring does not.
            launch { signIn.collect { if (it is SignInState.SignedOut) forget() } }
            launch {
                mutableState.map { it is SitesState.NeedsSite }.distinctUntilChanged().collectLatest { needsSite ->
                    while (needsSite) {
                        delay(membershipPoll)
                        awaitMembership()
                    }
                }
            }
            signIn.map { it is SignInState.SignedIn }.distinctUntilChanged().collect { signedIn ->
                session++
                loadingSession = NOT_LOADING
                refreshingSession = NOT_LOADING
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

    /**
     * Reads the Sites again without leaving the shown ones: pull-to-refresh and every foreground of
     * the overview, and after the Site was paused or resumed. It is how a Pause that ended by itself,
     * a new name or a new Role reach this device. Sites that came from this device are read as
     * [load] reads them. A read that fails changes nothing; a 401 ends the session.
     */
    public fun refresh() {
        val shown = mutableState.value as? SitesState.Ready ?: return
        if (shown.fromCache) {
            load()
            return
        }
        if (loadingSession == session) return
        if (refreshingSession == session) {
            refreshAgain = true
            return
        }
        val started = session
        refreshingSession = started
        refreshAgain = false
        scope.launch {
            val result = api.listSites()
            if (started != session) return@launch
            refreshingSession = NOT_LOADING
            val again = refreshAgain
            refreshAgain = false
            when (result) {
                is ApiResult.Ok -> {
                    cache.store(result.value.sites)
                    val latest = mutableState.value as? SitesState.Ready ?: return@launch
                    val next = stateFor(result.value.sites.map { it.toSummary() }, preferred = latest.current.id)
                    mutableState.value = if (next is SitesState.Ready) next.copy(creating = latest.creating) else next
                }

                is ApiResult.Failed -> {
                    if (result.failure == ApiFailure.Unauthorized) {
                        forget()
                        mutableState.value = SitesState.Idle
                    }
                }
            }
            // Asked again while this read was on its way (a pause or a resume answered meanwhile): what just
            // landed may be the older list, so the Sites are read once more.
            if (again && started == session) refresh()
        }
    }

    /**
     * One read of the Sites while Create Site shows for a user without a Membership (UX-DR90). A Site
     * that arrived (an accepted invitation) becomes current and its overview opens; an empty list or a
     * read that fails changes nothing. Skipped while Create Site is sending or another read runs.
     */
    private suspend fun awaitMembership() {
        val shown = mutableState.value as? SitesState.NeedsSite ?: return
        if (shown.form.working || loadingSession == session) return
        val started = session
        val result = api.listSites()
        if (started != session) return
        val now = mutableState.value as? SitesState.NeedsSite ?: return
        if (now.form.working) return
        when (result) {
            is ApiResult.Ok -> {
                val sites = result.value.sites.map { it.toSummary() }
                if (sites.isEmpty()) return
                cache.store(result.value.sites)
                choices.currentSiteId = sites.first().id
                mutableState.value = SitesState.Ready(sites, sites.first(), creating = null)
            }

            is ApiResult.Failed -> {
                if (result.failure == ApiFailure.Unauthorized) {
                    forget()
                    mutableState.value = SitesState.Idle
                }
            }
        }
    }

    /**
     * Runs [action] after a Pause or a Resume the Server accepted, of the Site or of one Device, and
     * after a 404 on one. The Lots, Lot detail and Devices engines read their lists again with it, so
     * every surface shows the Server's answer.
     */
    internal fun onPauseChanged(action: () -> Unit) {
        pauseChanged += action
    }

    /** A Pause or a Resume changed something on the Server: the lists are read again. */
    internal fun pauseChanged() {
        pauseChanged.forEach { it() }
    }

    /**
     * The Server's answer to a pause or resume of [siteId]: the Site shows it at once, so the Site
     * menu and the overview's line do not wait for the next read of the Sites.
     */
    internal fun pauseAnswered(
        siteId: String,
        answer: SitePauseDto,
    ) {
        val ready = mutableState.value as? SitesState.Ready ?: return
        val until = answer.pausedUntil?.takeIf { answer.paused }?.let(DevicesEngine::epochMsOf)

        fun SiteSummary.answered(): SiteSummary =
            if (id == siteId) copy(paused = answer.paused, pausedUntilEpochMs = until) else this
        mutableState.value = ready.copy(sites = ready.sites.map { it.answered() }, current = ready.current.answered())
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
        refreshingSession = NOT_LOADING
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

        /** How often Create Site reads the Sites again for a user without a Membership (UX-DR90). */
        public val MEMBERSHIP_POLL: Duration = 10.seconds

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
                ApiFailure.AlreadyInvited,
                ApiFailure.LastOwner,
                ApiFailure.MemberNotFound,
                ApiFailure.CalibrationNotDelivered,
                ApiFailure.ReminderCadenceNotDelivered,
                ApiFailure.Unexpected,
                -> SitesNotice.Unexpected
            }

        /** Maps the Server's fields one to one; a Site without `paused` is not paused. */
        private fun SiteDto.toSummary(): SiteSummary {
            val isPaused = paused == true
            return SiteSummary(
                id = id,
                name = name,
                role = SiteRole.fromServer(role),
                paused = isPaused,
                pausedUntilEpochMs = pausedUntil?.takeIf { isPaused }?.let(DevicesEngine::epochMsOf),
            )
        }

        /** A new Idempotency-Key: a random UUID. */
        @OptIn(ExperimentalUuidApi::class)
        public fun randomKey(): String = Uuid.random().toString()
    }
}
