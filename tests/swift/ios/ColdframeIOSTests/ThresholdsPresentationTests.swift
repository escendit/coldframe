import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

/// One column of the flat snapshot, defaulted to calibrated soil with a 30 % low and no high.
private struct Column {
  var sensorId = "s-soil"
  var quantity = "soilMoisture"
  var unit = "percent"
  var low = "30"
  var high = ""
  var originalLow = "30"
  var originalHigh = ""
  var proposedLow = ""
  var current = "40"
  var step = "5"
  var trackMin = 0.0
  var trackMax = 100.0
  var draggable = true
  var alerting = true
  var lowMustStayBelowHigh = false
  var lowRequired = false
  var offersProposal = false
}

private let soil = Column()
private let air = Column(
  sensorId: "s-air", quantity: "airTemperature", unit: "celsius", low: "", high: "",
  originalLow: "", proposedLow: "7", current: "14.4", step: "", trackMin: 0, trackMax: 30,
  draggable: false, alerting: false, offersProposal: true)

private func thresholds(
  surface: String = "ready", notice: String? = nil, noticeTryAgain: Bool = false,
  canEdit: Bool = true, working: Bool = false, dirty: Bool = false, canSave: Bool = false,
  saved: Bool = false, focusSensorId: String = "", columns: [Column] = [soil, air]
) -> ThresholdsPresentation {
  ThresholdsPresentation(
    surface: surface, notice: notice, noticeTryAgain: noticeTryAgain, lotId: "t",
    lotName: "Tomatoes", canEdit: canEdit, working: working, dirty: dirty, canSave: canSave,
    saved: saved, focusSensorId: focusSensorId, sensorIds: columns.map(\.sensorId),
    quantities: columns.map(\.quantity), units: columns.map(\.unit), lows: columns.map(\.low),
    highs: columns.map(\.high), originalLows: columns.map(\.originalLow),
    originalHighs: columns.map(\.originalHigh), proposedLows: columns.map(\.proposedLow),
    currents: columns.map(\.current), steps: columns.map(\.step),
    trackMins: columns.map(\.trackMin), trackMaxs: columns.map(\.trackMax),
    draggables: columns.map(\.draggable), alerting: columns.map(\.alerting),
    lowMustStayBelowHigh: columns.map(\.lowMustStayBelowHigh),
    lowRequired: columns.map(\.lowRequired), offersProposal: columns.map(\.offersProposal))
}

@Test("UX-DR45 the surfaces follow the core: idle, loading, failed and ready")
func thresholdsSurfaces() {
  #expect(thresholds(surface: "idle").surface == .idle)
  #expect(!thresholds(surface: "idle").isOpen)
  #expect(thresholds(surface: "loading").surface == .loading(name: "Tomatoes"))
  #expect(
    thresholds(surface: "failed", notice: "noSensor", noticeTryAgain: true).surface
      == .failed(name: "Tomatoes", notice: .noSensor, tryAgain: true))
  #expect(thresholds().isOpen)
  // A surface the core did not describe is not drawn.
  #expect(thresholds(surface: "elsewhere").surface == .idle)
}

@Test("UX-DR45 each Sensor has a Threshold column: track, low line, Reading marker, no-high marker")
func thresholdColumns() throws {
  let presentation = thresholds(focusSensorId: "s-air")
  let first = try #require(presentation.columns.first)

  #expect(presentation.columns.map(\.sensorId) == ["s-soil", "s-air"])
  #expect(presentation.focusSensorId == "s-air")
  #expect(first.title == .lotDetailQuantitySoilMoisture)
  #expect(first.lowFraction == 0.3)
  #expect(first.currentFraction == 0.4)
  // No high: the dashed "no high" marker, no high line.
  #expect(first.highFraction == nil)
  #expect(first.highText(context) == "no high")
  #expect(first.lowText(context) == "30 %")
  #expect(first.currentText(context) == "Now 40 %")
  #expect(first.stepValue == 5)
  #expect(first.draggable)
  // A free scale is typed, not dragged.
  let watched = presentation.columns[1]
  #expect(!watched.draggable)
  #expect(watched.lowText(context) == "Alerts are off")
}

@Test("UX-DR45 a column of an unknown quantity or unit is left out, never guessed")
func thresholdUnknownColumnDropped() {
  var strange = soil
  strange.quantity = "pressure"
  #expect(thresholds(columns: [strange, soil]).columns.count == 1)
}

@Test("UX-DR45 a column is one element for VoiceOver: Sensor, low, high and the Reading")
func thresholdSpoken() throws {
  let first = try #require(thresholds().columns.first)

  #expect(
    first.spokenText(context) == "Soil moisture, Low 30 %, High no high, Now 40 %")
}

@Test("UX-DR91 Low must stay below high appears inline under the field and the core gates Save")
func thresholdInlineError() throws {
  var bad = soil
  bad.low = "70"
  bad.high = "60"
  bad.lowMustStayBelowHigh = true
  let invalid = thresholds(dirty: true, canSave: false, columns: [bad])

  #expect(try #require(invalid.columns.first).inlineError == .thresholdsLowNotBelowHigh)
  #expect(!invalid.isSaveEnabled)
  #expect(try Catalogue.entries()["thresholds_low_not_below_high"] == "Low must stay below high.")

  var needsLow = air
  needsLow.high = "50"
  needsLow.lowRequired = true
  #expect(thresholds(columns: [needsLow]).columns[0].inlineError == .thresholdsLowRequired)
  #expect(soil.alerting && thresholds().columns[0].inlineError == nil)
}

@Test("UX-DR69 the modal has Cancel and Save, Save follows the core and says Saving while sent")
func thresholdCancelAndSave() {
  let clean = thresholds()
  #expect(clean.closeLabel == .modalCancel)
  #expect(clean.saveLabel == .thresholdsSave)
  #expect(!clean.isSaveEnabled)
  #expect(thresholds(dirty: true, canSave: true).isSaveEnabled)
  let sending = thresholds(working: true, dirty: true, canSave: true)
  #expect(sending.saveLabel == .thresholdsSaving)
  #expect(!sending.isSaveEnabled)
  #expect(thresholds().title.string == "Thresholds for Tomatoes")
}

@Test("UX-DR84 a Member sees the columns read-only: no Save, no edit control, Back instead of Cancel")
func thresholdMemberReadOnly() throws {
  let member = thresholds(canEdit: false)
  let column = try #require(member.columns.first)

  #expect(member.columns.count == 2)
  #expect(member.saveLabel == nil)
  #expect(member.closeLabel == .navBack)
  #expect(member.readOnlyNotice == .thresholdsReadOnly)
  #expect(member.alertsToggle(column) == nil)
  #expect(!member.showsAddHigh(column))
  #expect(!member.showsClearHigh(column))
  #expect(thresholds().readOnlyNotice == nil)
}

@Test("UX-DR84 an editor gets Add high, Clear high and Turn on or off Alerts where they apply")
func thresholdEditorControls() throws {
  let editor = thresholds()
  var withHigh = soil
  withHigh.high = "70"
  let watched = try #require(editor.columns.last)

  #expect(editor.showsAddHigh(try #require(editor.columns.first)))
  #expect(!editor.showsAddHigh(watched))
  let shown = thresholds(columns: [withHigh])
  #expect(shown.showsClearHigh(shown.columns[0]))
  #expect(editor.alertsToggle(try #require(editor.columns.first)) == .thresholdsTurnOff)
  #expect(editor.alertsToggle(watched) == .thresholdsTurnOn)
  // The Server's proposal for a Sensor without a default, never a proposed high.
  #expect(watched.offersProposal)
  #expect(watched.proposedLow == "7")
}

@Test("UX-DR91 a 403 race names the Site, a save that did not happen keeps the edits and says so")
func thresholdNotices() throws {
  #expect(ThresholdsNoticeKind.forbidden.takesSite)
  #expect(ThresholdsNoticeKind.notFound.takesSite)
  #expect(!ThresholdsNoticeKind.notSaved.takesSite)
  #expect(ThresholdsNoticeKind.notSaved.message == .thresholdsNoticeNotSaved)
  #expect(ThresholdsNoticeKind.invalid.announcement == .assertive)
  let entries = try Catalogue.entries()
  #expect(
    entries["thresholds_notice_forbidden"]
      == "You can't change this on %1$@. Ask an Owner or Administrator.")
  #expect(entries["thresholds_notice_invalid"]?.contains("Nothing was changed") == true)
  #expect(entries["thresholds_notice_not_saved"]?.contains("edits are kept") == true)
  let kept = thresholds(notice: "notSaved", dirty: true, canSave: true)
  #expect(kept.saveNotice == .notSaved)
  #expect(kept.columns[0].low == "30")
  // An unknown notice is drawn as unexpected, never as done.
  #expect(thresholds(notice: "elsewhere").saveNotice == .unexpected)
}

@Test("UX-DR45 the actions reach the service one for one")
@MainActor
func thresholdActionsReachTheService() {
  final class Spy: ThresholdsService {
    var calls: [String] = []
    func observe(_ onChange: @escaping @MainActor (ThresholdsPresentation) -> Void) {}
    func open(lotId: String, name: String, sensorId: String) {
      calls.append("open \(lotId) \(name) \(sensorId)")
    }
    func close() { calls.append("close") }
    func retry() { calls.append("retry") }
    func setLowText(sensorId: String, text: String) { calls.append("lowText \(sensorId) \(text)") }
    func setHighText(sensorId: String, text: String) {
      calls.append("highText \(sensorId) \(text)")
    }
    func setLow(sensorId: String, value: Double) { calls.append("low \(sensorId) \(value)") }
    func setHigh(sensorId: String, value: Double) { calls.append("high \(sensorId) \(value)") }
    func addHigh(sensorId: String) { calls.append("addHigh \(sensorId)") }
    func clearHigh(sensorId: String) { calls.append("clearHigh \(sensorId)") }
    func turnOnAlerts(sensorId: String) { calls.append("on \(sensorId)") }
    func turnOffAlerts(sensorId: String) { calls.append("off \(sensorId)") }
    func save() { calls.append("save") }
  }
  let spy = Spy()
  let actions = ThresholdsActions(service: spy)

  actions.open("t", "Tomatoes", "s")
  actions.setLowText("s", "25")
  actions.setHighText("s", "")
  actions.setLow("s", 25)
  actions.setHigh("s", 70)
  actions.addHigh("s")
  actions.clearHigh("s")
  actions.turnOnAlerts("s")
  actions.turnOffAlerts("s")
  actions.save()
  actions.retry()
  actions.close()

  #expect(
    spy.calls == [
      "open t Tomatoes s", "lowText s 25", "highText s ", "low s 25.0", "high s 70.0",
      "addHigh s", "clearHigh s", "on s", "off s", "save", "retry", "close",
    ])
}

// MARK: - Entry points (UX-DR84) and the chart band (UX-DR5, UX-DR32, UX-DR33)

@Test("UX-DR84 Set Thresholds shows for an editor, View Thresholds for a Member, hidden without a Sensor")
func lotDetailThresholdsAction() {
  var editor = LotDetailFixture.thresholds
  #expect(editor.build().thresholdsAction == .thresholdsActionSet)
  editor.canSetThresholds = false
  #expect(editor.build().thresholdsAction == .thresholdsActionView)
  editor.canViewThresholds = false
  #expect(editor.build().thresholdsAction == nil)
  var empty = LotDetailFixture.thresholds
  empty.noNode = true
  #expect(empty.build().thresholdsAction == nil)
}

@Test("UX-DR45 a Sensor cell opens Thresholds for its Sensor, only where Thresholds are offered")
func sensorCellOpensThresholds() throws {
  let detail = LotDetailFixture.thresholds.build()
  let cells = try #require(detail.sensors)

  #expect(detail.thresholdsSensorId(for: cells[0]) == "s-soil")
  #expect(detail.thresholdsSensorId(for: cells[1]) == "s-air")
  var withoutIds = LotDetailFixture.thresholds
  withoutIds.sensorIds = []
  let none = withoutIds.build()
  let noneCells = try #require(none.sensors)
  #expect(none.thresholdsSensorId(for: noneCells[0]) == nil)
}

@Test("UX-DR5 the chart draws the band, the low and the high line, and a legend for the solid bars")
func chartBand() throws {
  let detail = LotDetailFixture.thresholds.build()
  let chart = try #require(detail.chart)
  let band = try #require(chart.band)

  #expect(band.lowPercent == "30")
  #expect(band.lowFraction == 0.3)
  #expect(band.highFraction == 0.7)
  #expect(chart.legend(context) == "solid bar = below 30 %")
  #expect(detail.thresholdLowPercent == 30)
  #expect(detail.thresholdHighPercent == 70)
}

@Test("UX-DR5 without a high the band runs to the top, and without a band there is no legend")
func chartBandWithoutHigh() throws {
  var fixture = LotDetailFixture.thresholds
  fixture.band = ("30", "", 0.3, 0)
  let chart = try #require(fixture.build().chart)
  #expect(chart.band?.highFraction == nil)

  fixture.band = nil
  fixture.belowLowDays = []
  let plain = try #require(fixture.build().chart)
  #expect(plain.band == nil)
  #expect(plain.legend(context) == nil)
}

@Test("UX-DR32 a day under the low line is a solid below-low bar; the others are outlined")
func chartBelowLowBars() throws {
  let chart = try #require(LotDetailFixture.thresholds.build().chart)
  let below = chart.bars.filter(\.belowLow)

  #expect(below.map(\.day) == [LotDetailFixture.dayText(back: 3)])
  #expect(chart.bars.filter(\.present).count == 3)
  #expect(chart.belowLowBars.count == 1)
}

@Test("UX-DR33 the summary names the days below the low line, or says it was never below")
func chartSummaryNamesBelowLowDays() throws {
  let chart = try #require(LotDetailFixture.thresholds.build().chart)
  let below = chart.belowLowBars.map { chart.dayText($0.dayStart, context) }
  let summary = chart.summary(context)

  #expect(summary.contains("Below 30 % on \(below.joined(separator: ", "))."))
  #expect(below.count == 1)

  var never = LotDetailFixture.thresholds
  never.belowLowDays = []
  let calm = try #require(never.build().chart)
  #expect(calm.summary(context).hasSuffix("Never below 30 %."))
}

@Test("UX-DR33 selecting a below-low day reads it with the low line")
func chartReadoutBelowLow() throws {
  let chart = try #require(LotDetailFixture.thresholds.build().chart)
  let index = try #require(chart.bars.firstIndex(where: \.belowLow))

  #expect(chart.readout(at: index, context)?.hasSuffix("lowest 20 %, below 30 %") == true)
}

@Test("UX-DR69 the Calibrate confirmation offers Set Thresholds only when the core says so")
func calibrateConfirmationOffersThresholds() {
  func calibrate(offers: Bool) -> CalibratePresentation {
    CalibratePresentation(
      surface: "ready", notice: nil, noticeTryAgain: false, lotId: "t", lotName: "Tomatoes",
      step: "confirm", working: false, canRecord: false, hasFresh: false, lastRawValue: "612",
      lastReadingAt: "", readingSeqs: [], readingRawValues: [], readingAts: [], pickedSeq: "",
      dryRaw: "3000", wetRaw: "1200", percent: "", announcementId: 0, announcementKind: nil,
      announcementStep: nil, announcementRaw: "", announcementAt: "", announcementPercent: "",
      announcementLot: "Tomatoes", pausedBySite: false, offersResume: false,
      offersThresholds: offers)
  }

  #expect(calibrate(offers: true).offersThresholds)
  #expect(!calibrate(offers: false).offersThresholds)
  #expect(try! Catalogue.entries()["thresholds_next"] == "Set Thresholds")
}
