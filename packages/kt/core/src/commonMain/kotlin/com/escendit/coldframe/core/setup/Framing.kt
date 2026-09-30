package com.escendit.coldframe.core.setup

/**
 * Frames over BLE payloads (AD-25): every write and notification is `header(1) ‖ fragment`.
 * [HEADER_LAST] marks the last fragment of a frame, [HEADER_MORE] says more follow. A payload is
 * at most the negotiated write length (ATT MTU − 3), so a fragment is at most ATT MTU − 4 bytes;
 * a reassembled frame is at most [MAX_FRAME] bytes. Mirrors `packages/rs/setup/src/framing.rs`.
 */
public object Framing {
    public const val HEADER_MORE: Byte = 0x00
    public const val HEADER_LAST: Byte = 0x01

    /** The largest reassembled frame, in bytes. */
    public const val MAX_FRAME: Int = 1152

    /** The payload length of the default ATT MTU (23 − 3). */
    public const val DEFAULT_MAX_PAYLOAD: Int = 20

    /** The ATT MTU the app asks for: the Hub's 251, at most 517. */
    public const val REQUESTED_MTU: Int = 251

    /**
     * Splits [frame] into payloads of at most [maxPayload] bytes (at least 2). An empty frame is
     * one empty last fragment.
     */
    public fun fragments(
        frame: ByteArray,
        maxPayload: Int,
    ): List<ByteArray> {
        require(frame.size <= MAX_FRAME) { "frame too large" }
        val chunk = (maxPayload - 1).coerceAtLeast(1)
        val count = ((frame.size + chunk - 1) / chunk).coerceAtLeast(1)
        return (0 until count).map { index ->
            val start = index * chunk
            val end = minOf(start + chunk, frame.size)
            val header = if (index + 1 == count) HEADER_LAST else HEADER_MORE
            byteArrayOf(header) + frame.copyOfRange(start, end)
        }
    }
}

/** Why a payload could not be reassembled; the partial frame is dropped. */
public enum class FrameError {
    /** A payload without a header byte. */
    Empty,

    /** A header other than [Framing.HEADER_MORE] or [Framing.HEADER_LAST]. */
    BadHeader,

    /** The frame grew past [Framing.MAX_FRAME]; its remaining fragments are dropped. */
    Oversize,
}

/** One step of reassembly. */
public sealed interface Reassembled {
    /** A whole frame. */
    public class Frame(
        public val bytes: ByteArray,
    ) : Reassembled

    /** More fragments are needed. */
    public data object Pending : Reassembled

    public data class Failed(
        val error: FrameError,
    ) : Reassembled
}

/** Collects fragments into frames. Holds message bytes, so it has no `toString` of them. */
public class Reassembler {
    private var buffer = ByteArray(0)
    private var discarding = false

    /** Drops any partial frame. */
    public fun reset() {
        buffer.fill(0)
        buffer = ByteArray(0)
        discarding = false
    }

    public fun push(payload: ByteArray): Reassembled {
        if (payload.isEmpty()) {
            reset()
            return Reassembled.Failed(FrameError.Empty)
        }
        val last =
            when (payload[0]) {
                Framing.HEADER_LAST -> {
                    true
                }

                Framing.HEADER_MORE -> {
                    false
                }

                else -> {
                    reset()
                    return Reassembled.Failed(FrameError.BadHeader)
                }
            }
        if (discarding) {
            if (last) discarding = false
            return Reassembled.Pending
        }
        if (buffer.size + payload.size - 1 > Framing.MAX_FRAME) {
            reset()
            discarding = !last
            return Reassembled.Failed(FrameError.Oversize)
        }
        buffer += payload.copyOfRange(1, payload.size)
        if (!last) return Reassembled.Pending
        val frame = buffer
        buffer = ByteArray(0)
        return Reassembled.Frame(frame)
    }

    override fun toString(): String = "Reassembler"
}
