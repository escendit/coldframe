package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.sites.IosPause
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/**
 * The Devices engine for Swift, which observes flat [DevicesSnapshot]s and calls [load], [moveNode],
 * [unassignNode] and [setSilenceWindow], and pauses and resumes through [pause].
 */
public class IosDevices internal constructor(
    private val engine: DevicesEngine,
    private val scope: CoroutineScope,
) {
    /**
     * Pause and Resume from a Device row (Story 8.2): `openForDevice` and `resumeDevice` with the row's
     * Device ID. Its state is [DevicesSnapshot.pause].
     */
    public val pause: IosPause = IosPause(engine.pause)

    /**
     * Calls [onEach] on the main thread with the current snapshot and every change, of the Devices and
     * of [pause]. The silence of silent Devices in it is measured when it is taken; [current] takes a
     * new one for a minute tick.
     */
    public fun watch(onEach: (DevicesSnapshot) -> Unit): Watch =
        Watch(
            scope.launch {
                combine(engine.state, engine.pause.state) { devices, pause -> snapshotOf(devices, engine.now(), pause) }
                    .collect { onEach(it) }
            },
        )

    /** The snapshot as of now: the silence durations move, nothing else does. */
    public fun current(): DevicesSnapshot = snapshotOf(engine.state.value, engine.now(), engine.pause.state.value)

    /** Reads the list again: on every entry of the Devices tab, and for Try again. */
    public fun load() {
        engine.load()
    }

    /** Moves a Node to another Lot (UX-DR31, Administrator and up); the core refuses a Lot that cannot be picked. */
    public fun moveNode(
        nodeId: String,
        lotId: String,
    ) {
        engine.moveNode(nodeId, lotId)
    }

    /** Unassigns a Node from its Lot (UX-DR31); the shell confirms first, naming the Node. */
    public fun unassignNode(nodeId: String) {
        engine.unassignNode(nodeId)
    }

    /**
     * Sets the Silence Window of a Hub or Node to [windowSeconds] (Story 7.2, Administrator and up). The core
     * refuses a value outside 1 min to 30 days with the `outOfRange` notice and sends nothing.
     */
    public fun setSilenceWindow(
        deviceId: String,
        windowSeconds: Int,
    ) {
        engine.setSilenceWindow(deviceId, windowSeconds)
    }

    /**
     * Sets the Silence Window from what the User typed: [text] is a whole number of [unit] (`minutes`, `hours`
     * or `days`). Anything else, or a value outside 1 min to 30 days, shows the `outOfRange` notice and sends
     * nothing. Returns whether the value was accepted for sending, so the sheet knows whether to close.
     */
    public fun setSilenceWindowText(
        deviceId: String,
        text: String,
        unit: String,
    ): Boolean {
        val seconds = SilenceWindowUnit.fromKey(unit)?.let { SilenceWindow.secondsOf(text, it) }
        // An invalid entry goes to the engine as an out-of-range value, which it refuses with its notice.
        engine.setSilenceWindow(deviceId, seconds ?: 0)
        return seconds != null
    }

    /** "Use default": returns the Device to the Silence Window of its kind (a Node 6 h, a Hub 5 min). */
    public fun useDefaultSilenceWindow(deviceId: String) {
        engine.setSilenceWindow(deviceId, null)
    }
}
