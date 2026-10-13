package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.sites.IosPause
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/** Lot detail for Swift, which observes flat [LotDetailSnapshot]s and calls the actions. */
public class IosLotDetail internal constructor(
    private val engine: LotDetailEngine,
    private val scope: CoroutineScope,
) {
    /**
     * Pause and Resume from Lot detail (Story 8.2): `openForDevice` and `resumeDevice` with
     * [LotDetailSnapshot.pauseDeviceId]. Its state is [LotDetailSnapshot.pause].
     */
    public val pause: IosPause = IosPause(engine.pause)

    /** Calls [onEach] on the main thread with the current snapshot and every change, of the Lot and of [pause]. */
    public fun watch(onEach: (LotDetailSnapshot) -> Unit): Watch =
        Watch(
            scope.launch {
                combine(engine.state, engine.pause.state) { detail, pause -> snapshotOf(detail, engine.now(), pause) }
                    .collect { onEach(it) }
            },
        )

    /** The snapshot as of now, for the minute tick. */
    public fun current(): LotDetailSnapshot = snapshotOf(engine.state.value, engine.now(), engine.pause.state.value)

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
