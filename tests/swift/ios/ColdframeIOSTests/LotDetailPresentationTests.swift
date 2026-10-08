import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

private func heroOf(_ fixture: LotDetailFixture) throws -> LotDetailHeroPresentation {
  try #require(fixture.build().hero)
}

// MARK: - Hero (UX-DR27, UX-DR78)

@Test("UX-DR27 the hero names the Lot, its status icon and label with since, and the raw value")
func heroNeedsCalibration() throws {
  let hero = try heroOf(.needsCalibration)

  #expect(hero.name == "Peppers")
  #expect(hero.icon == .tools)
  #expect(hero.statusLabel == .lotTileLabelNeedsCalibration)
  #expect(hero.since(context) == "since 07:02")
  // No percentage before Calibration, ever: raw N, and the line says so.
  #expect(hero.valueText(context) == "raw 1840")
  #expect(hero.readingText(context) == "last Reading 07:02")
  #expect(hero.lowText(context) == nil)
  #expect(!hero.needsWaterFill)
  #expect(try Catalogue.entries()["lot_detail_since"] == "since %1$@")
}

@Test("UX-DR27 a percentage shows as ~35 with the Reading time and the low Threshold")
func heroPercent() throws {
  let hero = try heroOf(.ok)

  #expect(hero.valueText(context) == "~35")
  #expect(hero.readingText(context) == "±5 % · 07:02")
  #expect(hero.lowText(context) == "low 25%")
  #expect(hero.icon == .checkmarkOutline)
  #expect(!hero.needsWaterFill)
}

@Test("UX-DR27 only a Lot that needs water has the orange hero, with its own icon")
func heroOrangeOnlyForNeedsWater() throws {
  let water = try heroOf(.needsWater)
  #expect(water.needsWaterFill)
  #expect(water.variant.background == ColorTokens.statusWaterFill)
  #expect(water.variant.ink == ColorTokens.statusWaterInk)
  #expect(water.icon == .rainDrop)
  #expect(water.valueText(context) == "~20")
  for other: LotDetailFixture in [.ok, .nodeSilent, .pausedUntil, .needsCalibration, .noNodeLot] {
    let hero = try heroOf(other)
    #expect(!hero.needsWaterFill)
    #expect(hero.variant.background != ColorTokens.statusWaterFill)
  }
}

@Test("UX-DR27 a stale hero keeps the name and says what the status was, with no value")
func heroStale() throws {
  let hero = try heroOf(LotDetailFixture.needsWater.asStale)

  #expect(hero.isStale)
  #expect(hero.icon == .cloudOffline)
  #expect(hero.statusLabel == .lotTileWasNeedsWater)
  #expect(hero.valueText(context) == nil)
  #expect(hero.readingText(context) == nil)
  #expect(hero.lowText(context) == nil)
  #expect(hero.noteText(context) == nil)
  #expect(!hero.needsWaterFill)
}

@Test("UX-DR27 UX-DR78 a Lot paused by the Site says so, and an Admin+ is told how to resume")
func heroPausedBySite() throws {
  let hero = try heroOf(.pausedBySite)

  #expect(hero.statusLabel == .lotTileLabelPausedBySite)
  #expect(hero.icon == .pauseOutline)
  #expect(hero.noteText(context) == "Paused with the Site")
  #expect(hero.resumeSiteText(context) == "Resume the Site to resume this Node")
  // A Member is not told to resume anything: the core did not send the hint.
  var member = LotDetailFixture.pausedBySite
  member.heroResumeSiteHint = false
  #expect(try heroOf(member).resumeSiteText(context) == nil)
}

@Test("UX-DR78 needs calibration: the hero says no % until calibrated")
func heroNoteNeedsCalibration() throws {
  #expect(try heroOf(.needsCalibration).noteText(context) == "no % until calibrated")
}

@Test("UX-DR78 unknown: the hero shows the silence and says what to check, by who is silent")
func heroNoteUnknown() throws {
  let node = try heroOf(.nodeSilent)
  #expect(node.statusLabel == .lotTileLabelUnknownNode)
  #expect(node.icon == .help)
  #expect(node.valueText(context) == "6 h")
  #expect(node.noteText(context) == "Check power or range.")
  #expect(node.readingText(context) == "last Reading 01:05")

  let hub = try heroOf(.hubSilent)
  #expect(hub.statusLabel == .lotTileLabelUnknownHub)
  #expect(hub.valueText(context) == "12 min")
  #expect(hub.noteText(context) == "The Hub is silent; Lots behind it can't be read.")
}

@Test("UX-DR78 paused: until a date, or with no end, or with the Site")
func heroNotePaused() throws {
  let until = try heroOf(.pausedUntil)
  #expect(until.noteText(context) == "Paused until 1 Nov")
  #expect(until.valueText(context) == "—")

  var open = LotDetailFixture.pausedUntil
  open.heroNote = "paused"
  open.heroPausedUntil = ""
  #expect(try heroOf(open).noteText(context) == "Paused")
  // A note this client does not know shows nothing.
  var odd = LotDetailFixture.pausedUntil
  odd.heroNote = "sprouting"
  #expect(try heroOf(odd).noteText(context) == nil)
}

// MARK: - Sensor cells (UX-DR28)

@Test("UX-DR28 the Sensor cells show the Server's converted values with their units and times")
func sensorCells() throws {
  let cells = try #require(LotDetailFixture.needsCalibration.build().sensors)

  #expect(cells.map { $0.valueText(context) } == ["raw 1840", "14 °C", "78 %", "142 kΩ"])
  #expect(
    cells.map(\.label) == [
      .lotDetailQuantitySoilMoisture, .lotDetailQuantityAirTemperature,
      .lotDetailQuantityRelativeHumidity, .lotDetailQuantityGasResistance,
    ])
  #expect(cells.allSatisfy { $0.timeText(context) == "07:02" })
  let entries = try Catalogue.entries()
  #expect(entries["lot_detail_quantity_air_temperature"] == "Temperature")
  #expect(entries["lot_detail_quantity_gas_resistance"] == "Air (gas)")
}

@Test("UX-DR28 a cell is one element for VoiceOver: quantity, value, then its time")
func sensorCellSpoken() throws {
  let cells = try #require(LotDetailFixture.needsCalibration.build().sensors)

  #expect(cells[1].spokenText(context) == "Temperature, 14 °C, 07:02")
}

@Test("UX-DR28 UX-DR63 three Sensor cells in a row, one per row from Accessibility 1")
func sensorColumns() {
  #expect(LotDetailPresentation.sensorColumns(accessibilitySize: false) == 3)
  #expect(LotDetailPresentation.sensorColumns(accessibilitySize: true) == 1)
}

@Test("UX-DR28 a Node that has not reported shows No Readings yet; a stale detail shows no cell")
func sensorCellsMissing() throws {
  var bare = LotDetailFixture.needsCalibration
  bare.sensors = []
  #expect(bare.build().sensors == [])
  #expect(LotDetailFixture.needsCalibration.asStale.build().sensors == nil)
  // A quantity or unit this client does not know is left out, never guessed.
  var odd = LotDetailFixture.needsCalibration
  odd.sensors = [("soilMoisture", "1840", "raw"), ("windSpeed", "3", "ms")]
  #expect(odd.build().sensors?.count == 1)
  #expect(try Catalogue.entries()["lot_detail_no_readings"] == "No Readings yet.")
}

// MARK: - Device cells (UX-DR29)

@Test("UX-DR29 the Device cells show battery with charging, and last seen every 15 min")
func deviceCells() throws {
  let device = try #require(LotDetailFixture.ok.build().device)

  #expect(device.nodeId == "7c19")
  #expect(device.batteryText(context) == "62 %")
  #expect(device.chargingText(context) == "charging")
  #expect(device.lastSeenText(context) == "07:02")
  #expect(device.cadence == .lotDetailEveryFifteen)
  #expect(device.batteryIcon == nil)
  #expect(try Catalogue.entries()["lot_detail_every_fifteen"] == "every 15 min")
}

@Test("UX-DR29 a battery below 20 % shows battery--low and not charging")
func deviceCellsLowBattery() throws {
  let device = try #require(LotDetailFixture.needsWater.build().device)

  #expect(device.batteryText(context) == "14 %")
  #expect(device.batteryIcon == .batteryLow)
  #expect(device.chargingText(context) == "not charging")
}

@Test("UX-DR29 what the Server did not send reads as a dash, and a stale detail has no cell")
func deviceCellsMissing() throws {
  var unknown = LotDetailFixture.ok
  unknown.battery = ""
  unknown.charging = ""
  unknown.lastSeen = ""
  let device = try #require(unknown.build().device)
  #expect(device.batteryText(context) == "—")
  #expect(device.chargingText(context) == nil)
  #expect(device.lastSeenText(context) == "—")
  #expect(LotDetailFixture.ok.asStale.build().device == nil)
}

@Test("UX-DR29 UX-DR63 two Device cells in a row, one per row from Accessibility 1")
func deviceColumns() {
  #expect(LotDetailPresentation.deviceColumns(accessibilitySize: false) == 2)
  #expect(LotDetailPresentation.deviceColumns(accessibilitySize: true) == 1)
}

// MARK: - History chart (UX-DR32, UX-DR33)

@Test("UX-DR32 the chart has 30 days, ascending, with gaps for days without Readings")
func chartBarsAndGaps() throws {
  let chart = try #require(LotDetailFixture.needsCalibration.build().chart)

  #expect(chart.bars.count == 30)
  #expect(chart.bars.map(\.day) == chart.bars.map(\.day).sorted())
  #expect(chart.bars.last?.day == LotDetailFixture.dayText(back: 0))
  // Days 0, 1 and 3 have Readings; every other day is a gap, never a zero.
  let present = chart.bars.enumerated().filter { $0.element.present }.map { 29 - $0.offset }
  #expect(present == [3, 1, 0])
  #expect(chart.bars.filter { !$0.present }.allSatisfy { $0.fraction == 0 && $0.low.isEmpty })
  #expect(chart.bars[29].fraction == 0.5)
  #expect(chart.daysWithReadings == 3)
}

@Test("UX-DR32 a Lot with no Threshold has no band and every bar is a normal outlined bar")
func chartWithoutAThresholdHasNoBandAndNoBelowLowBars() throws {
  let chart = try #require(LotDetailFixture.needsCalibration.build().chart)
  #expect(chart.band == nil)
  #expect(!chart.bars.contains { $0.belowLow })
}

@Test("UX-DR32 the axis ends are UTC dates; a history day is written as the Server sent it")
func chartDayText() throws {
  let chart = try #require(LotDetailFixture.needsCalibration.build().chart)

  #expect(chart.dayText(chart.bars[29].dayStart, context) == "6 Oct")
  #expect(chart.dayText(chart.bars[0].dayStart, context) == "7 Sep")
}

@Test("UX-DR32 a quantity or unit this client does not know draws no chart")
func chartUnknownQuantity() {
  var odd = LotDetailFixture.needsCalibration
  odd.chartQuantity = "windSpeed"
  #expect(odd.build().chart == nil)
}

@Test("UX-DR33 the Sensor picker shows with more than one Sensor and marks the picked one")
func pickerSegments() {
  var detail = LotDetailFixture.needsCalibration
  detail.picked = "airTemperature"
  let presentation = detail.build()

  #expect(presentation.showsPicker)
  #expect(presentation.pickerSegments.map(\.value) == SensorQuantityKind.allCases)
  #expect(presentation.pickerSegments.map(\.isSelected) == [false, true, false, false])
  #expect(presentation.pickerSegments[1].traits == [.button, .selected])
  #expect(presentation.pickerSegments[1].showsCheckmark)
  #expect(presentation.pickerSegments[0].traits == [.button])

  var single = LotDetailFixture.needsCalibration
  single.sensors = [("soilMoisture", "1840", "raw")]
  #expect(!single.build().showsPicker)
}

@Test(
  "UX-DR33 tapping or dragging shows that day's low; temperature, humidity and gas show min to max")
func chartReadout() throws {
  let soil = try #require(LotDetailFixture.needsCalibration.build().chart)
  #expect(soil.readout(at: 29, context) == "6 Oct: lowest raw 1790")
  #expect(soil.readout(at: 28, context) == "5 Oct: lowest raw 1840")
  // A gap reads as its date only.
  #expect(soil.readout(at: 20, context) == "27 Sep")
  #expect(soil.readout(at: 99, context) == nil)

  var temperature = LotDetailFixture.needsCalibration
  temperature.chartQuantity = "airTemperature"
  temperature.chartUnit = "celsius"
  temperature.days = [0: ("6", "22", 30, 0.4)]
  let chart = try #require(temperature.build().chart)
  #expect(chart.readout(at: 29, context) == "6 Oct: 6 °C to 22 °C")
  #expect(SensorQuantityKind.soilMoisture.showsRange == false)
  #expect(SensorQuantityKind.gasResistance.showsRange)
}

@Test("UX-DR33 a position on the chart picks the bar under it, clamped to the 30 days")
func chartIndexAtPosition() throws {
  let chart = try #require(LotDetailFixture.needsCalibration.build().chart)

  #expect(chart.index(atFraction: 0) == 0)
  #expect(chart.index(atFraction: 0.5) == 15)
  #expect(chart.index(atFraction: 0.999) == 29)
  #expect(chart.index(atFraction: 1.4) == 29)
  #expect(chart.index(atFraction: -0.2) == 0)
}

@Test("UX-DR33 the chart does not animate: state changes swap instantly")
func chartDoesNotAnimate() throws {
  #expect(!Motion.animatesStateChanges)
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/LotDetailViews.swift")
  #expect(!views.contains("withAnimation"))
  #expect(!views.contains(".animation("))
  #expect(!views.contains(".transition("))
}

// MARK: - Lot detail (UX-DR63)

@Test("UX-DR63 Lot detail reads hero, Sensor cells, History chart, then Device cells")
func detailParts() {
  let presentation = LotDetailFixture.needsCalibration.build()

  #expect(presentation.surface == .ready)
  #expect(presentation.hero != nil)
  #expect(presentation.sensors?.count == 4)
  #expect(presentation.chart != nil)
  #expect(presentation.device != nil)
  #expect(!presentation.stale)
  #expect(presentation.staleHeader(siteName: "Home garden") == nil)
  // No admin strip: no Threshold, Calibrate or Pause action exists before Epics 5 and 8.
  #expect(!presentation.noNode)
}

@Test("UX-DR63 a no-Node Lot is the empty detail, with Add a Node for an Admin+ only")
func detailNoNode() throws {
  let presentation = LotDetailFixture.noNodeLot.build()

  #expect(presentation.noNode)
  #expect(presentation.canAddNode)
  #expect(presentation.sensors == nil)
  #expect(presentation.device == nil)
  #expect(presentation.chart == nil)
  #expect(try heroOf(.noNodeLot).statusLabel == .lotTileNoNode)
  var member = LotDetailFixture.noNodeLot
  member.canAddNode = false
  #expect(!member.build().canAddNode)
  #expect(try Catalogue.entries()["lot_detail_no_node"] == "This Lot has no Node.")
}

@Test("UX-DR63 UX-DR79 stale: the stale header and the hero, and no live value")
func detailStale() throws {
  let presentation = LotDetailFixture.ok.asStale.build()
  let header = try #require(presentation.staleHeader(siteName: "Home garden"))

  #expect(presentation.stale)
  #expect(header.title(context) == "Home garden · can't reach your Server")
  #expect(header.ageText(context) == "2 h 58 min old")
  #expect(header.detail(context).hasPrefix("Last data 07:02"))
  #expect(presentation.sensors == nil)
  #expect(presentation.device == nil)
  #expect(try heroOf(LotDetailFixture.ok.asStale).valueText(context) == nil)
}

@Test(
  "UX-DR63 a Lot being read shows its name and nothing else; a failure says what and offers a retry"
)
func detailLoadingAndFailed() {
  var loading = LotDetailFixture.ok
  loading.surface = "loading"
  #expect(loading.build().surface == .loading(name: "Herbs"))
  #expect(loading.build().hero == nil)

  var failed = LotDetailFixture.ok
  failed.surface = "failed"
  failed.notice = "unreachable"
  #expect(failed.build().surface == .failed(name: "Herbs", notice: .unreachable))
  #expect(LotDetailNoticeKind.unreachable.offersTryAgain)
  #expect(LotDetailNoticeKind.unexpected.offersTryAgain)
  #expect(!LotDetailNoticeKind.certificate.offersTryAgain)
  #expect(!LotDetailNoticeKind.notFound.offersTryAgain)
  #expect(LotDetailNoticeKind.notFound.takesSite && LotDetailNoticeKind.forbidden.takesSite)
  failed.notice = "sprouting"
  #expect(failed.build().surface == .failed(name: "Herbs", notice: .unexpected))
  #expect(LotDetailFixture().build().surface == .ready)
  var idle = LotDetailFixture()
  idle.surface = "idle"
  #expect(idle.build() == .idle)
}

@Test("UX-DR63 an unknown status or label is drawn as unknown, never as fine")
func detailUnknownNames() throws {
  var odd = LotDetailFixture.ok
  odd.heroVariant = "sprouting"
  odd.heroLabel = "sprouting"
  let hero = try heroOf(odd)

  #expect(hero.variant == .unknown)
  #expect(hero.statusLabel == .lotTileLabelUnknownNode)
}

// MARK: - Screen-reader labels (UX-DR98)

@Test("UX-DR98 the hero is one element: the Lot, its status, since, its value and its note")
func heroSpoken() throws {
  #expect(
    try heroOf(.needsCalibration).spokenText(context)
      == "Peppers, Needs Calibration, since 07:02, raw 1840, no % until calibrated")
  #expect(
    try heroOf(.nodeSilent).spokenText(context)
      == "Beans, Silent · unknown, since 07:02, 6 h, Check power or range.")
  #expect(
    try heroOf(.pausedBySite).spokenText(context)
      == "Leeks, Paused by Site, since 07:02, Paused with the Site, Resume the Site to resume this Node"
  )
  #expect(try heroOf(.noNodeLot).spokenText(context) == "Zucchini, no Node, since 07:02")
  #expect(
    try heroOf(LotDetailFixture.needsWater.asStale).spokenText(context)
      == "Tomatoes, Was needs water, since 07:02")
}

@Test("UX-DR98 the chart's accessibility label is its text summary")
func chartSummary() throws {
  let chart = try #require(LotDetailFixture.needsCalibration.build().chart)

  #expect(
    chart.summary(context)
      == "Soil moisture, 30 days, lowest raw 1790 on 6 Oct, 3 days with Readings.")
  var one = LotDetailFixture.needsCalibration
  one.days = [0: ("1790", "2050", 30, 0.5)]
  #expect(
    try #require(one.build().chart).summary(context)
      == "Soil moisture, 30 days, lowest raw 1790 on 6 Oct, 1 day with Readings.")
  var none = LotDetailFixture.needsCalibration
  none.days = [:]
  #expect(
    try #require(none.build().chart).summary(context) == "Soil moisture, 30 days, no Readings.")
  #expect(L10n.plurals.contains(.lotDetailChartSummary))
}

@Test("UX-DR98 the chart is one image element labelled by its summary, with the readout beside it")
func chartViewIsLabelled() throws {
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/LotDetailViews.swift")

  #expect(views.contains(".accessibilityLabel(Text(verbatim: chart.summary(context)))"))
  #expect(views.contains(".accessibilityAddTraits(.isImage)"))
  #expect(views.contains("DragGesture(minimumDistance: 0)"))
}

// MARK: - The flat snapshot

@Test("UX-DR63 the lists of the snapshot map to cells and bars one for one, whatever their length")
func snapshotShortLists() {
  var detail = LotDetailFixture.needsCalibration
  detail.sensors = [("soilMoisture", "1840", "raw"), ("airTemperature", "14", "celsius")]
  let presentation = detail.build()

  #expect(presentation.sensors?.map(\.quantity) == [.soilMoisture, .airTemperature])
  #expect(presentation.quantities == [.soilMoisture, .airTemperature])
  #expect(presentation.picked == .soilMoisture)
}
