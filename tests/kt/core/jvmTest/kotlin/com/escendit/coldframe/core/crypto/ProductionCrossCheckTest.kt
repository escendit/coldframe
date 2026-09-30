package com.escendit.coldframe.core.crypto

import kotlin.random.Random
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals

/**
 * The pure-Kotlin production crypto against the JDK's providers ([JdkCrypto]) on random inputs:
 * a second, independent implementation beside the shared vectors.
 */
class ProductionCrossCheckTest {
    private val random = Random(20260929)

    @Test
    fun x25519MatchesTheJdk() {
        repeat(40) {
            val private = random.nextBytes(32)
            val peer = JdkCrypto.x25519PublicKey(random.nextBytes(32))
            assertEquals(JdkCrypto.hex(JdkCrypto.x25519PublicKey(private)), X25519.publicKey(private).toLowerHex())
            assertEquals(
                JdkCrypto.hex(JdkCrypto.x25519(private, peer)),
                X25519.sharedSecret(private, peer).toLowerHex(),
            )
        }
    }

    @Test
    fun hkdfAndSha256MatchTheJdk() {
        repeat(40) {
            val salt = random.nextBytes(random.nextInt(0, 80))
            val ikm = random.nextBytes(random.nextInt(1, 200))
            val info = random.nextBytes(random.nextInt(0, 100))
            val length = random.nextInt(1, 200)
            assertContentEquals(JdkCrypto.sha256(ikm), Sha256.digest(ikm))
            assertContentEquals(JdkCrypto.hkdf(salt, ikm, info, length), Hkdf.derive(salt, ikm, length, info))
        }
    }

    @Test
    fun chaCha20Poly1305MatchesTheJdk() {
        repeat(40) {
            val key = random.nextBytes(32)
            val nonce = random.nextBytes(12)
            val aad = random.nextBytes(random.nextInt(0, 40))
            val plaintext = random.nextBytes(random.nextInt(0, 1200))
            val sealed = ChaCha20Poly1305.seal(key, nonce, aad, plaintext)
            assertContentEquals(JdkCrypto.seal(key, nonce, aad, plaintext), sealed)
            assertContentEquals(plaintext, ChaCha20Poly1305.open(key, nonce, aad, sealed))
        }
    }
}
