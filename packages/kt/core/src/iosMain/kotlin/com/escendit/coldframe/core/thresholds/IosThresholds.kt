package com.escendit.coldframe.core.thresholds

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** Thresholds for Swift, which observes flat [ThresholdsSnapshot]s and calls the actions. */
public class IosThresholds internal constructor(
    private val engine: ThresholdsEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (ThresholdsSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** Opens the Thresholds of a Lot, shown under [name] until the Server answers; [sensorId] is the cell it came from (or empty). */
    public fun open(
        lotId: String,
        name: String,
        sensorId: String,
    ) {
        engine.open(lotId, name, sensorId.ifEmpty { null })
    }

    /** Cancel: nothing is sent. */
    public fun close() {
        engine.close()
    }

    /** Try again after a failed open. */
    public fun retry() {
        engine.retry()
    }

    /** The low typed as text. */
    public fun setLowText(
        sensorId: String,
        text: String,
    ) {
        engine.setLowText(sensorId, text)
    }

    /** The high typed as text; empty clears it. */
    public fun setHighText(
        sensorId: String,
        text: String,
    ) {
        engine.setHighText(sensorId, text)
    }

    /** Drags the low line to a value (a percentage snaps to its step). */
    public fun setLow(
        sensorId: String,
        value: Double,
    ) {
        engine.setLow(sensorId, value)
    }

    /** Drags the high line to a value. */
    public fun setHigh(
        sensorId: String,
        value: Double,
    ) {
        engine.setHigh(sensorId, value)
    }

    /** "Add high". */
    public fun addHigh(sensorId: String) {
        engine.addHigh(sensorId)
    }

    /** Clears the high: the dashed "no high" marker. */
    public fun clearHigh(sensorId: String) {
        engine.clearHigh(sensorId)
    }

    /** Turns alerts on (the Server's proposed low when the Sensor has no default). */
    public fun turnOnAlerts(sensorId: String) {
        engine.turnOnAlerts(sensorId)
    }

    /** Turns alerts off. */
    public fun turnOffAlerts(sensorId: String) {
        engine.turnOffAlerts(sensorId)
    }

    /** Save. */
    public fun save() {
        engine.save()
    }
}
