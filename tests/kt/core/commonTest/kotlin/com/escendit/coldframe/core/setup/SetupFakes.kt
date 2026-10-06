package com.escendit.coldframe.core.setup

import coldframe.setup.v1.DeviceKind
import coldframe.setup.v1.EnrolmentResponse
import coldframe.setup.v1.Identity
import coldframe.setup.v1.SealedSetupMessage
import coldframe.setup.v1.SessionHello
import coldframe.setup.v1.SessionHelloReply
import coldframe.setup.v1.SetupError
import coldframe.setup.v1.SetupErrorCode
import coldframe.setup.v1.SetupMessage
import coldframe.setup.v1.WifiNetwork
import coldframe.setup.v1.WifiResult
import coldframe.setup.v1.WifiScanList
import coldframe.setup.v1.WifiSecurity
import coldframe.setup.v1.WifiStatus
import com.escendit.coldframe.core.crypto.CryptoException
import com.escendit.coldframe.core.crypto.CryptoSpec
import com.escendit.coldframe.core.crypto.SetupCipher
import com.escendit.coldframe.core.crypto.SetupRole
import com.escendit.coldframe.core.crypto.enrolmentKeyFingerprint
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.receiveAsFlow
import okio.ByteString.Companion.toByteString

/** The Hub's Device ID in the fakes: `3f2a…`, so its label reads "Hub 3F2A". */
val HUB_DEVICE_ID: ByteArray = "3f2a9c01b2d4e6f8".hexToByteArray()
const val HUB_CODE = "K7M2Q9XP"

/** The Server's enrolment key in the fakes, and its fingerprint. */
val SERVER_KEY: ByteArray = ByteArray(32) { (it * 7 + 1).toByte() }
val SERVER_FINGERPRINT: String = enrolmentKeyFingerprint(SERVER_KEY)

/** The Device end of a [FakeLink]. */
interface FakeDevice {
    var link: FakeLink?

    fun reset()

    /** One payload from the app. */
    fun onWrite(payload: ByteArray)
}

/**
 * A Hub that runs the real Device side of the session crypto (AD-25) over [SetupLink] payloads:
 * it answers the hello, opens every sealed message with its own [code], and records what the
 * app sent. [wifiResults] are answered to `WifiConfig` in order; an empty queue never answers.
 */
class FakeHub(
    val code: String = HUB_CODE,
    val networks: List<WifiNetwork> =
        listOf(
            WifiNetwork(
                ssid = "Novak-Home",
                rssi = -48,
                security = WifiSecurity.WIFI_SECURITY_WPA2_PERSONAL,
                channel = 6,
            ),
            WifiNetwork(ssid = "Neighbour", rssi = -70, security = WifiSecurity.WIFI_SECURITY_WPA3_ONLY, channel = 1),
            WifiNetwork(ssid = "Office", rssi = -80, security = WifiSecurity.WIFI_SECURITY_OTHER, channel = 11),
        ),
) : FakeDevice {
    /** What the app sent after the hello, in order, as message names. */
    val received = mutableListOf<String>()
    val messages = mutableListOf<SetupMessage>()
    val wifiResults = ArrayDeque<WifiStatus>()
    var enc: ByteArray = ByteArray(32) { (it + 100).toByte() }
    var ciphertext: ByteArray = ByteArray(48) { (it + 50).toByte() }
    var hellos = 0
    var refuseFingerprint = false

    /** Records a `WifiScanRequest` but never answers it, as a Hub still scanning. */
    var holdScan = false
    override var link: FakeLink? = null

    private var cipher: SetupCipher? = null
    private val reassembler = Reassembler()

    override fun reset() {
        cipher = null
        reassembler.reset()
    }

    /** One payload from the app. */
    override fun onWrite(payload: ByteArray) {
        val step = reassembler.push(payload)
        if (step is Reassembled.Frame) onFrame(step.bytes)
    }

    private fun onFrame(frame: ByteArray) {
        val open = cipher
        if (open == null) {
            val hello = SessionHello.ADAPTER.decode(frame)
            hellos++
            val private = ByteArray(32) { (it * 3 + hellos).toByte() }
            cipher = SetupCipher.of(SetupRole.Device, private, hello.app_public_key.toByteArray(), code)
            send(
                SessionHelloReply(
                    protocol_version = 1,
                    device_public_key = SetupCipher.publicKey(private).toByteString(),
                ).encode(),
            )
            return
        }
        val sealed = SealedSetupMessage.ADAPTER.decode(frame)
        val plain =
            try {
                open.open(sealed.counter, sealed.ciphertext.toByteArray())
            } catch (failure: CryptoException) {
                // A wrong code: one error under the Hub's own keys, then hang up.
                reply(SetupMessage(error = SetupError(code = SetupErrorCode.SETUP_ERROR_CODE_MALFORMED_MESSAGE)))
                link?.dropFromHub()
                return
            }
        val message = SetupMessage.ADAPTER.decode(plain)
        messages += message
        when {
            message.identity_request != null -> {
                received += "IdentityRequest"
                reply(
                    SetupMessage(
                        identity =
                            Identity(
                                device_id = HUB_DEVICE_ID.toByteString(),
                                kind = DeviceKind.DEVICE_KIND_HUB,
                                firmware_version = "0.1.0",
                            ),
                    ),
                )
            }

            message.wifi_scan_request != null -> {
                received += "WifiScanRequest"
                if (!holdScan) reply(SetupMessage(wifi_scan_list = WifiScanList(networks = networks)))
            }

            message.site_binding != null -> {
                received += "SiteBinding"
            }

            message.enrolment_request != null -> {
                received += "EnrolmentRequest"
                val request = message.enrolment_request
                if (refuseFingerprint ||
                    enrolmentKeyFingerprint(request.server_public_key.toByteArray()) != request.fingerprint
                ) {
                    reply(SetupMessage(error = SetupError(code = SetupErrorCode.SETUP_ERROR_CODE_FINGERPRINT_MISMATCH)))
                } else {
                    reply(
                        SetupMessage(
                            enrolment_response =
                                EnrolmentResponse(
                                    device_id = HUB_DEVICE_ID.toByteString(),
                                    enc = enc.toByteString(),
                                    ciphertext = ciphertext.toByteString(),
                                ),
                        ),
                    )
                }
            }

            message.wifi_config != null -> {
                received += "WifiConfig"
                wifiResults.removeFirstOrNull()?.let { reply(SetupMessage(wifi_result = WifiResult(status = it))) }
            }
        }
    }

    private fun reply(message: SetupMessage) {
        val sealed = cipher!!.seal(message.copy(protocol_version = CryptoSpec.PROTOCOL_MAJOR).encode())
        send(
            SealedSetupMessage(
                protocol_version = 1,
                counter = sealed.counter,
                ciphertext = sealed.ciphertext.toByteString(),
            ).encode(),
        )
    }

    private fun send(frame: ByteArray) {
        val current = link ?: return
        for (payload in Framing.fragments(frame, current.maxPayload)) current.notify(payload)
    }
}

/** The Node's Device ID in the fakes: `7c19…`, so it advertises as "Coldframe Node 7C19". */
val NODE_DEVICE_ID: ByteArray = "7c19aa01b2d4e6f8".hexToByteArray()
const val NODE_CODE = "N4D3C0DE"

/**
 * A Node in setup mode that runs the real Device side of the session crypto with the Node profile
 * of `packages/rs/setup/src/session.rs`: kind `NODE`, Wi-Fi messages refused with
 * `UNEXPECTED_MESSAGE`, enrolment without a prior binding. [received] is every message the app
 * sent after the hello, by name.
 */
class FakeNode(
    val code: String = NODE_CODE,
) : FakeDevice {
    val received = mutableListOf<String>()
    var enc: ByteArray = ByteArray(32) { (it + 7).toByte() }
    var ciphertext: ByteArray = ByteArray(48) { (it + 90).toByte() }
    var hellos = 0
    var kind: DeviceKind = DeviceKind.DEVICE_KIND_NODE

    /** Answers `IdentityRequest` with a `SetupError`. */
    var refuseIdentity = false
    var refuseFingerprint = false

    /** Records an `EnrolmentRequest` but never answers it. */
    var holdEnrolment = false

    /** Hangs up on an `EnrolmentRequest` instead of answering. */
    var dropOnEnrolment = false

    /** The Device ID of the `EnrolmentResponse`; the Identity always carries [NODE_DEVICE_ID]. */
    var responseDeviceId: ByteArray = NODE_DEVICE_ID
    override var link: FakeLink? = null

    private var cipher: SetupCipher? = null
    private val reassembler = Reassembler()

    override fun reset() {
        cipher = null
        reassembler.reset()
    }

    override fun onWrite(payload: ByteArray) {
        val step = reassembler.push(payload)
        if (step is Reassembled.Frame) onFrame(step.bytes)
    }

    private fun onFrame(frame: ByteArray) {
        val open = cipher
        if (open == null) {
            val hello = SessionHello.ADAPTER.decode(frame)
            hellos++
            val private = ByteArray(32) { (it * 5 + hellos).toByte() }
            cipher = SetupCipher.of(SetupRole.Device, private, hello.app_public_key.toByteArray(), code)
            send(
                SessionHelloReply(
                    protocol_version = 1,
                    device_public_key = SetupCipher.publicKey(private).toByteString(),
                ).encode(),
            )
            return
        }
        val sealed = SealedSetupMessage.ADAPTER.decode(frame)
        val plain =
            try {
                open.open(sealed.counter, sealed.ciphertext.toByteArray())
            } catch (failure: CryptoException) {
                // A wrong code: one error under the Node's own keys, then hang up.
                reply(error(SetupErrorCode.SETUP_ERROR_CODE_MALFORMED_MESSAGE))
                link?.dropFromHub()
                return
            }
        val message = SetupMessage.ADAPTER.decode(plain)
        when {
            message.identity_request != null -> {
                received += "IdentityRequest"
                if (refuseIdentity) {
                    reply(error(SetupErrorCode.SETUP_ERROR_CODE_UNEXPECTED_MESSAGE))
                } else {
                    reply(
                        SetupMessage(
                            identity =
                                Identity(
                                    device_id = NODE_DEVICE_ID.toByteString(),
                                    kind = kind,
                                    firmware_version = "0.1.0",
                                ),
                        ),
                    )
                }
            }

            message.wifi_scan_request != null -> {
                received += "WifiScanRequest"
                reply(error(SetupErrorCode.SETUP_ERROR_CODE_UNEXPECTED_MESSAGE))
            }

            message.wifi_config != null -> {
                received += "WifiConfig"
                reply(error(SetupErrorCode.SETUP_ERROR_CODE_UNEXPECTED_MESSAGE))
            }

            message.site_binding != null -> {
                received += "SiteBinding"
            }

            message.enrolment_request != null -> {
                received += "EnrolmentRequest"
                val request = message.enrolment_request
                when {
                    holdEnrolment -> {
                        Unit
                    }

                    dropOnEnrolment -> {
                        link?.dropFromHub()
                    }

                    refuseFingerprint ||
                        enrolmentKeyFingerprint(request.server_public_key.toByteArray()) != request.fingerprint -> {
                        reply(error(SetupErrorCode.SETUP_ERROR_CODE_FINGERPRINT_MISMATCH))
                    }

                    else -> {
                        reply(
                            SetupMessage(
                                enrolment_response =
                                    EnrolmentResponse(
                                        device_id = responseDeviceId.toByteString(),
                                        enc = enc.toByteString(),
                                        ciphertext = ciphertext.toByteString(),
                                    ),
                            ),
                        )
                    }
                }
            }
        }
    }

    private fun error(code: SetupErrorCode): SetupMessage = SetupMessage(error = SetupError(code = code))

    private fun reply(message: SetupMessage) {
        val sealed = cipher!!.seal(message.copy(protocol_version = CryptoSpec.PROTOCOL_MAJOR).encode())
        send(
            SealedSetupMessage(
                protocol_version = 1,
                counter = sealed.counter,
                ciphertext = sealed.ciphertext.toByteString(),
            ).encode(),
        )
    }

    private fun send(frame: ByteArray) {
        val current = link ?: return
        for (payload in Framing.fragments(frame, current.maxPayload)) current.notify(payload)
    }
}

/** A link to a [FakeDevice]; small payloads so every frame is fragmented. */
class FakeLink(
    private val hub: FakeDevice,
    override val maxPayload: Int,
) : SetupLink {
    private val channel = Channel<ByteArray>(Channel.UNLIMITED)
    var closed = false
        private set
    val writes = mutableListOf<ByteArray>()

    override val incoming: Flow<ByteArray> = channel.receiveAsFlow()

    override suspend fun write(payload: ByteArray) {
        if (closed) throw SetupLinkException("closed")
        require(payload.size <= maxPayload)
        writes += payload
        hub.onWrite(payload)
    }

    override suspend fun close() {
        closed = true
        channel.close()
    }

    fun notify(payload: ByteArray) {
        if (!closed) channel.trySend(payload)
    }

    /** The Hub hangs up, or the phone lost it. */
    fun dropFromHub() {
        closed = true
        channel.close()
    }
}

/**
 * A radio with scripted adverts, one [FakeHub] behind every id (or [device], for a Node), and a
 * settable state.
 */
class FakeSetupRadio(
    val hub: FakeHub = FakeHub(),
    initial: RadioState = RadioState.Ready,
    private val maxPayload: Int = 20,
    private val device: FakeDevice = hub,
) : SetupRadio {
    val mutableState = MutableStateFlow(initial)
    val adverts = MutableSharedFlow<SetupAdvert>(extraBufferCapacity = 16)
    var scans = 0
    var connects = 0
    var rechecks = 0
    var connectFails = false
    val links = mutableListOf<FakeLink>()

    override val state: StateFlow<RadioState> = mutableState

    override fun recheck() {
        rechecks++
    }

    override fun scan(): Flow<SetupAdvert> {
        scans++
        return adverts
    }

    override suspend fun connect(id: String): SetupLink {
        connects++
        if (connectFails) throw SetupLinkException("unreachable")
        device.reset()
        val link = FakeLink(device, maxPayload)
        device.link = link
        links += link
        return link
    }
}
