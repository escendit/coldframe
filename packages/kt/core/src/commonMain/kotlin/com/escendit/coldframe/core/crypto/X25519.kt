package com.escendit.coldframe.core.crypto

/**
 * X25519 (RFC 7748) in pure Kotlin: the Montgomery ladder over 16 limbs of 16 bits, with a
 * constant-time conditional swap, as TweetNaCl does it. The scalar is clamped; the top bit of the
 * u-coordinate is ignored. JDK `KeyAgreement` needs Android 33, so the core carries its own.
 */
internal object X25519 {
    const val KEY_LENGTH: Int = CryptoSpec.X25519_KEY_LENGTH

    private val BASE_POINT = ByteArray(KEY_LENGTH).also { it[0] = 9 }
    private val A24 = longArrayOf(0xDB41, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)

    /** The public key of [privateKey]. */
    fun publicKey(privateKey: ByteArray): ByteArray = scalarMult(privateKey, BASE_POINT)

    /**
     * The shared secret of [privateKey] and [publicKey]. A small-order [publicKey] gives the
     * all-zero output, which is refused ([CryptoFailure.InvalidPublicKey]).
     */
    fun sharedSecret(
        privateKey: ByteArray,
        publicKey: ByteArray,
    ): ByteArray {
        val shared = scalarMult(privateKey, publicKey)
        var any = 0
        for (byte in shared) any = any or byte.toInt()
        if (any == 0) throw CryptoException(CryptoFailure.InvalidPublicKey)
        return shared
    }

    fun scalarMult(
        scalar: ByteArray,
        point: ByteArray,
    ): ByteArray {
        require(scalar.size == KEY_LENGTH && point.size == KEY_LENGTH) { "X25519 keys are 32 bytes" }
        val z = scalar.copyOf()
        z[31] = ((z[31].toInt() and 127) or 64).toByte()
        z[0] = (z[0].toInt() and 248).toByte()
        val x = unpack(point)
        val a = LongArray(16)
        val b = x.copyOf()
        val c = LongArray(16)
        val d = LongArray(16)
        val e = LongArray(16)
        val f = LongArray(16)
        a[0] = 1
        d[0] = 1
        for (i in 254 downTo 0) {
            val bit = ((z[i ushr 3].toInt() and 0xff) ushr (i and 7)) and 1
            swap(a, b, bit)
            swap(c, d, bit)
            add(e, a, c)
            sub(a, a, c)
            add(c, b, d)
            sub(b, b, d)
            square(d, e)
            square(f, a)
            mul(a, c, a)
            mul(c, b, e)
            add(e, a, c)
            sub(a, a, c)
            square(b, a)
            sub(c, d, f)
            mul(a, c, A24)
            add(a, a, d)
            mul(c, c, a)
            mul(a, d, f)
            mul(d, b, x)
            square(b, e)
            swap(a, b, bit)
            swap(c, d, bit)
        }
        invert(c, c)
        mul(a, a, c)
        val out = pack(a)
        z.fill(0)
        for (limbs in listOf(a, b, c, d, e, f, x)) limbs.fill(0)
        return out
    }

    private fun unpack(bytes: ByteArray): LongArray {
        val out = LongArray(16)
        for (i in 0 until 16) {
            out[i] = (bytes[2 * i].toLong() and 0xff) + ((bytes[2 * i + 1].toLong() and 0xff) shl 8)
        }
        out[15] = out[15] and 0x7fff
        return out
    }

    private fun carry(o: LongArray) {
        for (i in 0 until 16) {
            o[i] += 1L shl 16
            val c = o[i] shr 16
            if (i < 15) o[i + 1] += c - 1 else o[0] += 38 * (c - 1)
            o[i] -= c shl 16
        }
    }

    /** Swaps [p] and [q] when [bit] is 1, without a branch on it. */
    private fun swap(
        p: LongArray,
        q: LongArray,
        bit: Int,
    ) {
        val mask = (bit - 1).toLong().inv()
        for (i in 0 until 16) {
            val t = mask and (p[i] xor q[i])
            p[i] = p[i] xor t
            q[i] = q[i] xor t
        }
    }

    private fun pack(n: LongArray): ByteArray {
        val t = n.copyOf()
        val m = LongArray(16)
        carry(t)
        carry(t)
        carry(t)
        for (j in 0 until 2) {
            m[0] = t[0] - 0xffed
            for (i in 1 until 15) {
                m[i] = t[i] - 0xffff - ((m[i - 1] shr 16) and 1)
                m[i - 1] = m[i - 1] and 0xffff
            }
            m[15] = t[15] - 0x7fff - ((m[14] shr 16) and 1)
            val b = ((m[15] shr 16) and 1).toInt()
            m[14] = m[14] and 0xffff
            swap(t, m, 1 - b)
        }
        val out = ByteArray(KEY_LENGTH)
        for (i in 0 until 16) {
            out[2 * i] = (t[i] and 0xff).toByte()
            out[2 * i + 1] = (t[i] shr 8).toByte()
        }
        t.fill(0)
        m.fill(0)
        return out
    }

    private fun add(
        o: LongArray,
        a: LongArray,
        b: LongArray,
    ) {
        for (i in 0 until 16) o[i] = a[i] + b[i]
    }

    private fun sub(
        o: LongArray,
        a: LongArray,
        b: LongArray,
    ) {
        for (i in 0 until 16) o[i] = a[i] - b[i]
    }

    private fun mul(
        o: LongArray,
        a: LongArray,
        b: LongArray,
    ) {
        val t = LongArray(31)
        for (i in 0 until 16) {
            for (j in 0 until 16) t[i + j] += a[i] * b[j]
        }
        for (i in 0 until 15) t[i] += 38 * t[i + 16]
        for (i in 0 until 16) o[i] = t[i]
        carry(o)
        carry(o)
    }

    private fun square(
        o: LongArray,
        a: LongArray,
    ) = mul(o, a, a)

    private fun invert(
        o: LongArray,
        input: LongArray,
    ) {
        val c = input.copyOf()
        for (a in 253 downTo 0) {
            square(c, c)
            if (a != 2 && a != 4) mul(c, c, input)
        }
        c.copyInto(o)
    }
}
