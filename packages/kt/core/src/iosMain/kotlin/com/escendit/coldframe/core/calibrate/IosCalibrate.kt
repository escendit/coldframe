package com.escendit.coldframe.core.calibrate

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** Calibrate for Swift, which observes flat [CalibrateSnapshot]s and calls the actions. */
public class IosCalibrate internal constructor(
    private val engine: CalibrateEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (CalibrateSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** Opens Calibrate for a Lot, shown under [name] until the Server answers. */
    public fun open(
        lotId: String,
        name: String,
    ) {
        engine.open(lotId, name)
    }

    /** Leaves Calibrate; the Server keeps a recorded dry point. */
    public fun close() {
        engine.close()
    }

    /** Try again after a failed open. */
    public fun retry() {
        engine.retry()
    }

    /** Picks (or clears) a stored Reading of the Recent Readings list by its `reading_seq`. */
    public fun pick(readingSeq: Long) {
        engine.pick(readingSeq)
    }

    /** Record dry / Record wet: records the picked or fresh Reading as the current step's point. */
    public fun record() {
        engine.record()
    }
}
