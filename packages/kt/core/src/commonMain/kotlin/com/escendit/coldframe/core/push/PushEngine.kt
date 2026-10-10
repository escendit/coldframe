package com.escendit.coldframe.core.push

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.RegisterPushDeviceRequestDto
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.site
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Push on this phone (Story 6.5): the notification permission and its one prompt (UX-DR122, UX-DR88), the
 * registration of the device's push token with the Server (UX-DR115), and where a tap on a notification leads
 * (UX-DR120). The shells hold the OS calls only: they report what the OS says, hand over the token the OS gives them
 * and the data of a tapped notification, and render [state].
 *
 * **Permission.** The shell calls [reportPermission] on start and on every foreground. While a Site is current, the
 * OS was never asked and this device never asked, [PushState.promptDue] is true: the overview shows the why-line,
 * then the OS prompt, and the shell calls [promptAnswered]. That is kept per device
 * ([DeviceChoices.notificationPermissionAsked]) and outlives every session, so the prompt shows once. Afterwards
 * anything but granted is [PushPermission.Denied].
 *
 * **Registration.** The shell calls [tokenReceived] whenever the OS gives it a token. The engine sends it with
 * `PUT /me/push-registrations/{installationId}` once per session: after sign-in, and again when the token changes.
 * The same token is not sent twice in a session; a registration the Server did not answer is sent again with the
 * next [reportPermission], one it refused only when the token changes. The installation ID is a UUID made once and
 * kept in [DeviceChoices.installationId].
 *
 * **Sign-out.** [unregister] removes the registration while the session is still valid; the sign-in engine runs it
 * before it clears the tokens. It is best effort: whatever the Server says, sign-out goes on. A registration on its
 * way is cancelled first, so it cannot land after the removal.
 *
 * **Tap.** [opened] takes the routing data of a notification. The engine waits until the Sites are loaded (a cold
 * start), switches to the notification's Site, and for an Alert or a Reminder waits for that Site's Lots; then
 * [PushState.route] is [PushRoute.LotDetail], or [PushRoute.Overview] for a summary, an unknown kind and a Lot that
 * is gone. A Site the User does not hold ends on the overview of the current Site. The shell shows the route and
 * calls [routeHandled]. A tap waiting when the session ends is dropped.
 */
public class PushEngine(
    private val api: PushApi,
    private val sites: SitesEngine,
    private val lots: StateFlow<LotsState>,
    private val choices: DeviceChoices,
    private val platform: PushPlatform,
    private val scope: CoroutineScope,
    signIn: StateFlow<SignInState>,
    private val newInstallationId: () -> String = SitesEngine::randomKey,
) {
    private val mutableState = MutableStateFlow(PushState.None)

    /** One observable state for the shells. */
    public val state: StateFlow<PushState> = mutableState.asStateFlow()

    /** Bumped when the session starts or ends, so an answer to an earlier session changes nothing. */
    private var session = 0
    private var signedIn = false

    /** Sign-out has removed the registration; nothing is registered until the session has ended. */
    private var leaving = false

    private var os: OsPermission? = null

    /** The token the OS gave last; it outlives the session, because the next User registers the same one. */
    private var device: Device? = null

    /** What the Server holds for this session, and what it refused. */
    private var registered: Device? = null
    private var refused: Device? = null
    private var registration: Job? = null

    private var pending: PushTap? = null
    private var route: PushRoute? = null

    init {
        sites.onSessionEnded {
            pending = null
            route = null
            publish()
        }
        scope.launch {
            launch {
                signIn.map { it is SignInState.SignedIn }.distinctUntilChanged().collect { now ->
                    session++
                    signedIn = now
                    leaving = false
                    registered = null
                    refused = null
                    publish()
                    if (now) register()
                }
            }
            launch { lots.collect { advance() } }
            sites.state.collect {
                publish()
                advance()
            }
        }
    }

    /** What the OS says about notifications: on start, and on every foreground (UX-DR88). */
    public fun reportPermission(status: OsPermission) {
        os = status
        publish()
        // The foreground is also when a registration that did not arrive is sent again.
        register()
    }

    /** The OS prompt was answered with [status]; it is not shown again on this device (UX-DR122). */
    public fun promptAnswered(status: OsPermission) {
        choices.notificationPermissionAsked = true
        reportPermission(status)
    }

    /**
     * The push token the OS gave: the FCM registration token, or the APNs device token as hex with its
     * [environment]. Called on every start and whenever the OS replaces the token. A blank token, and an APNs token
     * without an environment, is ignored.
     */
    public fun tokenReceived(
        token: String,
        environment: ApnsEnvironment? = null,
    ) {
        if (token.isBlank()) return
        val next =
            when (platform) {
                PushPlatform.Apns -> Device(token, environment ?: return)
                PushPlatform.Fcm -> Device(token, null)
            }
        device = next
        register()
    }

    /** A notification was tapped: [data] are its routing keys (`kind`, `siteId`, `lotId`, …). */
    public fun opened(data: Map<String, String>) {
        val tap = PushPayload.parse(data)?.tap ?: return
        pending = tap
        route = null
        publish()
        advance()
    }

    /** The shell has shown [PushState.route]. */
    public fun routeHandled() {
        if (route == null) return
        route = null
        publish()
    }

    /**
     * Sign-out: removes this installation's registration while the session is still valid. Best effort; nothing is
     * registered again until the session has ended.
     */
    internal suspend fun unregister() {
        leaving = true
        registration?.cancelAndJoin()
        registration = null
        registered = null
        val installationId = choices.installationId ?: return
        api.removePushDevice(installationId)
    }

    private fun register() {
        val next = device ?: return
        if (!signedIn || leaving || registration != null || next == registered || next == refused) return
        val started = session
        val installationId = choices.installationId ?: newInstallationId().also { choices.installationId = it }
        val request = RegisterPushDeviceRequestDto(platform.key, next.token, next.environment?.key)
        val job =
            scope.launch {
                val answer = api.registerPushDevice(installationId, request)
                registration = null
                if (started == session) {
                    when {
                        answer is ApiResult.Ok -> registered = next

                        answer is ApiResult.Failed && answer.failure == ApiFailure.Validation -> refused = next

                        // Not answered, or the session is over: the next foreground or sign-in sends it again.
                        else -> return@launch
                    }
                }
                // The token changed, or another session began, while this one was on its way.
                register()
            }
        // A registration that already answered has cleared the field itself.
        if (job.isActive) registration = job
    }

    private fun publish() {
        val status = os
        val asked = choices.notificationPermissionAsked
        val onOverview = signedIn && sites.state.value is SitesState.Ready
        mutableState.value =
            PushState(
                permission =
                    when {
                        status == null -> PushPermission.Unknown
                        status == OsPermission.Granted -> PushPermission.Granted
                        status == OsPermission.NotDetermined && !asked -> PushPermission.Unknown
                        else -> PushPermission.Denied
                    },
                promptDue = onOverview && status == OsPermission.NotDetermined && !asked,
                route = route,
            )
    }

    /** Takes the waiting tap one step further; every change of the Sites and the Lots calls it. */
    private fun advance() {
        val tap = pending ?: return
        when (val current = sites.state.value) {
            // No Membership: there is no overview to open.
            is SitesState.NeedsSite -> pending = null

            is SitesState.Ready -> advance(tap, current)

            // Still loading (a cold start), or not read: the tap waits.
            else -> Unit
        }
    }

    private fun advance(
        tap: PushTap,
        ready: SitesState.Ready,
    ) {
        val site = ready.sites.firstOrNull { it.id == tap.siteId }
        if (site == null) {
            // Sites kept on this device may lack a Site the Server knows: wait for its list.
            if (!ready.fromCache) show(PushRoute.Overview(ready.current.id))
            return
        }
        if (ready.current.id != site.id) {
            // The switch changes the Sites state, which brings this tap back here.
            sites.select(site.id)
            return
        }
        val lotId = tap.lotId
        if (lotId == null || !tap.opensLot) {
            show(PushRoute.Overview(site.id))
            return
        }
        val shown = lots.value
        // The Lots of the Site left behind are not this Site's.
        if (shown.site?.id != site.id) return
        when (shown) {
            is LotsState.Ready -> {
                val lot = shown.lots.firstOrNull { it.id == lotId }
                when {
                    lot != null -> show(PushRoute.LotDetail(site.id, lot.id, lot.name))

                    // Lots kept on this device may lack a new Lot: wait for the read that is running.
                    shown.refreshing -> Unit

                    else -> show(PushRoute.Overview(site.id))
                }
            }

            is LotsState.Failed -> {
                show(PushRoute.Overview(site.id))
            }

            is LotsState.Loading, LotsState.Idle -> {
                Unit
            }
        }
    }

    private fun show(next: PushRoute) {
        pending = null
        route = next
        publish()
    }

    private data class Device(
        val token: String,
        val environment: ApnsEnvironment?,
    )
}
