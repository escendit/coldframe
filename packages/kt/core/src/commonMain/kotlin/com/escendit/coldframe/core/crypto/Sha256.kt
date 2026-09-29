package com.escendit.coldframe.core.crypto

/**
 * SHA-256 (FIPS 180-4) and HMAC-SHA256 (RFC 2104) in pure Kotlin, so the same code runs on
 * Android 29 and iOS (AD-25). Nothing here depends on secret-dependent branches or lookups.
 */
internal object Sha256 {
    const val DIGEST_LENGTH: Int = 32
    private const val BLOCK_LENGTH = 64

    private val K =
        intArrayOf(
            0x428a2f98,
            0x71374491,
            0xb5c0fbcfL.toInt(),
            0xe9b5dba5L.toInt(),
            0x3956c25b,
            0x59f111f1,
            0x923f82a4L.toInt(),
            0xab1c5ed5L.toInt(),
            0xd807aa98L.toInt(),
            0x12835b01,
            0x243185be,
            0x550c7dc3,
            0x72be5d74,
            0x80deb1feL.toInt(),
            0x9bdc06a7L.toInt(),
            0xc19bf174L.toInt(),
            0xe49b69c1L.toInt(),
            0xefbe4786L.toInt(),
            0x0fc19dc6,
            0x240ca1cc,
            0x2de92c6f,
            0x4a7484aa,
            0x5cb0a9dc,
            0x76f988da,
            0x983e5152L.toInt(),
            0xa831c66dL.toInt(),
            0xb00327c8L.toInt(),
            0xbf597fc7L.toInt(),
            0xc6e00bf3L.toInt(),
            0xd5a79147L.toInt(),
            0x06ca6351,
            0x14292967,
            0x27b70a85,
            0x2e1b2138,
            0x4d2c6dfc,
            0x53380d13,
            0x650a7354,
            0x766a0abb,
            0x81c2c92eL.toInt(),
            0x92722c85L.toInt(),
            0xa2bfe8a1L.toInt(),
            0xa81a664bL.toInt(),
            0xc24b8b70L.toInt(),
            0xc76c51a3L.toInt(),
            0xd192e819L.toInt(),
            0xd6990624L.toInt(),
            0xf40e3585L.toInt(),
            0x106aa070,
            0x19a4c116,
            0x1e376c08,
            0x2748774c,
            0x34b0bcb5,
            0x391c0cb3,
            0x4ed8aa4a,
            0x5b9cca4f,
            0x682e6ff3,
            0x748f82ee,
            0x78a5636f,
            0x84c87814L.toInt(),
            0x8cc70208L.toInt(),
            0x90befffaL.toInt(),
            0xa4506cebL.toInt(),
            0xbef9a3f7L.toInt(),
            0xc67178f2L.toInt(),
        )

    private val INITIAL =
        intArrayOf(
            0x6a09e667,
            0xbb67ae85L.toInt(),
            0x3c6ef372,
            0xa54ff53aL.toInt(),
            0x510e527f,
            0x9b05688cL.toInt(),
            0x1f83d9ab,
            0x5be0cd19,
        )

    /** The digest of the concatenation of [parts]. */
    fun digest(vararg parts: ByteArray): ByteArray {
        val total = parts.sumOf { it.size }
        val padded = ((total + 9 + BLOCK_LENGTH - 1) / BLOCK_LENGTH) * BLOCK_LENGTH
        val message = ByteArray(padded)
        var offset = 0
        for (part in parts) {
            part.copyInto(message, offset)
            offset += part.size
        }
        message[total] = 0x80.toByte()
        val bits = total.toLong() * 8
        for (index in 0 until 8) message[padded - 1 - index] = (bits ushr (8 * index)).toByte()

        val state = INITIAL.copyOf()
        val w = IntArray(64)
        for (block in 0 until padded / BLOCK_LENGTH) {
            val base = block * BLOCK_LENGTH
            for (t in 0 until 16) w[t] = message.intBigEndian(base + 4 * t)
            for (t in 16 until 64) {
                val s0 = w[t - 15].rotateRight(7) xor w[t - 15].rotateRight(18) xor (w[t - 15] ushr 3)
                val s1 = w[t - 2].rotateRight(17) xor w[t - 2].rotateRight(19) xor (w[t - 2] ushr 10)
                w[t] = w[t - 16] + s0 + w[t - 7] + s1
            }
            var a = state[0]
            var b = state[1]
            var c = state[2]
            var d = state[3]
            var e = state[4]
            var f = state[5]
            var g = state[6]
            var h = state[7]
            for (t in 0 until 64) {
                val s1 = e.rotateRight(6) xor e.rotateRight(11) xor e.rotateRight(25)
                val choose = (e and f) xor (e.inv() and g)
                val temp1 = h + s1 + choose + K[t] + w[t]
                val s0 = a.rotateRight(2) xor a.rotateRight(13) xor a.rotateRight(22)
                val majority = (a and b) xor (a and c) xor (b and c)
                val temp2 = s0 + majority
                h = g
                g = f
                f = e
                e = d + temp1
                d = c
                c = b
                b = a
                a = temp1 + temp2
            }
            state[0] += a
            state[1] += b
            state[2] += c
            state[3] += d
            state[4] += e
            state[5] += f
            state[6] += g
            state[7] += h
        }
        message.fill(0)
        w.fill(0)
        val out = ByteArray(DIGEST_LENGTH)
        for (index in 0 until 8) out.putIntBigEndian(4 * index, state[index])
        return out
    }

    /** HMAC-SHA256 of the concatenation of [parts] under [key]. */
    fun hmac(
        key: ByteArray,
        vararg parts: ByteArray,
    ): ByteArray {
        val block = ByteArray(BLOCK_LENGTH)
        (if (key.size > BLOCK_LENGTH) digest(key) else key).copyInto(block)
        val inner = ByteArray(BLOCK_LENGTH) { (block[it].toInt() xor 0x36).toByte() }
        val outer = ByteArray(BLOCK_LENGTH) { (block[it].toInt() xor 0x5c).toByte() }
        val innerHash = digest(inner, *parts)
        val mac = digest(outer, innerHash)
        block.fill(0)
        inner.fill(0)
        outer.fill(0)
        innerHash.fill(0)
        return mac
    }

    private fun ByteArray.intBigEndian(offset: Int): Int =
        (this[offset].toInt() and 0xff shl 24) or
            (this[offset + 1].toInt() and 0xff shl 16) or
            (this[offset + 2].toInt() and 0xff shl 8) or
            (this[offset + 3].toInt() and 0xff)

    private fun ByteArray.putIntBigEndian(
        offset: Int,
        value: Int,
    ) {
        this[offset] = (value ushr 24).toByte()
        this[offset + 1] = (value ushr 16).toByte()
        this[offset + 2] = (value ushr 8).toByte()
        this[offset + 3] = value.toByte()
    }
}

/** HKDF-SHA256 (RFC 5869). */
internal object Hkdf {
    /** HKDF-Extract; an empty [salt] means 32 zero bytes. */
    fun extract(
        salt: ByteArray,
        ikm: ByteArray,
    ): ByteArray = Sha256.hmac(if (salt.isEmpty()) ByteArray(Sha256.DIGEST_LENGTH) else salt, ikm)

    /** HKDF-Expand of [length] bytes (at most 255 × 32) with the concatenation of [info]. */
    fun expand(
        prk: ByteArray,
        length: Int,
        vararg info: ByteArray,
    ): ByteArray {
        require(length in 0..255 * Sha256.DIGEST_LENGTH) { "HKDF output too long" }
        val out = ByteArray(length)
        var previous = ByteArray(0)
        var written = 0
        var counter = 1
        while (written < length) {
            val block = Sha256.hmac(prk, previous, *info, byteArrayOf(counter.toByte()))
            previous.fill(0)
            val take = minOf(block.size, length - written)
            block.copyInto(out, written, 0, take)
            written += take
            previous = block
            counter++
        }
        previous.fill(0)
        return out
    }

    fun derive(
        salt: ByteArray,
        ikm: ByteArray,
        length: Int,
        vararg info: ByteArray,
    ): ByteArray {
        val prk = extract(salt, ikm)
        val okm = expand(prk, length, *info)
        prk.fill(0)
        return okm
    }
}

/** Compares in time that depends only on the lengths. */
internal fun constantTimeEquals(
    a: ByteArray,
    b: ByteArray,
): Boolean {
    if (a.size != b.size) return false
    var difference = 0
    for (index in a.indices) difference = difference or (a[index].toInt() xor b[index].toInt())
    return difference == 0
}
