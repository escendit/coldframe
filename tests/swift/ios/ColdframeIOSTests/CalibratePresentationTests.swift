import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

/// A snapshot as the core flattens it, defaulted to the dry step of the Tomatoes with two stored
/// Readings and nothing fresh; every argument mirrors a `CalibrateSnapshot` field.
private func calibrate(
  surface: String = "ready", notice: String? = nil, noticeTryAgain: Bool = false,
  lotName: String? = "Tomatoes", step: String? = "dry", working: Bool = false,
  canRecord: Bool = false, hasFresh: Bool = false, lastRawValue: String = "612",
  lastReadingAt: String = String(Overview.readingMs), readingSeqs: [Int64] = [42, 41],
  readingRawValues: [Int64] = [612, 640],
  readingAts: [String] = [String(Overview.readingMs), String(Overview.earlierMs)],
  pickedSeq: String = "", dryRaw: String = "", wetRaw: String = "", percent: String = "",
  announcementId: Int = 0, announcementKind: String? = nil, announcementStep: String? = nil,
  announcementRaw: String = "", announcementAt: String = "", announcementPercent: String = "",
  pausedBySite: Bool = false, offersResume: Bool = false
) -> CalibratePresentation {
  CalibratePresentation(
    surface: surface, notice: notice, noticeTryAgain: noticeTryAgain, lotId: "t",
    lotName: lotName, step: step, working: working, canRecord: canRecord, hasFresh: hasFresh,
    lastRawValue: lastRawValue, lastReadingAt: lastReadingAt, readingSeqs: readingSeqs,
    readingRawValues: readingRawValues, readingAts: readingAts, pickedSeq: pickedSeq,
    dryRaw: dryRaw, wetRaw: wetRaw, percent: percent, announcementId: announcementId,
    announcementKind: announcementKind, announcementStep: announcementStep,
    announcementRaw: announcementRaw, announcementAt: announcementAt,
    announcementPercent: announcementPercent, announcementLot: "Tomatoes",
    pausedBySite: pausedBySite, offersResume: offersResume)
}

@Test("Story 5.2 the surfaces follow the core: idle, loading, failed, paused and ready")
func calibrateSurfaces() {
  #expect(calibrate(surface: "idle").surface == .idle)
  #expect(!calibrate(surface: "idle").isOpen)
  #expect(calibrate(surface: "loading").surface == .loading(name: "Tomatoes"))
  #expect(
    calibrate(surface: "failed", notice: "noSensor", noticeTryAgain: true).surface
      == .failed(name: "Tomatoes", notice: .noSensor, tryAgain: true))
  #expect(
    calibrate(surface: "paused", pausedBySite: true).surface
      == .paused(name: "Tomatoes", bySite: true, offersResume: false))
  #expect(calibrate().isOpen)
  // A surface the core did not describe is not drawn.
  #expect(calibrate(surface: "elsewhere").surface == .idle)
}

@Test("Story 5.2 the dry step waits: last raw value and time, the hint, Record dry disabled")
func calibrateDryWaiting() throws {
  let presentation = calibrate()

  #expect(presentation.step == .dry)
  #expect(presentation.step.counter == .calibrateStepDry)
  #expect(presentation.step.intro == .calibrateIntroDry)
  #expect(presentation.isWaiting)
  #expect(presentation.waitingTitle == .calibrateWaiting)
  #expect(presentation.hint == .calibrateHint)
  #expect(presentation.lastReadingText(context) == "Last Reading: raw 612 at 07:02")
  #expect(presentation.recordLabel == .calibrateRecordDry)
  #expect(!presentation.isRecordEnabled)
  #expect(presentation.freshText(context) == nil)
  // Nothing to announce while waiting: the waiting text is never spoken.
  #expect(presentation.spokenAnnouncement(context) == nil)
  #expect(Catalogue.resolve(presentation.title) == "Calibrate Tomatoes")
  #expect(try Catalogue.entries()["calibrate_waiting"] == "Waiting for the next Reading")
}

@Test("Story 5.2 without a stored Reading the waiting panel says so")
func calibrateNoReadingYet() {
  let presentation = calibrate(
    lastRawValue: "", lastReadingAt: "", readingSeqs: [], readingRawValues: [], readingAts: [])

  #expect(presentation.lastReadingText(context) == "No Reading yet.")
  #expect(presentation.readings.isEmpty)
}

@Test("Story 5.2 a fresh Reading enables Record dry and is announced politely")
func calibrateFreshReading() {
  let presentation = calibrate(
    canRecord: true, hasFresh: true, pickedSeq: "42", announcementId: 1,
    announcementKind: "freshReading", announcementStep: "dry", announcementRaw: "612",
    announcementAt: String(Overview.readingMs))

  #expect(presentation.isRecordEnabled)
  #expect(!presentation.isWaiting)
  #expect(presentation.freshText(context) == "New Reading 07:02, raw 612.")
  #expect(presentation.candidate?.readingSeq == 42)
  #expect(presentation.announcement?.announcement == .polite)
  #expect(
    presentation.spokenAnnouncement(context) == "New Reading 07:02, raw 612. Record dry is available.")
}

@Test("Story 5.2 the wet step announces Record wet")
func calibrateWetAnnouncement() {
  let presentation = calibrate(
    step: "wet", canRecord: true, hasFresh: true, pickedSeq: "42", dryRaw: "3000",
    announcementId: 2, announcementKind: "freshReading", announcementStep: "wet",
    announcementRaw: "1200", announcementAt: String(Overview.readingMs))

  #expect(presentation.step.counter == .calibrateStepWet)
  #expect(presentation.step.intro == .calibrateIntroWet)
  #expect(presentation.recordLabel == .calibrateRecordWet)
  #expect(
    presentation.spokenAnnouncement(context)
      == "New Reading 07:02, raw 1200. Record wet is available.")
}

@Test("Story 5.2 resuming opens at wet with the kept dry point")
func calibrateResumesAtWet() {
  let presentation = calibrate(step: "wet", dryRaw: "3000")

  #expect(presentation.step == .wet)
  #expect(presentation.dryRaw == "3000")
  #expect(presentation.isWaiting)
}

@Test("Story 5.2 a Reading picked from Recent Readings enables recording without waiting")
func calibratePickInstead() {
  let presentation = calibrate(canRecord: true, pickedSeq: "41")

  #expect(presentation.readings.map(\.isCandidate) == [false, true])
  #expect(presentation.readings[1].traits == [.button, .selected])
  #expect(presentation.readings[0].traits == [.button])
  #expect(presentation.readings[1].text(context) == "01:05, raw 640")
  #expect(presentation.isRecordEnabled)
  #expect(presentation.step.useLabel == .calibrateRecordDry)
  // Picked, not fresh: nothing to announce and no "New Reading" line.
  #expect(presentation.freshText(context) == nil)
}

@Test("Story 5.2 recording shows its working label in place and disables the button")
func calibrateRecording() {
  let presentation = calibrate(working: true, canRecord: false, pickedSeq: "42")

  #expect(presentation.recordLabel == .calibrateRecording)
  #expect(!presentation.isRecordEnabled)
}

@Test("Story 5.2 the confirmation shows both raw values and no percentage until one is stored")
func calibrateConfirmationPending() {
  let presentation = calibrate(step: "confirm", dryRaw: "3000", wetRaw: "1200")

  #expect(presentation.step == .confirm)
  #expect(presentation.step.recordLabel == nil)
  #expect(!presentation.showsRecent)
  #expect(Catalogue.resolve(presentation.pointsText) == "Dry raw 3000, wet raw 1200.")
  #expect(presentation.percent == nil)
  #expect(Catalogue.resolve(presentation.statusText) == "% appears with the next Reading")
}

@Test("Story 5.2 the confirmation updates in place when the first calibrated Reading arrives")
func calibrateConfirmationPercent() {
  let presentation = calibrate(
    step: "confirm", dryRaw: "3000", wetRaw: "1200", percent: "40", announcementId: 3,
    announcementKind: "firstPercent", announcementStep: "confirm", announcementPercent: "40")

  #expect(presentation.percent == 40)
  #expect(Catalogue.resolve(presentation.statusText) == "Tomatoes reads ~40 %")
  #expect(presentation.spokenAnnouncement(context) == "Tomatoes reads about 40 percent.")
}

@Test("Story 5.2 a paused Node gets the explanation and no waiting")
func calibratePaused() {
  let own = calibrate(surface: "paused", offersResume: true)
  let site = calibrate(surface: "paused", pausedBySite: true)

  #expect(own.pausedMessage(bySite: false) == .calibratePaused)
  #expect(site.pausedMessage(bySite: true) == .calibratePausedSite)
  #expect(
    Catalogue.resolve(Copy(.calibratePaused))
      == "This Node is paused. Readings resume after the Pause ends, so there is nothing to wait for now."
  )
  // Resume is the core's call: Admin+ and the Device's own Pause only.
  #expect(own.offersResume)
  #expect(!site.offersResume)
  #expect(!calibrate().offersResume)
}

@Test("Story 5.2 every notice says what happened, and an unknown one is unexpected")
func calibrateNotices() throws {
  for kind in CalibrateNoticeKind.allCases {
    #expect(try Catalogue.entries()[kind.message.rawValue] != nil, "\(kind)")
  }
  #expect(CalibrateNoticeKind.indistinct.message == .calibrateNoticeIndistinct)
  #expect(CalibrateNoticeKind.notDelivered.message == .calibrateNoticeNotDelivered)
  #expect(CalibrateNoticeKind.forbidden.takesSite)
  #expect(CalibrateNoticeKind.notFound.takesSite)
  #expect(!CalibrateNoticeKind.indistinct.takesSite)

  let unknown = calibrate(notice: "somethingNew")
  #expect(unknown.notice == .unexpected)
  // A save that was not confirmed keeps the recorded point on the step and says so.
  let kept = calibrate(notice: "notDelivered", canRecord: true, pickedSeq: "42")
  #expect(kept.notice == .notDelivered)
  #expect(kept.isRecordEnabled)
  #expect(kept.step == .dry)
}

@Test("Story 5.2 an unknown step is drawn as the first, never as done")
func calibrateUnknownStep() {
  #expect(calibrate(step: "somewhere").step == .dry)
}

@Test("Story 5.2 the Node-added outcome offers Calibrate only where the core says so")
func calibrateOnTheNodeOutcome() {
  let withCalibrate = nodeSetup(
    step: 5, lotName: "Tomatoes", outcome: "assigned", outcomePrimary: "done",
    outcomeSecondary: "calibrate", stoppedStep: 5)
  let without = nodeSetup(
    step: 5, lotName: "Tomatoes", outcome: "assigned", outcomePrimary: "done", stoppedStep: 5)
  let onError = nodeSetup(
    step: 5, outcome: "stoppedListening", outcomePrimary: "startOver",
    outcomeSecondary: "calibrate", stoppedStep: 3)

  #expect(withCalibrate.outcome?.secondary == .calibrate)
  #expect(withCalibrate.outcome?.secondary?.label == .calibrateAction)
  #expect(withCalibrate.outcome?.primary == .done)
  #expect(without.outcome?.secondary == nil)
  // Only a success offers it.
  #expect(onError.outcome?.secondary == nil)
}

@Test("Story 5.2 the needs-calibration tile gets its Calibrate control from the core, others do not")
func calibrateOnTheTile() {
  var calibrating = Overview.needsCalibration
  calibrating.opensCalibrate = true
  let tiles = Overview.lots([calibrating, Overview.ok, Overview.noNode]).tiles

  #expect(tiles.map(\.opensCalibrate) == [true, false, false])
  // The tile itself still opens Lot detail.
  #expect(tiles[0].opensLotDetail)
  // A Member never gets the control: the core sends none.
  #expect(Overview.tile(Overview.needsCalibration).opensCalibrate == false)
  #expect(
    Catalogue.resolve(Copy(.calibrateActionFor, .text("Carrots"))) == "Calibrate Carrots")
}

@Test("Story 5.2 Lot detail shows Calibrate only when the core says the Role and Sensor may")
func calibrateOnLotDetail() {
  var admin = LotDetailFixture.needsCalibration
  admin.canCalibrate = true

  #expect(admin.build().canCalibrate)
  #expect(!LotDetailFixture.needsCalibration.build().canCalibrate)
}

@Test("Story 5.2 every Calibrate entry of the catalogue follows the voice rules")
func calibrateCopy() throws {
  let entries = try Catalogue.entries()
  let keys = L10n.allCases.filter { $0.rawValue.hasPrefix("calibrate_") }
  #expect(!keys.isEmpty)
  for key in keys {
    #expect(entries[key.rawValue]?.isEmpty == false, "\(key)")
  }
  #expect(entries["calibrate_record_dry"] == "Record dry")
  #expect(entries["calibrate_confirm_pending"] == "% appears with the next Reading")
}

@MainActor
private final class CalibrateSpy: CalibrateService {
  var calls: [String] = []
  func observe(_ onChange: @escaping @MainActor (CalibratePresentation) -> Void) {}
  func open(lotId: String, name: String) { calls.append("open \(lotId) \(name)") }
  func close() { calls.append("close") }
  func retry() { calls.append("retry") }
  func pick(readingSeq: Int64) { calls.append("pick \(readingSeq)") }
  func record() { calls.append("record") }
}

#if canImport(SwiftUI)
  @Test("Story 5.2 opening, picking, recording, retrying and leaving reach the Calibrate service")
  @MainActor
  func calibrateActionsForward() {
    let spy = CalibrateSpy()
    let actions = CalibrateActions(service: spy)
    actions.open("t", "Tomatoes")
    actions.pick(41)
    actions.record()
    actions.retry()
    actions.close()
    #expect(spy.calls == ["open t Tomatoes", "pick 41", "record", "retry", "close"])
  }
#endif
