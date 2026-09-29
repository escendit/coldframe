package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** The Sites engine for Swift, which observes flat [SitesSnapshot]s and calls the actions. */
public class IosSites internal constructor(
    private val engine: SitesEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (SitesSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** Every IANA zone ID, sorted, for Change on the time-zone panel. */
    public fun availableTimeZones(): List<String> =
        com.escendit.coldframe.core.sites
            .availableTimeZones()

    public fun load() {
        engine.load()
    }

    public fun select(siteId: String) {
        engine.select(siteId)
    }

    public fun newSite() {
        engine.newSite()
    }

    public fun cancelNewSite() {
        engine.cancelNewSite()
    }

    public fun setName(name: String) {
        engine.setName(name)
    }

    public fun confirmTimeZone() {
        engine.confirmTimeZone()
    }

    public fun changeTimeZone() {
        engine.changeTimeZone()
    }

    public fun pickTimeZone(zoneId: String) {
        engine.pickTimeZone(zoneId)
    }

    public fun submit() {
        engine.submit()
    }
}
