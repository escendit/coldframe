package com.escendit.coldframe.core.notifications

import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Story 6.3: My notifications flattened for Swift. */
class NotificationSettingsSnapshotTest {
    private val home = SiteSummary("a", "Home garden", SiteRole.Member)

    private val ready =
        NotificationSettingsState.Ready(
            window = NotificationWindow("07:00", "22:00"),
            draft = NotificationWindow("06:30", "22:00"),
            windowWorking = false,
            windowSaved = false,
            timeZone = NotificationTimeZone("Europe/Zurich", null, changing = false),
            site =
                SiteNotificationSettings(
                    site = home,
                    muted = true,
                    reminderCadence = ReminderCadence.Every2Days,
                    siteReminderCadence = ReminderCadence.Daily,
                ),
            notice = null,
            noticeControl = null,
        )

    @Test
    fun uxDr72ReadyFlattensTheWindowTheZoneAndTheSite() {
        val snapshot = snapshotOf(ready)

        assertEquals("ready", snapshot.surface)
        assertNull(snapshot.notice)
        assertEquals("06:30", snapshot.windowFrom)
        assertEquals("22:00", snapshot.windowTo)
        assertEquals(390, snapshot.windowFromMinutes)
        assertEquals(1320, snapshot.windowToMinutes)
        assertEquals("07:00", snapshot.savedWindowFrom)
        assertEquals("22:00", snapshot.savedWindowTo)
        assertTrue(snapshot.windowDirty)
        assertTrue(snapshot.canSaveWindow)
        assertFalse(snapshot.windowOutOfOrder)
        assertFalse(snapshot.windowWorking)
        assertFalse(snapshot.windowSaved)
        assertEquals("Europe/Zurich", snapshot.timeZoneDetected)
        assertNull(snapshot.timeZoneChosen)
        assertFalse(snapshot.timeZoneChanging)
        assertFalse(snapshot.timeZoneWorking)
        assertTrue(snapshot.hasSite)
        assertEquals("a", snapshot.siteId)
        assertEquals("Home garden", snapshot.siteName)
        assertTrue(snapshot.muted)
        assertEquals("every2Days", snapshot.reminderCadence)
        assertEquals("daily", snapshot.siteReminderCadence)
        assertFalse(snapshot.siteWorking)
    }

    @Test
    fun uxDr50UseSiteSettingCrossesAsAnEmptyCadence() {
        val snapshot = snapshotOf(ready.copy(site = ready.site!!.copy(reminderCadence = null, working = true)))

        assertEquals("", snapshot.reminderCadence)
        assertEquals("daily", snapshot.siteReminderCadence)
        assertTrue(snapshot.siteWorking)
    }

    @Test
    fun uxDr72WithoutASiteTheSiteFieldsAreEmpty() {
        val snapshot = snapshotOf(ready.copy(site = null))

        assertFalse(snapshot.hasSite)
        assertNull(snapshot.siteId)
        assertNull(snapshot.siteName)
        assertFalse(snapshot.muted)
        assertEquals("", snapshot.reminderCadence)
        assertEquals("", snapshot.siteReminderCadence)
    }

    @Test
    fun uxDr48AChosenZoneAndNoDetectedZoneCross() {
        val chosen =
            snapshotOf(ready.copy(timeZone = NotificationTimeZone(null, "Pacific/Auckland", true, working = true)))

        assertEquals("", chosen.timeZoneDetected)
        assertEquals("Pacific/Auckland", chosen.timeZoneChosen)
        assertTrue(chosen.timeZoneChanging)
        assertTrue(chosen.timeZoneWorking)
    }

    @Test
    fun uxDr47AWindowOutOfOrderCannotBeSaved() {
        val snapshot = snapshotOf(ready.copy(draft = NotificationWindow("22:00", "07:00"), windowSaved = true))

        assertTrue(snapshot.windowOutOfOrder)
        assertFalse(snapshot.canSaveWindow)
        assertTrue(snapshot.windowSaved)
    }

    @Test
    fun everyNoticeAndControlHasTheKeySwiftParses() {
        assertEquals(
            listOf("invalid", "siteRefused", "notSaved", "unreachable", "certificate", "unexpected"),
            NotificationSettingsNotice.entries.map {
                snapshotOf(ready.copy(notice = it, noticeControl = NotificationControl.Window)).notice
            },
        )
        assertEquals(
            listOf("window", "timeZone", "mute", "reminderCadence"),
            NotificationControl.entries.map {
                snapshotOf(ready.copy(notice = NotificationSettingsNotice.NotSaved, noticeControl = it)).noticeControl
            },
        )
        assertTrue(snapshotOf(ready.copy(notice = NotificationSettingsNotice.NotSaved)).noticeTryAgain)
        assertFalse(snapshotOf(ready.copy(notice = NotificationSettingsNotice.Invalid)).noticeTryAgain)
    }

    @Test
    fun idleLoadingAndFailedCarryNoSettings() {
        assertEquals("idle", snapshotOf(NotificationSettingsState.Idle).surface)
        assertEquals("loading", snapshotOf(NotificationSettingsState.Loading).surface)
        val failed = snapshotOf(NotificationSettingsState.Failed(NotificationSettingsNotice.Unreachable))
        assertEquals("failed", failed.surface)
        assertEquals("unreachable", failed.notice)
        assertTrue(failed.noticeTryAgain)
        assertNull(failed.noticeControl)
        assertEquals("", failed.windowFrom)
        assertFalse(failed.hasSite)
        assertFalse(snapshotOf(NotificationSettingsState.Failed(NotificationSettingsNotice.Certificate)).noticeTryAgain)
    }
}
