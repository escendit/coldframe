package com.escendit.coldframe.core.crypto

/**
 * AEAD_CHACHA20_POLY1305 (RFC 8439) in pure Kotlin: ChaCha20 with a 32-bit block counter and a
 * 96-bit nonce, Poly1305 over 26-bit limbs, and a constant-time tag check. A sealed message is
 * the ciphertext followed by the 16-byte tag.
 */
internal object ChaCha20Poly1305 {
    const val KEY_LENGTH: Int = CryptoSpec.AEAD_KEY_LENGTH
    const val NONCE_LENGTH: Int = CryptoSpec.AEAD_NONCE_LENGTH
    const val TAG_LENGTH: Int = CryptoSpec.AEAD_TAG_LENGTH

    fun seal(
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
        plaintext: ByteArray,
    ): ByteArray {
        checkSizes(key, nonce)
        val ciphertext = ChaCha20.xor(key, nonce, 1, plaintext)
        val tag = tag(key, nonce, aad, ciphertext)
        return ciphertext + tag
    }

    /** Opens [sealed]; a wrong tag is [CryptoFailure.AuthenticationFailed] and nothing is decrypted. */
    fun open(
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
        sealed: ByteArray,
    ): ByteArray {
        checkSizes(key, nonce)
        if (sealed.size < TAG_LENGTH) throw CryptoException(CryptoFailure.AuthenticationFailed)
        val ciphertext = sealed.copyOfRange(0, sealed.size - TAG_LENGTH)
        val expected = tag(key, nonce, aad, ciphertext)
        val actual = sealed.copyOfRange(sealed.size - TAG_LENGTH, sealed.size)
        if (!constantTimeEquals(expected, actual)) throw CryptoException(CryptoFailure.AuthenticationFailed)
        return ChaCha20.xor(key, nonce, 1, ciphertext)
    }

    private fun checkSizes(
        key: ByteArray,
        nonce: ByteArray,
    ) {
        require(key.size == KEY_LENGTH) { "ChaCha20-Poly1305 keys are 32 bytes" }
        require(nonce.size == NONCE_LENGTH) { "ChaCha20-Poly1305 nonces are 12 bytes" }
    }

    private fun tag(
        key: ByteArray,
        nonce: ByteArray,
        aad: ByteArray,
        ciphertext: ByteArray,
    ): ByteArray {
        val oneTimeKey = ChaCha20.block(key, nonce, 0).copyOf(32)
        val lengths = ByteArray(16)
        for (index in 0 until 8) {
            lengths[index] = (aad.size.toLong() ushr (8 * index)).toByte()
            lengths[8 + index] = (ciphertext.size.toLong() ushr (8 * index)).toByte()
        }
        val tag =
            Poly1305.mac(
                oneTimeKey,
                aad + ByteArray(padding(aad.size)) + ciphertext + ByteArray(padding(ciphertext.size)) + lengths,
            )
        oneTimeKey.fill(0)
        return tag
    }

    private fun padding(length: Int): Int = (16 - length % 16) % 16
}

/** The ChaCha20 stream cipher (RFC 8439 section 2.4). */
internal object ChaCha20 {
    private val CONSTANTS = intArrayOf(0x61707865, 0x3320646e, 0x79622d32, 0x6b206574)

    /** One 64-byte key-stream block. */
    fun block(
        key: ByteArray,
        nonce: ByteArray,
        counter: Int,
    ): ByteArray {
        val state = IntArray(16)
        CONSTANTS.copyInto(state)
        for (index in 0 until 8) state[4 + index] = key.intLittleEndian(4 * index)
        state[12] = counter
        for (index in 0 until 3) state[13 + index] = nonce.intLittleEndian(4 * index)
        val working = state.copyOf()
        repeat(10) {
            quarterRound(working, 0, 4, 8, 12)
            quarterRound(working, 1, 5, 9, 13)
            quarterRound(working, 2, 6, 10, 14)
            quarterRound(working, 3, 7, 11, 15)
            quarterRound(working, 0, 5, 10, 15)
            quarterRound(working, 1, 6, 11, 12)
            quarterRound(working, 2, 7, 8, 13)
            quarterRound(working, 3, 4, 9, 14)
        }
        val out = ByteArray(64)
        for (index in 0 until 16) {
            val word = working[index] + state[index]
            out[4 * index] = word.toByte()
            out[4 * index + 1] = (word ushr 8).toByte()
            out[4 * index + 2] = (word ushr 16).toByte()
            out[4 * index + 3] = (word ushr 24).toByte()
        }
        state.fill(0)
        working.fill(0)
        return out
    }

    /** [input] XOR the key stream from block [counter] on. */
    fun xor(
        key: ByteArray,
        nonce: ByteArray,
        counter: Int,
        input: ByteArray,
    ): ByteArray {
        val out = ByteArray(input.size)
        var offset = 0
        var blockCounter = counter
        while (offset < input.size) {
            val stream = block(key, nonce, blockCounter++)
            val take = minOf(64, input.size - offset)
            for (index in 0 until take) {
                out[offset + index] = (input[offset + index].toInt() xor stream[index].toInt()).toByte()
            }
            stream.fill(0)
            offset += take
        }
        return out
    }

    private fun quarterRound(
        s: IntArray,
        a: Int,
        b: Int,
        c: Int,
        d: Int,
    ) {
        s[a] += s[b]
        s[d] = (s[d] xor s[a]).rotateLeft(16)
        s[c] += s[d]
        s[b] = (s[b] xor s[c]).rotateLeft(12)
        s[a] += s[b]
        s[d] = (s[d] xor s[a]).rotateLeft(8)
        s[c] += s[d]
        s[b] = (s[b] xor s[c]).rotateLeft(7)
    }
}

/** The Poly1305 one-time authenticator (RFC 8439 section 2.5), 26-bit limbs. */
internal object Poly1305 {
    private const val MASK = 0x3ffffffL

    fun mac(
        key: ByteArray,
        message: ByteArray,
    ): ByteArray {
        val r0 = key.uintLittleEndian(0) and 0x3ffffff
        val r1 = (key.uintLittleEndian(3) ushr 2) and 0x3ffff03
        val r2 = (key.uintLittleEndian(6) ushr 4) and 0x3ffc0ff
        val r3 = (key.uintLittleEndian(9) ushr 6) and 0x3f03fff
        val r4 = (key.uintLittleEndian(12) ushr 8) and 0x00fffff
        val s1 = r1 * 5
        val s2 = r2 * 5
        val s3 = r3 * 5
        val s4 = r4 * 5
        var h0 = 0L
        var h1 = 0L
        var h2 = 0L
        var h3 = 0L
        var h4 = 0L

        var offset = 0
        val block = ByteArray(17)
        while (offset < message.size) {
            val take = minOf(16, message.size - offset)
            block.fill(0)
            message.copyInto(block, 0, offset, offset + take)
            val high: Long
            if (take == 16) {
                high = 1L shl 24
            } else {
                block[take] = 1
                high = 0
            }
            h0 += block.uintLittleEndian(0) and MASK
            h1 += (block.uintLittleEndian(3) ushr 2) and MASK
            h2 += (block.uintLittleEndian(6) ushr 4) and MASK
            h3 += (block.uintLittleEndian(9) ushr 6) and MASK
            h4 += (block.uintLittleEndian(12) ushr 8) or high

            val d0 = h0 * r0 + h1 * s4 + h2 * s3 + h3 * s2 + h4 * s1
            var d1 = h0 * r1 + h1 * r0 + h2 * s4 + h3 * s3 + h4 * s2
            var d2 = h0 * r2 + h1 * r1 + h2 * r0 + h3 * s4 + h4 * s3
            var d3 = h0 * r3 + h1 * r2 + h2 * r1 + h3 * r0 + h4 * s4
            var d4 = h0 * r4 + h1 * r3 + h2 * r2 + h3 * r1 + h4 * r0

            var c = d0 ushr 26
            h0 = d0 and MASK
            d1 += c
            c = d1 ushr 26
            h1 = d1 and MASK
            d2 += c
            c = d2 ushr 26
            h2 = d2 and MASK
            d3 += c
            c = d3 ushr 26
            h3 = d3 and MASK
            d4 += c
            c = d4 ushr 26
            h4 = d4 and MASK
            h0 += c * 5
            c = h0 ushr 26
            h0 = h0 and MASK
            h1 += c
            offset += take
        }
        block.fill(0)

        var c = h1 ushr 26
        h1 = h1 and MASK
        h2 += c
        c = h2 ushr 26
        h2 = h2 and MASK
        h3 += c
        c = h3 ushr 26
        h3 = h3 and MASK
        h4 += c
        c = h4 ushr 26
        h4 = h4 and MASK
        h0 += c * 5
        c = h0 ushr 26
        h0 = h0 and MASK
        h1 += c

        // h + -p: take it when it does not underflow, without a branch.
        var g0 = h0 + 5
        c = g0 ushr 26
        g0 = g0 and MASK
        var g1 = h1 + c
        c = g1 ushr 26
        g1 = g1 and MASK
        var g2 = h2 + c
        c = g2 ushr 26
        g2 = g2 and MASK
        var g3 = h3 + c
        c = g3 ushr 26
        g3 = g3 and MASK
        val g4 = h4 + c - (1L shl 26)
        val useG = (g4 shr 63).inv()
        val useH = useG.inv()
        h0 = (h0 and useH) or (g0 and useG)
        h1 = (h1 and useH) or (g1 and useG)
        h2 = (h2 and useH) or (g2 and useG)
        h3 = (h3 and useH) or (g3 and useG)
        h4 = (h4 and useH) or (g4 and MASK and useG)

        val w0 = (h0 or (h1 shl 26)) and 0xffffffffL
        val w1 = ((h1 ushr 6) or (h2 shl 20)) and 0xffffffffL
        val w2 = ((h2 ushr 12) or (h3 shl 14)) and 0xffffffffL
        val w3 = ((h3 ushr 18) or (h4 shl 8)) and 0xffffffffL

        var f = w0 + key.uintLittleEndian(16)
        val t0 = f and 0xffffffffL
        f = w1 + key.uintLittleEndian(20) + (f ushr 32)
        val t1 = f and 0xffffffffL
        f = w2 + key.uintLittleEndian(24) + (f ushr 32)
        val t2 = f and 0xffffffffL
        f = w3 + key.uintLittleEndian(28) + (f ushr 32)
        val t3 = f and 0xffffffffL

        val out = ByteArray(16)
        for ((index, word) in listOf(t0, t1, t2, t3).withIndex()) {
            for (byte in 0 until 4) out[4 * index + byte] = (word ushr (8 * byte)).toByte()
        }
        return out
    }
}

private fun ByteArray.intLittleEndian(offset: Int): Int =
    (this[offset].toInt() and 0xff) or
        (this[offset + 1].toInt() and 0xff shl 8) or
        (this[offset + 2].toInt() and 0xff shl 16) or
        (this[offset + 3].toInt() and 0xff shl 24)

private fun ByteArray.uintLittleEndian(offset: Int): Long = intLittleEndian(offset).toLong() and 0xffffffffL
