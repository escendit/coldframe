package com.escendit.coldframe.core.setup

import coldframe.setup.v1.EnrolmentRequest
import coldframe.setup.v1.IdentityRequest
import coldframe.setup.v1.SetupErrorCode
import coldframe.setup.v1.SetupMessage
import coldframe.setup.v1.SiteBinding
import coldframe.setup.v1.WifiConfig
import coldframe.setup.v1.WifiScanRequest
import coldframe.setup.v1.WifiSecurity
import coldframe.setup.v1.WifiStatus
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.EnrolDeviceRequestDto
import com.escendit.coldframe.core.api.EnrolmentKeyDto
import com.escendit.coldframe.core.crypto.CryptoSpec
import com.escendit.coldframe.core.crypto.enrolmentKeyFingerprint
import com.escendit.coldframe.core.crypto.toLowerHex
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.ClosedReceiveChannelException
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okio.ByteString.Companion.toByteString
import kotlin.io.encoding.Base64
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** The enrolment calls of Add a Hub; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface EnrolmentApi {
    public suspend fun enrolmentKey(): ApiResult<EnrolmentKeyDto>

    public suspend fun enrolDevice(
        siteId: String,
        request: EnrolDeviceRequestDto,
        idempotencyKey: String,
    ): ApiResult<DeviceDto>
}

/**
 * Add a Hub (AD-25, UX-DR66): the setup client of the KMP core over a [SetupRadio]. One
 * [state] for the shells; every action is a method. Segments and steps advance only on real
 * events. Per BLE session: `IdentityRequest` (checks the code) → `WifiScanRequest` → on step 5
 * `SiteBinding` → `EnrolmentRequest` → `POST /sites/{siteId}/devices` with the sealed `K_dev`
 * relayed unread → `WifiConfig` → `WifiResult`. Binding and enrolment run once per session; a
 * retry after `WRONG_PASSWORD` or `NO_SERVER` resends only `WifiConfig`. No `WifiConfig` is sent
 * unless enrolment answered 201.
 *
 * The code and the Wi-Fi password never leave this engine except inside the sealed session,
 * and nothing here logs.
 */
public class HubSetupEngine(
    private val radio: SetupRadio,
    private val api: EnrolmentApi,
    private val sites: StateFlow<SitesState>,
    serverUrl: String,
    private val scope: CoroutineScope,
    private val newKey: () -> String = SitesEngine::randomKey,
    private val newSession: () -> SetupSession = { SetupSession() },
) {
    private val serverUrl = serverUrl.trimEnd('/')
    private val serverHost =
        this.serverUrl
            .substringAfter("://")
            .substringBefore('/')
            .substringBefore(':')
    private val mutableState = MutableStateFlow(HubSetupState.CLOSED)

    /** One observable state for the shells. */
    public val state: StateFlow<HubSetupState> = mutableState.asStateFlow()

    private val speaking = MutableStateFlow(false)

    /** Bumped on every open, close and start over: work of an older attempt is dropped. */
    private var flow = 0

    /** Bumped on every connect and deliberate disconnect: a close of an older link is ignored. */
    private var linkGeneration = 0
    private var link: SetupLink? = null
    private var frames: Channel<ByteArray>? = null
    private var session: SetupSession? = null

    /** Whether `SiteBinding` and enrolment succeeded in the current BLE session. */
    private var bound = false
    private var enrolmentKey: ByteArray? = null
    private var idempotency: Pair<String, String>? = null
    private var announcementId = 0
    private val announced = mutableSetOf<String>()

    private var watchJob: Job? = null
    private var sitesJob: Job? = null
    private var scanJob: Job? = null
    private var noHubJob: Job? = null
    private var workJob: Job? = null
    private var tickJob: Job? = null
    private var timeoutJob: Job? = null

    // Opening and closing

    /** Opens the flow on step 1 and starts scanning once the radio is ready. */
    public fun open() {
        if (mutableState.value.open) return
        val choices = siteChoices() ?: return
        flow++
        announced.clear()
        idempotency = null
        mutableState.value =
            HubSetupState.CLOSED.copy(
                open = true,
                radio = radio.state.value,
                site = HubSetupState.CLOSED.site.copy(sites = choices.first, selectedId = choices.second),
                serverHost = serverHost,
            )
        val started = flow
        watchJob =
            scope.launch {
                radio.state.collect { radioState ->
                    if (started != flow) return@collect
                    update { it.copy(radio = radioState) }
                    if (radioState == RadioState.Ready) startScan() else stopScan()
                }
            }
        sitesJob =
            scope.launch {
                // Signed out or without Sites: the flow ends. [close] cancels this watch.
                sites.first { it !is SitesState.Ready }
                if (mutableState.value.open) close()
            }
    }

    /** Closes the flow, disconnects and forgets the password. */
    public fun close() {
        flow++
        for (job in listOf(watchJob, sitesJob, scanJob, noHubJob, workJob, tickJob, timeoutJob)) job?.cancel()
        disconnect()
        enrolmentKey = null
        mutableState.value = HubSetupState.CLOSED
    }

    /** Re-reads the radio after a permission prompt or a return to the foreground. */
    public fun recheckRadio() {
        radio.recheck()
    }

    /**
     * Whether a screen reader is reading an announcement. The progress timeout waits until it
     * is false (UX-DR103).
     */
    public fun announcing(active: Boolean) {
        speaking.value = active
    }

    /** Top-left Back on steps 2 to 4 goes one step back; elsewhere it leaves (UX-DR39). */
    public fun back() {
        val current = mutableState.value
        if (!current.open || current.confirmingLeave) return
        val outcome = current.outcome
        when {
            outcome != null -> if (outcome.success) close() else leave()
            current.step == SetupStep.Scan || current.step == SetupStep.Progress -> leave()
            current.code.working -> Unit
            current.step == SetupStep.Code -> backToScan()
            current.step == SetupStep.Wifi -> update { it.copy(step = SetupStep.Code) }
            current.step == SetupStep.Site -> update { it.copy(step = SetupStep.Wifi) }
        }
    }

    /** Cancel on step 1 (and system back from a running step): asks once a Hub is selected. */
    public fun leave() {
        val current = mutableState.value
        if (!current.open) return
        if (current.selected == null || current.outcome?.success == true) {
            close()
        } else {
            update { it.copy(confirmingLeave = true) }
        }
    }

    /** "Stop setting up Hub 3F2A?" confirmed: disconnects and closes. */
    public fun confirmLeave() {
        if (mutableState.value.confirmingLeave) close()
    }

    public fun stayInFlow() {
        update { it.copy(confirmingLeave = false) }
    }

    // Step 1: scan and select

    public fun select(peripheralId: String) {
        val current = mutableState.value
        if (current.step != SetupStep.Scan || current.outcome != null) return
        val candidate = current.candidates.firstOrNull { it.peripheralId == peripheralId } ?: return
        update { it.copy(selected = candidate) }
    }

    /** Continues with the selected Hub to the setup code. */
    public fun continueFromScan() {
        val current = mutableState.value
        if (current.step != SetupStep.Scan || current.selected == null) return
        stopScan()
        update { it.copy(step = SetupStep.Code, noHubYet = false) }
    }

    private fun startScan() {
        val current = mutableState.value
        if (!current.open || current.step != SetupStep.Scan || current.outcome != null || scanJob?.isActive == true) {
            return
        }
        val started = flow
        noHubJob?.cancel()
        noHubJob =
            scope.launch {
                delay(NO_HUB_AFTER)
                if (started == flow && mutableState.value.candidates.isEmpty()) update { it.copy(noHubYet = true) }
            }
        scanJob =
            scope.launch {
                try {
                    radio.scan().collect { advert -> if (started == flow) onAdvert(advert) }
                } catch (cancellation: CancellationException) {
                    throw cancellation
                } catch (
                    @Suppress("TooGenericExceptionCaught") failed: Exception,
                ) {
                    // Bluetooth went off or lost its permission; the radio state says which.
                    radio.recheck()
                }
            }
    }

    private fun stopScan() {
        scanJob?.cancel()
        scanJob = null
        noHubJob?.cancel()
        noHubJob = null
    }

    private fun onAdvert(advert: SetupAdvert) {
        val name = advert.name ?: return
        if (!name.startsWith(SetupGatt.HUB_NAME_PREFIX)) return
        val shortId = name.removePrefix(SetupGatt.HUB_NAME_PREFIX).trim()
        if (shortId.isEmpty()) return
        val candidate = HubCandidate(advert.id, shortId, advert.rssi)
        val current = mutableState.value
        val others = current.candidates.filterNot { it.peripheralId == advert.id }
        // Strongest first; the platform identifier breaks ties so the order is stable.
        val sorted =
            (others + candidate).sortedWith(
                compareByDescending<HubCandidate> { it.rssi }.thenBy { it.shortId },
            )
        val selected = current.selected?.let { chosen -> sorted.firstOrNull { it.peripheralId == chosen.peripheralId } }
        val fresh = announced.add(advert.id)
        mutableState.value =
            current.copy(
                candidates = sorted,
                selected = selected ?: current.selected,
                noHubYet = false,
                announcement =
                    if (fresh) {
                        announcement(AnnouncementKind.CandidateFound, hub = shortId, signal = candidate.signal)
                    } else {
                        current.announcement
                    },
            )
    }

    private fun backToScan() {
        // Work in flight belongs to the session being left; it must not end on an outcome.
        workJob?.cancel()
        disconnect()
        update {
            it.copy(
                step = SetupStep.Scan,
                candidates = emptyList(),
                selected = null,
                code = it.code.copy(error = null, working = false, accepted = false),
                identity = null,
                wifi = WifiForm.EMPTY,
            )
        }
        if (radio.state.value == RadioState.Ready) startScan()
    }

    // Step 2: setup code

    public fun setCode(text: String) {
        update {
            if (it.code.working || it.code.accepted) it else it.copy(code = it.code.copy(text = text, error = null))
        }
    }

    /** Opens the BLE session with the code; the first sealed reply tells whether it matches. */
    public fun submitCode() {
        val current = mutableState.value
        val hub = current.selected ?: return
        if (current.step != SetupStep.Code || current.code.working) return
        if (current.code.accepted) {
            continueFromCode()
            return
        }
        val code = SetupCode.normalize(current.code.text)
        if (!SetupCode.isUsable(code)) {
            update { it.copy(code = it.code.copy(error = CodeError.Blank)) }
            return
        }
        update { it.copy(code = it.code.copy(error = null, working = true)) }
        work { started ->
            val message =
                try {
                    ensureLink(hub)
                    val fresh = newSession()
                    session?.close()
                    session = fresh
                    bound = false
                    send(fresh.hello())
                    fresh.onHelloReply(receive(), code)
                    exchange(SetupMessage(identity_request = IdentityRequest()))
                } catch (wrong: SessionException) {
                    if (wrong.error != SessionError.WrongSetupCode) throw wrong
                    // The Hub answered under its own keys and hangs up; the next try reconnects.
                    disconnect()
                    if (started == flow) {
                        update {
                            it.copy(
                                code = it.code.copy(working = false, error = CodeError.WrongCode),
                                announcement = announcement(AnnouncementKind.WrongCode, hub = hub.shortId),
                            )
                        }
                    }
                    return@work
                }
            val identity = message.identity ?: throw SessionException(SessionError.Malformed)
            if (identity.device_id.size != CryptoSpec.DEVICE_ID_LENGTH) throw SessionException(SessionError.Malformed)
            update {
                it.copy(
                    code = it.code.copy(working = false, accepted = true, error = null),
                    identity = HubIdentity(identity.device_id.toByteArray().toLowerHex(), identity.firmware_version),
                )
            }
        }
    }

    /** Continues after ACCEPTED to the Wi-Fi networks, which the Hub scans for now. */
    public fun continueFromCode() {
        val current = mutableState.value
        if (current.step != SetupStep.Code || !current.code.accepted) return
        update { it.copy(step = SetupStep.Wifi) }
        // One exchange at a time: a scan request still in flight fills the list when it lands.
        if (current.wifi.networks == null && workJob?.isActive != true) requestNetworks()
    }

    // Step 3: Wi-Fi

    private fun requestNetworks() {
        work {
            val reply = exchange(SetupMessage(wifi_scan_request = WifiScanRequest()))
            val list = reply.wifi_scan_list ?: throw SessionException(SessionError.Malformed)
            val rows = list.networks.map { WifiNetworkRow(it.ssid, it.security.toSecurity(), it.rssi) }
            update { it.copy(wifi = it.wifi.copy(networks = rows)) }
        }
    }

    /** Picks a network row; WPA3-only and other unsupported rows cannot be picked (UX-DR42). */
    public fun chooseNetwork(ssid: String) {
        val current = mutableState.value
        val row = current.wifi.networks?.firstOrNull { it.ssid == ssid } ?: return
        if (!row.security.supported || current.step != SetupStep.Wifi) return
        update {
            val keep = !it.wifi.other && it.wifi.ssid == ssid
            it.copy(
                wifi =
                    it.wifi.copy(
                        ssid = ssid,
                        other = false,
                        password = if (keep) it.wifi.password else "",
                        error = null,
                    ),
            )
        }
    }

    /** "Other network": the SSID is typed and sent as typed. */
    public fun chooseOtherNetwork() {
        update {
            if (it.step != SetupStep.Wifi || it.wifi.other) {
                it
            } else {
                it.copy(wifi = it.wifi.copy(ssid = "", other = true, password = "", error = null))
            }
        }
    }

    public fun setOtherSsid(ssid: String) {
        update { if (!it.wifi.other) it else it.copy(wifi = it.wifi.copy(ssid = ssid, error = null)) }
    }

    public fun setPassword(password: String) {
        update { it.copy(wifi = it.wifi.copy(password = password, error = null)) }
    }

    /** Checks the network and continues to the Site, or straight to step 5 once enrolled. */
    public fun continueFromWifi() {
        val current = mutableState.value
        if (current.step != SetupStep.Wifi) return
        val wifi = current.wifi
        val error =
            when {
                wifi.ssid.isEmpty() -> WifiError.SsidBlank
                wifi.ssid.encodeToByteArray().size > MAX_SSID_BYTES -> WifiError.SsidTooLong
                wifi.password.encodeToByteArray().size > MAX_PASSWORD_BYTES -> WifiError.PasswordTooLong
                else -> null
            }
        if (error != null) {
            update { it.copy(wifi = it.wifi.copy(error = error)) }
            return
        }
        if (bound) {
            startProgress()
            return
        }
        update { it.copy(step = SetupStep.Site) }
        if (enrolmentKey == null) loadKey()
    }

    // Step 4: Site and enrolment key

    public fun chooseSite(siteId: String) {
        update {
            if (it.step != SetupStep.Site || it.site.sites.none { site -> site.id == siteId }) {
                it
            } else {
                it.copy(site = it.site.copy(selectedId = siteId))
            }
        }
    }

    /** Try again after the enrolment key could not be read. */
    public fun retryKey() {
        if (mutableState.value.step == SetupStep.Site && enrolmentKey == null) loadKey()
    }

    private fun loadKey() {
        if (mutableState.value.site.loadingKey) return
        update { it.copy(site = it.site.copy(loadingKey = true, notice = null)) }
        val started = flow
        scope.launch {
            val result = api.enrolmentKey()
            if (started != flow) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    val key = decodeKey(result.value.publicKey)
                    val fingerprint = result.value.fingerprint.lowercase()
                    if (key == null || enrolmentKeyFingerprint(key) != fingerprint) {
                        fail(OutcomeKind.FingerprintMismatch, disconnect = true)
                        return@launch
                    }
                    enrolmentKey = key
                    update { it.copy(site = it.site.copy(loadingKey = false, fingerprint = fingerprint)) }
                }

                is ApiResult.Failed -> {
                    if (result.failure == ApiFailure.Unauthorized) {
                        close()
                        return@launch
                    }
                    val notice =
                        when (result.failure) {
                            ApiFailure.Unreachable -> KeyNotice.Unreachable
                            ApiFailure.Certificate -> KeyNotice.Certificate
                            else -> KeyNotice.Unexpected
                        }
                    update { it.copy(site = it.site.copy(loadingKey = false, notice = notice)) }
                }
            }
        }
    }

    /** Adds the Hub to the chosen Site: step 5. */
    public fun start() {
        val current = mutableState.value
        if (current.step != SetupStep.Site || current.site.selectedId == null || current.site.fingerprint == null) {
            return
        }
        startProgress()
    }

    // Step 5: progress

    private fun startProgress() {
        val current = mutableState.value
        val siteId = current.site.selectedId ?: return
        val identity = current.identity ?: return
        val key = enrolmentKey ?: return
        val wifi = current.wifi
        update {
            it.copy(
                step = SetupStep.Progress,
                outcome = null,
                progress = ProgressState(reached = if (bound) 1 else 0, elapsedSeconds = 0),
            )
        }
        startTicking()
        work { started ->
            if (!bound) {
                val session = session ?: throw SetupLinkException("no session")
                send(session.seal(SetupMessage(site_binding = SiteBinding(site_id = siteId, server_url = serverUrl))))
                val reply =
                    exchange(
                        SetupMessage(
                            enrolment_request =
                                EnrolmentRequest(
                                    server_public_key = key.toByteString(),
                                    fingerprint = current.site.fingerprint.orEmpty(),
                                ),
                        ),
                    )
                reply.error?.let { error ->
                    fail(
                        if (error.code == SetupErrorCode.SETUP_ERROR_CODE_FINGERPRINT_MISMATCH) {
                            OutcomeKind.FingerprintMismatch
                        } else {
                            OutcomeKind.HubRefused
                        },
                        disconnect = true,
                    )
                    return@work
                }
                val sealed = reply.enrolment_response ?: throw SessionException(SessionError.Malformed)
                if (sealed.device_id.toByteArray().toLowerHex() != identity.deviceId) {
                    throw SessionException(SessionError.Malformed)
                }
                val request =
                    EnrolDeviceRequestDto(
                        deviceId = identity.deviceId,
                        kind = DEVICE_KIND_HUB,
                        enc = BASE64URL.encode(sealed.enc.toByteArray()),
                        ciphertext = BASE64URL.encode(sealed.ciphertext.toByteArray()),
                    )
                val result = api.enrolDevice(siteId, request, keyFor(identity.deviceId, siteId))
                if (started != flow) return@work
                when (result) {
                    is ApiResult.Ok -> {
                        bound = true
                        update { it.copy(progress = it.progress.copy(reached = 1)) }
                    }

                    is ApiResult.Failed -> {
                        if (result.failure == ApiFailure.Unauthorized) {
                            close()
                            return@work
                        }
                        fail(enrolmentOutcome(result.failure), disconnect = true)
                        return@work
                    }
                }
            }
            sendWifi(wifi, started)
        }
    }

    private suspend fun sendWifi(
        wifi: WifiForm,
        started: Int,
    ) {
        val session = session ?: throw SetupLinkException("no session")
        send(session.seal(SetupMessage(wifi_config = WifiConfig(ssid = wifi.ssid, password = wifi.password))))
        if (started != flow) return
        update {
            it.copy(
                progress = it.progress.copy(reached = 2),
                announcement = announcement(AnnouncementKind.WifiSent, ssid = wifi.ssid),
            )
        }
        startTimeout(started)
        val reply = receive(timeout = null)
        timeoutJob?.cancel()
        if (started != flow) return
        val result = session.open(reply)
        val status =
            result.wifi_result?.status ?: run {
                fail(OutcomeKind.HubRefused, disconnect = true)
                return
            }
        when (status) {
            WifiStatus.WIFI_STATUS_CONNECTED -> {
                tickJob?.cancel()
                disconnect()
                update {
                    it.copy(
                        progress = it.progress.copy(reached = ProgressSegment.entries.size),
                        outcome = SetupOutcome(OutcomeKind.Online),
                        announcement = announcement(AnnouncementKind.ServerSees, hub = it.hubId),
                        wifi = it.wifi.copy(password = ""),
                    )
                }
            }

            WifiStatus.WIFI_STATUS_WRONG_PASSWORD -> {
                fail(OutcomeKind.WrongPassword, disconnect = false)
            }

            WifiStatus.WIFI_STATUS_NETWORK_NOT_FOUND -> {
                fail(OutcomeKind.NetworkNotFound, disconnect = false)
            }

            WifiStatus.WIFI_STATUS_UNSUPPORTED_SECURITY -> {
                fail(OutcomeKind.UnsupportedSecurity, disconnect = false)
            }

            WifiStatus.WIFI_STATUS_NO_SERVER -> {
                fail(OutcomeKind.NoServer, disconnect = false)
            }

            else -> {
                fail(OutcomeKind.HubRefused, disconnect = true)
            }
        }
    }

    private fun startTicking() {
        tickJob?.cancel()
        val started = flow
        tickJob =
            scope.launch {
                while (true) {
                    delay(1.seconds)
                    if (started != flow || mutableState.value.outcome != null) return@launch
                    update { it.copy(progress = it.progress.copy(elapsedSeconds = it.progress.elapsedSeconds + 1)) }
                }
            }
    }

    /** 90 s after `WifiConfig` without a `WifiResult`, held while a screen reader speaks (UX-DR103). */
    private fun startTimeout(started: Int) {
        timeoutJob?.cancel()
        timeoutJob =
            scope.launch {
                delay(PROGRESS_TIMEOUT)
                speaking.first { !it }
                if (started != flow || mutableState.value.outcome != null) return@launch
                workJob?.cancel()
                fail(OutcomeKind.Timeout, disconnect = true)
            }
    }

    // Outcome actions

    /** Runs an outcome screen's action. */
    public fun outcomeAction(action: OutcomeAction) {
        val current = mutableState.value
        val outcome = current.outcome ?: return
        if (action != outcome.primary && action != outcome.secondary) return
        when (action) {
            OutcomeAction.AddNode, OutcomeAction.Close -> {
                close()
            }

            OutcomeAction.ReenterPassword, OutcomeAction.ChooseNetwork -> {
                update {
                    it.copy(
                        step = SetupStep.Wifi,
                        outcome = null,
                        wifi = it.wifi.copy(password = "", other = it.wifi.other, error = null),
                    )
                }
            }

            OutcomeAction.OtherNetwork -> {
                update {
                    it.copy(
                        step = SetupStep.Wifi,
                        outcome = null,
                        wifi = it.wifi.copy(ssid = "", other = true, password = "", error = null),
                    )
                }
            }

            OutcomeAction.RetryWifi -> {
                startProgress()
            }

            OutcomeAction.Help -> {
                update { it.copy(outcome = outcome.copy(helpShown = true)) }
            }

            OutcomeAction.StartOver -> {
                startOver()
            }
        }
    }

    private fun startOver() {
        flow++
        for (job in listOf(workJob, tickJob, timeoutJob, noHubJob, scanJob)) job?.cancel()
        disconnect()
        val started = flow
        val current = mutableState.value
        mutableState.value =
            HubSetupState.CLOSED.copy(
                open = true,
                radio = radio.state.value,
                site = current.site.copy(loadingKey = false, notice = null),
                announcement = current.announcement,
                serverHost = serverHost,
            )
        watchJob?.cancel()
        watchJob =
            scope.launch {
                radio.state.collect { radioState ->
                    if (started != flow) return@collect
                    update { it.copy(radio = radioState) }
                    if (radioState == RadioState.Ready) startScan() else stopScan()
                }
            }
    }

    // BLE session plumbing

    /** Runs [block] as the one piece of BLE work in flight; failures become outcomes. */
    private fun work(block: suspend (started: Int) -> Unit) {
        val started = flow
        workJob =
            scope.launch {
                try {
                    block(started)
                } catch (cancellation: CancellationException) {
                    throw cancellation
                } catch (lost: SetupLinkException) {
                    if (started == flow) fail(OutcomeKind.LostConnection, disconnect = true)
                } catch (refused: SessionException) {
                    if (started == flow) fail(OutcomeKind.HubRefused, disconnect = true)
                }
            }
    }

    private suspend fun ensureLink(hub: HubCandidate) {
        if (link != null) return
        val generation = ++linkGeneration
        val connected = radio.connect(hub.peripheralId)
        if (generation != linkGeneration) {
            connected.close()
            throw SetupLinkException("superseded")
        }
        val channel = Channel<ByteArray>(Channel.UNLIMITED)
        link = connected
        frames = channel
        bound = false
        val reassembler = Reassembler()
        scope.launch {
            try {
                connected.incoming.collect { payload ->
                    val step = reassembler.push(payload)
                    if (step is Reassembled.Frame) channel.trySend(step.bytes)
                }
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught") failed: Exception,
            ) {
                // The link failed; handled below as a close.
            } finally {
                reassembler.reset()
                channel.close()
            }
            onLinkClosed(generation)
        }
    }

    private fun onLinkClosed(generation: Int) {
        if (generation != linkGeneration) return
        link = null
        bound = false
        // Work in flight still reads the frames that arrived before the close (a wrong-code
        // reply comes just before the Hub hangs up), then fails as a lost connection itself.
        if (workJob?.isActive == true) return
        disconnect()
        val current = mutableState.value
        if (current.open && current.outcome == null && current.step != SetupStep.Scan) {
            fail(OutcomeKind.LostConnection, disconnect = false)
        }
    }

    private fun disconnect() {
        linkGeneration++
        val closing = link
        link = null
        frames?.close()
        frames = null
        session?.close()
        session = null
        bound = false
        if (closing != null) scope.launch { runCatching { closing.close() } }
    }

    private suspend fun send(frame: ByteArray) {
        val current = link ?: throw SetupLinkException("not connected")
        for (payload in Framing.fragments(frame, current.maxPayload)) current.write(payload)
    }

    private suspend fun receive(timeout: Duration? = REPLY_TIMEOUT): ByteArray {
        val channel = frames ?: throw SetupLinkException("not connected")
        return try {
            if (timeout == null) {
                channel.receive()
            } else {
                withTimeoutOrNull(timeout) { channel.receive() } ?: throw SetupLinkException("no reply")
            }
        } catch (closed: ClosedReceiveChannelException) {
            throw SetupLinkException("link closed", closed)
        }
    }

    /** Sends one sealed request and opens the reply. */
    private suspend fun exchange(message: SetupMessage): SetupMessage {
        val session = session ?: throw SetupLinkException("no session")
        send(session.seal(message))
        return session.open(receive())
    }

    private fun fail(
        kind: OutcomeKind,
        disconnect: Boolean,
    ) {
        if (disconnect) disconnect()
        tickJob?.cancel()
        timeoutJob?.cancel()
        update {
            it.copy(
                outcome = SetupOutcome(kind),
                code = it.code.copy(working = false),
                site = it.site.copy(loadingKey = false),
                announcement =
                    announcement(
                        AnnouncementKind.Error,
                        hub = it.hubId,
                        ssid = it.wifi.ssid,
                        outcome = kind,
                    ),
            )
        }
    }

    // Helpers

    private fun keyFor(
        deviceId: String,
        siteId: String,
    ): String {
        val current = idempotency
        if (current != null && current.first == "$deviceId/$siteId") return current.second
        val fresh = newKey()
        idempotency = "$deviceId/$siteId" to fresh
        return fresh
    }

    private fun siteChoices(): Pair<List<SiteChoice>, String?>? {
        val ready = sites.value as? SitesState.Ready ?: return null
        val allowed =
            ready.sites
                .filter { it.role >= SiteRole.Administrator }
                .map { SiteChoice(it.id, it.name) }
        if (allowed.isEmpty()) return null
        val selected = allowed.firstOrNull { it.id == ready.current.id }?.id ?: allowed.first().id
        return allowed to selected
    }

    private fun announcement(
        kind: AnnouncementKind,
        hub: String? = null,
        ssid: String? = null,
        signal: SignalStrength? = null,
        outcome: OutcomeKind? = null,
    ): SetupAnnouncement =
        SetupAnnouncement(
            id = ++announcementId,
            kind = kind,
            assertive = kind == AnnouncementKind.WrongCode || kind == AnnouncementKind.Error,
            hub = hub,
            ssid = ssid,
            signal = signal,
            outcome = outcome,
        )

    private fun update(change: (HubSetupState) -> HubSetupState) {
        val current = mutableState.value
        if (!current.open) return
        mutableState.value = change(current)
    }

    public companion object {
        /** "No Hub in range yet" after this long without one (UX-DR94). */
        public val NO_HUB_AFTER: Duration = 30.seconds

        /** No `WifiResult` this long after `WifiConfig` ends on the timeout outcome (UX-DR40). */
        public val PROGRESS_TIMEOUT: Duration = 90.seconds

        /** One ordinary reply (identity, scan list, enrolment). */
        public val REPLY_TIMEOUT: Duration = 30.seconds

        public const val DEVICE_KIND_HUB: String = "hub"
        public const val MAX_SSID_BYTES: Int = 32
        public const val MAX_PASSWORD_BYTES: Int = 64

        private val BASE64URL = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT)
        private val BASE64URL_READ = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT_OPTIONAL)

        private fun decodeKey(text: String): ByteArray? =
            try {
                BASE64URL_READ.decode(text).takeIf { it.size == CryptoSpec.X25519_KEY_LENGTH }
            } catch (invalid: IllegalArgumentException) {
                null
            }

        private fun enrolmentOutcome(failure: ApiFailure): OutcomeKind =
            when (failure) {
                ApiFailure.DeviceOnAnotherSite, ApiFailure.LotClaimed -> OutcomeKind.OnAnotherSite
                ApiFailure.Forbidden -> OutcomeKind.NotAllowed
                ApiFailure.NotFound -> OutcomeKind.SiteGone
                else -> OutcomeKind.ServerUnreachable
            }

        private fun WifiSecurity.toSecurity(): NetworkSecurity =
            when (this) {
                WifiSecurity.WIFI_SECURITY_OPEN -> NetworkSecurity.Open
                WifiSecurity.WIFI_SECURITY_WPA2_PERSONAL -> NetworkSecurity.Wpa2
                WifiSecurity.WIFI_SECURITY_WPA3_TRANSITION -> NetworkSecurity.Wpa3Transition
                WifiSecurity.WIFI_SECURITY_WPA3_ONLY -> NetworkSecurity.Wpa3Only
                else -> NetworkSecurity.Other
            }
    }
}
