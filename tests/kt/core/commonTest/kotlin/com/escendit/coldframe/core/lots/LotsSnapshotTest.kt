package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesNotice
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class LotsSnapshotTest {
    private val home = SiteSummary("a", "Home", SiteRole.Administrator)

    private val ready =
        LotsState.Ready(
            site = home,
            lots =
                listOf(
                    LotSummary("l2", "Beans", LotStatus.NeedsWater),
                    LotSummary("l1", "Tomatoes", LotStatus.NoNode),
                ),
            siteName = SiteNameForm("Home", NameError.TooLong, working = false),
            create = CreateLotForm("Peppers", NameError.Blank, working = true, idempotencyKey = "secret-key"),
            renaming = RenameLotForm("l2", "Bean", null, working = true),
            removing = RemoveLotConfirmation("l1", "Tomatoes", working = false),
            notice = LotsNotice(LotsNoticeKind.LotClaimed, "Tomatoes"),
        )

    @Test
    fun uxDr20ReadyFlattensTheLotsInTheServerOrderWithContractStatuses() {
        val snapshot = snapshotOf(ready)

        assertEquals("ready", snapshot.surface)
        assertEquals("a", snapshot.siteId)
        assertEquals("administrator", snapshot.role)
        assertFalse(snapshot.canRenameSite)
        assertTrue(snapshot.canEditLots)
        assertFalse(snapshot.readOnlyNotice)
        assertEquals(listOf("l2", "l1"), snapshot.lotIds)
        assertEquals(listOf("Beans", "Tomatoes"), snapshot.lotNames)
        assertEquals(listOf("needsWater", "noNode"), snapshot.lotStatuses)
        assertEquals("tooLong", snapshot.siteNameError)
        assertEquals("Peppers", snapshot.newLotName)
        assertEquals("blank", snapshot.newLotNameError)
        assertTrue(snapshot.createWorking)
        assertEquals("l2", snapshot.renamingLotId)
        assertEquals("Bean", snapshot.renameDraft)
        assertTrue(snapshot.renameWorking)
        assertEquals("l1", snapshot.removingLotId)
        assertEquals("Tomatoes", snapshot.removingLotName)
        assertEquals("lotClaimed", snapshot.actionNotice)
        assertEquals("Tomatoes", snapshot.actionNoticeSubject)
    }

    @Test
    fun theKeyNeverCrossesTheBoundary() {
        assertFalse(snapshotOf(ready).toString().contains("secret-key"))
    }

    @Test
    fun everyNoticeKindHasTheKeySwiftParses() {
        assertEquals(
            listOf(
                "forbidden",
                "lotClaimed",
                "lotNotFound",
                "renameSiteUnavailable",
                "keyReused",
                "unreachable",
                "certificate",
                "unexpected",
            ),
            LotsNoticeKind.entries.map { snapshotOf(ready.copy(notice = LotsNotice(it))).actionNotice },
        )
    }

    @Test
    fun uxDr84AMemberIsReadOnly() {
        val snapshot = snapshotOf(ready.copy(site = home.copy(role = SiteRole.Member)))

        assertEquals("member", snapshot.role)
        assertFalse(snapshot.canRenameSite)
        assertFalse(snapshot.canEditLots)
        assertTrue(snapshot.readOnlyNotice)
    }

    @Test
    fun failedCarriesTheLoadNoticeAndLoadingTheSite() {
        val failed = snapshotOf(LotsState.Failed(home, SitesNotice.Unreachable))
        assertEquals("failed", failed.surface)
        assertEquals("unreachable", failed.notice)
        assertTrue(failed.noticeTryAgain)
        assertTrue(failed.lotIds.isEmpty())

        val loading = snapshotOf(LotsState.Loading(home))
        assertEquals("loading", loading.surface)
        assertEquals("Home", loading.siteName)

        val idle = snapshotOf(LotsState.Idle)
        assertEquals("idle", idle.surface)
        assertNull(idle.siteId)
    }
}
