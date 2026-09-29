package com.escendit.coldframe.core.crypto

import java.math.BigInteger
import java.nio.ByteBuffer
import java.security.KeyFactory
import java.security.MessageDigest
import java.security.spec.NamedParameterSpec
import java.security.spec.XECPrivateKeySpec
import java.security.spec.XECPublicKeySpec
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.KDF
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.HKDFParameterSpec
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * The Device crypto contract on the JDK's own providers (X25519, HKDF, HMAC, ChaCha20-Poly1305), in test
 * scope only: it proves the generated [CryptoSpec] constants reproduce the shared vectors on the JVM. The
 * multiplatform production crypto is Story 3.6's.
 */
internal enum class JdkFailure {
    AUTHENTICATION_FAILED,
    WRONG_SETUP_CODE,
    REPLAY,
    INVALID_PUBLIC_KEY,
    INVALID_SETUP_CODE,
}

internal class JdkCryptoFailure(
    val failure: JdkFailure,
) : Exception(failure.name)

internal object JdkCrypto {
    private val ascii = Charsets.US_ASCII

    fun hex(bytes: ByteArray): String = bytes.toHexString()

    fun unhex(text: String): ByteArray = text.hexToByteArray()

    fun sha256(data: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(data)

    fun hmacSha256(
        key: ByteArray,
        data: ByteArray,
    ): ByteArray =
        Mac.getInstance("HmacSHA256").run {
            init(SecretKeySpec(key, "HmacSHA256"))
            doFinal(data)
        }

    /** HKDF-Extract; an empty salt means 32 zero bytes (RFC 5869). */
    fun hkdfExtract(
        salt: ByteArray,
        ikm: ByteArray,
    ): ByteArray {
        val spec =
            HKDFParameterSpec
                .ofExtract()
                .addIKM(ikm)
                .addSalt(if (salt.isEmpty()) ByteArray(32) else salt)
                .extractOnly()
        return KDF.getInstance("HKDF-SHA256").deriveData(spec)
    }

    fun hkdfExpand(
        prk: ByteArray,
        info: ByteArray,
        length: Int,
    ): ByteArray =
        KDF
            .getInstance("HKDF-SHA256")
            .deriveData(HKDFParameterSpec.expandOnly(SecretKeySpec(prk, "Generic"), info, length))

    fun hkdf(
        salt: ByteArray,
        ikm: ByteArray,
        info: ByteArray,
        length: Int,
    ): ByteArray = hkdfExpand(hkdfExtract(salt, ikm), info, length)

    // ---------- X25519 ----------

    private val basePoint = ByteArray(CryptoSpec.X25519_KEY_LENGTH).also { it[0] = 9 }

    private fun uCoordinate(publicKey: ByteArray): BigInteger {
        val bigEndian = publicKey.reversedArray()
        bigEndian[0] = (bigEndian[0].toInt() and 0x7f).toByte()
        return BigInteger(1, bigEndian)
    }

    /** X25519 on raw keys; refuses the all-zero output. */
    fun x25519(
        privateKey: ByteArray,
        publicKey: ByteArray,
    ): ByteArray {
        val factory = KeyFactory.getInstance("XDH")
        val agreement = KeyAgreement.getInstance("XDH")
        agreement.init(factory.generatePrivate(XECPrivateKeySpec(NamedParameterSpec.X25519, privateKey)))
        val shared =
            try {
                // The JDK refuses a small-order point already in doPhase.
                agreement.doPhase(
                    factory.generatePublic(XECPublicKeySpec(NamedParameterSpec.X25519, uCoordinate(publicKey))),
                    true,
                )
                agreement.generateSecret()
            } catch (exception: Exception) {
                throw JdkCryptoFailure(JdkFailure.INVALID_PUBLIC_KEY)
            }
        if (shared.all { it == 0.toByte() }) throw JdkCryptoFailure(JdkFailure.INVALID_PUBLIC_KEY)
        return shared
    }

    fun x25519PublicKey(privateKey: ByteArray): ByteArray = x25519(privateKey, basePoint)

    // ---------- ChaCha20-Poly1305 ----------

    private fun cipher(
        mode: Int,
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
    ): Cipher =
        Cipher.getInstance("ChaCha20-Poly1305").apply {
            init(mode, SecretKeySpec(key, "ChaCha20"), IvParameterSpec(nonce))
            updateAAD(aad)
        }

    fun seal(
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
        plaintext: ByteArray,
    ): ByteArray = cipher(Cipher.ENCRYPT_MODE, key, nonce, aad).doFinal(plaintext)

    fun open(
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
        sealed: ByteArray,
    ): ByteArray {
        if (sealed.size < CryptoSpec.AEAD_TAG_LENGTH) throw JdkCryptoFailure(JdkFailure.AUTHENTICATION_FAILED)
        return try {
            cipher(Cipher.DECRYPT_MODE, key, nonce, aad).doFinal(sealed)
        } catch (exception: AEADBadTagException) {
            throw JdkCryptoFailure(JdkFailure.AUTHENTICATION_FAILED)
        }
    }

    private fun u64(value: Long): ByteArray = ByteBuffer.allocate(8).putLong(value).array()

    // ---------- Key hierarchy ----------

    class DeviceKeys(
        val deviceKey: ByteArray,
    ) {
        val sealKey: ByteArray = purposeKey(deviceKey, CryptoSpec.SEAL_LABEL)
        val ackKey: ByteArray = purposeKey(deviceKey, CryptoSpec.ACK_LABEL)
        val hubAuthKey: ByteArray = purposeKey(deviceKey, CryptoSpec.HUB_AUTH_LABEL)
        val deviceId: ByteArray =
            hkdf(ByteArray(0), deviceKey, CryptoSpec.DEVICE_ID_LABEL.toByteArray(ascii), CryptoSpec.DEVICE_ID_LENGTH)

        override fun toString(): String = "DeviceKeys(${hex(deviceId)})"
    }

    fun deviceKey(rootKey: ByteArray): ByteArray {
        require(rootKey.size == CryptoSpec.ROOT_KEY_LENGTH)
        return hmacSha256(rootKey, CryptoSpec.DEVICE_KEY_LABEL.toByteArray(ascii))
    }

    fun purposeKey(
        deviceKey: ByteArray,
        label: String,
    ): ByteArray = hkdf(ByteArray(0), deviceKey, label.toByteArray(ascii), CryptoSpec.PURPOSE_KEY_LENGTH)

    fun deviceKeys(rootKey: ByteArray): DeviceKeys = DeviceKeys(deviceKey(rootKey))

    // ---------- Frames ----------

    fun frameNonce(
        deviceId: ByteArray,
        counter: Long,
    ): ByteArray = deviceId.copyOf(CryptoSpec.FRAME_NONCE_DEVICE_ID_PREFIX_LENGTH) + u64(counter)

    fun frameAad(
        deviceId: ByteArray,
        counter: Long,
    ): ByteArray = byteArrayOf(CryptoSpec.PROTOCOL_MAJOR.toByte()) + deviceId + u64(counter)

    fun sealFrame(
        key: ByteArray,
        deviceId: ByteArray,
        counter: Long,
        plaintext: ByteArray,
    ): ByteArray = seal(key, frameNonce(deviceId, counter), frameAad(deviceId, counter), plaintext)

    fun openFrame(
        key: ByteArray,
        deviceId: ByteArray,
        counter: Long,
        sealed: ByteArray,
    ): ByteArray = open(key, frameNonce(deviceId, counter), frameAad(deviceId, counter), sealed)

    /** Counters are unsigned 64-bit; compared with [java.lang.Long.compareUnsigned]. */
    class ReplayWindow {
        var highest: Long? = null
            private set
        private var seen = 0L

        fun wouldAccept(counter: Long): Boolean {
            val top = highest ?: return true
            if (java.lang.Long.compareUnsigned(counter, top) > 0) return true
            val age = top - counter
            return java.lang.Long.compareUnsigned(age, CryptoSpec.REPLAY_WINDOW.toLong()) < 0 &&
                seen and (1L shl age.toInt()) == 0L
        }

        fun accept(counter: Long) {
            if (!wouldAccept(counter)) throw JdkCryptoFailure(JdkFailure.REPLAY)
            val top = highest
            when {
                top == null -> {
                    seen = 1L
                    highest = counter
                }

                java.lang.Long.compareUnsigned(counter, top) <= 0 -> {
                    seen = seen or (1L shl (top - counter).toInt())
                }

                else -> {
                    val shift = counter - top
                    seen = (if (java.lang.Long.compareUnsigned(shift, 64) >= 0) 0L else seen shl shift.toInt()) or 1L
                    highest = counter
                }
            }
        }
    }

    fun openFrame(
        key: ByteArray,
        deviceId: ByteArray,
        counter: Long,
        sealed: ByteArray,
        window: ReplayWindow,
    ): ByteArray {
        if (!window.wouldAccept(counter)) throw JdkCryptoFailure(JdkFailure.REPLAY)
        val plaintext = openFrame(key, deviceId, counter, sealed)
        window.accept(counter)
        return plaintext
    }

    // ---------- HPKE (RFC 9180 base mode) ----------

    private fun i2osp(
        value: Int,
        length: Int,
    ): ByteArray = ByteArray(length) { index -> (value shr (8 * (length - 1 - index))).toByte() }

    private val version = CryptoSpec.HPKE_VERSION_LABEL.toByteArray(ascii)
    private val kemSuite = "KEM".toByteArray(ascii) + i2osp(CryptoSpec.HPKE_KEM_ID, 2)
    private val hpkeSuite =
        "HPKE".toByteArray(ascii) + i2osp(CryptoSpec.HPKE_KEM_ID, 2) + i2osp(CryptoSpec.HPKE_KDF_ID, 2) +
            i2osp(CryptoSpec.HPKE_AEAD_ID, 2)

    private fun labeledExtract(
        suite: ByteArray,
        salt: ByteArray,
        label: String,
        ikm: ByteArray,
    ): ByteArray = hkdfExtract(salt, version + suite + label.toByteArray(ascii) + ikm)

    private fun labeledExpand(
        suite: ByteArray,
        prk: ByteArray,
        label: String,
        info: ByteArray,
        length: Int,
    ): ByteArray = hkdfExpand(prk, i2osp(length, 2) + version + suite + label.toByteArray(ascii) + info, length)

    fun deriveKeyPair(ikm: ByteArray): Pair<ByteArray, ByteArray> {
        val dkpPrk = labeledExtract(kemSuite, ByteArray(0), CryptoSpec.HPKE_LABEL_DKP_PRK, ikm)
        val privateKey =
            labeledExpand(kemSuite, dkpPrk, CryptoSpec.HPKE_LABEL_SK, ByteArray(0), CryptoSpec.X25519_KEY_LENGTH)
        return privateKey to x25519PublicKey(privateKey)
    }

    fun hpkeSharedSecret(
        dh: ByteArray,
        enc: ByteArray,
        recipientPublicKey: ByteArray,
    ): ByteArray {
        val eaePrk = labeledExtract(kemSuite, ByteArray(0), CryptoSpec.HPKE_LABEL_EAE_PRK, dh)
        return labeledExpand(kemSuite, eaePrk, CryptoSpec.HPKE_LABEL_SHARED_SECRET, enc + recipientPublicKey, 32)
    }

    fun keySchedule(
        sharedSecret: ByteArray,
        info: ByteArray,
    ): Pair<ByteArray, ByteArray> {
        val pskIdHash = labeledExtract(hpkeSuite, ByteArray(0), CryptoSpec.HPKE_LABEL_PSK_ID_HASH, ByteArray(0))
        val infoHash = labeledExtract(hpkeSuite, ByteArray(0), CryptoSpec.HPKE_LABEL_INFO_HASH, info)
        val context = byteArrayOf(CryptoSpec.HPKE_MODE.toByte()) + pskIdHash + infoHash
        val secret = labeledExtract(hpkeSuite, sharedSecret, CryptoSpec.HPKE_LABEL_SECRET, ByteArray(0))
        return labeledExpand(hpkeSuite, secret, CryptoSpec.HPKE_LABEL_KEY, context, CryptoSpec.AEAD_KEY_LENGTH) to
            labeledExpand(hpkeSuite, secret, CryptoSpec.HPKE_LABEL_BASE_NONCE, context, CryptoSpec.AEAD_NONCE_LENGTH)
    }

    /** Base-mode Seal at sequence 0: `enc` and the ciphertext. */
    fun sealBase(
        recipientPublicKey: ByteArray,
        ikmE: ByteArray,
        info: ByteArray,
        aad: ByteArray,
        plaintext: ByteArray,
    ): Pair<ByteArray, ByteArray> {
        val (ephemeral, enc) = deriveKeyPair(ikmE)
        val shared = hpkeSharedSecret(x25519(ephemeral, recipientPublicKey), enc, recipientPublicKey)
        val (key, baseNonce) = keySchedule(shared, info)
        return enc to seal(key, baseNonce, aad, plaintext)
    }

    fun openBase(
        recipientPrivateKey: ByteArray,
        enc: ByteArray,
        info: ByteArray,
        aad: ByteArray,
        ciphertext: ByteArray,
    ): ByteArray {
        val shared = hpkeSharedSecret(x25519(recipientPrivateKey, enc), enc, x25519PublicKey(recipientPrivateKey))
        val (key, baseNonce) = keySchedule(shared, info)
        return open(key, baseNonce, aad, ciphertext)
    }

    val enrolmentInfo: ByteArray = CryptoSpec.ENROLMENT_INFO.toByteArray(ascii)

    fun fingerprint(publicKey: ByteArray): String = hex(sha256(publicKey))

    // ---------- Setup session ----------

    /** The app → Device and Device → app keys. */
    fun setupKeys(
        ownPrivateKey: ByteArray,
        peerPublicKey: ByteArray,
        appPublicKey: ByteArray,
        devicePublicKey: ByteArray,
        popCode: String,
    ): Pair<ByteArray, ByteArray> {
        if (popCode.isEmpty() || popCode.length > CryptoSpec.SETUP_MAX_CODE_LENGTH || popCode.any { it.code > 0x7f }) {
            throw JdkCryptoFailure(JdkFailure.INVALID_SETUP_CODE)
        }
        val okm =
            hkdf(
                popCode.uppercase().toByteArray(ascii),
                x25519(ownPrivateKey, peerPublicKey),
                CryptoSpec.SETUP_LABEL.toByteArray(ascii) + appPublicKey + devicePublicKey,
                CryptoSpec.SETUP_OKM_LENGTH,
            )

        fun key(offset: Int) = okm.copyOfRange(offset, offset + CryptoSpec.SETUP_KEY_LENGTH)
        return key(CryptoSpec.SETUP_APP_TO_HUB_KEY_OFFSET) to key(CryptoSpec.SETUP_HUB_TO_APP_KEY_OFFSET)
    }

    fun setupNonce(counter: Long): ByteArray = ByteArray(CryptoSpec.SETUP_NONCE_PREFIX_LENGTH) + u64(counter)

    class SetupSession(
        isApp: Boolean,
        ownPrivateKey: ByteArray,
        peerPublicKey: ByteArray,
        popCode: String,
    ) {
        private val sendKey: ByteArray
        private val receiveKey: ByteArray
        private var nextSend = CryptoSpec.SETUP_FIRST_COUNTER
        private var nextReceive = CryptoSpec.SETUP_FIRST_COUNTER
        private var openedAny = false
        private val aad = byteArrayOf(CryptoSpec.PROTOCOL_MAJOR.toByte())

        init {
            val ownPublicKey = x25519PublicKey(ownPrivateKey)
            val (appToDevice, deviceToApp) =
                if (isApp) {
                    setupKeys(ownPrivateKey, peerPublicKey, ownPublicKey, peerPublicKey, popCode)
                } else {
                    setupKeys(ownPrivateKey, peerPublicKey, peerPublicKey, ownPublicKey, popCode)
                }
            sendKey = if (isApp) appToDevice else deviceToApp
            receiveKey = if (isApp) deviceToApp else appToDevice
        }

        fun seal(plaintext: ByteArray): Pair<Long, ByteArray> {
            val counter = nextSend++
            return counter to seal(sendKey, setupNonce(counter), aad, plaintext)
        }

        fun open(
            counter: Long,
            ciphertext: ByteArray,
        ): ByteArray {
            if (java.lang.Long.compareUnsigned(counter, nextReceive) < 0) throw JdkCryptoFailure(JdkFailure.REPLAY)
            val plaintext =
                try {
                    open(receiveKey, setupNonce(counter), aad, ciphertext)
                } catch (failure: JdkCryptoFailure) {
                    throw if (openedAny) failure else JdkCryptoFailure(JdkFailure.WRONG_SETUP_CODE)
                }
            openedAny = true
            nextReceive = counter + 1
            return plaintext
        }
    }

    // ---------- Heartbeat ----------

    fun heartbeatCanonical(
        method: String,
        path: String,
        body: ByteArray,
        timestampMs: Long,
        nonce: ByteArray,
    ): String {
        require(nonce.size == CryptoSpec.HEARTBEAT_NONCE_LENGTH)
        return listOf(method, path, hex(sha256(body)), timestampMs.toString(), hex(nonce))
            .joinToString(CryptoSpec.HEARTBEAT_SEPARATOR)
    }

    fun heartbeatSignature(
        hubAuthKey: ByteArray,
        canonical: String,
    ): ByteArray = hmacSha256(hubAuthKey, canonical.toByteArray(Charsets.UTF_8))

    fun heartbeatVerify(
        hubAuthKey: ByteArray,
        canonical: String,
        signature: ByteArray,
    ): Boolean = MessageDigest.isEqual(heartbeatSignature(hubAuthKey, canonical), signature)
}
