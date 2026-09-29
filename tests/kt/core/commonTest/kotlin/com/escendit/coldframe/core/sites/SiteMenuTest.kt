package com.escendit.coldframe.core.sites

import kotlin.test.Test
import kotlin.test.assertEquals

class SiteMenuTest {
    @Test
    fun uxDr22InStory18TheMenuHoldsOnlySiteSettings() {
        for (role in SiteRole.entries) {
            assertEquals(listOf(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true)), SiteMenu.items(role))
        }
    }

    @Test
    fun uxDr22PauseIsForAdministratorsAndOwnersOnceAvailable() {
        val expected =
            listOf(
                SiteMenuItem(SiteMenuAction.Pause, enabled = true),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true),
            )
        assertEquals(expected, SiteMenu.items(SiteRole.Owner, pauseAvailable = true))
        assertEquals(expected, SiteMenu.items(SiteRole.Administrator, pauseAvailable = true))
    }

    @Test
    fun uxDr22PauseIsHiddenForMembers() {
        assertEquals(
            listOf(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true)),
            SiteMenu.items(SiteRole.Member, pauseAvailable = true),
        )
    }

    @Test
    fun uxDr22APausedSiteOffersResume() {
        assertEquals(
            SiteMenuAction.Resume,
            SiteMenu.items(SiteRole.Owner, pauseAvailable = true, paused = true).first().action,
        )
    }

    @Test
    fun uxDr22InStaleModeEveryItemIsDisabled() {
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Pause, enabled = false),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = false),
            ),
            SiteMenu.items(SiteRole.Owner, pauseAvailable = true, stale = true),
        )
    }
}
