package com.escendit.coldframe.core.notifications

/**
 * [NotificationSettingsState] flattened for Swift: enum values cross as catalogue key suffixes (`notSaved`,
 * `timeZone`), cadences as contract values (`daily`, `every2Days`). No token or URL crosses.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`. [notice] is the [NotificationSettingsNotice] key of a failed
 * load, or of the last change that was not saved, with [noticeTryAgain]; [noticeControl] is the control a failed
 * change belongs to (`window`, `timeZone`, `mute`, `reminderCadence`), `null` for a failed load. The rest is empty
 * unless `ready`:
 * - Notification Window: [windowFrom] and [windowTo] are the draft (`"HH:mm"`), [windowFromMinutes] and
 *   [windowToMinutes] its minutes since midnight for the 24 h bar, [savedWindowFrom] and [savedWindowTo] what the
 *   Server holds (the helper names [windowFrom]), [windowDirty], [windowOutOfOrder] (the window must start before
 *   it ends), [canSaveWindow], [windowWorking] and [windowSaved].
 * - Time zone: [timeZoneDetected] (the proposal, empty when there is none and the User picks from the list),
 *   [timeZoneChosen] (`null` until the User chose; the panel shows until then), [timeZoneChanging] and
 *   [timeZoneWorking].
 * - The current Site: [hasSite] (false: only the window and the time zone show), [siteId], [siteName], [muted],
 *   [reminderCadence] (empty for "Use Site setting"), [siteReminderCadence] and [siteWorking].
 */
public data class NotificationSettingsSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val noticeControl: String?,
    val windowFrom: String,
    val windowTo: String,
    val windowFromMinutes: Int,
    val windowToMinutes: Int,
    val savedWindowFrom: String,
    val savedWindowTo: String,
    val windowDirty: Boolean,
    val windowOutOfOrder: Boolean,
    val canSaveWindow: Boolean,
    val windowWorking: Boolean,
    val windowSaved: Boolean,
    val timeZoneDetected: String,
    val timeZoneChosen: String?,
    val timeZoneChanging: Boolean,
    val timeZoneWorking: Boolean,
    val hasSite: Boolean,
    val siteId: String?,
    val siteName: String?,
    val muted: Boolean,
    val reminderCadence: String,
    val siteReminderCadence: String,
    val siteWorking: Boolean,
)

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

/** The snapshot of [state]. */
public fun snapshotOf(state: NotificationSettingsState): NotificationSettingsSnapshot {
    val ready = state as? NotificationSettingsState.Ready
    val notice = (state as? NotificationSettingsState.Failed)?.notice ?: ready?.notice
    val site = ready?.site
    return NotificationSettingsSnapshot(
        surface =
            when (state) {
                NotificationSettingsState.Idle -> "idle"
                NotificationSettingsState.Loading -> "loading"
                is NotificationSettingsState.Failed -> "failed"
                is NotificationSettingsState.Ready -> "ready"
            },
        notice = notice?.key(),
        noticeTryAgain = notice?.tryAgain == true,
        noticeControl = ready?.noticeControl?.takeIf { ready.notice != null }?.key(),
        windowFrom = ready?.draft?.from.orEmpty(),
        windowTo = ready?.draft?.to.orEmpty(),
        windowFromMinutes = ready?.draft?.fromMinutes ?: 0,
        windowToMinutes = ready?.draft?.toMinutes ?: 0,
        savedWindowFrom = ready?.window?.from.orEmpty(),
        savedWindowTo = ready?.window?.to.orEmpty(),
        windowDirty = ready?.windowDirty == true,
        windowOutOfOrder = ready?.windowOutOfOrder == true,
        canSaveWindow = ready?.canSaveWindow == true,
        windowWorking = ready?.windowWorking == true,
        windowSaved = ready?.windowSaved == true,
        timeZoneDetected = ready?.timeZone?.detected.orEmpty(),
        timeZoneChosen = ready?.timeZone?.chosen,
        timeZoneChanging = ready?.timeZone?.changing == true,
        timeZoneWorking = ready?.timeZone?.working == true,
        hasSite = site != null,
        siteId = site?.site?.id,
        siteName = site?.site?.name,
        muted = site?.muted == true,
        reminderCadence = site?.reminderCadence?.key.orEmpty(),
        siteReminderCadence = site?.siteReminderCadence?.key.orEmpty(),
        siteWorking = site?.working == true,
    )
}
