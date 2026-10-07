package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/** Lot detail for Swift, which observes flat [LotDetailSnapshot]s and calls the actions. */
public class IosLotDetail internal constructor(
    private val engine: LotDetailEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (LotDetailSnapshot) -> Unit): Watch =
        engine.state.watch(scope) { onEach(snapshotOf(it, engine.now())) }

    /** The snapshot as of now, for the minute tick. */
    public fun current(): LotDetailSnapshot = snapshotOf(engine.state.value, engine.now())

    /** Calls [onEach] when stale mode is entered or left, for a polite announcement. */
    public fun watchEvents(onEach: (LotsEventSnapshot) -> Unit): Watch =
        Watch(scope.launch { engine.events.collect { onEach(snapshotOf(it)) } })

    /** Opens a Lot, shown under [name] until the Server answers. */
    public fun open(
        lotId: String,
        name: String,
    ) {
        engine.open(lotId, name)
    }

    public fun close() {
        engine.close()
    }

    /** Pull-to-refresh, foreground, and Try again. */
    public fun refresh() {
        engine.refresh()
    }

    /** Shows the history of a quantity key of [LotDetailSnapshot.quantities] (`soilMoisture`, …). */
    public fun pick(quantity: String) {
        SensorQuantity.entries
            .firstOrNull {
                it.name.replaceFirstChar { c ->
                    c.lowercase()
                } == quantity
            }?.let(engine::pick)
    }
}
