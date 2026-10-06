package com.escendit.coldframe.core.setup

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** Add a Node for Swift, which observes flat [NodeSetupSnapshot]s and calls the actions. */
public class IosNodeSetup internal constructor(
    private val engine: NodeSetupEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (NodeSetupSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** Opens on the current Site; [lotId] is the Lot of a *no Node* tile, or null. */
    public fun open(lotId: String?) {
        engine.open(lotId = lotId)
    }

    public fun close() {
        engine.close()
    }

    public fun recheckRadio() {
        engine.recheckRadio()
    }

    public fun announcing(active: Boolean) {
        engine.announcing(active)
    }

    public fun back() {
        engine.back()
    }

    public fun leave() {
        engine.leave()
    }

    public fun confirmLeave() {
        engine.confirmLeave()
    }

    public fun stayInFlow() {
        engine.stayInFlow()
    }

    public fun continueFromPress() {
        engine.continueFromPress()
    }

    public fun select(peripheralId: String) {
        engine.select(peripheralId)
    }

    public fun continueFromScan() {
        engine.continueFromScan()
    }

    public fun setCode(text: String) {
        engine.setCode(text)
    }

    public fun submitCode() {
        engine.submitCode()
    }

    public fun continueFromCode() {
        engine.continueFromCode()
    }

    public fun retryLots() {
        engine.retryLots()
    }

    public fun chooseLot(lotId: String) {
        engine.chooseLot(lotId)
    }

    public fun openNewLot() {
        engine.openNewLot()
    }

    public fun setNewLotName(name: String) {
        engine.setNewLotName(name)
    }

    public fun createLot() {
        engine.createLot()
    }

    public fun assign() {
        engine.assign()
    }

    /** [action] is the `outcomePrimary` value of the snapshot. */
    public fun outcomeAction(action: String) {
        nodeOutcomeActionOf(action)?.let { engine.outcomeAction(it) }
    }
}
