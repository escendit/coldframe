package com.escendit.coldframe.core.crypto

import com.escendit.coldframe.core.crypto.JdkCrypto.hex
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals

/** The JDK primitives reproduce the RFC vectors in `vectors.json`, copied verbatim from the RFCs. */
class CryptoAnchorsTest {
    @Test
    fun rfc5869HkdfSha256TestCase1() {
        val anchor = Vectors.anchor("rfc5869TestCase1")
        val prk = JdkCrypto.hkdfExtract(anchor.bytes("salt"), anchor.bytes("ikm"))
        assertEquals(anchor.text("prk"), hex(prk))
        assertEquals(
            anchor.text("okm"),
            hex(JdkCrypto.hkdfExpand(prk, anchor.bytes("info"), anchor.getValue("length").int())),
        )
    }

    @Test
    fun rfc7748X25519() {
        val anchor = Vectors.anchor("rfc7748Section61")
        assertEquals(anchor.text("alicePublic"), hex(JdkCrypto.x25519PublicKey(anchor.bytes("alicePrivate"))))
        assertEquals(anchor.text("bobPublic"), hex(JdkCrypto.x25519PublicKey(anchor.bytes("bobPrivate"))))
        assertEquals(
            anchor.text("sharedSecret"),
            hex(JdkCrypto.x25519(anchor.bytes("alicePrivate"), anchor.bytes("bobPublic"))),
        )
        assertEquals(
            anchor.text("sharedSecret"),
            hex(JdkCrypto.x25519(anchor.bytes("bobPrivate"), anchor.bytes("alicePublic"))),
        )
    }

    @Test
    fun rfc8439ChaCha20Poly1305() {
        val anchor = Vectors.anchor("rfc8439Section282")
        val sealed =
            JdkCrypto.seal(
                anchor.bytes("key"),
                anchor.bytes("nonce"),
                anchor.bytes("aad"),
                anchor.bytes("plaintext"),
            )
        assertEquals(anchor.text("ciphertext") + anchor.text("tag"), hex(sealed))
    }

    @Test
    fun rfc9180A21BaseModeFirstEncryption() {
        val anchor = Vectors.anchor("rfc9180A21")
        assertEquals(CryptoSpec.HPKE_KEM_ID, anchor.getValue("kemId").int())
        assertEquals(CryptoSpec.HPKE_KDF_ID, anchor.getValue("kdfId").int())
        assertEquals(CryptoSpec.HPKE_AEAD_ID, anchor.getValue("aeadId").int())
        val (skE, pkE) = JdkCrypto.deriveKeyPair(anchor.bytes("ikmE"))
        assertEquals(anchor.text("skEm"), hex(skE))
        assertEquals(anchor.text("pkEm"), hex(pkE))
        val shared = JdkCrypto.hpkeSharedSecret(JdkCrypto.x25519(skE, anchor.bytes("pkRm")), pkE, anchor.bytes("pkRm"))
        assertEquals(anchor.text("sharedSecret"), hex(shared))
        val (key, baseNonce) = JdkCrypto.keySchedule(shared, anchor.bytes("info"))
        assertEquals(anchor.text("key"), hex(key))
        assertEquals(anchor.text("baseNonce"), hex(baseNonce))
        val (enc, ciphertext) =
            JdkCrypto.sealBase(
                anchor.bytes("pkRm"),
                anchor.bytes("ikmE"),
                anchor.bytes("info"),
                anchor.bytes("aad"),
                anchor.bytes("pt"),
            )
        assertEquals(anchor.text("enc"), hex(enc))
        assertEquals(anchor.text("ct"), hex(ciphertext))
        assertContentEquals(
            anchor.bytes("pt"),
            JdkCrypto.openBase(anchor.bytes("skRm"), enc, anchor.bytes("info"), anchor.bytes("aad"), ciphertext),
        )
    }
}
