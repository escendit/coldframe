package com.escendit.coldframe.android

import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotUnknownCause
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.SilentHub
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.sites.SiteRole
import java.time.Instant

/**
 * Lots of every status as the Server would send them (Story 4.7), told against [now]. The shell
 * tests only render them: status, order and every field are fixed here, as they are the Server's.
 */
object LotFixtures {
    val now: Instant = Instant.parse("2026-10-06T07:04:00Z")

    /** The last successful read of the stale fixtures: 2 h 12 min before [now]. */
    val fetchedAt: Instant = Instant.parse("2026-10-06T04:52:00Z")

    private fun at(text: String): Long = Instant.parse(text).toEpochMilli()

    val needsWater =
        LotSummary(
            id = "lot-tomatoes",
            name = "Tomatoes",
            status = LotStatus.NeedsWater,
            statusSinceEpochMs = at("2026-10-06T06:10:00Z"),
            lastReadingAtEpochMs = at("2026-10-06T07:02:00Z"),
            moisturePercent = 21.4,
            lowThresholdPercent = 30.0,
        )

    val needsCalibration =
        LotSummary(
            id = "lot-peppers",
            name = "Peppers",
            status = LotStatus.NeedsCalibration,
            statusSinceEpochMs = at("2026-10-05T18:00:00Z"),
            lastReadingAtEpochMs = at("2026-10-06T07:00:00Z"),
        )

    /** A silent Node: 6 h since its last Reading, which was about 40 %. */
    val unknownNode =
        LotSummary(
            id = "lot-beans",
            name = "Beans",
            status = LotStatus.Unknown,
            statusSinceEpochMs = at("2026-10-06T02:00:00Z"),
            lastReadingAtEpochMs = at("2026-10-06T01:04:00Z"),
            unknownCause = LotUnknownCause.Node,
            moisturePercent = 41.0,
        )

    /** A silent Hub: 30 min since the status changed; a last Reading without a percentage. */
    val unknownHub =
        LotSummary(
            id = "lot-herbs",
            name = "Herbs",
            status = LotStatus.Unknown,
            statusSinceEpochMs = at("2026-10-06T06:34:00Z"),
            lastReadingAtEpochMs = at("2026-10-06T06:30:00Z"),
            unknownCause = LotUnknownCause.Hub,
        )

    /** A Node that has declared nothing yet: 2 d since it was assigned, no Reading. */
    val unknownNoReading =
        LotSummary(
            id = "lot-chillies",
            name = "Chillies",
            status = LotStatus.Unknown,
            statusSinceEpochMs = at("2026-10-04T07:04:00Z"),
            unknownCause = LotUnknownCause.Node,
        )

    val ok =
        LotSummary(
            id = "lot-lettuce",
            name = "Lettuce",
            status = LotStatus.Ok,
            statusSinceEpochMs = at("2026-10-05T07:00:00Z"),
            lastReadingAtEpochMs = at("2026-10-06T07:01:00Z"),
            moisturePercent = 36.0,
            lowThresholdPercent = 25.0,
        )

    /** OK without a percentage or a Reading: what the Server sends before Epics 5 and 6. */
    val okBare =
        LotSummary(
            id = "lot-basil",
            name = "Basil",
            status = LotStatus.Ok,
            statusSinceEpochMs = at("2026-10-05T07:00:00Z"),
        )

    val paused =
        LotSummary(
            id = "lot-squash",
            name = "Squash",
            status = LotStatus.Paused,
            statusSinceEpochMs = at("2026-10-01T07:00:00Z"),
            pausedBy = listOf(LotPauseSource.Device),
            pausedUntilEpochMs = at("2026-11-01T00:00:00Z"),
        )

    val pausedBySite =
        LotSummary(
            id = "lot-kale",
            name = "Kale",
            status = LotStatus.Paused,
            statusSinceEpochMs = at("2026-10-01T07:00:00Z"),
            pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site),
        )

    val noNode =
        LotSummary(
            id = "lot-carrots",
            name = "Carrots",
            status = LotStatus.NoNode,
            statusSinceEpochMs = at("2026-09-28T07:00:00Z"),
        )

    // Story 7.2: silence as the Server names it.

    /** Hub 3F2A, last heard 12 min before [now]. */
    val silentHub = SilentHub("3f2a9c0d1e4b5a67", at("2026-10-06T06:52:00Z"))

    /** A Node last heard 6 h ago; its silent Alert opened later, which is not what the tile counts from. */
    val silentNode =
        unknownNode.copy(
            statusSinceEpochMs = at("2026-10-06T07:00:00Z"),
            silentSinceEpochMs = at("2026-10-06T01:04:00Z"),
        )

    /** A Lot behind [silentHub]: unknown with the Hub as cause, silent for as long as the Hub. */
    val behindSilentHub =
        unknownHub.copy(
            statusSinceEpochMs = at("2026-10-06T06:57:00Z"),
            silentHubId = silentHub.deviceId,
            silentSinceEpochMs = silentHub.silentSinceEpochMs,
        )

    /** The Server's order with a silent Hub: needs water, the unknown Lots above every OK Lot, then OK. */
    val withSilentHub: List<LotSummary> = listOf(needsWater, silentNode, behindSilentHub, ok)

    /** The overview while [silentHub] is silent. */
    fun hubSilent(
        lots: List<LotSummary> = withSilentHub,
        role: SiteRole = SiteRole.Owner,
        staleReason: StaleReason? = null,
    ): LotsState.Ready = ready(lots, role, staleReason).copy(silentHubs = listOf(silentHub))

    // Story 8.2: the Site paused over winter, until 1 Mar 2027.

    val sitePausedUntil: Instant = Instant.parse("2027-03-01T00:00:00Z")

    private fun bySite(lot: LotSummary): LotSummary =
        lot.copy(
            status = LotStatus.Paused,
            statusSinceEpochMs = at("2026-10-06T07:00:00Z"),
            unknownCause = null,
            pausedBy = listOf(LotPauseSource.Site),
            pausedUntilEpochMs = sitePausedUntil.toEpochMilli(),
        )

    /** The Server's Lots of a paused Site: every Lot with a Node paused by the Site, then the one without. */
    val allPausedBySite: List<LotSummary> = listOf(bySite(needsWater), bySite(unknownNode), bySite(ok), noNode)

    /** The overview of a Site the Server lists as paused until [sitePausedUntil]. */
    fun pausedSite(
        role: SiteRole = SiteRole.Owner,
        lots: List<LotSummary> = allPausedBySite,
        staleReason: StaleReason? = null,
    ): LotsState.Ready {
        val ready = ready(lots, role, staleReason)
        return ready.copy(site = ready.site.copy(paused = true, pausedUntilEpochMs = sitePausedUntil.toEpochMilli()))
    }

    /** One Lot per tile variant, in the Server's order. */
    val everyVariant: List<LotSummary> =
        listOf(
            needsWater,
            needsCalibration,
            unknownNode,
            unknownHub,
            unknownNoReading,
            ok,
            okBare,
            paused,
            pausedBySite,
            noNode,
        )

    /** One Lot per status, in the Server's order: needsWater, needsCalibration, unknown, ok, paused, noNode. */
    val everyStatus: List<LotSummary> = listOf(needsWater, needsCalibration, unknownNode, ok, paused, noNode)

    fun ready(
        lots: List<LotSummary> = everyVariant,
        role: SiteRole = SiteRole.Owner,
        staleReason: StaleReason? = null,
        refreshing: Boolean = false,
        fetchedAt: Instant = if (staleReason == null) now else this.fetchedAt,
    ): LotsState.Ready =
        LotsState.Ready(
            site = homeSite(role),
            lots = lots,
            siteName = SiteNameForm("Home garden", null, false),
            create = CreateLotForm("", null, false, "key-1"),
            renaming = null,
            removing = null,
            notice = null,
            fetchedAtEpochMs = fetchedAt.toEpochMilli(),
            staleReason = staleReason,
            refreshing = refreshing,
        )

    /** Clock times carry a narrow no-break space in en-US; tests compare with a plain space. */
    fun String.plainSpaces(): String = replace(' ', ' ').replace(' ', ' ')
}
