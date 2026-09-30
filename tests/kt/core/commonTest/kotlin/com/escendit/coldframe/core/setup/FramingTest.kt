package com.escendit.coldframe.core.setup

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/** `header(1) ‖ fragment` framing, as `packages/rs/setup/src/framing.rs` does it. */
class FramingTest {
    private fun reassemble(payloads: List<ByteArray>): ByteArray {
        val reassembler = Reassembler()
        var frame: ByteArray? = null
        for (payload in payloads) {
            val step = reassembler.push(payload)
            if (step is Reassembled.Frame) frame = step.bytes
        }
        return frame!!
    }

    @Test
    fun fragmentsFitThePayloadAndOnlyTheLastIsMarkedLast() {
        val frame = ByteArray(1117) { it.toByte() }
        for (maxPayload in listOf(20, 185, 248)) {
            val payloads = Framing.fragments(frame, maxPayload)
            assertTrue(payloads.all { it.size <= maxPayload }, "$maxPayload")
            assertTrue(payloads.dropLast(1).all { it[0] == Framing.HEADER_MORE })
            assertEquals(Framing.HEADER_LAST, payloads.last()[0])
            assertContentEquals(frame, reassemble(payloads))
        }
    }

    @Test
    fun anEmptyFrameIsOneEmptyLastFragment() {
        val payloads = Framing.fragments(ByteArray(0), 20)
        assertEquals(1, payloads.size)
        assertContentEquals(byteArrayOf(Framing.HEADER_LAST), payloads.single())
    }

    @Test
    fun aBadHeaderOrAnEmptyPayloadDropsThePartialFrame() {
        val reassembler = Reassembler()
        assertIs<Reassembled.Pending>(reassembler.push(byteArrayOf(0, 1, 2)))
        assertEquals(Reassembled.Failed(FrameError.BadHeader), reassembler.push(byteArrayOf(7, 3)))
        assertEquals(Reassembled.Failed(FrameError.Empty), reassembler.push(ByteArray(0)))
        val next = reassembler.push(byteArrayOf(1, 9))
        assertContentEquals(byteArrayOf(9), (next as Reassembled.Frame).bytes)
    }

    @Test
    fun anOversizeFrameIsDroppedUpToItsLastFragment() {
        val reassembler = Reassembler()
        val chunk = byteArrayOf(0) + ByteArray(600)
        assertIs<Reassembled.Pending>(reassembler.push(chunk))
        assertEquals(Reassembled.Failed(FrameError.Oversize), reassembler.push(chunk))
        assertIs<Reassembled.Pending>(reassembler.push(byteArrayOf(1, 1)))
        val next = reassembler.push(byteArrayOf(1, 5))
        assertContentEquals(byteArrayOf(5), (next as Reassembled.Frame).bytes)
    }
}
