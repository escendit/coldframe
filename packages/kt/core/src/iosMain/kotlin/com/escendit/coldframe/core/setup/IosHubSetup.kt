package com.escendit.coldframe.core.setup

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** Add a Hub for Swift, which observes flat [HubSetupSnapshot]s and calls the actions. */
public class IosHubSetup internal constructor(
    private val engine: HubSetupEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (HubSetupSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    public fun open() {
        engine.open()
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

    public fun chooseNetwork(ssid: String) {
        engine.chooseNetwork(ssid)
    }

    public fun chooseOtherNetwork() {
        engine.chooseOtherNetwork()
    }

    public fun setOtherSsid(ssid: String) {
        engine.setOtherSsid(ssid)
    }

    public fun setPassword(password: String) {
        engine.setPassword(password)
    }

    public fun continueFromWifi() {
        engine.continueFromWifi()
    }

    public fun chooseSite(siteId: String) {
        engine.chooseSite(siteId)
    }

    public fun retryKey() {
        engine.retryKey()
    }

    public fun start() {
        engine.start()
    }

    /** [action] is an `outcomePrimary` or `outcomeSecondary` value of the snapshot. */
    public fun outcomeAction(action: String) {
        outcomeActionOf(action)?.let { engine.outcomeAction(it) }
    }
}
