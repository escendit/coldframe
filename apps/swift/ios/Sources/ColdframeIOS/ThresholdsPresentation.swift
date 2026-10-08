import Foundation

/// Why Thresholds could not open or were not saved, as the core named it (UX-DR91). Each message
/// says what happened, what did not change and what to do next.
public enum ThresholdsNoticeKind: String, CaseIterable, Sendable {
  case invalid
  case forbidden
  case notFound
  case noSensor
  case notSaved
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .invalid: .thresholdsNoticeInvalid
    case .forbidden: .thresholdsNoticeForbidden
    case .notFound: .thresholdsNoticeNotFound
    case .noSensor: .thresholdsNoSensors
    case .notSaved: .thresholdsNoticeNotSaved
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .thresholdsNoticeUnexpected
    }
  }

  /// The two notices that name the Site (`%@`).
  public var takesSite: Bool { self == .forbidden || self == .notFound }

  /// A save that did not happen is said at once.
  public var announcement: Announcement { .assertive }
}

/// The Threshold column of one Sensor (UX-DR45), as the core flattened it: a vertical track with
/// the low line, the current Reading marker and the dashed "no high" marker. `low` and `high` are
/// the draft being edited, the numbers as the core wrote them (empty for none). The shell draws;
/// it never validates (the core gates Save, the Server decides, AD-14).
public struct ThresholdColumnPresentation: Equatable, Sendable, Identifiable {
  public let sensorId: String
  public let quantity: SensorQuantityKind
  public let unit: SensorUnitKind
  public let low: String
  public let high: String
  public let originalLow: String
  public let originalHigh: String
  public let proposedLow: String
  public let current: String
  /// The step a drag moves in (5 for calibrated soil), empty for a free scale.
  public let step: String
  public let trackMin: Double
  public let trackMax: Double
  public let draggable: Bool
  public let alerting: Bool
  public let lowMustStayBelowHigh: Bool
  public let lowRequired: Bool
  public let offersProposal: Bool

  public var id: String { sensorId }

  public init(
    sensorId: String, quantity: SensorQuantityKind, unit: SensorUnitKind, low: String = "",
    high: String = "", originalLow: String = "", originalHigh: String = "",
    proposedLow: String = "", current: String = "", step: String = "", trackMin: Double = 0,
    trackMax: Double = 100, draggable: Bool = false, alerting: Bool = false,
    lowMustStayBelowHigh: Bool = false, lowRequired: Bool = false, offersProposal: Bool = false
  ) {
    self.sensorId = sensorId
    self.quantity = quantity
    self.unit = unit
    self.low = low
    self.high = high
    self.originalLow = originalLow
    self.originalHigh = originalHigh
    self.proposedLow = proposedLow
    self.current = current
    self.step = step
    self.trackMin = trackMin
    self.trackMax = trackMax
    self.draggable = draggable
    self.alerting = alerting
    self.lowMustStayBelowHigh = lowMustStayBelowHigh
    self.lowRequired = lowRequired
    self.offersProposal = offersProposal
  }

  public var title: L10n { quantity.label }
  public var hasHigh: Bool { !high.isEmpty }
  public var stepValue: Double? { Double(step) }

  /// 0...1 of the track for a value (0 at the bottom); nil for an empty or unreadable value.
  public func fraction(of value: String) -> Double? {
    guard let number = Double(value), trackMax > trackMin else { return nil }
    return min(1, max(0, (number - trackMin) / (trackMax - trackMin)))
  }

  public var lowFraction: Double? { fraction(of: low) }
  public var highFraction: Double? { fraction(of: high) }
  public var currentFraction: Double? { fraction(of: current) }

  /// `30 %`, `7 °C`: a number of the column with its unit.
  public func valueText(_ number: String, _ context: CopyContext) -> String {
    context.resolve(unit.copy(number))
  }

  /// "Now 40 %", or "No Reading yet" when the Sensor has no Reading in this unit.
  public func currentText(_ context: CopyContext) -> String {
    current.isEmpty
      ? context.text(.thresholdsNoReading)
      : context.text(.thresholdsNow, .text(valueText(current, context)))
  }

  /// "no high" while the high is empty; the value otherwise.
  public func highText(_ context: CopyContext) -> String {
    hasHigh ? valueText(high, context) : context.text(.thresholdsNoHigh)
  }

  /// "Alerts are off" for a watched Sensor; the low otherwise.
  public func lowText(_ context: CopyContext) -> String {
    alerting ? valueText(low, context) : context.text(.thresholdsAlertsOff)
  }

  /// "Low must stay below high." appears inline under the field; a high without a low has its own line.
  public var inlineError: L10n? {
    if lowMustStayBelowHigh { return .thresholdsLowNotBelowHigh }
    if lowRequired { return .thresholdsLowRequired }
    return nil
  }

  /// The column for VoiceOver: the Sensor, its low and high, and where the Reading stands.
  public func spokenText(_ context: CopyContext) -> String {
    context.joined(
      .listComma,
      [
        context.text(title),
        context.text(.thresholdsLow) + " " + lowText(context),
        context.text(.thresholdsHigh) + " " + highText(context),
        currentText(context),
      ]) ?? context.text(title)
  }
}

/// What Thresholds shows.
public enum ThresholdsSurface: Equatable, Sendable {
  /// Thresholds is not open.
  case idle
  case loading(name: String)
  case failed(name: String, notice: ThresholdsNoticeKind, tryAgain: Bool)
  case ready
}

/// Thresholds as the SwiftUI shell sees it (Story 5.4, UX-DR45, UX-DR69, UX-DR84, UX-DR91), built
/// from the core's flat `ThresholdsSnapshot`: a modal with Cancel and Save and a Threshold column
/// per Sensor. A Member sees the same columns read-only with no edit control (hidden, never
/// disabled). The shell never computes a rule or a role (AD-14).
public struct ThresholdsPresentation: Equatable, Sendable {
  public let surface: ThresholdsSurface
  public let lotId: String?
  public let lotName: String
  public let canEdit: Bool
  public let isWorking: Bool
  public let isDirty: Bool
  public let canSave: Bool
  public let isSaved: Bool
  public let focusSensorId: String?
  public let columns: [ThresholdColumnPresentation]
  public let notice: ThresholdsNoticeKind?

  public static let idle = ThresholdsPresentation(surface: .idle)

  public init(
    surface: ThresholdsSurface, lotId: String? = nil, lotName: String = "", canEdit: Bool = false,
    isWorking: Bool = false, isDirty: Bool = false, canSave: Bool = false, isSaved: Bool = false,
    focusSensorId: String? = nil, columns: [ThresholdColumnPresentation] = [],
    notice: ThresholdsNoticeKind? = nil
  ) {
    self.surface = surface
    self.lotId = lotId
    self.lotName = lotName
    self.canEdit = canEdit
    self.isWorking = isWorking
    self.isDirty = isDirty
    self.canSave = canSave
    self.isSaved = isSaved
    self.focusSensorId = focusSensorId
    self.columns = columns
    self.notice = notice
  }

  /// Mirrors the flat snapshot field for field. An optional number is a decimal string that is
  /// empty when absent; an unknown notice is drawn as unexpected, a column of an unknown
  /// quantity or unit is left out, never guessed.
  public init(
    surface: String, notice: String?, noticeTryAgain: Bool, lotId: String?, lotName: String?,
    canEdit: Bool, working: Bool, dirty: Bool, canSave: Bool, saved: Bool, focusSensorId: String,
    sensorIds: [String], quantities: [String], units: [String], lows: [String], highs: [String],
    originalLows: [String], originalHighs: [String], proposedLows: [String], currents: [String],
    steps: [String], trackMins: [Double], trackMaxs: [Double], draggables: [Bool],
    alerting: [Bool], lowMustStayBelowHigh: [Bool], lowRequired: [Bool], offersProposal: [Bool]
  ) {
    let name = lotName ?? ""
    let noticeKind = notice.map { ThresholdsNoticeKind(rawValue: $0) ?? .unexpected }
    switch surface {
    case "loading":
      self.init(surface: .loading(name: name), lotId: lotId, lotName: name)
      return
    case "failed":
      self.init(
        surface: .failed(name: name, notice: noticeKind ?? .unexpected, tryAgain: noticeTryAgain),
        lotId: lotId, lotName: name)
      return
    case "ready": break
    default:
      self.init(surface: .idle)
      return
    }
    let counts: [Int] = [
      sensorIds.count, quantities.count, units.count, lows.count, highs.count, originalLows.count,
      originalHighs.count, proposedLows.count, currents.count, steps.count, trackMins.count,
      trackMaxs.count, draggables.count, alerting.count, lowMustStayBelowHigh.count,
      lowRequired.count, offersProposal.count,
    ]
    let count = counts.min() ?? 0
    let columns: [ThresholdColumnPresentation] = (0..<count).compactMap { index in
      guard let quantity = SensorQuantityKind(rawValue: quantities[index]),
        let unit = SensorUnitKind(rawValue: units[index])
      else { return nil }
      return ThresholdColumnPresentation(
        sensorId: sensorIds[index], quantity: quantity, unit: unit, low: lows[index],
        high: highs[index], originalLow: originalLows[index],
        originalHigh: originalHighs[index], proposedLow: proposedLows[index],
        current: currents[index], step: steps[index], trackMin: trackMins[index],
        trackMax: trackMaxs[index], draggable: draggables[index], alerting: alerting[index],
        lowMustStayBelowHigh: lowMustStayBelowHigh[index], lowRequired: lowRequired[index],
        offersProposal: offersProposal[index])
    }
    self.init(
      surface: .ready, lotId: lotId, lotName: name, canEdit: canEdit, isWorking: working,
      isDirty: dirty, canSave: canSave, isSaved: saved,
      focusSensorId: focusSensorId.isEmpty ? nil : focusSensorId, columns: columns,
      notice: noticeKind)
  }

  /// Whether Thresholds is on screen: anything but idle.
  public var isOpen: Bool { surface != .idle }

  /// "Thresholds for Tomatoes".
  public var title: Copy { Copy(.thresholdsTitle, .text(lotName)) }

  /// Cancel for an editor; Back where nothing can be changed (UX-DR69, UX-DR84).
  public var closeLabel: L10n { canEdit ? .modalCancel : .navBack }

  /// Save, or "Saving…" in place while it is sent. Nil where the Role cannot save: the control
  /// is hidden, not disabled.
  public var saveLabel: L10n? {
    guard canEdit else { return nil }
    return isWorking ? .thresholdsSaving : .thresholdsSave
  }

  public var isSaveEnabled: Bool { canSave && !isWorking }

  /// The line a read-only view carries instead of edit controls.
  public var readOnlyNotice: L10n? { canEdit ? nil : .thresholdsReadOnly }

  /// "Add high" shows on an alerting column without a high, for an editor.
  public func showsAddHigh(_ column: ThresholdColumnPresentation) -> Bool {
    canEdit && column.alerting && !column.hasHigh
  }

  /// "Clear high" shows where there is a high to clear.
  public func showsClearHigh(_ column: ThresholdColumnPresentation) -> Bool {
    canEdit && column.hasHigh
  }

  /// "Turn on Alerts" on a watched column (it offers the Server's proposed low), "Turn off Alerts" on an alerting one.
  public func alertsToggle(_ column: ThresholdColumnPresentation) -> L10n? {
    guard canEdit else { return nil }
    return column.alerting ? .thresholdsTurnOff : .thresholdsTurnOn
  }

  /// What the screen posts when a save did not happen; nil otherwise.
  public var saveNotice: ThresholdsNoticeKind? { notice }
}

/// Thresholds of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosThresholds` of `ColdframeCore`; the Server calls and every rule never leave the core.
@MainActor
public protocol ThresholdsService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (ThresholdsPresentation) -> Void)
  /// Opens the Thresholds of a Lot, shown under `name` until the Server answers; `sensorId` is the cell it came from (or empty).
  func open(lotId: String, name: String, sensorId: String)
  /// Cancel: nothing is sent.
  func close()
  /// Try again after a failed open.
  func retry()
  func setLowText(sensorId: String, text: String)
  func setHighText(sensorId: String, text: String)
  func setLow(sensorId: String, value: Double)
  func setHigh(sensorId: String, value: Double)
  func addHigh(sensorId: String)
  func clearHigh(sensorId: String)
  func turnOnAlerts(sensorId: String)
  func turnOffAlerts(sensorId: String)
  func save()
}
