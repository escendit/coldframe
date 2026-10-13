package com.escendit.coldframe.core.sites

/**
 * Pause and Resume of one surface for Swift (Story 8.2). `IosLots.pause`, `IosLotDetail.pause` and
 * `IosDevices.pause` are three of these: each belongs to its surface, whose snapshot carries its
 * state as `pause` ([PauseSnapshot]). Only an Administrator or Owner does anything; a Member's call
 * is ignored.
 */
public class IosPause internal constructor(
    private val engine: PauseEngine,
) {
    /** The state as of now; the surface's `watch` delivers every change. */
    public fun current(): PauseSnapshot = snapshotOf(engine.state.value)

    /** Pause from the Site menu: opens the sheet with Whole Site as its only scope. */
    public fun openForSite() {
        engine.openForSite()
    }

    /** Pause from Lot detail or a Devices row: opens the sheet with This Device selected, and Whole Site. */
    public fun openForDevice(deviceId: String) {
        engine.openForDevice(deviceId)
    }

    /** Picks a scope of [PauseSnapshot.sheetScopes]: `device` or `site`. Anything else is ignored. */
    public fun setScope(scope: String) {
        PauseScope.fromKey(scope)?.let(engine::setScope)
    }

    /** The "Until" date as the picker gave it, in calendar numbers (month 1 to 12). No time zone is involved. */
    public fun setUntil(
        year: Int,
        month: Int,
        day: Int,
    ) {
        engine.setUntil(year, month, day)
    }

    /** Empties "Until": the Pause lasts until someone resumes. */
    public fun clearUntil() {
        engine.setUntil(null)
    }

    /** Cancel: closes the sheet and sends nothing. Ignored while the request runs. */
    public fun cancel() {
        engine.cancel()
    }

    /**
     * The primary button, and Try again inside the sheet. Accepted, the sheet closes
     * ([PauseSnapshot.sheetOpen] turns false); refused, it stays open and says why.
     */
    public fun save() {
        engine.save()
    }

    /** Resume from the Site menu: one tap, no sheet. */
    public fun resumeSite() {
        engine.resumeSite()
    }

    /** Resume from Lot detail or a Devices row: lifts the Device's own Pause. */
    public fun resumeDevice(deviceId: String) {
        engine.resumeDevice(deviceId)
    }

    /** Try again after a Resume that did not happen ([PauseSnapshot.failureTryAgain]). */
    public fun retry() {
        engine.retry()
    }
}
