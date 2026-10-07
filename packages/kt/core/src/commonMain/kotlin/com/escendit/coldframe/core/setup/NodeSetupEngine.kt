package com.escendit.coldframe.core.setup

import coldframe.setup.v1.DeviceKind
import coldframe.setup.v1.EnrolmentRequest
import coldframe.setup.v1.IdentityRequest
import coldframe.setup.v1.SetupErrorCode
import coldframe.setup.v1.SetupMessage
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.EnrolDeviceRequestDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.crypto.CryptoSpec
import com.escendit.coldframe.core.crypto.enrolmentKeyFingerprint
import com.escendit.coldframe.core.crypto.toLowerHex
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.NameError
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

/**
 * Add a Node (AD-25, UX-DR67): the setup client of the KMP core over a [SetupRadio], for one
 * Site. One [state] for the shells; every action is a method.
 *
 * One BLE session per code attempt, on Continue of step 3: connect → hello → `IdentityRequest`
 * (a reply that does not open is the wrong code; the kind must be `NODE`) → `GET /enrolment-key`
 * with the fingerprint recomputed → `EnrolmentRequest` → `EnrolmentResponse`. The engine then
 * holds the sealed `K_dev` in memory, disconnects, and only then shows the accepted chip. A Node
 * drops an idle connection after 60 s and listens for 180 s in total, so nothing is held open
 * through the Lot picker; ending the connection also ends the Node's setup window. A Node is
 * never sent a `SiteBinding`, a `WifiScanRequest` or a `WifiConfig`.
 *
 * Step 4 needs no BLE: the Lot is a Server matter. [assign] posts the sealed key, relayed unread,
 * with the Lot; a refused Lot is answered on step 4 and retried without a second session.
 *
 * The setup code never leaves this engine except as the salt of the session keys, and nothing
 * here logs.
 */
public class NodeSetupEngine(
    private val radio: SetupRadio,
    private val api: EnrolmentApi,
    private val lots: LotsApi,
    private val sites: StateFlow<SitesState>,
    private val scope: CoroutineScope,
    private val onAssigned: () -> Unit = {},
    private val onCalibrate: (lotId: String, lotName: String) -> Unit = { _, _ -> },
    private val newKey: () -> String = SitesEngine::randomKey,
    private val newSession: () -> SetupSession = { SetupSession() },
) {
    private val mutableState = MutableStateFlow(NodeSetupState.CLOSED)

    /** One observable state for the shells. */
    public val state: StateFlow<NodeSetupState> = mutableState.asStateFlow()

    private val speaking = MutableStateFlow(false)

    /** Bumped on every open, close and start over: work of an older attempt is dropped. */
    private var flow = 0

    /** Bumped on every connect and deliberate disconnect: a close of an older link is ignored. */
    private var linkGeneration = 0
    private var link: SetupLink? = null
    private var frames: Channel<ByteArray>? = null
    private var session: SetupSession? = null

    /** The Node's sealed `K_dev`, held from the enrolment exchange until it is assigned. */
    private var sealed: SealedKey? = null

    /** The Lot the flow was opened for (a *no Node* tile), preselected when it can be picked. */
    private var wantedLot: String? = null
    private var idempotency: Pair<String, String>? = null
    private var createKey: String? = null
    private var announcementId = 0
    private val announced = mutableSetOf<String>()

    /** The peripherals of the shown list in the order they were first heard. */
    private val heard = mutableListOf<String>()

    private var watchJob: Job? = null
    private var sitesJob: Job? = null
    private var scanJob: Job? = null
    private var noNodeJob: Job? = null
    private var workJob: Job? = null
    private var lotsJob: Job? = null

    // Opening and closing

    /**
     * Opens the flow on step 1 for [siteId], or the current Site. Does nothing unless the caller
     * is an Administrator or Owner there. [lotId] is preselected on step 4 if it has no Node.
     */
    public fun open(
        siteId: String? = null,
        lotId: String? = null,
    ) {
        if (mutableState.value.open) return
        val ready = sites.value as? SitesState.Ready ?: return
        val site = ready.sites.firstOrNull { it.id == (siteId ?: ready.current.id) } ?: return
        if (site.role < SiteRole.Administrator) return
        flow++
        announced.clear()
        heard.clear()
        sealed = null
        createKey = null
        wantedLot = lotId
        mutableState.value =
            NodeSetupState.CLOSED.copy(
                open = true,
                radio = radio.state.value,
                siteId = site.id,
                siteName = site.name,
            )
        watchRadio()
        sitesJob =
            scope.launch {
                // Signed out or without Sites: the flow ends. [close] cancels this watch.
                sites.first { it !is SitesState.Ready }
                if (mutableState.value.open) close()
            }
    }

    // The Role on the flow's Site; adding a Node already needs Administrator, so a success may calibrate.
    private fun canCalibrate(): Boolean {
        val ready = sites.value as? SitesState.Ready ?: return false
        val role = ready.sites.firstOrNull { it.id == mutableState.value.siteId }?.role ?: return false
        return role >= SiteRole.Administrator
    }

    /** Closes the flow, disconnects and forgets the sealed key. */
    public fun close() {
        flow++
        for (job in listOf(watchJob, sitesJob, scanJob, noNodeJob, workJob, lotsJob)) job?.cancel()
        disconnect()
        sealed = null
        mutableState.value = NodeSetupState.CLOSED
    }

    /** Re-reads the radio after a permission prompt or a return to the foreground. */
    public fun recheckRadio() {
        radio.recheck()
    }

    /**
     * Whether a screen reader is reading an announcement, as the shells report it for every setup
     * flow. No step of Add a Node ends on a timer, so nothing waits on it here.
     */
    public fun announcing(active: Boolean) {
        speaking.value = active
    }

    /** Top-left: Cancel on step 1, one step back on 2 and 3, and the leave question on step 4. */
    public fun back() {
        val current = mutableState.value
        if (!current.open || current.confirmingLeave) return
        when {
            current.outcome != null -> close()
            current.step == NodeSetupStep.Press -> close()
            current.step == NodeSetupStep.Scan -> backToPress()
            current.code.working -> Unit
            current.step == NodeSetupStep.Code -> backToScan()
            else -> leave()
        }
    }

    /**
     * System back: asks once a Node is selected; before that it goes where [back] goes. An
     * assignment under way is left to answer first, so none lands unseen.
     */
    public fun leave() {
        val current = mutableState.value
        if (!current.open || current.lots.assigning) return
        when {
            current.outcome != null -> close()
            current.selected != null -> update { it.copy(confirmingLeave = true) }
            current.step == NodeSetupStep.Scan -> backToPress()
            else -> close()
        }
    }

    /** "Stop setting up Node 7C19?" confirmed: disconnects and closes. */
    public fun confirmLeave() {
        if (mutableState.value.confirmingLeave) close()
    }

    public fun stayInFlow() {
        update { it.copy(confirmingLeave = false) }
    }

    // Step 1: press

    /** "Look for the Node": on to the list, scanning while the radio is ready. */
    public fun continueFromPress() {
        val current = mutableState.value
        if (!current.open || current.step != NodeSetupStep.Press || current.outcome != null) return
        update { it.copy(step = NodeSetupStep.Scan) }
        if (radio.state.value == RadioState.Ready) startScan()
    }

    // Step 2: scan and select

    public fun select(peripheralId: String) {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Scan || current.outcome != null) return
        val candidate = current.candidates.firstOrNull { it.peripheralId == peripheralId } ?: return
        update { it.copy(selected = candidate) }
    }

    /** Continues with the selected Node to the setup code. */
    public fun continueFromScan() {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Scan || current.selected == null) return
        stopScan()
        update { it.copy(step = NodeSetupStep.Code, noNodeYet = false) }
    }

    private fun watchRadio() {
        val started = flow
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

    private fun startScan() {
        val current = mutableState.value
        if (!current.open ||
            current.step != NodeSetupStep.Scan ||
            current.outcome != null ||
            scanJob?.isActive == true
        ) {
            return
        }
        val started = flow
        noNodeJob?.cancel()
        noNodeJob =
            scope.launch {
                delay(NO_NODE_AFTER)
                if (started == flow && mutableState.value.candidates.isEmpty()) update { it.copy(noNodeYet = true) }
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
        noNodeJob?.cancel()
        noNodeJob = null
    }

    private fun onAdvert(advert: SetupAdvert) {
        val name = advert.name ?: return
        if (!name.startsWith(SetupGatt.NODE_NAME_PREFIX)) return
        val shortId = name.removePrefix(SetupGatt.NODE_NAME_PREFIX).trim()
        if (shortId.isEmpty()) return
        val current = mutableState.value
        if (current.step != NodeSetupStep.Scan) return
        if (advert.id !in heard) heard += advert.id
        // Every listed Node is in setup mode, so the badge goes to the one first heard last.
        val latest = heard.last()
        val candidate = NodeCandidate(advert.id, shortId, advert.rssi, pressedJustNow = false)
        // Strongest first; the short name breaks ties so the order is stable.
        val sorted =
            (current.candidates.filterNot { it.peripheralId == advert.id } + candidate)
                .map { it.copy(pressedJustNow = it.peripheralId == latest) }
                .sortedWith(compareByDescending<NodeCandidate> { it.rssi }.thenBy { it.shortId })
        val selected = current.selected?.let { chosen -> sorted.firstOrNull { it.peripheralId == chosen.peripheralId } }
        val fresh = announced.add(advert.id)
        mutableState.value =
            current.copy(
                candidates = sorted,
                selected = selected ?: current.selected,
                noNodeYet = false,
                announcement =
                    if (fresh) {
                        announcement(NodeAnnouncementKind.CandidateFound, node = shortId, signal = candidate.signal)
                    } else {
                        current.announcement
                    },
            )
    }

    private fun backToPress() {
        stopScan()
        heard.clear()
        update {
            it.copy(step = NodeSetupStep.Press, candidates = emptyList(), selected = null, noNodeYet = false)
        }
    }

    private fun backToScan() {
        // Work in flight belongs to the session being left; it must not end on an outcome.
        workJob?.cancel()
        lotsJob?.cancel()
        disconnect()
        sealed = null
        heard.clear()
        update {
            it.copy(
                step = NodeSetupStep.Scan,
                candidates = emptyList(),
                selected = null,
                code = it.code.copy(error = null, working = false, accepted = false),
                deviceId = null,
                lots = LotForm.EMPTY,
            )
        }
        if (radio.state.value == RadioState.Ready) startScan()
    }

    // Step 3: setup code and the BLE session

    public fun setCode(text: String) {
        update {
            if (it.code.working || it.code.accepted) it else it.copy(code = it.code.copy(text = text, error = null))
        }
    }

    /**
     * Runs the BLE session with the code: the first sealed reply tells whether it matches, then
     * the Node seals its key for the Server. Nothing is sent to the Server's Devices here.
     */
    public fun submitCode() {
        val current = mutableState.value
        val node = current.selected ?: return
        if (current.step != NodeSetupStep.Code || current.code.working || current.outcome != null) return
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
            try {
                ensureLink(node)
            } catch (gone: SetupLinkException) {
                // The 180 s window is not signalled: a Node that cannot be connected to has left it.
                if (started == flow) fail(NodeOutcomeKind.StoppedListening)
                return@work
            }
            val identity =
                try {
                    val fresh = newSession()
                    session?.close()
                    session = fresh
                    send(fresh.hello())
                    fresh.onHelloReply(receive(), code)
                    exchange(SetupMessage(identity_request = IdentityRequest())).identity
                } catch (wrong: SessionException) {
                    if (wrong.error != SessionError.WrongSetupCode) throw wrong
                    // The Node answered under its own keys and hangs up; the next try reconnects.
                    disconnect()
                    if (started == flow) {
                        update {
                            it.copy(
                                code = it.code.copy(working = false, error = CodeError.WrongCode),
                                announcement = announcement(NodeAnnouncementKind.WrongCode, node = node.shortId),
                            )
                        }
                    }
                    return@work
                }
            if (identity == null ||
                identity.kind != DeviceKind.DEVICE_KIND_NODE ||
                identity.device_id.size != CryptoSpec.DEVICE_ID_LENGTH
            ) {
                throw SessionException(SessionError.Malformed)
            }
            val deviceId = identity.device_id.toByteArray().toLowerHex()
            val key = enrolmentKey(started) ?: return@work
            enrol(deviceId, key)
        }
    }

    /** `GET /enrolment-key`, checked against its fingerprint; null when the flow ended on an outcome. */
    private suspend fun enrolmentKey(started: Int): Pair<ByteArray, String>? {
        val result = api.enrolmentKey()
        if (started != flow) return null
        return when (result) {
            is ApiResult.Ok -> {
                val key = decodeKey(result.value.publicKey)
                val fingerprint = result.value.fingerprint.lowercase()
                if (key == null || enrolmentKeyFingerprint(key) != fingerprint) {
                    fail(NodeOutcomeKind.FingerprintMismatch)
                    null
                } else {
                    key to fingerprint
                }
            }

            is ApiResult.Failed -> {
                if (result.failure == ApiFailure.Unauthorized) close() else fail(NodeOutcomeKind.ServerUnreachable)
                null
            }
        }
    }

    /** `EnrolmentRequest` → `EnrolmentResponse`: keeps the sealed key, hangs up, then shows the chip. */
    private suspend fun enrol(
        deviceId: String,
        key: Pair<ByteArray, String>,
    ) {
        val reply =
            exchange(
                SetupMessage(
                    enrolment_request =
                        EnrolmentRequest(server_public_key = key.first.toByteString(), fingerprint = key.second),
                ),
            )
        reply.error?.let { error ->
            fail(
                if (error.code == SetupErrorCode.SETUP_ERROR_CODE_FINGERPRINT_MISMATCH) {
                    NodeOutcomeKind.FingerprintMismatch
                } else {
                    NodeOutcomeKind.NodeRefused
                },
            )
            return
        }
        val response = reply.enrolment_response ?: throw SessionException(SessionError.Malformed)
        if (response.device_id.toByteArray().toLowerHex() != deviceId) throw SessionException(SessionError.Malformed)
        sealed =
            SealedKey(
                deviceId = deviceId,
                enc = BASE64URL.encode(response.enc.toByteArray()),
                ciphertext = BASE64URL.encode(response.ciphertext.toByteArray()),
            )
        // Ending the connection closes the Node's setup window, so it measures and sleeps at once.
        disconnect()
        update { it.copy(code = it.code.copy(working = false, accepted = true, error = null), deviceId = deviceId) }
    }

    /** Continues after ACCEPTED to the Lot picker, which reads the Site's Lots now. */
    public fun continueFromCode() {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Code || !current.code.accepted || sealed == null) return
        update { it.copy(step = NodeSetupStep.Lot) }
        if (current.lots.choices == null) loadLots(select = wantedLot)
    }

    // Step 4: the Lot

    /** Try again after the Lots could not be read. */
    public fun retryLots() {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Lot || current.outcome != null || !current.lots.retryable) return
        update { it.copy(lots = it.lots.copy(notice = null)) }
        loadLots(select = wantedLot)
    }

    /**
     * Reads the Site's Lots. [select] is picked if it has no Node; otherwise the current pick is
     * kept while it can still be picked. A failed read keeps a list that is already shown.
     * [keepNotice] keeps the notice of a create that did not happen.
     */
    private fun loadLots(
        select: String? = null,
        keepNotice: Boolean = false,
    ) {
        val started = flow
        val siteId = mutableState.value.siteId
        lotsJob?.cancel()
        lotsJob =
            scope.launch {
                val result = lots.listLots(siteId)
                if (started != flow) return@launch
                when (result) {
                    is ApiResult.Ok -> {
                        // The Server's order, never re-sorted (UX-DR20); removed Lots are not listed.
                        val choices =
                            result.value.lots
                                .filter { it.removed != true }
                                .map { it.toChoice() }
                        update { state ->
                            // An assignment under way or done names its Lot: a late read must not
                            // drop the selection, or the outcome loses the Lot's name.
                            if (state.lots.assigning || state.outcome != null) return@update state
                            val wanted = select ?: state.lots.selectedId
                            val read = !keepNotice && state.lots.notice?.kind in READ_NOTICES
                            state.copy(
                                lots =
                                    state.lots.copy(
                                        choices = choices,
                                        selectedId = choices.firstOrNull { it.id == wanted && it.selectable }?.id,
                                        notice = if (read) null else state.lots.notice,
                                    ),
                            )
                        }
                    }

                    is ApiResult.Failed -> {
                        if (result.failure == ApiFailure.Unauthorized) {
                            close()
                        } else {
                            update { state ->
                                if (state.lots.choices != null) {
                                    state
                                } else {
                                    state.copy(
                                        lots = state.lots.copy(notice = LotPickerNotice(readNotice(result.failure))),
                                    )
                                }
                            }
                        }
                    }
                }
            }
    }

    /** Picks a Lot; one that has a Node cannot be picked (UX-DR38). */
    public fun chooseLot(lotId: String) {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Lot || current.outcome != null) return
        if (current.lots.assigning || current.lots.creating) return
        val choice = current.lots.choices?.firstOrNull { it.id == lotId } ?: return
        if (!choice.selectable) return
        update {
            it.copy(
                lots =
                    it.lots.copy(
                        selectedId = lotId,
                        newLotOpen = false,
                        newLotName = "",
                        newLotError = null,
                        notice = null,
                    ),
            )
        }
    }

    /** "+ New Lot": opens the inline name field. */
    public fun openNewLot() {
        val current = mutableState.value
        if (current.step != NodeSetupStep.Lot || current.outcome != null) return
        if (current.lots.choices == null || current.lots.assigning || current.lots.newLotOpen) return
        // The field replaces the pick, so only one primary button shows: Create Lot.
        update {
            it.copy(lots = it.lots.copy(selectedId = null, newLotOpen = true, newLotName = "", newLotError = null))
        }
    }

    public fun setNewLotName(name: String) {
        update {
            if (!it.lots.newLotOpen || it.lots.creating) {
                it
            } else {
                it.copy(lots = it.lots.copy(newLotName = name, newLotError = null))
            }
        }
    }

    /**
     * Creates the Lot named in the inline field, with the same check as Create Lot, then reads the
     * list again and selects it. The Lot stays if the assignment fails later: a Lot without a
     * Node is an ordinary state.
     */
    public fun createLot() {
        val current = mutableState.value
        val form = current.lots
        if (current.step != NodeSetupStep.Lot || current.outcome != null) return
        if (!form.newLotOpen || form.creating || form.assigning) return
        val error = CreateSiteForm.validate(form.newLotName)
        if (error != null) {
            update { it.copy(lots = it.lots.copy(newLotError = error)) }
            return
        }
        val name = form.newLotName.trim()
        val key = createKey ?: newKey().also { createKey = it }
        update { it.copy(lots = it.lots.copy(creating = true, newLotError = null, notice = null)) }
        val started = flow
        scope.launch {
            val result = lots.createLot(current.siteId, name, key)
            if (started != flow) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    createKey = null
                    val created = result.value.toChoice()
                    update { state ->
                        val known = state.lots.choices.orEmpty()
                        state.copy(
                            lots =
                                state.lots.copy(
                                    choices = if (known.any { it.id == created.id }) known else known + created,
                                    selectedId = created.id.takeIf { created.selectable },
                                    newLotOpen = false,
                                    newLotName = "",
                                    newLotError = null,
                                    creating = false,
                                ),
                        )
                    }
                    loadLots(select = created.id)
                }

                is ApiResult.Failed -> {
                    createFailed(result.failure, name)
                }
            }
        }
    }

    private fun createFailed(
        failure: ApiFailure,
        sent: String,
    ) {
        when (failure) {
            ApiFailure.Unauthorized -> {
                close()
            }

            ApiFailure.Validation -> {
                val reason = if (sent.length > CreateSiteForm.MAX_NAME_LENGTH) NameError.TooLong else NameError.Blank
                update { it.copy(lots = it.lots.copy(creating = false, newLotError = reason)) }
            }

            else -> {
                // A 422 means the key was used for another name; the next attempt needs a new one.
                update {
                    it.copy(lots = it.lots.copy(creating = false, notice = LotPickerNotice(readNotice(failure))))
                }
                if (failure == ApiFailure.KeyReused) {
                    // An earlier attempt with this key may have created a Lot the picker lacks.
                    createKey = null
                    loadLots(keepNotice = true)
                }
            }
        }
    }

    /**
     * "Put 7C19 in Tomatoes": `POST /sites/{siteId}/devices` with the sealed key and the Lot. The
     * Idempotency-Key is made once per Device and Site and reused on every retry.
     */
    public fun assign() {
        val current = mutableState.value
        val lot = current.lots.selected ?: return
        val key = sealed ?: return
        if (current.step != NodeSetupStep.Lot || current.outcome != null) return
        if (current.lots.assigning || current.lots.creating) return
        update { it.copy(lots = it.lots.copy(assigning = true, notice = null)) }
        val started = flow
        scope.launch {
            val request =
                EnrolDeviceRequestDto(
                    deviceId = key.deviceId,
                    kind = DEVICE_KIND_NODE,
                    enc = key.enc,
                    ciphertext = key.ciphertext,
                    lotId = lot.id,
                )
            val result = api.enrolDevice(current.siteId, request, keyFor(key.deviceId, current.siteId))
            if (started != flow) return@launch
            when (result) {
                is ApiResult.Ok -> {
                    sealed = null
                    update {
                        it.copy(
                            step = NodeSetupStep.Outcome,
                            lots = it.lots.copy(assigning = false),
                            outcome =
                                NodeOutcome(
                                    NodeOutcomeKind.Assigned,
                                    NodeSetupStep.Outcome.number,
                                    calibrateLotId = lot.id.takeIf { canCalibrate() },
                                ),
                            announcement =
                                announcement(NodeAnnouncementKind.Assigned, node = it.nodeId, lot = lot.name),
                        )
                    }
                    // The Lot's tile and the Devices list are read again from the Server.
                    onAssigned()
                }

                is ApiResult.Failed -> {
                    assignFailed(result.failure, lot)
                }
            }
        }
    }

    private fun assignFailed(
        failure: ApiFailure,
        lot: LotChoice,
    ) {
        when (failure) {
            ApiFailure.Unauthorized -> {
                close()
            }

            ApiFailure.LotClaimed -> {
                update { state ->
                    val marked = state.lots.choices?.map { if (it.id == lot.id) it.copy(selectable = false) else it }
                    state.copy(
                        lots =
                            state.lots.copy(
                                choices = marked,
                                selectedId = null,
                                assigning = false,
                                notice = LotPickerNotice(LotPickerNoticeKind.LotTaken, lot.name),
                            ),
                        announcement =
                            announcement(NodeAnnouncementKind.LotTaken, node = state.nodeId, lot = lot.name),
                    )
                }
                loadLots()
            }

            ApiFailure.NotFound -> {
                update { state ->
                    state.copy(
                        lots =
                            state.lots.copy(
                                choices = state.lots.choices?.filterNot { it.id == lot.id },
                                selectedId = null,
                                assigning = false,
                                notice = LotPickerNotice(LotPickerNoticeKind.LotGone),
                            ),
                    )
                }
                loadLots()
            }

            ApiFailure.DeviceAssigned -> {
                fail(NodeOutcomeKind.AlreadyAssigned)
            }

            ApiFailure.DeviceOnAnotherSite -> {
                fail(NodeOutcomeKind.OnAnotherSite)
            }

            ApiFailure.Forbidden -> {
                fail(NodeOutcomeKind.NotAllowed)
            }

            else -> {
                // No answer, 5xx, 422 or 400: the button retries with the same key.
                update {
                    it.copy(
                        lots =
                            it.lots.copy(assigning = false, notice = LotPickerNotice(LotPickerNoticeKind.AssignFailed)),
                    )
                }
            }
        }
    }

    // Outcome actions

    /** Runs the outcome screen's action. */
    public fun outcomeAction(action: NodeOutcomeAction) {
        val outcome = mutableState.value.outcome ?: return
        if (action != outcome.primary && action != outcome.secondary) return
        when (action) {
            NodeOutcomeAction.Done, NodeOutcomeAction.Close -> {
                close()
            }

            NodeOutcomeAction.StartOver -> {
                startOver()
            }

            NodeOutcomeAction.Calibrate -> {
                val lotId = outcome.calibrateLotId ?: return
                val name = mutableState.value.lotName.orEmpty()
                close()
                onCalibrate(lotId, name)
            }
        }
    }

    private fun startOver() {
        flow++
        for (job in listOf(workJob, noNodeJob, scanJob, lotsJob)) job?.cancel()
        disconnect()
        sealed = null
        heard.clear()
        val current = mutableState.value
        mutableState.value =
            NodeSetupState.CLOSED.copy(
                open = true,
                radio = radio.state.value,
                siteId = current.siteId,
                siteName = current.siteName,
                announcement = current.announcement,
            )
        watchRadio()
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
                    if (started == flow) fail(NodeOutcomeKind.LostConnection)
                } catch (refused: SessionException) {
                    if (started == flow) fail(NodeOutcomeKind.NodeRefused)
                }
            }
    }

    private suspend fun ensureLink(node: NodeCandidate) {
        if (link != null) return
        val generation = ++linkGeneration
        val connected = radio.connect(node.peripheralId)
        if (generation != linkGeneration) {
            connected.close()
            throw SetupLinkException("superseded")
        }
        val channel = Channel<ByteArray>(Channel.UNLIMITED)
        link = connected
        frames = channel
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
                // The link failed. The work in flight still reads the frames that arrived before the
                // close (a wrong-code reply comes just before the Node hangs up), then sees the
                // closed channel and ends as a lost connection.
            } finally {
                reassembler.reset()
                channel.close()
            }
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
        if (closing != null) scope.launch { runCatching { closing.close() } }
    }

    private suspend fun send(frame: ByteArray) {
        val current = link ?: throw SetupLinkException("not connected")
        for (payload in Framing.fragments(frame, current.maxPayload)) current.write(payload)
    }

    private suspend fun receive(): ByteArray {
        val channel = frames ?: throw SetupLinkException("not connected")
        return try {
            withTimeoutOrNull(REPLY_TIMEOUT) { channel.receive() } ?: throw SetupLinkException("no reply")
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

    /** Ends on an error outcome, on the step it stopped on; always disconnected. */
    private fun fail(kind: NodeOutcomeKind) {
        disconnect()
        update {
            it.copy(
                outcome = NodeOutcome(kind, it.step.number),
                code = it.code.copy(working = false),
                lots = it.lots.copy(assigning = false, creating = false),
                announcement =
                    announcement(NodeAnnouncementKind.Error, node = it.nodeId, lot = it.lotName, outcome = kind),
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

    private fun announcement(
        kind: NodeAnnouncementKind,
        node: String? = null,
        lot: String? = null,
        signal: SignalStrength? = null,
        outcome: NodeOutcomeKind? = null,
    ): NodeAnnouncement =
        NodeAnnouncement(
            id = ++announcementId,
            kind = kind,
            assertive = kind in ASSERTIVE,
            node = node,
            lot = lot,
            signal = signal,
            outcome = outcome,
        )

    private fun update(change: (NodeSetupState) -> NodeSetupState) {
        val current = mutableState.value
        if (!current.open) return
        mutableState.value = change(current)
    }

    /** The sealed `K_dev` as it goes to the Server: base64url, never read and never printed. */
    private class SealedKey(
        val deviceId: String,
        val enc: String,
        val ciphertext: String,
    ) {
        override fun toString(): String = "SealedKey"
    }

    public companion object {
        /** "No Node in range yet" after this long without one (UX-DR94). */
        public val NO_NODE_AFTER: Duration = 30.seconds

        /** One reply (hello, identity, enrolment). A Node drops an idle connection after 60 s. */
        public val REPLY_TIMEOUT: Duration = 30.seconds

        public const val DEVICE_KIND_NODE: String = "node"

        private val BASE64URL = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT)
        private val BASE64URL_READ = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT_OPTIONAL)

        private val ASSERTIVE =
            setOf(NodeAnnouncementKind.WrongCode, NodeAnnouncementKind.LotTaken, NodeAnnouncementKind.Error)

        /** The notices a successful read of the Lots clears; the others say why an assignment did not happen. */
        private val READ_NOTICES =
            setOf(LotPickerNoticeKind.Unreachable, LotPickerNoticeKind.Certificate, LotPickerNoticeKind.Unexpected)

        private fun decodeKey(text: String): ByteArray? =
            try {
                BASE64URL_READ.decode(text).takeIf { it.size == CryptoSpec.X25519_KEY_LENGTH }
            } catch (invalid: IllegalArgumentException) {
                null
            }

        private fun readNotice(failure: ApiFailure): LotPickerNoticeKind =
            when (failure) {
                ApiFailure.Unreachable -> LotPickerNoticeKind.Unreachable
                ApiFailure.Certificate -> LotPickerNoticeKind.Certificate
                else -> LotPickerNoticeKind.Unexpected
            }

        /** A Lot can be picked only while its status is `noNode`. */
        private fun LotDto.toChoice(): LotChoice =
            LotChoice(id, name, selectable = LotStatus.fromServer(status) == LotStatus.NoNode)
    }
}
