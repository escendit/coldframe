package com.escendit.coldframe.core.crypto

/** Why a crypto step failed. Carries no key, code or message content. */
public enum class CryptoFailure {
    /** A sealed message did not authenticate. */
    AuthenticationFailed,

    /** The first sealed message of a session did not open: the setup codes differ. */
    WrongSetupCode,

    /** A counter did not increase. */
    Replay,

    /** A small-order public key (the X25519 output was all zero). */
    InvalidPublicKey,

    /** A setup code that is empty, longer than [CryptoSpec.SETUP_MAX_CODE_LENGTH] or not ASCII. */
    InvalidSetupCode,
}

public class CryptoException(
    public val failure: CryptoFailure,
) : Exception(failure.name)

/** `n` bytes from the platform's cryptographically secure generator. */
internal expect fun secureRandom(size: Int): ByteArray

/** Which end of a setup session this is. */
public enum class SetupRole { App, Device }

/**
 * The BLE setup session (AD-25): X25519 between the app and the Device, the setup code mixed in
 * as the HKDF salt, then ChaCha20-Poly1305 per message with one counter per direction.
 *
 * `okm = HKDF-SHA256(salt = uppercase code, ikm = X25519, info = "coldframe/setup/v1" ‖ app_pub ‖
 * hub_pub, L = 64)`; bytes 0..32 seal app → Hub, 32..64 seal Hub → app. Nonce `0x00000000 ‖
 * counter_be64`; AAD the protocol major. Deliberately no `toString` of any key.
 */
public class SetupCipher private constructor(
    private val sendKey: ByteArray,
    private val receiveKey: ByteArray,
) {
    private var nextSend = CryptoSpec.SETUP_FIRST_COUNTER
    private var nextReceive = CryptoSpec.SETUP_FIRST_COUNTER
    private var openedAny = false

    /** One sealed message: its counter and the ciphertext with the tag. */
    public class Sealed(
        public val counter: Long,
        public val ciphertext: ByteArray,
    )

    /** Seals the next outgoing message. */
    public fun seal(plaintext: ByteArray): Sealed {
        val counter = nextSend
        if (counter == -1L) throw CryptoException(CryptoFailure.Replay)
        val ciphertext = ChaCha20Poly1305.seal(sendKey, nonce(counter), AAD, plaintext)
        nextSend = counter + 1
        return Sealed(counter, ciphertext)
    }

    /**
     * Opens an incoming message. Its counter (unsigned) must be at least the next expected one.
     * The first failure to open is [CryptoFailure.WrongSetupCode]; a later one
     * [CryptoFailure.AuthenticationFailed].
     */
    public fun open(
        counter: Long,
        ciphertext: ByteArray,
    ): ByteArray {
        if (counter.toULong() < nextReceive.toULong() || counter == -1L) {
            throw CryptoException(CryptoFailure.Replay)
        }
        val plaintext =
            try {
                ChaCha20Poly1305.open(receiveKey, nonce(counter), AAD, ciphertext)
            } catch (failure: CryptoException) {
                throw if (openedAny) failure else CryptoException(CryptoFailure.WrongSetupCode)
            }
        openedAny = true
        nextReceive = counter + 1
        return plaintext
    }

    /** Wipes the direction keys; the cipher is unusable afterwards. */
    public fun close() {
        sendKey.fill(0)
        receiveKey.fill(0)
    }

    override fun toString(): String = "SetupCipher"

    public companion object {
        private val AAD = byteArrayOf(CryptoSpec.PROTOCOL_MAJOR.toByte())
        private val LABEL = CryptoSpec.SETUP_LABEL.encodeToByteArray()

        /** The public key of an ephemeral X25519 [privateKey]. */
        public fun publicKey(privateKey: ByteArray): ByteArray = X25519.publicKey(privateKey)

        /** 32 fresh random bytes for an ephemeral private key. */
        public fun newPrivateKey(): ByteArray = secureRandom(CryptoSpec.X25519_KEY_LENGTH)

        /** The nonce of the message with [counter]. */
        public fun nonce(counter: Long): ByteArray {
            val nonce = ByteArray(CryptoSpec.AEAD_NONCE_LENGTH)
            for (index in 0 until 8) {
                nonce[CryptoSpec.SETUP_NONCE_PREFIX_LENGTH + index] = (counter ushr (8 * (7 - index))).toByte()
            }
            return nonce
        }

        /**
         * The two direction keys, app → Device first. [code] is uppercased (ASCII only) and used
         * as the salt; it must be 1 to [CryptoSpec.SETUP_MAX_CODE_LENGTH] ASCII characters.
         */
        public fun deriveKeys(
            ownPrivateKey: ByteArray,
            peerPublicKey: ByteArray,
            appPublicKey: ByteArray,
            devicePublicKey: ByteArray,
            code: String,
        ): Pair<ByteArray, ByteArray> {
            if (code.isEmpty() || code.length > CryptoSpec.SETUP_MAX_CODE_LENGTH || code.any { it.code > 0x7f }) {
                throw CryptoException(CryptoFailure.InvalidSetupCode)
            }
            val salt = code.map { if (it in 'a'..'z') it - ('a' - 'A') else it }.joinToString("").encodeToByteArray()
            val shared = X25519.sharedSecret(ownPrivateKey, peerPublicKey)
            val okm = Hkdf.derive(salt, shared, CryptoSpec.SETUP_OKM_LENGTH, LABEL, appPublicKey, devicePublicKey)
            shared.fill(0)
            salt.fill(0)
            val appToDevice =
                okm.copyOfRange(
                    CryptoSpec.SETUP_APP_TO_HUB_KEY_OFFSET,
                    CryptoSpec.SETUP_APP_TO_HUB_KEY_OFFSET + CryptoSpec.SETUP_KEY_LENGTH,
                )
            val deviceToApp =
                okm.copyOfRange(
                    CryptoSpec.SETUP_HUB_TO_APP_KEY_OFFSET,
                    CryptoSpec.SETUP_HUB_TO_APP_KEY_OFFSET + CryptoSpec.SETUP_KEY_LENGTH,
                )
            okm.fill(0)
            return appToDevice to deviceToApp
        }

        /** One end of a session from this end's [ownPrivateKey], the peer's key and the [code]. */
        public fun of(
            role: SetupRole,
            ownPrivateKey: ByteArray,
            peerPublicKey: ByteArray,
            code: String,
        ): SetupCipher {
            val ownPublicKey = X25519.publicKey(ownPrivateKey)
            val (appPublic, devicePublic) =
                when (role) {
                    SetupRole.App -> ownPublicKey to peerPublicKey
                    SetupRole.Device -> peerPublicKey to ownPublicKey
                }
            val (appToDevice, deviceToApp) = deriveKeys(ownPrivateKey, peerPublicKey, appPublic, devicePublic, code)
            return when (role) {
                SetupRole.App -> SetupCipher(appToDevice, deviceToApp)
                SetupRole.Device -> SetupCipher(deviceToApp, appToDevice)
            }
        }
    }
}

/** Lowercase hex SHA-256 of an enrolment public key: the fingerprint the app shows and checks. */
public fun enrolmentKeyFingerprint(publicKey: ByteArray): String = Sha256.digest(publicKey).toLowerHex()

internal fun ByteArray.toLowerHex(): String {
    val digits = "0123456789abcdef"
    val out = StringBuilder(size * 2)
    for (byte in this) {
        val value = byte.toInt() and 0xff
        out.append(digits[value ushr 4]).append(digits[value and 0xf])
    }
    return out.toString()
}
