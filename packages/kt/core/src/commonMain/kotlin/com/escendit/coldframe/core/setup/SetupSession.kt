package com.escendit.coldframe.core.setup

import coldframe.setup.v1.SealedSetupMessage
import coldframe.setup.v1.SessionHello
import coldframe.setup.v1.SessionHelloReply
import coldframe.setup.v1.SetupMessage
import com.escendit.coldframe.core.crypto.CryptoException
import com.escendit.coldframe.core.crypto.CryptoFailure
import com.escendit.coldframe.core.crypto.CryptoSpec
import com.escendit.coldframe.core.crypto.SetupCipher
import com.escendit.coldframe.core.crypto.SetupRole
import okio.ByteString.Companion.toByteString
import okio.IOException

/** Why a session step failed. Carries no key, code or message content. */
public enum class SessionError {
    /** The Device's first sealed reply did not open: the setup code is wrong. */
    WrongSetupCode,

    /** A later reply did not authenticate, or its counter did not increase. */
    Tampered,

    /** A frame did not decode, or carried another protocol version. */
    Malformed,

    /** The key exchange failed: a small-order key or an unusable code. */
    KeyExchange,
}

public class SessionException(
    public val error: SessionError,
) : Exception(error.name)

/**
 * The app end of one BLE setup session, as `packages/rs/setup/src/app.rs` does it: [hello] makes
 * the `SessionHello` frame, [onHelloReply] derives the keys from the code the user typed, [seal]
 * frames each request and [open] reads each reply. Deliberately no `toString` of its keys.
 */
public class SetupSession(
    private val privateKey: ByteArray = SetupCipher.newPrivateKey(),
) {
    private var cipher: SetupCipher? = null

    /** The `SessionHello` frame. */
    public fun hello(): ByteArray =
        SessionHello(
            protocol_version = CryptoSpec.PROTOCOL_MAJOR,
            app_public_key = SetupCipher.publicKey(privateKey).toByteString(),
        ).encode()

    /** Reads `SessionHelloReply` and derives the session keys with the normalized [code]. */
    public fun onHelloReply(
        frame: ByteArray,
        code: String,
    ) {
        val reply = decode { SessionHelloReply.ADAPTER.decode(frame) }
        if (reply.protocol_version != CryptoSpec.PROTOCOL_MAJOR ||
            reply.device_public_key.size != CryptoSpec.X25519_KEY_LENGTH
        ) {
            throw SessionException(SessionError.Malformed)
        }
        cipher =
            try {
                SetupCipher.of(SetupRole.App, privateKey, reply.device_public_key.toByteArray(), code)
            } catch (failure: CryptoException) {
                throw SessionException(SessionError.KeyExchange)
            }
    }

    /** Seals [message] into a `SealedSetupMessage` frame. */
    public fun seal(message: SetupMessage): ByteArray {
        val cipher = cipher ?: throw SessionException(SessionError.KeyExchange)
        val plain = message.copy(protocol_version = CryptoSpec.PROTOCOL_MAJOR).encode()
        val sealed = cipher.seal(plain)
        plain.fill(0)
        return SealedSetupMessage(
            protocol_version = CryptoSpec.PROTOCOL_MAJOR,
            counter = sealed.counter,
            ciphertext = sealed.ciphertext.toByteString(),
        ).encode()
    }

    /** Opens a `SealedSetupMessage` frame from the Device. */
    public fun open(frame: ByteArray): SetupMessage {
        val cipher = cipher ?: throw SessionException(SessionError.KeyExchange)
        val sealed = decode { SealedSetupMessage.ADAPTER.decode(frame) }
        if (sealed.protocol_version != CryptoSpec.PROTOCOL_MAJOR) throw SessionException(SessionError.Malformed)
        val plain =
            try {
                cipher.open(sealed.counter, sealed.ciphertext.toByteArray())
            } catch (failure: CryptoException) {
                throw SessionException(
                    if (failure.failure ==
                        CryptoFailure.WrongSetupCode
                    ) {
                        SessionError.WrongSetupCode
                    } else {
                        SessionError.Tampered
                    },
                )
            }
        val message = decode { SetupMessage.ADAPTER.decode(plain) }
        plain.fill(0)
        if (message.protocol_version != CryptoSpec.PROTOCOL_MAJOR) throw SessionException(SessionError.Malformed)
        return message
    }

    /** Wipes the keys. */
    public fun close() {
        cipher?.close()
        cipher = null
        privateKey.fill(0)
    }

    override fun toString(): String = "SetupSession"

    private inline fun <T> decode(read: () -> T): T =
        try {
            read()
        } catch (malformed: IOException) {
            throw SessionException(SessionError.Malformed)
        } catch (malformed: IllegalArgumentException) {
            throw SessionException(SessionError.Malformed)
        } catch (malformed: IllegalStateException) {
            throw SessionException(SessionError.Malformed)
        }
}
