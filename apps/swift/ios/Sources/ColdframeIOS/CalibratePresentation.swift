import Foundation

/// The steps of Calibrate (Story 5.2, UX-DR66): the probe in dry soil, then in water, then the
/// confirmation. The core owns the order and every rule; these are its names.
public enum CalibrateStepKind: String, CaseIterable, Sendable {
  case dry
  case wet
  case confirm

  /// "Step 1 of 2: dry"; the confirmation has no counter.
  public var counter: L10n? {
    switch self {
    case .dry: .calibrateStepDry
    case .wet: .calibrateStepWet
    case .confirm: nil
    }
  }

  public var intro: L10n? {
    switch self {
    case .dry: .calibrateIntroDry
    case .wet: .calibrateIntroWet
    case .confirm: nil
    }
  }

  /// "Record dry" / "Record wet"; the confirmation records nothing.
  public var recordLabel: L10n? {
    switch self {
    case .dry: .calibrateRecordDry
    case .wet: .calibrateRecordWet
    case .confirm: nil
    }
  }

  /// The action a Reading of the Recent Readings list stands for: the step's record label.
  public var useLabel: L10n? { recordLabel }
}

/// Why a Calibrate step did not happen or the flow could not open, as the core named it. Each
/// message says what happened, what did not change and what to do next.
public enum CalibrateNoticeKind: String, CaseIterable, Sendable {
  case indistinct
  case notDelivered
  case forbidden
  case notFound
  case noSensor
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .indistinct: .calibrateNoticeIndistinct
    case .notDelivered: .calibrateNoticeNotDelivered
    case .forbidden: .calibrateNoticeForbidden
    case .notFound: .calibrateNoticeNotFound
    case .noSensor: .calibrateNoSensor
    case .unreachable: .calibrateNoticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .calibrateNoticeUnexpected
    }
  }

  /// The two notices that name the Site (`%@`).
  public var takesSite: Bool { self == .forbidden || self == .notFound }

  /// An assertive notice: a step that did not happen is said at once.
  public var announcement: Announcement { .assertive }
}

/// What Calibrate says aloud, as the core named it. The waiting text is never announced.
public enum CalibrateAnnouncementKind: String, CaseIterable, Sendable {
  case freshReading
  case firstPercent
}

/// One announcement of Calibrate (UX-DR105), made once per `id`, always polite.
public struct CalibrateAnnouncementPresentation: Equatable, Sendable {
  public let id: Int
  public let kind: CalibrateAnnouncementKind
  public let step: CalibrateStepKind
  public let raw: String
  public let at: Date?
  public let percent: String
  public let lot: String

  public init(
    id: Int, kind: CalibrateAnnouncementKind, step: CalibrateStepKind, raw: String, at: Date?,
    percent: String, lot: String
  ) {
    self.id = id
    self.kind = kind
    self.step = step
    self.raw = raw
    self.at = at
    self.percent = percent
    self.lot = lot
  }

  public var announcement: Announcement { .polite }

  /// "New Reading 07:17, raw 612. Record dry is available." or "Tomatoes reads about 40 percent."
  public func text(_ context: CopyContext) -> String? {
    switch kind {
    case .freshReading:
      guard let at, step != .confirm else { return nil }
      return context.text(
        step == .dry ? .calibrateAnnounceDry : .calibrateAnnounceWet, .text(context.when(at)),
        .text(raw))
    case .firstPercent:
      guard !percent.isEmpty else { return nil }
      return context.text(.calibrateAnnouncePercent, .text(lot), .text(percent))
    }
  }
}

/// A recent stored Reading of the Sensor, in the Recent Readings list: `readingSeq` is what a
/// point names. It is picked instead of waiting; the core says which one the next record uses.
public struct CalibrateReadingPresentation: Equatable, Sendable, Identifiable {
  public let readingSeq: Int64
  public let rawValue: Int64
  public let measuredAt: Date?
  public let isCandidate: Bool

  public var id: Int64 { readingSeq }

  public init(readingSeq: Int64, rawValue: Int64, measuredAt: Date?, isCandidate: Bool) {
    self.readingSeq = readingSeq
    self.rawValue = rawValue
    self.measuredAt = measuredAt
    self.isCandidate = isCandidate
  }

  /// "07:17, raw 612".
  public func text(_ context: CopyContext) -> String {
    context.text(
      .calibrateRecentItem, .text(measuredAt.map { context.when($0) } ?? ""),
      .text(String(rawValue)))
  }

  /// Selection is a trait and a checkmark, never colour alone.
  public var traits: Set<ControlTrait> { isCandidate ? [.button, .selected] : [.button] }
}

/// What Calibrate shows.
public enum CalibrateSurface: Equatable, Sendable {
  /// Calibrate is not open.
  case idle
  case loading(name: String)
  case failed(name: String, notice: CalibrateNoticeKind, tryAgain: Bool)
  /// The Node is paused: nothing is waited for. `offersResume` is Admin+ on the Device's own Pause.
  case paused(name: String, bySite: Bool, offersResume: Bool)
  case ready
}

/// Calibrate as the SwiftUI shell sees it (Story 5.2, UX-DR66), built from the core's flat
/// `CalibrateSnapshot`: dry, then wet, then the confirmation. The shell draws what the core
/// decided; it never computes a step, a freshness, a percentage or a Role rule (AD-14).
public struct CalibratePresentation: Equatable, Sendable {
  public let surface: CalibrateSurface
  public let lotId: String?
  public let lotName: String
  public let step: CalibrateStepKind
  public let isWorking: Bool
  /// Record dry / Record wet is enabled: a fresh or a picked Reading exists and nothing is in flight.
  public let canRecord: Bool
  public let hasFresh: Bool
  /// The raw value of the newest stored Reading, empty without one.
  public let lastRaw: String
  public let lastReadingAt: Date?
  public let readings: [CalibrateReadingPresentation]
  public let dryRaw: String
  public let wetRaw: String
  /// Nil until the Server stored a calibrated Reading; never a guess.
  public let percent: Int?
  public let notice: CalibrateNoticeKind?
  public let announcement: CalibrateAnnouncementPresentation?

  public static let idle = CalibratePresentation(surface: .idle)

  public init(
    surface: CalibrateSurface, lotId: String? = nil, lotName: String = "",
    step: CalibrateStepKind = .dry, isWorking: Bool = false, canRecord: Bool = false,
    hasFresh: Bool = false, lastRaw: String = "", lastReadingAt: Date? = nil,
    readings: [CalibrateReadingPresentation] = [], dryRaw: String = "", wetRaw: String = "",
    percent: Int? = nil, notice: CalibrateNoticeKind? = nil,
    announcement: CalibrateAnnouncementPresentation? = nil
  ) {
    self.surface = surface
    self.lotId = lotId
    self.lotName = lotName
    self.step = step
    self.isWorking = isWorking
    self.canRecord = canRecord
    self.hasFresh = hasFresh
    self.lastRaw = lastRaw
    self.lastReadingAt = lastReadingAt
    self.readings = readings
    self.dryRaw = dryRaw
    self.wetRaw = wetRaw
    self.percent = percent
    self.notice = notice
    self.announcement = announcement
  }

  /// Mirrors the flat snapshot field for field. Times are Unix milliseconds in decimal; an
  /// optional number is a decimal string that is empty when absent; an unknown notice is drawn as
  /// unexpected, an unknown step as the first one, never as done.
  public init(
    surface: String, notice: String?, noticeTryAgain: Bool, lotId: String?, lotName: String?,
    step: String?, working: Bool, canRecord: Bool, hasFresh: Bool, lastRawValue: String,
    lastReadingAt: String, readingSeqs: [Int64], readingRawValues: [Int64],
    readingAts: [String], pickedSeq: String, dryRaw: String, wetRaw: String, percent: String,
    announcementId: Int, announcementKind: String?, announcementStep: String?,
    announcementRaw: String, announcementAt: String, announcementPercent: String,
    announcementLot: String?, pausedBySite: Bool, offersResume: Bool
  ) {
    let name = lotName ?? ""
    let noticeKind = notice.map { CalibrateNoticeKind(rawValue: $0) ?? .unexpected }
    switch surface {
    case "loading":
      self.init(surface: .loading(name: name), lotId: lotId, lotName: name)
      return
    case "failed":
      self.init(
        surface: .failed(name: name, notice: noticeKind ?? .unexpected, tryAgain: noticeTryAgain),
        lotId: lotId, lotName: name)
      return
    case "paused":
      self.init(
        surface: .paused(name: name, bySite: pausedBySite, offersResume: offersResume),
        lotId: lotId, lotName: name)
      return
    case "ready": break
    default:
      self.init(surface: .idle)
      return
    }
    let candidate = Int64(pickedSeq)
    let count = min(readingSeqs.count, readingRawValues.count, readingAts.count)
    let readings = (0..<count).map {
      CalibrateReadingPresentation(
        readingSeq: readingSeqs[$0], rawValue: readingRawValues[$0],
        measuredAt: Self.date(readingAts[$0]), isCandidate: readingSeqs[$0] == candidate)
    }
    var announcement: CalibrateAnnouncementPresentation?
    if announcementId > 0,
      let kind = announcementKind.flatMap(CalibrateAnnouncementKind.init(rawValue:))
    {
      announcement = CalibrateAnnouncementPresentation(
        id: announcementId, kind: kind,
        step: announcementStep.flatMap(CalibrateStepKind.init(rawValue:)) ?? .dry,
        raw: announcementRaw, at: Self.date(announcementAt), percent: announcementPercent,
        lot: announcementLot ?? name)
    }
    self.init(
      surface: .ready, lotId: lotId, lotName: name,
      step: step.flatMap(CalibrateStepKind.init(rawValue:)) ?? .dry, isWorking: working,
      canRecord: canRecord, hasFresh: hasFresh, lastRaw: lastRawValue,
      lastReadingAt: Self.date(lastReadingAt), readings: readings, dryRaw: dryRaw, wetRaw: wetRaw,
      percent: Int(percent), notice: noticeKind, announcement: announcement)
  }

  private static func date(_ epochMs: String) -> Date? {
    Int64(epochMs).map { Date(timeIntervalSince1970: Double($0) / 1000) }
  }

  /// Whether Calibrate is on screen: anything but idle replaces the tab shell.
  public var isOpen: Bool { surface != .idle }

  /// "Calibrate Tomatoes".
  public var title: Copy { Copy(.calibrateTitle, .text(lotName)) }

  /// "Back": leaving keeps the dry point on the Server, so returning resumes at wet.
  public var backLabel: L10n { .navBack }

  // Waiting

  /// The waiting panel shows while a step has no fresh or picked Reading to record.
  public var isWaiting: Bool {
    step != .confirm && !canRecord && !isWorking && !hasFresh
  }

  /// The waiting panel's title, "Waiting for the next Reading". Never announced.
  public let waitingTitle: L10n = .calibrateWaiting

  /// "Last Reading: raw 612 at 07:17", or "No Reading yet.".
  public func lastReadingText(_ context: CopyContext) -> String {
    guard !lastRaw.isEmpty else { return context.text(.calibrateNoneYet) }
    return context.text(
      .calibrateLast, .text(lastRaw), .text(lastReadingAt.map { context.when($0) } ?? ""))
  }

  /// The hint to short-press the Node's setup button.
  public let hint: L10n = .calibrateHint

  /// The Reading Record dry / Record wet would record: the picked one, else the fresh one.
  public var candidate: CalibrateReadingPresentation? { readings.first(where: \.isCandidate) }

  /// "New Reading 07:17, raw 612." once a fresh Reading arrived.
  public func freshText(_ context: CopyContext) -> String? {
    guard hasFresh, let candidate else { return nil }
    return context.text(
      .calibrateFresh, .text(candidate.measuredAt.map { context.when($0) } ?? ""),
      .text(String(candidate.rawValue)))
  }

  // Record

  /// "Record dry" / "Record wet", "Recording…" in place while the point is sent.
  public var recordLabel: L10n? { isWorking ? .calibrateRecording : step.recordLabel }

  public var isRecordEnabled: Bool { canRecord && !isWorking }

  public var showsRecent: Bool { step != .confirm }

  // Confirmation

  /// "Dry raw 3000, wet raw 1200."
  public var pointsText: Copy { Copy(.calibrateConfirmPoints, .text(dryRaw), .text(wetRaw)) }

  /// "% appears with the next Reading" until the Server stored a calibrated Reading, then
  /// "Tomatoes reads ~40 %" in place.
  public var statusText: Copy {
    if let percent {
      return Copy(.calibrateConfirmReads, .text(lotName), .text(String(percent)))
    }
    return Copy(.calibrateConfirmPending)
  }

  // Paused

  /// The explanation that replaces waiting on a paused Node.
  public func pausedMessage(bySite: Bool) -> L10n {
    bySite ? .calibratePausedSite : .calibratePaused
  }

  /// Resume is offered only when the core says so: Admin+ and the Device's own Pause. The core
  /// has no Resume call before Epic 8, so the shell draws no control for it yet.
  public var offersResume: Bool {
    if case .paused(_, _, let offers) = surface { return offers }
    return false
  }

  /// What the flow posts for the current announcement, or nil when there is none.
  public func spokenAnnouncement(_ context: CopyContext) -> String? {
    announcement?.text(context)
  }
}

/// Calibrate of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosCalibrate` of `ColdframeCore`; the Server calls and every rule never leave the core.
@MainActor
public protocol CalibrateService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (CalibratePresentation) -> Void)
  /// Opens Calibrate for a Lot, shown under `name` until the Server answers.
  func open(lotId: String, name: String)
  /// Leaves Calibrate; the Server keeps a recorded dry point.
  func close()
  /// Try again after a failed open.
  func retry()
  /// Picks (or clears) a stored Reading of the Recent Readings list.
  func pick(readingSeq: Int64)
  /// Record dry / Record wet.
  func record()
}
