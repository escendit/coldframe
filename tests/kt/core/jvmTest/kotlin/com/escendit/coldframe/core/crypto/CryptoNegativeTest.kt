package com.escendit.coldframe.core.crypto

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** What must fail: a wrong PoP code, tampering, a replayed counter, the wrong enrolment key. */
class CryptoNegativeTest {
    private val setup = Vectors.list("setup").first()

    private fun sessions(
        appCode: String,
        hubCode: String,
    ): Pair<JdkCrypto.SetupSession, JdkCrypto.SetupSession> {
        val appPrivateKey = setup.bytes("appPrivateKey")
        val hubPrivateKey = setup.bytes("hubPrivateKey")
        return JdkCrypto.SetupSession(true, appPrivateKey, JdkCrypto.x25519PublicKey(hubPrivateKey), appCode) to
            JdkCrypto.SetupSession(false, hubPrivateKey, JdkCrypto.x25519PublicKey(appPrivateKey), hubCode)
    }

    private fun failureOf(block: () -> Unit): Failure = assertFailsWith<CryptoFailure> { block() }.failure

    @Test
    fun aWrongSetupCodeFailsTheFirstMessageWithADistinctError() {
        val (app, hub) = sessions(setup.text("popCode"), setup.text("wrongPopCode"))
        val (counter, ciphertext) = app.seal("identity please".toByteArray())
        assertEquals(Failure.WRONG_SETUP_CODE, failureOf { hub.open(counter, ciphertext) })
    }

    @Test
    fun aTamperedSetupMessageAfterTheFirstIsAnAuthenticationFailure() {
        val (app, hub) = sessions(setup.text("popCode"), setup.text("normalizedPopCode"))
        val (first, firstCiphertext) = app.seal("one".toByteArray())
        hub.open(first, firstCiphertext)
        val (second, secondCiphertext) = app.seal("two".toByteArray())
        secondCiphertext[0] = (secondCiphertext[0].toInt() xor 1).toByte()
        assertEquals(Failure.AUTHENTICATION_FAILED, failureOf { hub.open(second, secondCiphertext) })
    }

    @Test
    fun aReplayedSetupCounterIsRefused() {
        val (app, hub) = sessions(setup.text("popCode"), setup.text("popCode"))
        val (counter, ciphertext) = app.seal("one".toByteArray())
        hub.open(counter, ciphertext)
        assertEquals(Failure.REPLAY, failureOf { hub.open(counter, ciphertext) })
    }

    @Test
    fun anInvalidSetupCodeIsRefused() {
        val code = setup.text("popCode")
        for (wrong in listOf("", "A".repeat(CryptoSpec.SETUP_MAX_CODE_LENGTH + 1), "Ä1B2C3")) {
            assertEquals(Failure.INVALID_SETUP_CODE, failureOf { sessions(code, wrong) }, wrong)
        }
    }

    @Test
    fun aSmallOrderPublicKeyIsRefused() {
        val vector = Vectors.list("enrolment").first()
        val keys = JdkCrypto.deviceKeys(vector.bytes("rootKey"))
        assertEquals(
            Failure.INVALID_PUBLIC_KEY,
            failureOf {
                JdkCrypto.sealBase(
                    ByteArray(32),
                    ByteArray(32),
                    JdkCrypto.enrolmentInfo,
                    keys.deviceId,
                    keys.deviceKey,
                )
            },
        )
    }

    @Test
    fun aTamperedFrameOrChangedAadFails() {
        val vector = Vectors.list("frames").first()
        val key = vector.bytes("key")
        val deviceId = vector.bytes("deviceId")
        val counter = vector.number("counter")
        val sealed = vector.bytes("ciphertext")

        val flipped = sealed.copyOf().also { it[0] = (it[0].toInt() xor 0x80).toByte() }
        assertEquals(Failure.AUTHENTICATION_FAILED, failureOf { JdkCrypto.openFrame(key, deviceId, counter, flipped) })
        val flippedTag = sealed.copyOf().also { it[it.lastIndex] = (it[it.lastIndex].toInt() xor 1).toByte() }
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openFrame(key, deviceId, counter, flippedTag) },
        )
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openFrame(key, deviceId, counter + 1, sealed) },
        )
        val otherDevice = deviceId.copyOf().also { it[7] = (it[7].toInt() xor 1).toByte() }
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openFrame(key, otherDevice, counter, sealed) },
        )
    }

    @Test
    fun aReplayedFrameIsRefusedAndAForgedOneDoesNotMoveTheWindow() {
        val vector = Vectors.list("frames").first()
        val key = vector.bytes("key")
        val deviceId = vector.bytes("deviceId")
        val counter = vector.number("counter")
        val sealed = vector.bytes("ciphertext")
        val window = JdkCrypto.ReplayWindow()

        val forged = sealed.copyOf().also { it[1] = (it[1].toInt() xor 1).toByte() }
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openFrame(key, deviceId, counter, forged, window) },
        )
        assertNull(window.highest)
        JdkCrypto.openFrame(key, deviceId, counter, sealed, window)
        assertEquals(Failure.REPLAY, failureOf { JdkCrypto.openFrame(key, deviceId, counter, sealed, window) })
    }

    @Test
    fun enrolmentOpensOnlyWithTheRightKeyAndDeviceId() {
        val vector = Vectors.list("enrolment").first()
        val recipient = vector.bytes("recipientPrivateKey")
        val deviceId = vector.bytes("deviceId")
        val enc = vector.bytes("enc")
        val ciphertext = vector.bytes("ciphertext")

        val wrongKey = recipient.copyOf().also { it[0] = (it[0].toInt() xor 0x40).toByte() }
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openBase(wrongKey, enc, JdkCrypto.enrolmentInfo, deviceId, ciphertext) },
        )
        val otherId = deviceId.copyOf().also { it[0] = (it[0].toInt() xor 1).toByte() }
        assertEquals(
            Failure.AUTHENTICATION_FAILED,
            failureOf { JdkCrypto.openBase(recipient, enc, JdkCrypto.enrolmentInfo, otherId, ciphertext) },
        )
    }

    @Test
    fun aChangedBodyOrPathBreaksTheHeartbeatSignature() {
        val vector = Vectors.list("heartbeat").first()
        val key = vector.bytes("hubAuthKey")
        val body = vector.bytes("body")
        val signature = vector.bytes("signature")

        fun canonical(
            path: String = vector.text("path"),
            requestBody: ByteArray = body,
            timestamp: Long = vector.number("timestampMs"),
        ) = JdkCrypto.heartbeatCanonical(vector.text("method"), path, requestBody, timestamp, vector.bytes("nonce"))

        assertTrue(JdkCrypto.heartbeatVerify(key, canonical(), signature))
        val changedBody = body.copyOf().also { it[0] = (it[0].toInt() xor 1).toByte() }
        assertFalse(JdkCrypto.heartbeatVerify(key, canonical(requestBody = changedBody), signature))
        assertFalse(JdkCrypto.heartbeatVerify(key, canonical(path = "/device/ingest"), signature))
        assertFalse(JdkCrypto.heartbeatVerify(key, canonical(timestamp = vector.number("timestampMs") + 1), signature))
    }
}
