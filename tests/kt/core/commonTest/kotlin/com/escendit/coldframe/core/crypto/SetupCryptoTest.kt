package com.escendit.coldframe.core.crypto

import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

/**
 * The production crypto (pure Kotlin, every target) against the shared vectors and the RFC
 * anchors in packages/crypto-spec/vectors.json: the cross-language risk of Epic 3.
 */
class SetupCryptoTest {
    private val setup = SharedVectors.list("setup").first()

    @Test
    fun rfc5869HkdfTestCase1() {
        val anchor = SharedVectors.anchor("rfc5869TestCase1")
        val prk = Hkdf.extract(anchor.hex("salt"), anchor.hex("ikm"))
        assertEquals(anchor.string("prk"), prk.toLowerHex())
        assertEquals(
            anchor.string("okm"),
            Hkdf.expand(prk, anchor.string("length").toInt(), anchor.hex("info")).toLowerHex(),
        )
    }

    @Test
    fun rfc7748Section61X25519() {
        val anchor = SharedVectors.anchor("rfc7748Section61")
        assertEquals(anchor.string("alicePublic"), X25519.publicKey(anchor.hex("alicePrivate")).toLowerHex())
        assertEquals(anchor.string("bobPublic"), X25519.publicKey(anchor.hex("bobPrivate")).toLowerHex())
        assertEquals(
            anchor.string("sharedSecret"),
            X25519.sharedSecret(anchor.hex("alicePrivate"), anchor.hex("bobPublic")).toLowerHex(),
        )
        assertEquals(
            anchor.string("sharedSecret"),
            X25519.sharedSecret(anchor.hex("bobPrivate"), anchor.hex("alicePublic")).toLowerHex(),
        )
    }

    @Test
    fun rfc8439Section282ChaCha20Poly1305() {
        val anchor = SharedVectors.anchor("rfc8439Section282")
        val sealed =
            ChaCha20Poly1305.seal(anchor.hex("key"), anchor.hex("nonce"), anchor.hex("aad"), anchor.hex("plaintext"))
        assertEquals(anchor.string("ciphertext") + anchor.string("tag"), sealed.toLowerHex())
        assertContentEquals(
            anchor.hex("plaintext"),
            ChaCha20Poly1305.open(anchor.hex("key"), anchor.hex("nonce"), anchor.hex("aad"), sealed),
        )
    }

    @Test
    fun sha256OfTheEnrolmentKeyIsItsFingerprint() {
        val vector = SharedVectors.list("enrolment").first()
        assertEquals(vector.string("fingerprint"), enrolmentKeyFingerprint(vector.hex("recipientPublicKey")))
        assertEquals(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Sha256.digest(ByteArray(0)).toLowerHex(),
        )
    }

    @Test
    fun theFrameVectorsOpenWithTheSameAead() {
        for (vector in SharedVectors.list("frames")) {
            val deviceId = vector.hex("deviceId")
            val counter = vector.counter("counter")
            val nonce = vector.hex("nonce")
            assertContentEquals(
                vector.hex("plaintext"),
                ChaCha20Poly1305.open(vector.hex("key"), nonce, vector.hex("aad"), vector.hex("ciphertext")),
                vector.string("name"),
            )
            assertEquals(deviceId.size, CryptoSpec.DEVICE_ID_LENGTH)
            assertEquals(counter.toULong(), vector.string("counter").toULong())
        }
    }

    @Test
    fun theSetupVectorsDeriveTheKeysAndSealEveryMessage() {
        val appPrivate = setup.hex("appPrivateKey")
        val hubPrivate = setup.hex("hubPrivateKey")
        assertEquals(setup.string("appPublicKey"), X25519.publicKey(appPrivate).toLowerHex())
        assertEquals(setup.string("hubPublicKey"), X25519.publicKey(hubPrivate).toLowerHex())
        assertEquals(
            setup.string("sharedSecret"),
            X25519.sharedSecret(appPrivate, setup.hex("hubPublicKey")).toLowerHex(),
        )
        val (appToHub, hubToApp) =
            SetupCipher.deriveKeys(
                appPrivate,
                setup.hex("hubPublicKey"),
                setup.hex("appPublicKey"),
                setup.hex("hubPublicKey"),
                setup.string("popCode"),
            )
        assertEquals(setup.string("appToHubKey"), appToHub.toLowerHex())
        assertEquals(setup.string("hubToAppKey"), hubToApp.toLowerHex())
        assertEquals(setup.string("aad"), byteArrayOf(CryptoSpec.PROTOCOL_MAJOR.toByte()).toLowerHex())

        val app = SetupCipher.of(SetupRole.App, appPrivate, setup.hex("hubPublicKey"), setup.string("popCode"))
        val hub =
            SetupCipher.of(
                SetupRole.Device,
                hubPrivate,
                setup.hex("appPublicKey"),
                setup.string("normalizedPopCode"),
            )
        for (vector in setup.getValue("messages").jsonArray.map { it.jsonObject }) {
            val counter = vector.counter("counter")
            assertEquals(vector.string("nonce"), SetupCipher.nonce(counter).toLowerHex())
            val (sender, receiver) = if (vector.string("direction") == "appToHub") app to hub else hub to app
            val sealed = sender.seal(vector.hex("plaintext"))
            assertEquals(counter, sealed.counter)
            assertEquals(vector.string("ciphertext"), sealed.ciphertext.toLowerHex())
            assertContentEquals(vector.hex("plaintext"), receiver.open(counter, vector.hex("ciphertext")))
        }
    }

    private fun sessions(
        appCode: String,
        hubCode: String,
    ): Pair<SetupCipher, SetupCipher> {
        val appPrivate = setup.hex("appPrivateKey")
        val hubPrivate = setup.hex("hubPrivateKey")
        return SetupCipher.of(SetupRole.App, appPrivate, X25519.publicKey(hubPrivate), appCode) to
            SetupCipher.of(SetupRole.Device, hubPrivate, X25519.publicKey(appPrivate), hubCode)
    }

    private fun failureOf(block: () -> Unit): CryptoFailure = assertFailsWith<CryptoException> { block() }.failure

    @Test
    fun aWrongSetupCodeFailsTheFirstMessageWithADistinctError() {
        val (app, hub) = sessions(setup.string("popCode"), setup.string("wrongPopCode"))
        val sealed = app.seal("identity please".encodeToByteArray())
        assertEquals(CryptoFailure.WrongSetupCode, failureOf { hub.open(sealed.counter, sealed.ciphertext) })
        // And the Device's reply under its own keys does not open for the app either.
        val reply = hub.seal("error".encodeToByteArray())
        assertEquals(CryptoFailure.WrongSetupCode, failureOf { app.open(reply.counter, reply.ciphertext) })
    }

    @Test
    fun aTamperedMessageAfterTheFirstIsAnAuthenticationFailure() {
        val (app, hub) = sessions(setup.string("popCode"), setup.string("normalizedPopCode"))
        val first = app.seal("one".encodeToByteArray())
        hub.open(first.counter, first.ciphertext)
        val second = app.seal("two".encodeToByteArray())
        second.ciphertext[0] = (second.ciphertext[0].toInt() xor 1).toByte()
        assertEquals(CryptoFailure.AuthenticationFailed, failureOf { hub.open(second.counter, second.ciphertext) })
        val tag = app.seal("three".encodeToByteArray())
        tag.ciphertext[tag.ciphertext.lastIndex] = (tag.ciphertext.last().toInt() xor 1).toByte()
        assertEquals(CryptoFailure.AuthenticationFailed, failureOf { hub.open(tag.counter, tag.ciphertext) })
    }

    @Test
    fun aReplayedCounterIsRefused() {
        val (app, hub) = sessions(setup.string("popCode"), setup.string("popCode"))
        val sealed = app.seal("one".encodeToByteArray())
        hub.open(sealed.counter, sealed.ciphertext)
        assertEquals(CryptoFailure.Replay, failureOf { hub.open(sealed.counter, sealed.ciphertext) })
        assertEquals(CryptoFailure.Replay, failureOf { hub.open(-1L, sealed.ciphertext) })
    }

    @Test
    fun anInvalidSetupCodeIsRefused() {
        val code = setup.string("popCode")
        for (wrong in listOf("", "A".repeat(CryptoSpec.SETUP_MAX_CODE_LENGTH + 1), "Ä1B2C3")) {
            assertEquals(CryptoFailure.InvalidSetupCode, failureOf { sessions(code, wrong) }, wrong)
        }
    }

    @Test
    fun aSmallOrderPublicKeyIsRefused() {
        val private = setup.hex("appPrivateKey")
        assertEquals(CryptoFailure.InvalidPublicKey, failureOf { X25519.sharedSecret(private, ByteArray(32)) })
        val one = ByteArray(32).also { it[0] = 1 }
        assertEquals(CryptoFailure.InvalidPublicKey, failureOf { X25519.sharedSecret(private, one) })
    }

    @Test
    fun aTruncatedMessageIsAnAuthenticationFailure() {
        val (app, hub) = sessions(setup.string("popCode"), setup.string("popCode"))
        val first = app.seal("one".encodeToByteArray())
        hub.open(first.counter, first.ciphertext)
        assertEquals(CryptoFailure.AuthenticationFailed, failureOf { hub.open(1, ByteArray(5)) })
    }

    @Test
    fun secureRandomGivesFreshBytes() {
        val a = secureRandom(32)
        val b = secureRandom(32)
        assertEquals(32, a.size)
        assertEquals(false, a.contentEquals(b))
    }
}
