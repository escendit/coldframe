package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** The Devices engine for Swift, which observes flat [DevicesSnapshot]s and calls [load], [moveNode] and [unassignNode]. */
public class IosDevices internal constructor(
    private val engine: DevicesEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (DevicesSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

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
}
