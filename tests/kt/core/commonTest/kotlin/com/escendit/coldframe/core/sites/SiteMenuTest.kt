package com.escendit.coldframe.core.sites

import kotlin.test.Test
import kotlin.test.assertEquals

class SiteMenuTest {
    @Test
    fun uxDr22ASurfaceThatCannotPauseHoldsOnlySiteSettings() {
        for (role in SiteRole.entries) {
            assertEquals(
                listOf(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true)),
                SiteMenu.items(role, pauseAvailable = false),
            )
        }
    }

    @Test
    fun uxDr22TheMenuOffersPauseByDefaultAndFollowsTheSitesPausedMember() {
        val pause = SiteMenuItem(SiteMenuAction.Pause, enabled = true)
        val resume = SiteMenuItem(SiteMenuAction.Resume, enabled = true)
        val settings = SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true)
        assertEquals(listOf(pause, settings), SiteMenu.items(SiteRole.Owner))
        assertEquals(listOf(pause, settings), SiteMenu.items(SiteSummary("a", "Home garden", SiteRole.Administrator)))
        // `Site.paused` is the Server's: a paused Site offers Resume, also when it has no Node.
        assertEquals(
            listOf(resume, settings),
            SiteMenu.items(SiteSummary("a", "Home garden", SiteRole.Owner, paused = true)),
        )
        // A Member has neither, paused or not.
        assertEquals(
            listOf(settings),
            SiteMenu.items(SiteSummary("a", "Home garden", SiteRole.Member, paused = true)),
        )
    }

    @Test
    fun uxDr22InStaleModeResumeIsDisabledToo() {
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Resume, enabled = false),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = false),
            ),
            SiteMenu.items(SiteSummary("a", "Home garden", SiteRole.Owner, paused = true), stale = true),
        )
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
