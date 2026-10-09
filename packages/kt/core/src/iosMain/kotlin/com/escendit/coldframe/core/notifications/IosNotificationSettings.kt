package com.escendit.coldframe.core.notifications

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/** My notifications for Swift, which observes flat [NotificationSettingsSnapshot]s and calls the actions. */
public class IosNotificationSettings internal constructor(
    private val engine: NotificationSettingsEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (NotificationSettingsSnapshot) -> Unit): Watch =
        engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** Every IANA zone ID, sorted, for Change on the time-zone panel. */
    public fun availableTimeZones(): List<String> = engine.timeZones()

    /** Reads the settings again: every entry of My notifications. */
    public fun load() {
        engine.load()
    }

    /** Try again: after a failed load, or for the change that was not saved. */
    public fun retry() {
        engine.retry()
    }

    /** The start of the Notification Window as `"HH:mm"` (24 h); text that is no time is ignored. */
    public fun setWindowFrom(time: String) {
        engine.setWindowFrom(time)
    }

    /** The end of the Notification Window as `"HH:mm"` (24 h). */
    public fun setWindowTo(time: String) {
        engine.setWindowTo(time)
    }

    /** The start of the Notification Window from a picker's hour and minute. */
    public fun setWindowFromTime(
        hour: Int,
        minute: Int,
    ) {
        engine.setWindowFrom(NotificationWindow.timeOf(hour, minute))
    }

    /** The end of the Notification Window from a picker's hour and minute. */
    public fun setWindowToTime(
        hour: Int,
        minute: Int,
    ) {
        engine.setWindowTo(NotificationWindow.timeOf(hour, minute))
    }

    /** Save the Notification Window. */
    public fun saveWindow() {
        engine.saveWindow()
    }

    /** Confirm on the time-zone panel: the proposed zone becomes the User's choice. */
    public fun confirmTimeZone() {
        engine.confirmTimeZone()
    }

    /** Change on the time-zone panel: shows the searchable list. */
    public fun changeTimeZone() {
        engine.changeTimeZone()
    }

    /** A zone picked from the list. */
    public fun pickTimeZone(zoneId: String) {
        engine.pickTimeZone(zoneId)
    }

    /** The "Mute ‹Site›" switch of the current Site. */
    public fun setMuted(muted: Boolean) {
        engine.setMuted(muted)
    }

    /** My Reminder cadence for the current Site: `daily`, `every2Days`, or empty for "Use Site setting". */
    public fun setReminderCadence(cadence: String) {
        if (cadence.isEmpty()) {
            engine.setReminderCadence(null)
        } else {
            ReminderCadence.fromServer(cadence)?.let { engine.setReminderCadence(it) }
        }
    }
}
