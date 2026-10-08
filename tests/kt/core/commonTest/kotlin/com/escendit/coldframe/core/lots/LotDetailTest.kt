package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.LotHistoryDayDto
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Story 4.8: Lot detail's pure models. Everything here only formats what the Server sent. */
class LotDetailTest {
    private val now = 1_791_270_120_000L // 2026-10-06T07:02:00Z
    private val fetched = now - 5 * 60_000

    private fun reading(
        quantity: SensorQuantity,
        value: Double,
        unit: SensorUnit,
    ) = SensorReading(quantity, value, unit, now - 60_000)

    private val sensors =
        listOf(
            reading(SensorQuantity.SoilMoisture, 1840.0, SensorUnit.Raw),
            reading(SensorQuantity.AirTemperature, 14.4, SensorUnit.Celsius),
            reading(SensorQuantity.RelativeHumidity, 77.6, SensorUnit.Percent),
            reading(SensorQuantity.GasResistance, 142.37, SensorUnit.KiloOhm),
        )

    private fun lot(
        status: LotStatus,
        node: NodeStatus? = NodeStatus("7c19aaaaaaaaaaaa", 62, ChargeState.Charging, now - 60_000),
        sensors: List<SensorReading> = this.sensors,
        unknownCause: LotUnknownCause? = null,
        pausedBy: List<LotPauseSource> = emptyList(),
        pausedUntil: Long? = null,
        moisture: Double? = null,
    ) = LotSummary(
        id = "t",
        name = "Tomatoes",
        status = status,
        statusSinceEpochMs = now - 3_600_000,
        lastReadingAtEpochMs = now - 60_000,
        unknownCause = unknownCause,
        pausedBy = pausedBy,
        pausedUntilEpochMs = pausedUntil,
        moisturePercent = moisture,
        node = node,
        sensors = sensors,
    )

    private fun ready(
        lot: LotSummary,
        role: SiteRole = SiteRole.Owner,
        stale: Boolean = false,
        history: Map<SensorQuantity, LotHistory> = emptyMap(),
    ) = LotDetailState.Ready(
        site = SiteSummary("a", "Home", role),
        lot = lot,
        picked = SensorQuantity.SoilMoisture,
        history = history,
        historyUnavailable = false,
        fetchedAtEpochMs = fetched,
        staleReason = if (stale) StaleReason.Unreachable else null,
        refreshing = false,
    )

    private fun detail(
        lot: LotSummary,
        role: SiteRole = SiteRole.Owner,
        stale: Boolean = false,
    ) = LotDetail.of(ready(lot, role, stale), now)

    @Test
    fun uxDr28SensorValuesAreFormattedWithoutConvertingThem() {
        val cells = assertNotNull(detail(lot(LotStatus.NeedsCalibration)).sensors)

        assertEquals(listOf("1840", "14", "78", "142"), cells.map { it.number })
        assertEquals(
            listOf(SensorUnit.Raw, SensorUnit.Celsius, SensorUnit.Percent, SensorUnit.KiloOhm),
            cells.map { it.unit },
        )
        assertEquals(now - 60_000, cells.first().measuredAtEpochMs)
    }

    @Test
    fun uxDr28GasResistanceIsShownToThreeSignificantDigits() {
        assertEquals("142", SensorFormat.number(142.37, SensorUnit.KiloOhm))
        assertEquals("14.2", SensorFormat.number(14.237, SensorUnit.KiloOhm))
        assertEquals("1.42", SensorFormat.number(1.4237, SensorUnit.KiloOhm))
        assertEquals("1420", SensorFormat.number(1423.0, SensorUnit.KiloOhm))
        assertEquals("14.0", SensorFormat.number(14.0, SensorUnit.KiloOhm))
        assertEquals("0.500", SensorFormat.number(0.5, SensorUnit.KiloOhm))
        assertEquals("-3", SensorFormat.number(-2.6, SensorUnit.Celsius))
    }

    @Test
    fun uxDr28ALotWithANodeAndNoReadingHasNoCells() {
        val detail = detail(lot(LotStatus.NeedsCalibration, sensors = emptyList()))

        assertEquals(emptyList(), detail.sensors)
        assertNotNull(detail.device)
        assertTrue(detail.quantities.isEmpty())
    }

    @Test
    fun uxDr29TheDeviceCellsCarryBatteryChargingAndLastSeenAndFlagALowBattery() {
        val device = assertNotNull(detail(lot(LotStatus.Ok)).device)
        assertEquals(DeviceCells("7c19aaaaaaaaaaaa", 62, false, ChargeState.Charging, now - 60_000), device)

        val low = assertNotNull(detail(lot(LotStatus.Ok, node = NodeStatus("n", 19, ChargeState.NotCharging))).device)
        assertTrue(low.batteryLow)
        assertFalse(SensorFormat.batteryLow(20))
        assertFalse(SensorFormat.batteryLow(null))

        val unknown = assertNotNull(detail(lot(LotStatus.Ok, node = NodeStatus("n"))).device)
        assertNull(unknown.batteryPercent)
        assertNull(unknown.charging)
        assertNull(unknown.lastSeenAtEpochMs)
    }

    @Test
    fun uxDr63AStaleDetailShowsNoLiveValue() {
        val stale = detail(lot(LotStatus.NeedsCalibration, moisture = 40.0), stale = true)

        assertTrue(stale.stale)
        assertNull(stale.sensors)
        assertNull(stale.device)
        assertEquals(HeroValueKind.None, stale.hero.valueKind)
        assertEquals(LotTileVariant.Stale, stale.hero.variant)
        assertEquals(HeroNote.None, stale.hero.note)
        assertEquals(StaleAge(0, 0, 5), stale.staleAge)
    }

    @Test
    fun uxDr63ANoNodeLotHasAnEmptyDetailAndAdminsMayAddANode() {
        val empty = lot(LotStatus.NoNode, node = null, sensors = emptyList())

        assertTrue(detail(empty).noNode)
        assertNull(detail(empty).sensors)
        assertNull(detail(empty).device)
        assertTrue(detail(empty, SiteRole.Administrator).canAddNode)
        assertFalse(detail(empty, SiteRole.Member).canAddNode)
    }

    @Test
    fun uxDr27TheHeroShowsRawWhileUncalibratedAndThePercentOnlyWhenTheServerSendsIt() {
        val calibrating = detail(lot(LotStatus.NeedsCalibration, moisture = 40.0)).hero
        assertEquals(HeroValueKind.Raw, calibrating.valueKind)
        assertEquals("1840", calibrating.rawNumber)
        assertNull(calibrating.soilPercent)

        val ok = detail(lot(LotStatus.Ok, moisture = 36.0)).hero
        assertEquals(HeroValueKind.Percent, ok.valueKind)
        assertEquals(35, ok.soilPercent)

        assertEquals(HeroValueKind.None, detail(lot(LotStatus.Ok)).hero.valueKind)
        assertEquals(HeroValueKind.None, detail(lot(LotStatus.NeedsCalibration, sensors = emptyList())).hero.valueKind)
    }

    @Test
    fun uxDr27OnlyNeedsWaterFillsTheHeroWithOrange() {
        assertTrue(detail(lot(LotStatus.NeedsWater, moisture = 20.0)).hero.needsWaterFill)
        for (status in LotStatus.entries - LotStatus.NeedsWater) {
            assertFalse(detail(lot(status)).hero.needsWaterFill, status.name)
        }
        assertFalse(detail(lot(LotStatus.NeedsWater), stale = true).hero.needsWaterFill)
    }

    @Test
    fun uxDr27ThePausedHeroSaysPausedWithTheSiteAndOffersTheResumeHintToAdminsOnly() {
        val paused = lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Site))

        val owner = detail(paused, SiteRole.Owner).hero
        assertEquals(HeroNote.PausedWithSite, owner.note)
        assertTrue(owner.resumeSiteHint)
        assertFalse(detail(paused, SiteRole.Member).hero.resumeSiteHint)
        assertFalse(detail(lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Device))).hero.resumeSiteHint)
    }

    @Test
    fun uxDr78TheHeroNotesFollowTheStatus() {
        assertEquals(HeroNote.NoPercentUntilCalibrated, detail(lot(LotStatus.NeedsCalibration)).hero.note)
        assertEquals(
            HeroNote.CheckPowerOrRange,
            detail(lot(LotStatus.Unknown, unknownCause = LotUnknownCause.Node)).hero.note,
        )
        assertEquals(HeroNote.CheckPowerOrRange, detail(lot(LotStatus.Unknown)).hero.note)
        assertEquals(HeroNote.HubSilent, detail(lot(LotStatus.Unknown, unknownCause = LotUnknownCause.Hub)).hero.note)
        assertEquals(
            HeroNote.PausedUntil,
            detail(lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Device), pausedUntil = now)).hero.note,
        )
        assertEquals(
            HeroNote.Paused,
            detail(lot(LotStatus.Paused, pausedBy = listOf(LotPauseSource.Device))).hero.note,
        )
        assertEquals(HeroNote.None, detail(lot(LotStatus.Ok)).hero.note)
    }

    @Test
    fun uxDr78TheUnknownHeroCarriesTheSilenceAndTheLastReading() {
        val hero = detail(lot(LotStatus.Unknown, unknownCause = LotUnknownCause.Node)).hero

        assertEquals(LotDuration(1, LotDurationUnit.Minutes), hero.duration)
        assertEquals(now - 60_000, hero.readingAtEpochMs)
        assertEquals(LotTileLabel.Silent, hero.label)
    }

    // History chart (UX-DR32, UX-DR33)

    private fun day(
        text: String,
        low: Double,
        high: Double = low,
        count: Int = 4,
    ) = LotHistoryDayDto(text, low, high, count)

    @Test
    fun uxDr32TheChartHasThirtyBarsEndingTodayWithGapsForMissingDays() {
        val chart =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Raw,
                listOf(day("2026-10-04", 1800.0), day("2026-10-06", 1790.0)),
                now,
            )

        assertEquals(30, chart.bars.size)
        assertEquals("2026-09-07", chart.bars.first().day)
        assertEquals("2026-10-06", chart.bars.last().day)
        assertEquals(2, chart.daysWithReadings)
        assertEquals(listOf(false, true, false, true), chart.bars.takeLast(4).map { it.present })
        val gap = chart.bars[28]
        assertFalse(gap.present)
        assertEquals(0.0, gap.fraction)
        assertNull(gap.lowNumber)
        assertTrue(chart.bars.last().fraction > 0.9)
    }

    @Test
    fun uxDr32AFullChartScalesDailyLowsToTheLargestOne() {
        val days = (0 until 30).map { day(HistoryChart.dayText(20_732L - 29 + it), 1000.0 + it * 10) }

        val chart = HistoryChart.of(SensorQuantity.SoilMoisture, SensorUnit.Raw, days, now)

        assertEquals(30, chart.daysWithReadings)
        assertEquals(1.0, chart.bars.last().fraction)
        assertTrue(chart.bars.first().fraction in 0.5..1.0)
        assertEquals("1000", chart.lowestNumber)
        assertEquals("1290", chart.highestNumber)
    }

    @Test
    fun uxDr33TheSummaryNamesTheLowestAndHighestDayAndAnEmptyChartIsAllGaps() {
        val chart =
            HistoryChart.of(
                SensorQuantity.AirTemperature,
                SensorUnit.Celsius,
                listOf(day("2026-10-01", 6.4, 15.2), day("2026-10-02", 9.0, 21.6)),
                now,
            )

        assertEquals("6", chart.lowestNumber)
        assertEquals("2026-10-01", chart.lowestDay)
        assertEquals("22", chart.highestNumber)
        assertEquals("2026-10-02", chart.highestDay)
        assertEquals("15", chart.bars.first { it.day == "2026-10-01" }.highNumber)

        val empty = HistoryChart.of(SensorQuantity.AirTemperature, SensorUnit.Celsius, emptyList(), now)
        assertEquals(0, empty.daysWithReadings)
        assertTrue(empty.bars.none { it.present })
        assertNull(empty.lowestNumber)
    }

    @Test
    fun uxDr33OnlyTheNonSoilQuantitiesShowARange() {
        assertFalse(SensorQuantity.SoilMoisture.showsRange)
        assertTrue(SensorQuantity.AirTemperature.showsRange)
        assertTrue(SensorQuantity.RelativeHumidity.showsRange)
        assertTrue(SensorQuantity.GasResistance.showsRange)
    }

    @Test
    fun uxDr33AColdDayBelowZeroStaysInsideTheAxis() {
        val chart =
            HistoryChart.of(
                SensorQuantity.AirTemperature,
                SensorUnit.Celsius,
                listOf(day("2026-10-05", -4.0), day("2026-10-06", 8.0)),
                now,
            )

        assertTrue(chart.bars.all { it.fraction in 0.0..1.0 })
        assertTrue(chart.bars[28].fraction < chart.bars[29].fraction)
    }

    @Test
    fun uxDr63TheDetailBuildsItsChartForThePickedQuantity() {
        val history =
            mapOf(
                SensorQuantity.SoilMoisture to
                    LotHistory(SensorQuantity.SoilMoisture, SensorUnit.Raw, listOf(day("2026-10-06", 1790.0))),
            )

        val detail = LotDetail.of(ready(lot(LotStatus.NeedsCalibration), history = history), now)

        assertEquals(SensorQuantity.SoilMoisture, detail.picked)
        assertEquals(1, detail.chart?.daysWithReadings)
        assertEquals(sensors.map { it.quantity }, detail.quantities)
    }

    @Test
    fun uxDr30NodesKeepTheServersOrderAndFormatBattery() {
        val nodes =
            with(com.escendit.coldframe.core.devices.DevicesEngine) {
                listOf(
                    com.escendit.coldframe.core.api.DeviceListItemDto(
                        "h1",
                        "hub",
                        true,
                        lastSeenAt = "2026-10-06T07:02:00.000Z",
                    ),
                    com.escendit.coldframe.core.api.DeviceListItemDto(
                        "n2",
                        "node",
                        false,
                        lotId = "l",
                        lastSeenAt = "2026-10-06T07:02:00.000Z",
                        lotName = "Tomatoes",
                        batteryPercent = 19,
                        charging = "notCharging",
                    ),
                    com.escendit.coldframe.core.api
                        .DeviceListItemDto("n1", "node", false),
                ).toNodes()
            }

        assertEquals(listOf("n2", "n1"), nodes.map { it.id })
        assertEquals("Tomatoes", nodes[0].lotName)
        assertTrue(nodes[0].batteryLow)
        assertEquals(ChargeState.NotCharging, nodes[0].charging)
        assertEquals(1_791_270_120_000L, nodes[0].lastSeenAtEpochMs)
        assertNull(nodes[1].lotName)
        assertNull(nodes[1].batteryPercent)
    }

    @Test
    fun uxDr30TheDevicesSnapshotCarriesTheNodesAsParallelLists() {
        val state =
            com.escendit.coldframe.core.devices.DevicesState.Ready(
                SiteSummary("a", "Home", SiteRole.Owner),
                emptyList(),
                listOf(
                    com.escendit.coldframe.core.devices.NodeSummary(
                        "n1",
                        "l",
                        "Tomatoes",
                        19,
                        true,
                        ChargeState.NotCharging,
                        5L,
                    ),
                    com.escendit.coldframe.core.devices
                        .NodeSummary("n2", null, null, null, false, null, null),
                ),
            )

        val snapshot =
            com.escendit.coldframe.core.devices
                .snapshotOf(state)

        assertEquals(listOf("n1", "n2"), snapshot.nodeIds)
        assertEquals(listOf("Tomatoes", ""), snapshot.nodeLotNames)
        assertEquals(listOf("19", ""), snapshot.nodeBatteries)
        assertEquals(listOf(true, false), snapshot.nodeBatteryLow)
        assertEquals(listOf("notCharging", ""), snapshot.nodeCharging)
        assertEquals(listOf("5", ""), snapshot.nodeLastSeen)
    }

    private val calibratableSoil =
        SensorReading(SensorQuantity.SoilMoisture, 1840.0, SensorUnit.Raw, now - 60_000, "s-1", calibratable = true)

    @Test
    fun story52CalibrateShowsForAdministratorsOnALiveLotWithACalibratableSensor() {
        val calibrating = lot(LotStatus.NeedsCalibration, sensors = listOf(calibratableSoil))

        assertTrue(detail(calibrating, SiteRole.Owner).canCalibrate)
        assertTrue(detail(calibrating, SiteRole.Administrator).canCalibrate)
        // Hidden, never disabled, for a Member; nothing live in stale mode.
        assertFalse(detail(calibrating, SiteRole.Member).canCalibrate)
        assertFalse(detail(calibrating, SiteRole.Owner, stale = true).canCalibrate)
    }

    @Test
    fun story52CalibrateIsHiddenWithoutACalibratableSensor() {
        assertFalse(detail(lot(LotStatus.NeedsCalibration)).canCalibrate)
        assertFalse(
            detail(
                lot(LotStatus.NeedsCalibration, sensors = listOf(calibratableSoil.copy(calibratable = false))),
            ).canCalibrate,
        )
        assertFalse(
            detail(
                lot(LotStatus.NeedsCalibration, sensors = listOf(calibratableSoil.copy(sensorId = null))),
            ).canCalibrate,
        )
        assertFalse(detail(lot(LotStatus.NoNode, node = null, sensors = emptyList())).canCalibrate)
    }

    @Test
    fun story52ARecalibratableOkLotOffersCalibrateToo() {
        assertTrue(
            detail(
                lot(LotStatus.Ok, sensors = listOf(calibratableSoil.copy(unit = SensorUnit.Percent, value = 40.0))),
            ).canCalibrate,
        )
    }

    private val soilPercentDays =
        listOf(
            LotHistoryDayDto("2026-10-04", 45.0, 60.0, 90),
            LotHistoryDayDto("2026-10-05", 20.0, 55.0, 90),
            LotHistoryDayDto("2026-10-06", 30.0, 50.0, 30),
        )

    @Test
    fun uxDr5TheChartDrawsTheThresholdBandOnlyForSoilMoistureInPercent() {
        val chart =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Percent,
                soilPercentDays,
                now,
                lowPercent = 30,
                highPercent = 70,
            )
        val band = assertNotNull(chart.band)

        assertEquals(30, band.lowPercent)
        assertEquals(70, band.highPercent)
        assertEquals(0.3, band.lowFraction)
        assertEquals(0.7, band.highFraction)

        val raw =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Raw,
                soilPercentDays,
                now,
                lowPercent = 30,
                highPercent = 70,
            )
        val air =
            HistoryChart.of(
                SensorQuantity.AirTemperature,
                SensorUnit.Celsius,
                soilPercentDays,
                now,
                lowPercent = 30,
            )
        val none = HistoryChart.of(SensorQuantity.SoilMoisture, SensorUnit.Percent, soilPercentDays, now)
        assertNull(raw.band)
        assertNull(air.band)
        assertNull(none.band)
    }

    @Test
    fun uxDr5AnEmptyHighLeavesTheBandOpenAtTheTop() {
        val chart =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Percent,
                soilPercentDays,
                now,
                lowPercent = 30,
            )

        val band = assertNotNull(chart.band)
        assertNull(band.highPercent)
        assertNull(band.highFraction)
    }

    @Test
    fun uxDr32ADailyLowUnderTheLowThresholdIsASolidBelowLowBar() {
        val chart =
            HistoryChart.of(
                SensorQuantity.SoilMoisture,
                SensorUnit.Percent,
                soilPercentDays,
                now,
                lowPercent = 30,
                highPercent = 70,
            )

        val below = chart.bars.filter { it.belowLow }
        assertEquals(listOf("2026-10-05"), below.map { it.day })
        // A day exactly at the low is not below it, and a gap never is.
        assertFalse(chart.bars.single { it.day == "2026-10-06" }.belowLow)
        assertEquals(1, chart.bars.count { it.belowLow })
        assertEquals(listOf("2026-10-05"), chart.belowLowDays)
        // The axis is the fixed 0 to 100 of a percentage, so a bar sits where its value is.
        assertEquals(0.2, chart.bars.single { it.day == "2026-10-05" }.fraction)
    }

    @Test
    fun uxDr33TheBelowLowDaysAreNamedForTheAccessibleSummaryAndNoneWithoutALowThreshold() {
        val with =
            HistoryChart.of(SensorQuantity.SoilMoisture, SensorUnit.Percent, soilPercentDays, now, lowPercent = 40)
        assertEquals(listOf("2026-10-05", "2026-10-06"), with.belowLowDays)

        val without = HistoryChart.of(SensorQuantity.SoilMoisture, SensorUnit.Percent, soilPercentDays, now)
        assertEquals(emptyList(), without.belowLowDays)
    }

    private fun thresholdsReady(
        role: SiteRole,
        thresholds: SoilThresholds? = SoilThresholds("s-1", 30, 70),
        stale: Boolean = false,
        lowFromLot: Double? = null,
        withSensorId: Boolean = true,
    ) = LotDetail.of(
        ready(
            lot(
                LotStatus.Ok,
                sensors =
                    listOf(
                        SensorReading(
                            SensorQuantity.SoilMoisture,
                            40.0,
                            SensorUnit.Percent,
                            now - 60_000,
                            sensorId = "s-1".takeIf { withSensorId },
                            calibratable = true,
                        ),
                    ),
                moisture = 40.0,
            ).copy(lowThresholdPercent = lowFromLot),
            role,
            stale,
            history =
                mapOf(
                    SensorQuantity.SoilMoisture to
                        LotHistory(SensorQuantity.SoilMoisture, SensorUnit.Percent, soilPercentDays),
                ),
        ).copy(soilThresholds = thresholds),
        now,
    )

    @Test
    fun uxDr84OwnersAndAdministratorsSetThresholdsAndAMemberOnlyViewsThem() {
        assertTrue(thresholdsReady(SiteRole.Owner).canSetThresholds)
        assertTrue(thresholdsReady(SiteRole.Administrator).canSetThresholds)

        val member = thresholdsReady(SiteRole.Member)
        assertFalse(member.canSetThresholds)
        assertTrue(member.canViewThresholds)
        assertEquals(30, member.thresholdLowPercent)
        assertEquals(70, member.thresholdHighPercent)
    }

    @Test
    fun uxDr84NoThresholdsAreOfferedWhileStaleOrWithoutASensorId() {
        val stale = thresholdsReady(SiteRole.Owner, stale = true)
        assertFalse(stale.canSetThresholds)
        assertFalse(stale.canViewThresholds)
        assertNull(stale.thresholdLowPercent)

        val noSensor = thresholdsReady(SiteRole.Owner, withSensorId = false)
        assertFalse(noSensor.canSetThresholds)
        assertFalse(noSensor.canViewThresholds)
    }

    @Test
    fun uxDr5TheBandFallsBackToTheLotsLowThresholdWhenTheThresholdsReadFailed() {
        val detail = thresholdsReady(SiteRole.Owner, thresholds = null, lowFromLot = 25.0)

        assertEquals(25, assertNotNull(detail.chart?.band).lowPercent)
        assertNull(detail.chart?.band?.highPercent)
        assertEquals(25, detail.thresholdLowPercent)
    }

    @Test
    fun uxDr5TheSensorCellKnowsItsSensorForTheTapThatOpensThresholds() {
        val cells = assertNotNull(thresholdsReady(SiteRole.Owner).sensors)

        assertEquals("s-1", cells.single().sensorId)
    }

    @Test
    fun uxDr33TheSnapshotCarriesTheBandAndTheBelowLowBars() {
        val state =
            ready(
                lot(
                    LotStatus.Ok,
                    sensors =
                        listOf(
                            SensorReading(SensorQuantity.SoilMoisture, 40.0, SensorUnit.Percent, now, "s-1", true),
                        ),
                ).copy(moisturePercent = 40.0),
                history =
                    mapOf(
                        SensorQuantity.SoilMoisture to
                            LotHistory(SensorQuantity.SoilMoisture, SensorUnit.Percent, soilPercentDays),
                    ),
            ).copy(soilThresholds = SoilThresholds("s-1", 30, null))

        val snapshot = snapshotOf(state, now)

        assertTrue(snapshot.chartHasBand)
        assertEquals("30", snapshot.chartBandLowPercent)
        assertEquals("", snapshot.chartBandHighPercent)
        assertEquals(0.3, snapshot.chartBandLowFraction)
        assertEquals(30, snapshot.barBelowLow.size)
        assertEquals(1, snapshot.barBelowLow.count { it })
        assertEquals(listOf("2026-10-05"), snapshot.chartBelowLowDays)
        assertTrue(snapshot.canSetThresholds)
        assertEquals("30", snapshot.thresholdLowPercent)
    }

    @Test
    fun uxDr18ACalibratedInRangeLotShowsThePercentageAndOk() {
        val detail = detail(lot(LotStatus.Ok, moisture = 40.0))

        assertEquals(HeroValueKind.Percent, detail.hero.valueKind)
        assertEquals(40, detail.hero.soilPercent)
        assertEquals(LotStatus.Ok, detail.hero.status)
    }
}
