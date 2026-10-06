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
        val snapshot = snapshotOf(ready, NOW)

        assertEquals("ready", snapshot.surface)
        assertEquals("a", snapshot.siteId)
        assertEquals("administrator", snapshot.role)
        assertFalse(snapshot.canRenameSite)
        assertTrue(snapshot.canEditLots)
        assertTrue(snapshot.canAddNode)
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
        assertFalse(snapshotOf(ready, NOW).toString().contains("secret-key"))
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
            LotsNoticeKind.entries.map { snapshotOf(ready.copy(notice = LotsNotice(it)), NOW).actionNotice },
        )
    }

    @Test
    fun uxDr84AMemberIsReadOnly() {
        val snapshot = snapshotOf(ready.copy(site = home.copy(role = SiteRole.Member)), NOW)

        assertEquals("member", snapshot.role)
        assertFalse(snapshot.canRenameSite)
        assertFalse(snapshot.canEditLots)
        assertFalse(snapshot.canAddNode)
        assertTrue(snapshot.readOnlyNotice)
    }

    @Test
    fun failedCarriesTheLoadNoticeAndLoadingTheSite() {
        val failed = snapshotOf(LotsState.Failed(home, SitesNotice.Unreachable), NOW)
        assertEquals("failed", failed.surface)
        assertEquals("unreachable", failed.notice)
        assertTrue(failed.noticeTryAgain)
        assertTrue(failed.lotIds.isEmpty())

        val loading = snapshotOf(LotsState.Loading(home), NOW)
        assertEquals("loading", loading.surface)
        assertEquals("Home", loading.siteName)

        val idle = snapshotOf(LotsState.Idle, NOW)
        assertEquals("idle", idle.surface)
        assertNull(idle.siteId)
    }

    // Story 4.7: the overview, flattened

    private val minute = 60_000L
    private val hour = 60 * minute

    private val overview =
        ready.copy(
            lots =
                listOf(
                    LotSummary(
                        "w",
                        "Tomatoes",
                        LotStatus.NeedsWater,
                        lastReadingAtEpochMs = NOW - 5 * minute,
                        moisturePercent = 21.7,
                        lowThresholdPercent = 30.0,
                    ),
                    LotSummary(
                        "u",
                        "Beans",
                        LotStatus.Unknown,
                        statusSinceEpochMs = NOW - hour,
                        lastReadingAtEpochMs = NOW - 6 * hour,
                        unknownCause = LotUnknownCause.Node,
                    ),
                    LotSummary(
                        "p",
                        "Strawberries",
                        LotStatus.Paused,
                        pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site),
                        pausedUntilEpochMs = NOW + 24 * hour,
                    ),
                    LotSummary("n", "Potatoes", LotStatus.NoNode),
                ),
            fetchedAtEpochMs = NOW - minute,
        )

    @Test
    fun uxDr18TheTilesCrossAsParallelListsWithEmptyStringsForAbsentNumbers() {
        val snapshot = snapshotOf(overview, NOW)

        assertEquals(listOf("needsWater", "unknown", "paused", "noNode"), snapshot.lotStatuses)
        assertEquals(listOf("needsWater", "unknown", "paused", "noNode"), snapshot.lotVariants)
        assertEquals(listOf("needsWater", "silent", "pausedBySite", "noNode"), snapshot.lotLabels)
        assertEquals(listOf("soil", "duration", "dash", "plus"), snapshot.lotValues)
        assertEquals(listOf("reading", "lastReading", "pausedUntil", "addNode"), snapshot.lotFoots)
        assertEquals(listOf("needsWater", "nodeSilent", "pausedUntil", "noNode"), snapshot.lotSpokens)
        assertEquals(listOf("20", "", "", ""), snapshot.lotSoilPercents)
        assertEquals(listOf("30", "", "", ""), snapshot.lotLowPercents)
        assertEquals(listOf("${NOW - 5 * minute}", "${NOW - 6 * hour}", "", ""), snapshot.lotReadingAts)
        assertEquals(listOf("", "${NOW - 6 * hour}", "", ""), snapshot.lotDurationSinces)
        assertEquals(listOf("", "6", "", ""), snapshot.lotDurationValues)
        assertEquals(listOf("", "hours", "", ""), snapshot.lotDurationUnits)
        assertEquals(listOf(false, false, true, false), snapshot.lotPausedBySite)
        assertEquals(listOf("", "", "${NOW + 24 * hour}", ""), snapshot.lotPausedUntils)
        assertEquals(listOf(false, false, false, true), snapshot.lotOpensAddNode)
    }

    @Test
    fun uxDr21TheHeadlineAndCountsCrossAsKeys() {
        val snapshot = snapshotOf(overview, NOW)

        assertEquals("needsWater", snapshot.headline)
        assertEquals(1, snapshot.headlineCount)
        assertEquals("Tomatoes", snapshot.headlineLotName)
        assertEquals("", snapshot.headlinePausedUntil)
        assertFalse(snapshot.headlinePausedInk)
        assertEquals(listOf("unknown", "paused", "noNode"), snapshot.countStatuses)
        assertEquals(listOf(1, 1, 1), snapshot.countValues)

        val paused = snapshotOf(overview.copy(lots = overview.lots.subList(2, 4)), NOW)
        assertEquals("paused", paused.headline)
        assertEquals("${NOW + 24 * hour}", paused.headlinePausedUntil)
        assertTrue(paused.headlinePausedInk)
    }

    @Test
    fun uxDr24ALiveSnapshotIsNotStaleAndItsMenuIsEnabled() {
        val snapshot = snapshotOf(overview, NOW)

        assertFalse(snapshot.stale)
        assertNull(snapshot.staleReason)
        assertEquals(NOW - minute, snapshot.fetchedAtEpochMs)
        assertEquals(listOf(0, 0, 0), listOf(snapshot.staleAgeDays, snapshot.staleAgeHours, snapshot.staleAgeMinutes))
        assertEquals(listOf("siteSettings"), snapshot.menuItems)
        assertTrue(snapshot.menuEnabled)
    }

    @Test
    fun uxDr24AStaleSnapshotCarriesTheAgeTheStaleTilesAndADisabledMenu() {
        val stale =
            overview.copy(
                fetchedAtEpochMs = NOW - 2 * hour - 12 * minute,
                staleReason = StaleReason.Unreachable,
            )

        val snapshot = snapshotOf(stale, NOW)

        assertTrue(snapshot.stale)
        assertEquals("unreachable", snapshot.staleReason)
        assertEquals(NOW - 2 * hour - 12 * minute, snapshot.fetchedAtEpochMs)
        assertEquals(listOf(0, 2, 12), listOf(snapshot.staleAgeDays, snapshot.staleAgeHours, snapshot.staleAgeMinutes))
        assertEquals(List(4) { "stale" }, snapshot.lotVariants)
        assertEquals(listOf("needsWater", "silent", "pausedBySite", "noNode"), snapshot.lotLabels)
        assertEquals(List(4) { "none" }, snapshot.lotValues)
        assertEquals(List(4) { "asOf" }, snapshot.lotFoots)
        assertEquals(List(4) { "" }, snapshot.lotSoilPercents)
        assertEquals(List(4) { false }, snapshot.lotOpensAddNode)
        assertFalse(snapshot.menuEnabled)
        assertEquals("cached", snapshotOf(stale.copy(staleReason = StaleReason.Cached), NOW).staleReason)
    }

    @Test
    fun uxDr80LoadingAndFailedCarryNoOverview() {
        for (state in listOf(
            LotsState.Loading(home),
            LotsState.Failed(home, SitesNotice.Unreachable),
            LotsState.Idle,
        )) {
            val snapshot = snapshotOf(state, NOW)
            assertFalse(snapshot.stale)
            assertNull(snapshot.headline)
            assertTrue(snapshot.lotVariants.isEmpty())
            assertEquals(0, snapshot.fetchedAtEpochMs)
        }
        assertTrue(snapshotOf(LotsState.Loading(home), NOW).menuEnabled)
    }

    @Test
    fun uxDr106TheStaleEventsCrossWithTheirKindAndTime() {
        assertEquals(
            LotsEventSnapshot("enteredStale", "a", 42),
            snapshotOf(LotsEvent.EnteredStale("a", 42)),
        )
        assertEquals(LotsEventSnapshot("leftStale", "a", 0), snapshotOf(LotsEvent.LeftStale("a")))
    }

    private companion object {
        const val NOW = 1_791_270_120_000L
    }
}
