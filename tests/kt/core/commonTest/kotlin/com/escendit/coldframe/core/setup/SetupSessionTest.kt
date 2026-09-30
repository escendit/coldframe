package com.escendit.coldframe.core.setup

import coldframe.setup.v1.IdentityRequest
import coldframe.setup.v1.SealedSetupMessage
import coldframe.setup.v1.SessionHello
import coldframe.setup.v1.SessionHelloReply
import coldframe.setup.v1.SetupMessage
import com.escendit.coldframe.core.crypto.SetupCipher
import com.escendit.coldframe.core.crypto.SetupRole
import okio.ByteString.Companion.toByteString
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull

/** The app end of the session against a Device end made from the same production crypto. */
class SetupSessionTest {
    private val devicePrivate = ByteArray(32) { (it + 9).toByte() }

    private fun opened(
        appCode: String,
        deviceCode: String,
    ): Pair<SetupSession, SetupCipher> {
        val app = SetupSession(ByteArray(32) { (it + 1).toByte() })
        val hello = SessionHello.ADAPTER.decode(app.hello())
        assertEquals(1, hello.protocol_version)
        val device = SetupCipher.of(SetupRole.Device, devicePrivate, hello.app_public_key.toByteArray(), deviceCode)
        app.onHelloReply(
            SessionHelloReply(1, SetupCipher.publicKey(devicePrivate).toByteString()).encode(),
            appCode,
        )
        return app to device
    }

    private fun sealedBy(
        device: SetupCipher,
        message: SetupMessage,
    ): ByteArray {
        val sealed = device.seal(message.copy(protocol_version = 1).encode())
        return SealedSetupMessage(1, sealed.counter, sealed.ciphertext.toByteString()).encode()
    }

    @Test
    fun aSealedRequestOpensOnTheDeviceWithTheSameCode() {
        val (app, device) = opened("K7M2Q9XP", "K7M2Q9XP")
        val frame = SealedSetupMessage.ADAPTER.decode(app.seal(SetupMessage(identity_request = IdentityRequest())))
        assertEquals(0L, frame.counter)
        val plain = device.open(frame.counter, frame.ciphertext.toByteArray())
        assertNotNull(SetupMessage.ADAPTER.decode(plain).identity_request)
        val reply = app.open(sealedBy(device, SetupMessage(identity_request = IdentityRequest())))
        assertNotNull(reply.identity_request)
    }

    @Test
    fun aFirstReplyThatDoesNotOpenIsAWrongCode() {
        val (app, device) = opened("K7M2Q9XP", "K7M2Q9XQ")
        val error = assertFailsWith<SessionException> { app.open(sealedBy(device, SetupMessage())) }
        assertEquals(SessionError.WrongSetupCode, error.error)
    }

    @Test
    fun aTamperedOrReplayedLaterReplyIsRefused() {
        val (app, device) = opened("K7M2Q9XP", "K7M2Q9XP")
        val first = sealedBy(device, SetupMessage())
        app.open(first)
        assertEquals(SessionError.Tampered, assertFailsWith<SessionException> { app.open(first) }.error)
        val second = sealedBy(device, SetupMessage())
        second[second.lastIndex] = (second.last().toInt() xor 1).toByte()
        assertEquals(SessionError.Tampered, assertFailsWith<SessionException> { app.open(second) }.error)
    }

    @Test
    fun garbageAndOtherVersionsAreMalformed() {
        val (app, _) = opened("K7M2Q9XP", "K7M2Q9XP")
        assertEquals(
            SessionError.Malformed,
            assertFailsWith<SessionException> { app.open(byteArrayOf(0x0a, 0x7f)) }.error,
        )
        val other = SetupSession()
        assertEquals(
            SessionError.Malformed,
            assertFailsWith<SessionException> {
                other.onHelloReply(SessionHelloReply(2, ByteArray(32).toByteString()).encode(), "A")
            }.error,
        )
        assertEquals(
            SessionError.KeyExchange,
            assertFailsWith<SessionException> {
                other.onHelloReply(SessionHelloReply(1, ByteArray(32).toByteString()).encode(), "A")
            }.error,
        )
    }
}
