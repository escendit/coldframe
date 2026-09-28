package com.escendit.coldframe.core.crypto

import com.escendit.coldframe.core.crypto.JdkCrypto.hex
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** The generated [CryptoSpec] on the JDK's providers reproduces every shared vector. */
class CryptoVectorsTest {
    @Test
    fun theVectorsUseTheGeneratedProtocolMajor() {
        assertEquals(CryptoSpec.PROTOCOL_MAJOR, Vectors.root.getValue("protocolMajor").int())
    }

    @Test
    fun keyHierarchy() {
        assertTrue(Vectors.list("keyHierarchy").isNotEmpty())
        for (vector in Vectors.list("keyHierarchy")) {
            val deviceKey = JdkCrypto.deviceKey(vector.bytes("rootKey"))
            assertEquals(vector.text("deviceKey"), hex(deviceKey))
            val keys = JdkCrypto.DeviceKeys(deviceKey)
            assertEquals(vector.text("sealKey"), hex(keys.sealKey))
            assertEquals(vector.text("ackKey"), hex(keys.ackKey))
            assertEquals(vector.text("hubAuthKey"), hex(keys.hubAuthKey))
            assertEquals(vector.text("deviceId"), hex(keys.deviceId))
            assertEquals(CryptoSpec.DEVICE_ID_HEX_LENGTH, hex(keys.deviceId).length)
        }
    }

    @Test
    fun framesAndDownlinks() {
        val frames = Vectors.list("frames")
        assertTrue(frames.any { it.text("purpose") == "seal" } && frames.any { it.text("purpose") == "ack" })
        for (vector in frames) {
            val keys = JdkCrypto.deviceKeys(vector.bytes("rootKey"))
            val key = if (vector.text("purpose") == "seal") keys.sealKey else keys.ackKey
            assertEquals(vector.text("key"), hex(key))
            val deviceId = vector.bytes("deviceId")
            assertContentEquals(keys.deviceId, deviceId)
            val counter = vector.number("counter")
            assertEquals(vector.text("nonce"), hex(JdkCrypto.frameNonce(deviceId, counter)))
            assertEquals(vector.text("aad"), hex(JdkCrypto.frameAad(deviceId, counter)))
            assertEquals(CryptoSpec.FRAME_AAD_LENGTH, JdkCrypto.frameAad(deviceId, counter).size)
            assertEquals(
                vector.text("ciphertext"),
                hex(JdkCrypto.sealFrame(key, deviceId, counter, vector.bytes("plaintext"))),
            )
            assertContentEquals(
                vector.bytes("plaintext"),
                JdkCrypto.openFrame(key, deviceId, counter, vector.bytes("ciphertext")),
            )
        }
    }

    @Test
    fun replayWindow() {
        val replay = Vectors.root.getValue("replay").jsonObject
        assertEquals(CryptoSpec.REPLAY_WINDOW, replay.getValue("window").int())
        val steps = replay.getValue("steps").jsonArray.map { it.jsonObject }
        assertTrue(steps.any { !it.getValue("accepted").jsonPrimitive.boolean })
        val window = JdkCrypto.ReplayWindow()
        for (step in steps) {
            val counter = step.number("counter")
            val accepted = window.wouldAccept(counter)
            if (accepted) window.accept(counter)
            assertEquals(step.getValue("accepted").jsonPrimitive.boolean, accepted, "counter ${step.text("counter")}")
        }
    }

    @Test
    fun enrolment() {
        assertTrue(Vectors.list("enrolment").isNotEmpty())
        for (vector in Vectors.list("enrolment")) {
            val keys = JdkCrypto.deviceKeys(vector.bytes("rootKey"))
            val recipientPrivateKey = vector.bytes("recipientPrivateKey")
            val recipientPublicKey = JdkCrypto.x25519PublicKey(recipientPrivateKey)
            assertEquals(vector.text("recipientPublicKey"), hex(recipientPublicKey))
            assertEquals(vector.text("fingerprint"), JdkCrypto.fingerprint(recipientPublicKey))
            assertContentEquals(JdkCrypto.enrolmentInfo, vector.bytes("info"))

            val (ephemeral, enc) = JdkCrypto.deriveKeyPair(vector.bytes("ikmE"))
            assertEquals(vector.text("ephemeralPrivateKey"), hex(ephemeral))
            assertEquals(vector.text("enc"), hex(enc))
            val shared =
                JdkCrypto.hpkeSharedSecret(
                    JdkCrypto.x25519(ephemeral, recipientPublicKey),
                    enc,
                    recipientPublicKey,
                )
            assertEquals(vector.text("sharedSecret"), hex(shared))
            val (key, baseNonce) = JdkCrypto.keySchedule(shared, JdkCrypto.enrolmentInfo)
            assertEquals(vector.text("key"), hex(key))
            assertEquals(vector.text("baseNonce"), hex(baseNonce))

            val (sealedEnc, ciphertext) =
                JdkCrypto.sealBase(
                    recipientPublicKey,
                    vector.bytes("ikmE"),
                    JdkCrypto.enrolmentInfo,
                    keys.deviceId,
                    keys.deviceKey,
                )
            assertEquals(vector.text("enc"), hex(sealedEnc))
            assertEquals(vector.text("ciphertext"), hex(ciphertext))
            val opened =
                JdkCrypto.openBase(
                    recipientPrivateKey,
                    vector.bytes("enc"),
                    JdkCrypto.enrolmentInfo,
                    vector.bytes("deviceId"),
                    vector.bytes("ciphertext"),
                )
            assertEquals(vector.text("deviceKey"), hex(opened))
        }
    }

    @Test
    fun setupSession() {
        assertTrue(Vectors.list("setup").isNotEmpty())
        for (vector in Vectors.list("setup")) {
            val appPrivateKey = vector.bytes("appPrivateKey")
            val hubPrivateKey = vector.bytes("hubPrivateKey")
            val appPublicKey = JdkCrypto.x25519PublicKey(appPrivateKey)
            val hubPublicKey = JdkCrypto.x25519PublicKey(hubPrivateKey)
            assertEquals(vector.text("appPublicKey"), hex(appPublicKey))
            assertEquals(vector.text("hubPublicKey"), hex(hubPublicKey))
            assertEquals(vector.text("sharedSecret"), hex(JdkCrypto.x25519(appPrivateKey, hubPublicKey)))
            assertContentEquals(byteArrayOf(CryptoSpec.PROTOCOL_MAJOR.toByte()), vector.bytes("aad"))
            val code = vector.text("popCode")
            for ((own, peer) in listOf(appPrivateKey to hubPublicKey, hubPrivateKey to appPublicKey)) {
                val (appToHub, hubToApp) = JdkCrypto.setupKeys(own, peer, appPublicKey, hubPublicKey, code)
                assertEquals(vector.text("appToHubKey"), hex(appToHub))
                assertEquals(vector.text("hubToAppKey"), hex(hubToApp))
            }

            val app = JdkCrypto.SetupSession(true, appPrivateKey, hubPublicKey, code)
            val hub = JdkCrypto.SetupSession(false, hubPrivateKey, appPublicKey, vector.text("normalizedPopCode"))
            for (message in vector.getValue("messages").jsonArray.map { it.jsonObject }) {
                val (sender, receiver) = if (message.text("direction") == "appToHub") app to hub else hub to app
                assertEquals(message.text("nonce"), hex(JdkCrypto.setupNonce(message.number("counter"))))
                val (counter, ciphertext) = sender.seal(message.bytes("plaintext"))
                assertEquals(message.number("counter"), counter)
                assertEquals(message.text("ciphertext"), hex(ciphertext))
                assertContentEquals(message.bytes("plaintext"), receiver.open(counter, ciphertext))
            }
        }
    }

    @Test
    fun heartbeat() {
        assertTrue(Vectors.list("heartbeat").isNotEmpty())
        for (vector in Vectors.list("heartbeat")) {
            val keys = JdkCrypto.deviceKeys(vector.bytes("rootKey"))
            assertEquals(vector.text("hubAuthKey"), hex(keys.hubAuthKey))
            val canonical =
                JdkCrypto.heartbeatCanonical(
                    vector.text("method"),
                    vector.text("path"),
                    vector.bytes("body"),
                    vector.number("timestampMs"),
                    vector.bytes("nonce"),
                )
            assertEquals(vector.text("canonical"), canonical)
            assertTrue(canonical.contains(vector.text("bodyHash")))
            val signature = hex(JdkCrypto.heartbeatSignature(keys.hubAuthKey, canonical))
            assertEquals(vector.text("signature"), signature)

            val headers = vector.getValue("headers").jsonObject.mapValues { it.value.jsonPrimitive.content }
            assertEquals(
                mapOf(
                    CryptoSpec.HEARTBEAT_DEVICE_HEADER to hex(keys.deviceId),
                    CryptoSpec.HEARTBEAT_TIMESTAMP_HEADER to vector.text("timestampMs"),
                    CryptoSpec.HEARTBEAT_NONCE_HEADER to vector.text("nonce"),
                    CryptoSpec.HEARTBEAT_SIGNATURE_HEADER to signature,
                ),
                headers,
            )
        }
    }
}
