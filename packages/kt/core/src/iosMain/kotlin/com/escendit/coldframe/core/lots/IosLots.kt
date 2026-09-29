package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** The Lots engine and Site settings for Swift, which observes flat [LotsSnapshot]s and calls the actions. */
public class IosLots internal constructor(
    private val engine: LotsEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (LotsSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    public fun load() {
        engine.load()
    }

    public fun setSiteName(name: String) {
        engine.setSiteName(name)
    }

    public fun renameSite() {
        engine.renameSite()
    }

    public fun setNewLotName(name: String) {
        engine.setNewLotName(name)
    }

    public fun createLot() {
        engine.createLot()
    }

    public fun startRename(lotId: String) {
        engine.startRename(lotId)
    }

    public fun setRename(name: String) {
        engine.setRename(name)
    }

    public fun rename() {
        engine.rename()
    }

    public fun cancelRename() {
        engine.cancelRename()
    }

    public fun askRemove(lotId: String) {
        engine.askRemove(lotId)
    }

    public fun confirmRemove() {
        engine.confirmRemove()
    }

    public fun cancelRemove() {
        engine.cancelRemove()
    }
}
