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

    init {
        scope.launch {
            signIn.map { it is SignInState.SignedIn }.distinctUntilChanged().collect { signedIn ->
                session++
                if (signedIn) load() else mutableState.value = SitesState.Idle
            }
        }
    }

    /** Reads the Sites; also Try again after [SitesState.Failed]. */
    public fun load() {
        if (mutableState.value == SitesState.Loading) return
        val started = session
        mutableState.value = SitesState.Loading
        scope.launch {
            val result = api.listSites()
            if (started != session) return@launch
            mutableState.value =
                when (result) {
                    is ApiResult.Ok -> {
                        stateFor(
                            result.value.sites.map { it.toSummary() },
                            preferred = choices.currentSiteId,
                        )
                    }

                    is ApiResult.Failed -> {
                        failedState(result.failure)
                    }
                }
        }
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

    /** Confirm on the panel: the detected zone becomes the user's choice on this device. */
    public fun confirmTimeZone() {
        updateForm { form ->
            val zone = form.timeZone.shown
            choices.timeZone = zone
            form.copy(timeZone = form.timeZone.copy(chosen = zone, changing = false))
        }
    }

    /** Change on the panel: shows the searchable list. */
    public fun changeTimeZone() {
        updateForm { it.copy(timeZone = it.timeZone.copy(changing = true)) }
    }

    /** A zone picked from the list; unknown IDs are ignored. */
    public fun pickTimeZone(zoneId: String) {
        if (zoneId !in availableZones) return
        updateForm { form ->
            choices.timeZone = zoneId
            form.copy(timeZone = form.timeZone.copy(chosen = zoneId, changing = false))
        }
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

    private fun failedState(failure: ApiFailure): SitesState =
        if (failure == ApiFailure.Unauthorized) SitesState.Idle else SitesState.Failed(noticeOf(failure))

    private fun newForm(cancellable: Boolean): CreateSiteForm =
        CreateSiteForm(
            name = "",
            nameError = null,
            working = false,
            notice = null,
            idempotencyKey = newKey(),
            timeZone = TimeZoneProposal(detected = detectTimeZone(), chosen = choices.timeZone, changing = false),
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
                ApiFailure.Unexpected,
                -> SitesNotice.Unexpected
            }

        private fun SiteDto.toSummary(): SiteSummary = SiteSummary(id, name, SiteRole.fromServer(role))

        /** A new Idempotency-Key: a random UUID. */
        @OptIn(ExperimentalUuidApi::class)
        public fun randomKey(): String = Uuid.random().toString()
    }
}
