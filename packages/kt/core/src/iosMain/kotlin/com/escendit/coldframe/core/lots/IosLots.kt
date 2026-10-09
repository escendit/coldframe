package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/** The Lots engine and Site settings for Swift, which observes flat [LotsSnapshot]s and calls the actions. */
public class IosLots internal constructor(
    private val engine: LotsEngine,
    private val scope: CoroutineScope,
) {
    /**
     * Calls [onEach] on the main thread with the current snapshot and every change. Ages and
     * durations in it are measured when it is taken; [current] takes a new one for the minute tick.
     */
    public fun watch(onEach: (LotsSnapshot) -> Unit): Watch =
        engine.state.watch(scope) { onEach(snapshotOf(it, engine.now())) }

    /** The snapshot as of now: the stale age and the tiles' durations move, nothing else does. */
    public fun current(): LotsSnapshot = snapshotOf(engine.state.value, engine.now())

    /**
     * Calls [onEach] on the main thread when stale mode is entered or left, for a polite
     * announcement. Nothing is replayed, and a first load or an unchanged refresh sends nothing.
     */
    public fun watchEvents(onEach: (LotsEventSnapshot) -> Unit): Watch =
        Watch(scope.launch { engine.events.collect { onEach(snapshotOf(it)) } })

    /** Pull-to-refresh, and every time the overview comes to the front. */
    public fun refresh() {
        engine.refresh()
    }

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

    /**
     * Picks the Site's Reminder cadence in Site settings: `daily` or `every2Days` (Owner or Administrator).
     * Any other value is ignored.
     */
    public fun setReminderCadence(cadence: String) {
        ReminderCadence.fromServer(cadence)?.let(engine::setReminderCadence)
    }

    /** Try again after the `reminderCadenceNotSaved` notice. */
    public fun retryReminderCadence() {
        engine.retryReminderCadence()
    }
}
