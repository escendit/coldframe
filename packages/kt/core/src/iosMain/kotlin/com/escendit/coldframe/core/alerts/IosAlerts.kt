package com.escendit.coldframe.core.alerts

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** The Alerts engine for Swift, which observes flat [AlertsSnapshot]s and calls [load] and [refresh]. */
public class IosAlerts internal constructor(
    private val engine: AlertsEngine,
    private val scope: CoroutineScope,
) {
    /**
     * Calls [onEach] on the main thread with the current snapshot and every change. The silence in the
     * titles is measured when it is taken; [current] takes a new one for a minute tick.
     */
    public fun watch(onEach: (AlertsSnapshot) -> Unit): Watch =
        engine.state.watch(scope) { onEach(snapshotOf(it, engine.now())) }

    /** The snapshot as of now: the silence durations in the titles move, nothing else does. */
    public fun current(): AlertsSnapshot = snapshotOf(engine.state.value, engine.now())

    /** Reads the Alerts again: on every entry of the Alerts tab, and for Try again. Shown rows stay meanwhile. */
    public fun load() {
        engine.load()
    }

    /** Pull-to-refresh, and every time the app comes to the front, so the tab count is current on any tab. */
    public fun refresh() {
        engine.refresh()
    }
}
