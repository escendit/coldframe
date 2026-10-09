package com.escendit.coldframe.core.notifications

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.NotificationSettingsDto
import com.escendit.coldframe.core.api.NotificationWindowRequestDto
import com.escendit.coldframe.core.api.SetSiteNotificationSettingsRequestDto
import com.escendit.coldframe.core.api.SiteNotificationSettingsDto
import com.escendit.coldframe.core.api.UpdateNotificationSettingsRequestDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.availableTimeZones
import com.escendit.coldframe.core.sites.detectedTimeZone
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * My notifications (Story 6.3, UX-DR72): the Notification Window (UX-DR47), the time zone (UX-DR48), and for the
 * current Site the mute (UX-DR49) and my Reminder cadence (UX-DR50). Every value is the Server's; this engine
 * only holds the window draft and shows a change while it is on its way.
 *
 * It reads when the session becomes [SignInState.SignedIn], so the time zone reaches the Server before the surface
 * is opened, and again on every [load]. It follows the current Site of [sites]; without one only the window and
 * the time zone are read.
 *
 * **Hand-over of the device zone (DW-23).** On the first read of a session: while the Server holds no chosen zone,
 * a zone kept in [DeviceChoices.timeZone] (confirmed on Create Site) is sent as the User's choice and removed once
 * the Server answered, whatever the answer; it stays for the next read when the Server did not answer. Without a
 * kept zone the device's zone is sent as the detected one. A zone the User chose on the Server always wins: the
 * kept one is dropped and nothing is sent.
 *
 * The segmented choice and the switch apply at once; the window has Save. One change is sent at a time. A change
 * that was not saved puts its control back at the Server's value with a [NotificationSettingsNotice]; [retry]
 * sends it again. A 401 ends the session.
 */
public class NotificationSettingsEngine(
    private val api: NotificationSettingsApi,
    private val sites: SitesEngine,
    private val choices: DeviceChoices,
    private val scope: CoroutineScope,
    signIn: StateFlow<SignInState>,
    private val detectTimeZone: () -> String = ::detectedTimeZone,
    private val zones: () -> List<String> = ::availableTimeZones,
) {
    private val mutableState = MutableStateFlow<NotificationSettingsState>(NotificationSettingsState.Idle)

    /** One observable state for the shells. */
    public val state: StateFlow<NotificationSettingsState> = mutableState.asStateFlow()

    /** Bumped when the session starts or ends, so answers to an earlier session are dropped. */
    private var session = 0

    /** Bumped by every read; only the newest read's answer is applied. */
    private var reads = 0
    private var signedIn = false

    /** The hand-over of the device zone is still to do in this session. */
    private var handOverDue = false

    private var mine: NotificationSettingsDto? = null
    private var forSite: Pair<SiteSummary, SiteNotificationSettingsDto>? = null

    /** The window being edited; `null` while it is the Server's. */
    private var draft: NotificationWindow? = null
    private var changing = false
    private var windowSaved = false
    private var pending: Change? = null
    private var failed: Change? = null
    private var notice: NotificationSettingsNotice? = null
    private var noticeControl: NotificationControl? = null

    private val availableZones: List<String> by lazy { zones() }

    init {
        sites.onSessionEnded { reset() }
        sites.onTimeZoneChosen { chosenOnCreateSite(it) }
        scope.launch {
            launch {
                signIn.map { it is SignInState.SignedIn }.distinctUntilChanged().collect { now ->
                    signedIn = now
                    reset()
                    if (now) {
                        handOverDue = true
                        mutableState.value = NotificationSettingsState.Loading
                        read()
                    }
                }
            }
            sites.state
                .map { (it as? SitesState.Ready)?.current }
                .distinctUntilChanged()
                .collect { follow(it) }
        }
    }

    /** Every IANA zone ID, sorted, for Change on the time-zone panel. */
    public fun timeZones(): List<String> = availableZones

    /**
     * Reads the settings again: every entry of My notifications, and Try again after a failed load. What is shown
     * stays while it runs. Ignored while a change is on its way.
     */
    public fun load() {
        if (!signedIn || pending != null) return
        if (mine == null) mutableState.value = NotificationSettingsState.Loading
        read()
    }

    /** Try again: reads again after a failed load, or sends the change that was not saved again. */
    public fun retry() {
        if (mutableState.value is NotificationSettingsState.Failed) {
            load()
            return
        }
        val change = failed ?: return
        if (notice?.tryAgain == true) send(change)
    }

    /** The start of the window as `"HH:mm"`; text that is no time leaves the draft as it is. */
    public fun setWindowFrom(time: String) {
        editWindow(time) { window -> window.copy(from = time) }
    }

    /** The end of the window as `"HH:mm"`. */
    public fun setWindowTo(time: String) {
        editWindow(time) { window -> window.copy(to = time) }
    }

    /** Save: sends the draft's start and end. A window that does not start before it ends sends nothing. */
    public fun saveWindow() {
        val ready = mutableState.value as? NotificationSettingsState.Ready ?: return
        if (!ready.canSaveWindow) return
        send(Change.Window(ready.draft.from, ready.draft.to))
    }

    /** Confirm on the panel: the proposed zone becomes the User's choice. */
    public fun confirmTimeZone() {
        val ready = mutableState.value as? NotificationSettingsState.Ready ?: return
        if (ready.timeZone.confirmed || pending != null) return
        val zone = ready.timeZone.shown ?: return
        changing = false
        send(Change.Zone(zone))
    }

    /** Change on the panel: shows the searchable list. */
    public fun changeTimeZone() {
        if (mine == null || pending != null) return
        changing = true
        publish()
    }

    /** A zone picked from the list; an ID that is not in the list is ignored. */
    public fun pickTimeZone(zoneId: String) {
        if (mine == null || pending != null || zoneId !in availableZones) return
        changing = false
        send(Change.Zone(zoneId))
    }

    /** The mute switch of the current Site: applies at once, and affects only this User. */
    public fun setMuted(muted: Boolean) {
        val site = (mutableState.value as? NotificationSettingsState.Ready)?.site ?: return
        // The Server replaces both values with every write, so the cadence in force goes with it.
        send(Change.Site(site.site.id, muted, site.reminderCadence, NotificationControl.Mute))
    }

    /** My Reminder cadence for the current Site; `null` is "Use Site setting". Applies at once. */
    public fun setReminderCadence(cadence: ReminderCadence?) {
        val site = (mutableState.value as? NotificationSettingsState.Ready)?.site ?: return
        send(Change.Site(site.site.id, site.muted, cadence, NotificationControl.ReminderCadence))
    }

    private fun editWindow(
        time: String,
        change: (NotificationWindow) -> NotificationWindow,
    ) {
        val ready = mutableState.value as? NotificationSettingsState.Ready ?: return
        if (ready.windowWorking || NotificationWindow.minutesOf(time) == null) return
        draft = change(ready.draft)
        windowSaved = false
        if (noticeControl == NotificationControl.Window) clearNotice()
        publish()
    }

    /** A zone confirmed or picked on Create Site: it goes to the Server now, or with the next read. */
    private fun chosenOnCreateSite(zoneId: String) {
        if (mine == null || pending != null) {
            // The Sites engine keeps it on this device; the hand-over of the next read sends it.
            handOverDue = true
            return
        }
        send(Change.Zone(zoneId))
    }

    private fun follow(site: SiteSummary?) {
        // Not read yet: the read on its way asks for the current Site when it gets there.
        if (mine == null) return
        val shown = forSite
        if (site?.id == shown?.first?.id) {
            // Same Site with a new name or Role.
            if (site != null && shown != null) {
                forSite = site to shown.second
                publish()
            }
            return
        }
        forSite = null
        // A mute or cadence that was not saved belongs to the Site left behind: its notice must not show
        // under the new Site, and Try again must not write the old one.
        if (failed is Change.Site) failed = null
        if (noticeControl == NotificationControl.Mute ||
            noticeControl == NotificationControl.ReminderCadence
        ) {
            clearNotice()
        }
        publish()
        if (site != null) read()
    }

    private fun currentSite(): SiteSummary? = (sites.state.value as? SitesState.Ready)?.current

    private fun read() {
        val started = session
        val turn = ++reads
        scope.launch {
            val answer = api.getMyNotificationSettings()
            if (started != session || turn != reads) return@launch
            var settings =
                when (answer) {
                    is ApiResult.Ok -> answer.value
                    is ApiResult.Failed -> return@launch loadFailed(answer.failure)
                }
            if (handOverDue) settings = handOver(settings, started, turn) ?: return@launch
            val site = currentSite()
            var siteSettings: SiteNotificationSettingsDto? = null
            if (site != null) {
                val siteAnswer = api.getSiteNotificationSettings(site.id)
                if (started != session || turn != reads) return@launch
                siteSettings =
                    when (siteAnswer) {
                        is ApiResult.Ok -> siteAnswer.value
                        is ApiResult.Failed -> return@launch loadFailed(siteAnswer.failure)
                    }
            }
            landed(settings, if (site != null && siteSettings != null) site to siteSettings else null)
        }
    }

    /**
     * Sends the kept zone as the User's choice, else the device's zone as the detected one, while the Server holds
     * no chosen zone. Returns the settings in force, or `null` when the answer is no longer wanted.
     */
    private suspend fun handOver(
        settings: NotificationSettingsDto,
        started: Int,
        turn: Int,
    ): NotificationSettingsDto? {
        val kept = choices.timeZone
        val request =
            when {
                // A zone the User chose is never replaced; the kept one is obsolete.
                settings.timeZoneConfirmed -> {
                    null
                }

                kept != null -> {
                    UpdateNotificationSettingsRequestDto(timeZone = kept)
                }

                else -> {
                    deviceZone()
                        ?.takeIf { it != settings.timeZone }
                        ?.let { UpdateNotificationSettingsRequestDto(detectedTimeZone = it) }
                }
            }
        if (request == null) {
            if (settings.timeZoneConfirmed) choices.timeZone = null
            handOverDue = false
            return settings
        }
        val answer = api.updateMyNotificationSettings(request)
        if (started != session || turn != reads) return null
        return when (answer) {
            is ApiResult.Ok -> {
                if (request.timeZone != null) choices.timeZone = null
                handOverDue = false
                answer.value
            }

            is ApiResult.Failed -> {
                when {
                    answer.failure == ApiFailure.Unauthorized -> {
                        signedOut()
                        null
                    }

                    // No answer: the kept zone stays, and the next read tries again.
                    !answered(answer.failure) -> {
                        settings
                    }

                    // The Server answered, a refusal included: the kept zone has been handed over.
                    else -> {
                        if (request.timeZone != null) choices.timeZone = null
                        handOverDue = false
                        settings
                    }
                }
            }
        }
    }

    private fun landed(
        settings: NotificationSettingsDto,
        site: Pair<SiteSummary, SiteNotificationSettingsDto>?,
    ) {
        // The Site changed while its settings were read: read the new one's.
        if (currentSite()?.id != site?.first?.id) {
            mine = settings
            forSite = null
            publish()
            read()
            return
        }
        val before = mine?.window?.let { NotificationWindow(it.from, it.to) }
        // A draft that was not edited follows the Server.
        if (draft == before) draft = null
        mine = settings
        forSite = site
        publish()
    }

    private fun loadFailed(failure: ApiFailure) {
        if (failure == ApiFailure.Unauthorized) {
            signedOut()
            return
        }
        clear()
        mutableState.value =
            NotificationSettingsState.Failed(
                when (failure) {
                    ApiFailure.Unreachable -> NotificationSettingsNotice.Unreachable
                    ApiFailure.Certificate -> NotificationSettingsNotice.Certificate
                    else -> NotificationSettingsNotice.Unexpected
                },
            )
    }

    private fun send(change: Change) {
        if (mine == null || pending != null) return
        pending = change
        failed = null
        clearNotice()
        windowSaved = false
        if (change is Change.Window) draft = NotificationWindow(change.from, change.to)
        // A read on its way would bring back the values from before this change.
        reads++
        publish()
        val started = session
        scope.launch {
            when (change) {
                is Change.Site -> {
                    val request = SetSiteNotificationSettingsRequestDto(change.muted, change.cadence?.key)
                    val answer = api.setSiteNotificationSettings(change.siteId, request)
                    if (started != session) return@launch
                    pending = null
                    siteAnswered(change, answer)
                }

                is Change.Window, is Change.Zone -> {
                    val answer = api.updateMyNotificationSettings(change.request())
                    if (started != session) return@launch
                    pending = null
                    when (answer) {
                        is ApiResult.Ok -> saved(change, answer.value)
                        is ApiResult.Failed -> notSaved(change, answer.failure)
                    }
                }
            }
            if (started != session) return@launch
            publish()
            // The Site changed while the change was on its way: its settings are still to read.
            if (currentSite()?.id != forSite?.first?.id) read()
        }
    }

    private fun siteAnswered(
        change: Change.Site,
        answer: ApiResult<SiteNotificationSettingsDto>,
    ) {
        val shown = forSite
        // An answer for a Site left behind changes nothing that is shown.
        val current = shown?.first?.id == change.siteId
        when (answer) {
            is ApiResult.Ok -> {
                if (shown != null && current) forSite = shown.first to answer.value
            }

            is ApiResult.Failed -> {
                if (current || answer.failure == ApiFailure.Unauthorized) notSaved(change, answer.failure)
            }
        }
    }

    private fun saved(
        change: Change,
        settings: NotificationSettingsDto,
    ) {
        mine = settings
        when (change) {
            is Change.Window -> {
                draft = null
                windowSaved = true
            }

            is Change.Zone -> {
                if (choices.timeZone == change.zoneId) choices.timeZone = null
            }

            is Change.Site -> {
                Unit
            }
        }
    }

    /** The control goes back to the Server's value, with a notice; a 401 ends the session. */
    private fun notSaved(
        change: Change,
        failure: ApiFailure,
    ) {
        if (failure == ApiFailure.Unauthorized) {
            signedOut()
            return
        }
        if (change is Change.Window) draft = null
        // The Server answered about this zone, so it no longer waits on this device.
        if (change is Change.Zone && answered(failure) && choices.timeZone == change.zoneId) choices.timeZone = null
        val shown =
            when {
                failure == ApiFailure.Validation -> {
                    NotificationSettingsNotice.Invalid
                }

                failure == ApiFailure.Certificate -> {
                    NotificationSettingsNotice.Certificate
                }

                change is Change.Site && (failure == ApiFailure.Forbidden || failure == ApiFailure.NotFound) -> {
                    NotificationSettingsNotice.SiteRefused
                }

                else -> {
                    NotificationSettingsNotice.NotSaved
                }
            }
        notice = shown
        noticeControl = change.control
        failed = change.takeIf { shown.tryAgain }
    }

    /** Whether the Server answered at all: not for no answer, a 5xx, or a certificate that could not be verified. */
    private fun answered(failure: ApiFailure): Boolean = !failure.transport && failure != ApiFailure.Certificate

    private fun deviceZone(): String? = detectTimeZone().takeIf { it.isNotBlank() }

    private fun publish() {
        val settings = mine ?: return
        val window = NotificationWindow(settings.window.from, settings.window.to)
        val change = pending
        val chosen = settings.timeZone.takeIf { settings.timeZoneConfirmed }
        val sending = (change as? Change.Zone)?.zoneId
        // The Create Site panel names the zone the Server holds, or the one on its way to it.
        sites.timeZoneFromServer(sending ?: chosen)
        mutableState.value =
            NotificationSettingsState.Ready(
                window = window,
                draft = draft ?: window,
                windowWorking = change is Change.Window,
                windowSaved = windowSaved,
                timeZone =
                    NotificationTimeZone(
                        // The device's zone first, then the one the Server stored as detected, then none.
                        detected = deviceZone() ?: settings.timeZone.takeIf { !settings.timeZoneConfirmed },
                        chosen = sending ?: chosen,
                        changing = changing,
                        working = sending != null,
                    ),
                site = forSite?.let { (site, dto) -> siteSettings(site, dto, change as? Change.Site) },
                notice = notice,
                noticeControl = noticeControl,
            )
    }

    private fun siteSettings(
        site: SiteSummary,
        dto: SiteNotificationSettingsDto,
        change: Change.Site?,
    ): SiteNotificationSettings {
        val sending = change?.takeIf { it.siteId == site.id }
        return SiteNotificationSettings(
            site = site,
            muted = sending?.muted ?: dto.muted,
            reminderCadence = if (sending != null) sending.cadence else ReminderCadence.fromServer(dto.reminderCadence),
            // The Server's default, should it name a cadence this app does not know.
            siteReminderCadence = ReminderCadence.fromServer(dto.siteReminderCadence) ?: ReminderCadence.Daily,
            working = sending != null,
        )
    }

    private fun clearNotice() {
        notice = null
        noticeControl = null
    }

    /** A 401: the session is over, and nothing kept on this device outlives it. */
    private fun signedOut() {
        reset()
        sites.forget()
    }

    private fun reset() {
        session++
        clear()
        mutableState.value = NotificationSettingsState.Idle
    }

    private fun clear() {
        mine = null
        forSite = null
        draft = null
        changing = false
        windowSaved = false
        pending = null
        failed = null
        clearNotice()
    }

    /** One change on its way to the Server, kept to send it again. */
    private sealed interface Change {
        val control: NotificationControl

        fun request(): UpdateNotificationSettingsRequestDto = UpdateNotificationSettingsRequestDto()

        data class Window(
            val from: String,
            val to: String,
        ) : Change {
            override val control: NotificationControl get() = NotificationControl.Window

            override fun request(): UpdateNotificationSettingsRequestDto =
                UpdateNotificationSettingsRequestDto(window = NotificationWindowRequestDto(from, to))
        }

        data class Zone(
            val zoneId: String,
        ) : Change {
            override val control: NotificationControl get() = NotificationControl.TimeZone

            override fun request(): UpdateNotificationSettingsRequestDto =
                UpdateNotificationSettingsRequestDto(timeZone = zoneId)
        }

        data class Site(
            val siteId: String,
            val muted: Boolean,
            val cadence: ReminderCadence?,
            override val control: NotificationControl,
        ) : Change
    }
}
