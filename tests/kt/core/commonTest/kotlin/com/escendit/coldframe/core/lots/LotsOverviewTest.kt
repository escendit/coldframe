package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.sites.SiteMenuAction
import com.escendit.coldframe.core.sites.SiteMenuItem
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesNotice
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Story 4.7: the headline, the counts subline, the tiles and the formatters both shells draw from. */
class LotsOverviewTest {
    private fun lot(
        status: LotStatus,
        name: String = status.key,
        pausedBy: List<LotPauseSource> = emptyList(),
        pausedUntil: Long? = null,
    ) = LotSummary("id-$name", name, status, pausedBy = pausedBy, pausedUntilEpochMs = pausedUntil)

    private fun tile(
        lot: LotSummary,
        stale: Boolean = false,
        canAddNode: Boolean = true,
    ) = LotTile.of(lot, stale = stale, fetchedAtEpochMs = FETCHED, nowEpochMs = NOW, canAddNode = canAddNode)

    private fun ready(
        lots: List<LotSummary>,
        role: SiteRole = SiteRole.Owner,
        staleReason: StaleReason? = null,
        sitePaused: Boolean = false,
    ) = LotsState.Ready(
        site = SiteSummary("a", "Home", role, paused = sitePaused),
        lots = lots,
        siteName = SiteNameForm("Home", null, working = false),
        create = CreateLotForm("", null, working = false, idempotencyKey = "key"),
        renaming = null,
        removing = null,
        notice = null,
        fetchedAtEpochMs = FETCHED,
        staleReason = staleReason,
    )

    // Formatters

    @Test
    fun uxDr128SoilMoistureIsRoundedToTheNearestFiveWithATilde() {
        assertEquals("~20", SoilMoisture.format(21.7))
        assertEquals("~20", SoilMoisture.format(17.5))
        assertEquals("~15", SoilMoisture.format(17.4))
        assertEquals("~25", SoilMoisture.format(22.5))
        assertEquals("~0", SoilMoisture.format(2.4))
        assertEquals("~100", SoilMoisture.format(99.0))
        assertEquals(35, SoilMoisture.rounded(35.0))
        assertEquals(100, SoilMoisture.rounded(140.0))
        assertEquals(0, SoilMoisture.rounded(-3.0))
    }

    @Test
    fun uxDr18DurationsAreMinutesUnderAnHourHoursUnderADayThenDays() {
        fun since(ms: Long) = LotDuration.between(NOW - ms, NOW)
        assertEquals(LotDuration(0, LotDurationUnit.Minutes), since(59_000))
        assertEquals(LotDuration(12, LotDurationUnit.Minutes), since(12 * MINUTE))
        assertEquals(LotDuration(59, LotDurationUnit.Minutes), since(HOUR - 1))
        assertEquals(LotDuration(1, LotDurationUnit.Hours), since(HOUR))
        assertEquals(LotDuration(6, LotDurationUnit.Hours), since(6 * HOUR + 59 * MINUTE))
        assertEquals(LotDuration(23, LotDurationUnit.Hours), since(24 * HOUR - 1))
        assertEquals(LotDuration(1, LotDurationUnit.Days), since(24 * HOUR))
        assertEquals(LotDuration(3, LotDurationUnit.Days), since(3 * 24 * HOUR + 5 * HOUR))
        // A Server clock ahead of the phone's never shows a negative duration.
        assertEquals(LotDuration(0, LotDurationUnit.Minutes), LotDuration.between(NOW + HOUR, NOW))
    }

    @Test
    fun uxDr24TheStaleAgeIsDaysHoursAndMinutes() {
        assertEquals(StaleAge(0, 2, 12), StaleAge.between(NOW - 2 * HOUR - 12 * MINUTE - 30_000, NOW))
        assertEquals(StaleAge(0, 0, 0), StaleAge.between(NOW - 59_000, NOW))
        assertEquals(StaleAge(1, 1, 1), StaleAge.between(NOW - 25 * HOUR - MINUTE, NOW))
        assertEquals(StaleAge(0, 0, 0), StaleAge.between(NOW + HOUR, NOW))
    }

    // Headline (UX-DR129)

    @Test
    fun uxDr129NoLotWithANodeIsNoReadingsYetWithoutCounts() {
        val none = listOf(lot(LotStatus.NoNode, "Tomatoes"), lot(LotStatus.NoNode, "Beans"))

        assertEquals(LotsHeadline(LotsHeadlineKind.NoReadings), LotsHeadline.of(none))
        assertEquals(LotsHeadline(LotsHeadlineKind.NoReadings), LotsHeadline.of(emptyList()))
        assertTrue(LotCounts.of(none).isEmpty())
        assertTrue(LotCounts.of(emptyList()).isEmpty())
    }

    @Test
    fun uxDr129OneLotThatNeedsWaterIsNamed() {
        val headline =
            LotsHeadline.of(listOf(lot(LotStatus.NeedsWater, "Tomatoes"), lot(LotStatus.Unknown), lot(LotStatus.Ok)))

        assertEquals(LotsHeadline(LotsHeadlineKind.NeedsWater, count = 1, lotName = "Tomatoes"), headline)
        assertFalse(headline.pausedInk)
    }

    @Test
    fun uxDr129SeveralLotsThatNeedWaterAreCounted() {
        val lots =
            listOf(lot(LotStatus.NeedsWater, "Tomatoes"), lot(LotStatus.NeedsWater, "Beans"), lot(LotStatus.Paused))

        assertEquals(LotsHeadline(LotsHeadlineKind.NeedsWater, count = 2), LotsHeadline.of(lots))
    }

    @Test
    fun uxDr129UnknownAndNeedsCalibrationLotsCannotBeRead() {
        assertEquals(
            LotsHeadline(LotsHeadlineKind.CantBeRead, count = 1),
            LotsHeadline.of(listOf(lot(LotStatus.NeedsCalibration), lot(LotStatus.Ok), lot(LotStatus.NoNode))),
        )
        assertEquals(
            LotsHeadline(LotsHeadlineKind.CantBeRead, count = 3),
            LotsHeadline.of(
                listOf(
                    lot(LotStatus.NeedsCalibration),
                    lot(LotStatus.Unknown, "a"),
                    lot(LotStatus.Unknown, "b"),
                    lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Site)),
                ),
            ),
        )
    }

    @Test
    fun uxDr129EveryLotWithANodePausedByTheSiteIsPausedUntilTheSharedEnd() {
        val site = listOf(LotPauseSource.Site)
        val both = listOf(LotPauseSource.Device, LotPauseSource.Site)
        val shared =
            LotsHeadline.of(
                listOf(
                    lot(LotStatus.Paused, "a", site, pausedUntil = NOW + HOUR),
                    lot(LotStatus.Paused, "b", both, pausedUntil = NOW + HOUR),
                    lot(LotStatus.NoNode),
                ),
            )

        assertEquals(LotsHeadline(LotsHeadlineKind.Paused, pausedUntilEpochMs = NOW + HOUR), shared)
        assertTrue(shared.pausedInk)
    }

    @Test
    fun uxDr129PausedHasNoDateWhenTheEndsDifferOrOneIsOpen() {
        val site = listOf(LotPauseSource.Site)
        assertEquals(
            LotsHeadline(LotsHeadlineKind.Paused),
            LotsHeadline.of(
                listOf(
                    lot(LotStatus.Paused, "a", site, pausedUntil = NOW + HOUR),
                    lot(LotStatus.Paused, "b", site, pausedUntil = NOW + 2 * HOUR),
                ),
            ),
        )
        assertEquals(
            LotsHeadline(LotsHeadlineKind.Paused),
            LotsHeadline.of(
                listOf(lot(LotStatus.Paused, "a", site, pausedUntil = NOW + HOUR), lot(LotStatus.Paused, "b", site)),
            ),
        )
        assertEquals(LotsHeadline(LotsHeadlineKind.Paused), LotsHeadline.of(listOf(lot(LotStatus.Paused, "a", site))))
    }

    @Test
    fun uxDr129OtherwiseNothingNeedsWater() {
        val device = listOf(LotPauseSource.Device)
        assertEquals(
            LotsHeadline(LotsHeadlineKind.NothingNeedsWater),
            LotsHeadline.of(listOf(lot(LotStatus.Ok), lot(LotStatus.NoNode))),
        )
        // A Lot paused on its own, or an OK Lot next to Site-paused ones, is not a paused Site.
        assertEquals(
            LotsHeadline(LotsHeadlineKind.NothingNeedsWater),
            LotsHeadline.of(listOf(lot(LotStatus.Paused, pausedBy = device))),
        )
        assertEquals(
            LotsHeadline(LotsHeadlineKind.NothingNeedsWater),
            LotsHeadline.of(listOf(lot(LotStatus.Ok), lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Site)))),
        )
    }

    // Counts subline (UX-DR21)

    @Test
    fun uxDr21TheCountsAreTheNonZeroOnesInTheServerOrderWithoutNeedsWater() {
        val lots =
            listOf(
                lot(LotStatus.NeedsWater, "a"),
                lot(LotStatus.NeedsWater, "b"),
                lot(LotStatus.Unknown, "c"),
                lot(LotStatus.Unknown, "d"),
                lot(LotStatus.Ok, "e"),
                lot(LotStatus.Ok, "f"),
                lot(LotStatus.Paused, "g"),
                lot(LotStatus.NoNode, "h"),
            )

        assertEquals(
            listOf(
                LotCount(LotStatus.Unknown, 2),
                LotCount(LotStatus.Ok, 2),
                LotCount(LotStatus.Paused, 1),
                LotCount(LotStatus.NoNode, 1),
            ),
            LotCounts.of(lots),
        )
        assertEquals(
            listOf(LotCount(LotStatus.NeedsCalibration, 1)),
            LotCounts.of(listOf(lot(LotStatus.NeedsWater), lot(LotStatus.NeedsCalibration))),
        )
        assertTrue(LotCounts.of(listOf(lot(LotStatus.NeedsWater))).isEmpty())
    }

    // Tile variants (UX-DR18)

    @Test
    fun uxDr18NeedsWaterShowsThePercentageTheReadingTimeAndTheLowThreshold() {
        val lot =
            LotSummary(
                "t",
                "Tomatoes",
                LotStatus.NeedsWater,
                statusSinceEpochMs = NOW - 2 * HOUR,
                lastReadingAtEpochMs = NOW - 5 * MINUTE,
                moisturePercent = 21.7,
                lowThresholdPercent = 30.4,
            )

        assertEquals(
            LotTile(
                id = "t",
                name = "Tomatoes",
                status = LotStatus.NeedsWater,
                variant = LotTileVariant.NeedsWater,
                label = LotTileLabel.NeedsWater,
                value = LotTileValue.Soil,
                foot = LotTileFoot.Reading,
                spoken = LotTileSpoken.NeedsWater,
                soilPercent = 20,
                lowPercent = 30,
                readingAtEpochMs = NOW - 5 * MINUTE,
            ),
            tile(lot),
        )
    }

    @Test
    fun uxDr18OkShowsOnlyThePartsTheServerSent() {
        val full =
            tile(
                LotSummary(
                    "h",
                    "Herbs",
                    LotStatus.Ok,
                    lastReadingAtEpochMs = NOW - MINUTE,
                    moisturePercent = 35.0,
                    lowThresholdPercent = 25.0,
                ),
            )
        assertEquals(LotTileVariant.Ok, full.variant)
        assertEquals(LotTileLabel.Ok, full.label)
        assertEquals(LotTileSpoken.Ok, full.spoken)
        assertEquals(LotTileValue.Soil, full.value)
        assertEquals(35, full.soilPercent)
        assertEquals(LotTileFoot.Reading, full.foot)
        assertEquals(25, full.lowPercent)

        // Before Calibration and Thresholds exist the Server sends neither percentage.
        val timeOnly = tile(LotSummary("h", "Herbs", LotStatus.Ok, lastReadingAtEpochMs = NOW - MINUTE))
        assertEquals(LotTileValue.None, timeOnly.value)
        assertNull(timeOnly.soilPercent)
        assertEquals(LotTileFoot.Reading, timeOnly.foot)
        assertEquals(NOW - MINUTE, timeOnly.readingAtEpochMs)
        assertNull(timeOnly.lowPercent)

        val bare = tile(LotSummary("h", "Herbs", LotStatus.Ok))
        assertEquals(LotTileValue.None, bare.value)
        assertEquals(LotTileFoot.None, bare.foot)
    }

    @Test
    fun uxDr18ASilentNodeWithoutASilentSinceIsMeasuredFromItsLastReading() {
        val lot =
            LotSummary(
                "b",
                "Beans",
                LotStatus.Unknown,
                statusSinceEpochMs = NOW - HOUR,
                lastReadingAtEpochMs = NOW - 6 * HOUR - 10 * MINUTE,
                unknownCause = LotUnknownCause.Node,
                moisturePercent = 41.0,
            )

        assertEquals(
            LotTile(
                id = "b",
                name = "Beans",
                status = LotStatus.Unknown,
                variant = LotTileVariant.Unknown,
                label = LotTileLabel.Silent,
                value = LotTileValue.Duration,
                foot = LotTileFoot.WasPercentAt,
                spoken = LotTileSpoken.NodeSilent,
                soilPercent = 40,
                readingAtEpochMs = NOW - 6 * HOUR - 10 * MINUTE,
                durationSinceEpochMs = NOW - 6 * HOUR - 10 * MINUTE,
                duration = LotDuration(6, LotDurationUnit.Hours),
            ),
            tile(lot),
        )
    }

    @Test
    fun uxDr18ASilentHubWithoutASilentSinceIsMeasuredFromStatusSince() {
        val tile =
            tile(
                LotSummary(
                    "b",
                    "Beans",
                    LotStatus.Unknown,
                    statusSinceEpochMs = NOW - 12 * MINUTE,
                    lastReadingAtEpochMs = NOW - 20 * MINUTE,
                    unknownCause = LotUnknownCause.Hub,
                ),
            )

        assertEquals(LotTileLabel.HubSilent, tile.label)
        assertEquals(LotTileSpoken.HubSilent, tile.spoken)
        assertEquals(NOW - 12 * MINUTE, tile.durationSinceEpochMs)
        assertEquals(LotDuration(12, LotDurationUnit.Minutes), tile.duration)
        // Without a percentage the foot is "last Reading ‹t›".
        assertEquals(LotTileFoot.LastReading, tile.foot)
        assertEquals(NOW - 20 * MINUTE, tile.readingAtEpochMs)
        assertNull(tile.soilPercent)
    }

    @Test
    fun uxDr18ASilentNodeIsMeasuredFromTheServersSilentSinceRoundedDown() {
        val tile =
            tile(
                LotSummary(
                    "b",
                    "Beans",
                    LotStatus.Unknown,
                    // The Alert opened 5 min ago; the Node was last heard 6 h 5 min ago.
                    statusSinceEpochMs = NOW - 5 * MINUTE,
                    lastReadingAtEpochMs = NOW - 7 * HOUR,
                    unknownCause = LotUnknownCause.Node,
                    moisturePercent = 41.0,
                    silentSinceEpochMs = NOW - 6 * HOUR - 5 * MINUTE,
                ),
            )

        assertEquals(NOW - 6 * HOUR - 5 * MINUTE, tile.durationSinceEpochMs)
        assertEquals(LotDuration(6, LotDurationUnit.Hours), tile.duration)
        assertEquals(LotTileValue.Duration, tile.value)
        // The foot still names the last Reading: "was ~40 % at ‹t›".
        assertEquals(LotTileFoot.WasPercentAt, tile.foot)
        assertEquals(NOW - 7 * HOUR, tile.readingAtEpochMs)
        assertEquals(40, tile.soilPercent)
    }

    @Test
    fun uxDr18ASilentHubIsMeasuredFromTheServersSilentSince() {
        val tile =
            tile(
                LotSummary(
                    "l",
                    "Lettuce",
                    LotStatus.Unknown,
                    statusSinceEpochMs = NOW - 7 * MINUTE,
                    lastReadingAtEpochMs = NOW - 20 * MINUTE,
                    unknownCause = LotUnknownCause.Hub,
                    silentHubId = "3f2a9c0d1e4b5a67",
                    silentSinceEpochMs = NOW - 12 * MINUTE - 59_000,
                ),
            )

        assertEquals(LotTileLabel.HubSilent, tile.label)
        assertEquals(LotTileSpoken.HubSilent, tile.spoken)
        assertEquals(LotDuration(12, LotDurationUnit.Minutes), tile.duration)
    }

    @Test
    fun uxDr18ASilenceIsToldInMinutesUnderAnHourHoursUnderADayAndDaysFromThereAlwaysRoundedDown() {
        fun silentFor(ms: Long) =
            tile(LotSummary("b", "Beans", LotStatus.Unknown, silentSinceEpochMs = NOW - ms)).duration

        assertEquals(LotDuration(0, LotDurationUnit.Minutes), silentFor(59_000))
        assertEquals(LotDuration(59, LotDurationUnit.Minutes), silentFor(HOUR - 1))
        assertEquals(LotDuration(1, LotDurationUnit.Hours), silentFor(HOUR))
        assertEquals(LotDuration(23, LotDurationUnit.Hours), silentFor(24 * HOUR - 1))
        assertEquals(LotDuration(1, LotDurationUnit.Days), silentFor(24 * HOUR))
        assertEquals(LotDuration(2, LotDurationUnit.Days), silentFor(3 * 24 * HOUR - 1))
    }

    @Test
    fun uxDr81EverySilentHubHasANoticeWithItsShortNameAndItsSilenceInTheServersOrder() {
        val state =
            ready(listOf(lot(LotStatus.Ok, "Herbs"))).copy(
                silentHubs =
                    listOf(
                        SilentHub("3f2a9c0d1e4b5a67", NOW - 12 * MINUTE - 30_000),
                        SilentHub("1b00aa11bb22cc33", NOW - 3 * HOUR),
                    ),
            )

        val overview = LotsOverview.of(state, NOW)

        assertEquals(
            listOf(
                HubNotice(
                    deviceId = "3f2a9c0d1e4b5a67",
                    shortName = "3F2A",
                    silentSinceEpochMs = NOW - 12 * MINUTE - 30_000,
                    duration = LotDuration(12, LotDurationUnit.Minutes),
                ),
                HubNotice("1b00aa11bb22cc33", "1B00", NOW - 3 * HOUR, LotDuration(3, LotDurationUnit.Hours)),
            ),
            overview.hubNotices,
        )
        // No Lot is behind either Hub: the notices show all the same, and no Lot changes.
        assertEquals(listOf(LotTileVariant.Ok), overview.tiles.map { it.variant })
        // A minute later the durations have moved.
        assertEquals(
            LotDuration(13, LotDurationUnit.Minutes),
            LotsOverview
                .of(state, NOW + MINUTE)
                .hubNotices
                .first()
                .duration,
        )
    }

    @Test
    fun uxDr81WithoutASilentHubThereIsNoNoticeAndStaleDataNeverSaysSilentFor() {
        val lots = listOf(lot(LotStatus.Unknown, "Beans"))
        assertEquals(emptyList(), LotsOverview.of(ready(lots), NOW).hubNotices)

        val hubs = listOf(SilentHub("3f2a9c0d1e4b5a67", NOW - 12 * MINUTE))
        for (reason in StaleReason.entries) {
            assertEquals(
                emptyList(),
                LotsOverview.of(ready(lots, staleReason = reason).copy(silentHubs = hubs), NOW).hubNotices,
            )
        }
    }

    @Test
    fun uxDr18AnUnknownLotWithoutAReadingIsMeasuredFromStatusSinceAndSaysNoReadingsYet() {
        val tile =
            tile(
                LotSummary(
                    "b",
                    "Beans",
                    LotStatus.Unknown,
                    statusSinceEpochMs = NOW - 2 * 24 * HOUR,
                    unknownCause = LotUnknownCause.Node,
                    moisturePercent = 40.0,
                ),
            )

        assertEquals(LotTileValue.Duration, tile.value)
        assertEquals(NOW - 2 * 24 * HOUR, tile.durationSinceEpochMs)
        assertEquals(LotDuration(2, LotDurationUnit.Days), tile.duration)
        assertEquals(LotTileFoot.NoReadingsYet, tile.foot)
        assertNull(tile.soilPercent)

        // A Server that sent no statusSince leaves nothing to measure from.
        val bare = tile(LotSummary("b", "Beans", LotStatus.Unknown))
        assertEquals(LotTileValue.None, bare.value)
        assertNull(bare.duration)
        assertEquals(LotTileLabel.Silent, bare.label)
    }

    @Test
    fun uxDr77AnUnknownStatusStringRendersAsUnknown() {
        val tile = tile(LotSummary("x", "New", LotStatus.fromServer("later"), statusSinceEpochMs = NOW - HOUR))

        assertEquals(LotStatus.Unknown, tile.status)
        assertEquals(LotTileVariant.Unknown, tile.variant)
        assertEquals(LotTileLabel.Silent, tile.label)
        assertEquals(LotDuration(1, LotDurationUnit.Hours), tile.duration)
        assertNull(LotUnknownCause.fromServer("later"))
        assertNull(LotPauseSource.fromServer("later"))
    }

    @Test
    fun uxDr18NeedsCalibrationShowsRawAndNeverAPercentage() {
        val lot =
            LotSummary(
                "p",
                "Peppers",
                LotStatus.NeedsCalibration,
                lastReadingAtEpochMs = NOW - MINUTE,
                moisturePercent = 33.0,
                lowThresholdPercent = 30.0,
            )

        assertEquals(
            LotTile(
                id = "p",
                name = "Peppers",
                status = LotStatus.NeedsCalibration,
                variant = LotTileVariant.NeedsCalibration,
                label = LotTileLabel.NeedsCalibration,
                value = LotTileValue.Raw,
                foot = LotTileFoot.NoPercentUntilCalibrated,
                spoken = LotTileSpoken.NeedsCalibration,
            ),
            tile(lot),
        )
    }

    @Test
    fun uxDr18PausedShowsItsEndOrPausedAndSaysWhenTheSitePausedIt() {
        val device = tile(lot(LotStatus.Paused, "Strawberries", listOf(LotPauseSource.Device)))
        assertEquals(LotTileVariant.Paused, device.variant)
        assertEquals(LotTileLabel.Paused, device.label)
        assertEquals(LotTileValue.Dash, device.value)
        assertEquals(LotTileFoot.Paused, device.foot)
        assertEquals(LotTileSpoken.Paused, device.spoken)
        assertFalse(device.pausedBySite)

        val site = tile(lot(LotStatus.Paused, "Strawberries", listOf(LotPauseSource.Device, LotPauseSource.Site)))
        assertEquals(LotTileLabel.PausedBySite, site.label)
        assertEquals(LotTileFoot.Paused, site.foot)
        assertEquals(LotTileSpoken.PausedWithSite, site.spoken)
        assertTrue(site.pausedBySite)

        val until = tile(lot(LotStatus.Paused, "Strawberries", listOf(LotPauseSource.Site), pausedUntil = NOW + HOUR))
        assertEquals(LotTileLabel.PausedBySite, until.label)
        assertEquals(LotTileFoot.PausedUntil, until.foot)
        assertEquals(LotTileSpoken.PausedUntil, until.spoken)
        assertEquals(NOW + HOUR, until.pausedUntilEpochMs)
    }

    @Test
    fun uxDr20ANoNodeTileStartsAddANodeOnlyForAdministratorsAndOwners() {
        val admin = tile(lot(LotStatus.NoNode, "Potatoes"))
        assertEquals(
            LotTile(
                id = "id-Potatoes",
                name = "Potatoes",
                status = LotStatus.NoNode,
                variant = LotTileVariant.NoNode,
                label = LotTileLabel.NoNode,
                value = LotTileValue.Plus,
                foot = LotTileFoot.AddNode,
                spoken = LotTileSpoken.NoNode,
                opensAddNode = true,
            ),
            admin,
        )
        assertFalse(tile(lot(LotStatus.NoNode, "Potatoes"), canAddNode = false).opensAddNode)
        // No other tile has a tap target in this story.
        for (status in LotStatus.entries - LotStatus.NoNode) {
            assertFalse(tile(lot(status)).opensAddNode, status.key)
        }
    }

    // Stale (UX-DR19)

    @Test
    fun uxDr19EveryStatusHasAStaleTileWithItsLabelAsOfTheLastRefreshAndNoValue() {
        val expected =
            mapOf(
                LotStatus.NeedsWater to LotTileLabel.NeedsWater,
                LotStatus.NeedsCalibration to LotTileLabel.NeedsCalibration,
                LotStatus.Unknown to LotTileLabel.Silent,
                LotStatus.Ok to LotTileLabel.Ok,
                LotStatus.Paused to LotTileLabel.Paused,
                LotStatus.NoNode to LotTileLabel.NoNode,
            )
        for ((status, label) in expected) {
            val lot =
                LotSummary(
                    "x",
                    "Lot",
                    status,
                    statusSinceEpochMs = NOW - HOUR,
                    lastReadingAtEpochMs = NOW - HOUR,
                    moisturePercent = 20.0,
                    lowThresholdPercent = 30.0,
                    pausedUntilEpochMs = NOW + HOUR,
                )

            assertEquals(
                LotTile(
                    id = "x",
                    name = "Lot",
                    status = status,
                    variant = LotTileVariant.Stale,
                    label = label,
                    value = LotTileValue.None,
                    foot = LotTileFoot.AsOf,
                    spoken = LotTileSpoken.Stale,
                    asOfEpochMs = FETCHED,
                ),
                tile(lot, stale = true),
                status.key,
            )
        }
    }

    @Test
    fun uxDr19AStaleTileKeepsTheHubAndSiteLabels() {
        val hub = tile(LotSummary("b", "Beans", LotStatus.Unknown, unknownCause = LotUnknownCause.Hub), stale = true)
        assertEquals(LotTileLabel.HubSilent, hub.label)

        val site = tile(lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Site)), stale = true)
        assertEquals(LotTileLabel.PausedBySite, site.label)
        assertTrue(site.pausedBySite)
    }

    // The whole overview

    @Test
    fun uxDr20TheOverviewKeepsTheServerOrderAndIsLive() {
        val lots =
            listOf(lot(LotStatus.Ok, "Herbs"), lot(LotStatus.NeedsWater, "Tomatoes"), lot(LotStatus.NoNode, "Beans"))

        val overview = LotsOverview.of(ready(lots), NOW)

        assertFalse(overview.stale)
        assertNull(overview.staleAge)
        assertEquals(FETCHED, overview.fetchedAtEpochMs)
        assertEquals(listOf("Herbs", "Tomatoes", "Beans"), overview.tiles.map { it.name })
        assertEquals(
            listOf(LotTileVariant.Ok, LotTileVariant.NeedsWater, LotTileVariant.NoNode),
            overview.tiles.map { it.variant },
        )
        assertEquals(LotsHeadline(LotsHeadlineKind.NeedsWater, 1, "Tomatoes"), overview.headline)
        assertEquals(listOf(LotCount(LotStatus.Ok, 1), LotCount(LotStatus.NoNode, 1)), overview.counts)
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Pause, enabled = true),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true),
            ),
            overview.menu,
        )
        assertFalse(overview.sitePaused)
        assertFalse(overview.memberPausedNotice)
        assertTrue(overview.tiles.last().opensAddNode)
        assertFalse(
            LotsOverview
                .of(ready(lots, SiteRole.Member), NOW)
                .tiles
                .last()
                .opensAddNode,
        )
    }

    @Test
    fun uxDr79InStaleModeEveryTileIsStaleTheAgeTicksAndNothingStartsAFlow() {
        val lots = listOf(lot(LotStatus.NeedsWater, "Tomatoes"), lot(LotStatus.NoNode, "Beans"))

        for (reason in StaleReason.entries) {
            val overview = LotsOverview.of(ready(lots, staleReason = reason), NOW)

            assertTrue(overview.stale)
            assertEquals(StaleAge(0, 2, 12), overview.staleAge)
            assertEquals(FETCHED, overview.fetchedAtEpochMs)
            assertTrue(overview.tiles.all { it.variant == LotTileVariant.Stale && it.asOfEpochMs == FETCHED })
            assertTrue(overview.tiles.none { it.opensAddNode })
        }
        // A minute later only the age has moved.
        assertEquals(
            StaleAge(0, 2, 13),
            LotsOverview.of(ready(lots, staleReason = StaleReason.Unreachable), NOW + MINUTE).staleAge,
        )
    }

    @Test
    fun uxDr22InStaleModeTheSiteMenuItemsAreDisabledWithNeedsYourServer() {
        val stale = ready(emptyList(), staleReason = StaleReason.Unreachable)

        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Pause, enabled = false),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = false),
            ),
            stale.siteMenu(),
        )
        assertEquals(stale.siteMenu(), LotsOverview.of(stale, NOW).menu)
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Pause, enabled = true),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true),
            ),
            ready(emptyList()).siteMenu(),
        )
        assertEquals(
            listOf(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = false)),
            ready(emptyList(), role = SiteRole.Member, staleReason = StaleReason.Unreachable).siteMenu(),
        )
        // Loading and a failed load are not stale mode; without a Site there is no menu.
        val home = SiteSummary("a", "Home", SiteRole.Member)
        assertTrue(LotsState.Loading(home).siteMenu().all { it.enabled })
        assertTrue(LotsState.Failed(home, SitesNotice.Unreachable).siteMenu().all { it.enabled })
        assertTrue(LotsState.Idle.siteMenu().isEmpty())
    }

    // The paused Site (Story 8.2)

    private val pausedBySite =
        listOf(
            lot(LotStatus.Paused, "Beans", pausedBy = listOf(LotPauseSource.Site), pausedUntil = NOW + 30 * 24 * HOUR),
            lot(LotStatus.Paused, "Herbs", pausedBy = listOf(LotPauseSource.Site), pausedUntil = NOW + 30 * 24 * HOUR),
            lot(LotStatus.NoNode, "Peas"),
        )

    @Test
    fun uxDr83APausedSiteHasTheNoAlertsLineThePausedHeadlineAndResumeInItsMenu() {
        val overview = LotsOverview.of(ready(pausedBySite, sitePaused = true), NOW)

        assertTrue(overview.sitePaused)
        assertFalse(overview.memberPausedNotice)
        assertEquals(
            LotsHeadline(LotsHeadlineKind.Paused, pausedUntilEpochMs = NOW + 30 * 24 * HOUR),
            overview.headline,
        )
        assertTrue(overview.headline.pausedInk)
        assertEquals(
            listOf(LotTileLabel.PausedBySite, LotTileLabel.PausedBySite, LotTileLabel.NoNode),
            overview.tiles.map { it.label },
        )
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Resume, enabled = true),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true),
            ),
            overview.menu,
        )
    }

    @Test
    fun uxDr83TheLineAndTheMenuFollowTheSitesPausedMemberAndTheHeadlineItsOwnRule() {
        // A paused Site without a Node: the line shows and the Site can be resumed; the headline has nothing to count.
        val empty = LotsOverview.of(ready(listOf(lot(LotStatus.NoNode)), sitePaused = true), NOW)
        assertTrue(empty.sitePaused)
        assertEquals(LotsHeadlineKind.NoReadings, empty.headline.kind)
        assertEquals(SiteMenuAction.Resume, empty.menu.first().action)

        // Every Lot paused by the Site, but the Site is not (the Sites are read a moment later): no line, Pause.
        val lagging = LotsOverview.of(ready(pausedBySite), NOW)
        assertFalse(lagging.sitePaused)
        assertEquals(LotsHeadlineKind.Paused, lagging.headline.kind)
        assertEquals(SiteMenuAction.Pause, lagging.menu.first().action)

        // A paused Site whose Lots still read live (delivery is on its way): the line shows, the headline counts.
        val delivering = LotsOverview.of(ready(listOf(lot(LotStatus.Ok)), sitePaused = true), NOW)
        assertTrue(delivering.sitePaused)
        assertEquals(LotsHeadlineKind.NothingNeedsWater, delivering.headline.kind)
    }

    @Test
    fun uxDr84AMemberOnAPausedSiteGetsTheOneNoticeAndNoPauseOrResume() {
        val member = LotsOverview.of(ready(pausedBySite, role = SiteRole.Member, sitePaused = true), NOW)

        assertTrue(member.sitePaused)
        assertTrue(member.memberPausedNotice)
        assertEquals(listOf(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = true)), member.menu)
        // Not paused: no notice.
        assertFalse(LotsOverview.of(ready(pausedBySite, role = SiteRole.Member), NOW).memberPausedNotice)
    }

    @Test
    fun uxDr79InStaleModeTheOverviewSaysNothingAboutThePauseAndResumeNeedsTheServer() {
        val stale =
            LotsOverview.of(
                ready(pausedBySite, role = SiteRole.Member, staleReason = StaleReason.Unreachable, sitePaused = true),
                NOW,
            )
        assertFalse(stale.sitePaused)
        assertFalse(stale.memberPausedNotice)

        val owner = LotsOverview.of(ready(pausedBySite, staleReason = StaleReason.Unreachable, sitePaused = true), NOW)
        assertEquals(
            listOf(
                SiteMenuItem(SiteMenuAction.Resume, enabled = false),
                SiteMenuItem(SiteMenuAction.SiteSettings, enabled = false),
            ),
            owner.menu,
        )
    }

    private companion object {
        const val MINUTE = 60_000L
        const val HOUR = 60 * MINUTE
        const val NOW = 1_791_270_120_000L
        const val FETCHED = NOW - 2 * HOUR - 12 * MINUTE
    }

    @Test
    fun story52OnlyALiveNeedsCalibrationTileOffersCalibrateAndOnlyToAdministratorsAndOwners() {
        val needs = lot(LotStatus.NeedsCalibration, "Tomatoes")

        assertTrue(LotTile.of(needs, false, FETCHED, NOW, canAddNode = true, canCalibrate = true).opensCalibrate)
        assertFalse(LotTile.of(needs, false, FETCHED, NOW, canAddNode = true, canCalibrate = false).opensCalibrate)
        assertFalse(LotTile.of(needs, true, FETCHED, NOW, canAddNode = true, canCalibrate = true).opensCalibrate)
        for (status in LotStatus.entries - LotStatus.NeedsCalibration) {
            assertFalse(
                LotTile.of(lot(status), false, FETCHED, NOW, canAddNode = true, canCalibrate = true).opensCalibrate,
                status.key,
            )
        }
        val lots = listOf(needs)
        assertTrue(
            LotsOverview
                .of(ready(lots, SiteRole.Administrator), NOW)
                .tiles
                .single()
                .opensCalibrate,
        )
        assertTrue(
            LotsOverview
                .of(ready(lots, SiteRole.Owner), NOW)
                .tiles
                .single()
                .opensCalibrate,
        )
        assertFalse(
            LotsOverview
                .of(ready(lots, SiteRole.Member), NOW)
                .tiles
                .single()
                .opensCalibrate,
        )
    }
}
