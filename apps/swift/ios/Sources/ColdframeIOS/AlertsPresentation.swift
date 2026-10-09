import ColdframeDesignTokens
import Foundation

/// Why the Alerts could not be read. No row and no count is shown with it.
public enum AlertsNoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate

  public var message: L10n {
    switch self {
    case .unreachable: .alertsUnreachable
    case .certificate: .noticeCertificate
    }
  }

  /// The certificate notice has no action: it is never retried insecurely (AD-13).
  public var offersTryAgain: Bool { self == .unreachable }
}

/// The sections of the Alerts surface (UX-DR64), in order, as the core named them.
public enum AlertGroupKind: String, CaseIterable, Sendable {
  case threshold
  case health
  case closed

  public var title: L10n {
    switch self {
    case .threshold: .alertsGroupThreshold
    case .health: .alertsGroupHealth
    case .closed: .alertsGroupClosed
    }
  }
}

/// How an Alert row is drawn (DESIGN.md `alert-row-*`), as the core named it. Every variant
/// differs from the others in fill and border, so none is told by colour alone (UX-DR99).
public enum AlertRowVariantKind: String, CaseIterable, Sendable {
  /// Solid orange: an open low-side soil-moisture Threshold Alert, and nothing else.
  case needsWater
  /// `layer-01` with a 2 pt solid `border-strong`: every other open Threshold Alert.
  case threshold
  /// The hatch with a 1 pt dashed border and the text on a plate: an open Health Alert.
  case health
  /// An outline only, in `text-secondary`: a closed Alert.
  case closed

  /// The flat background, or nil for a transparent or hatched row.
  public var background: ThemedColor? {
    switch self {
    case .needsWater: ColorTokens.statusWaterFill
    case .threshold: ColorTokens.layer01
    case .health, .closed: nil
    }
  }

  /// Hatched rows put their text on a solid plate (UX-DR12).
  public var isHatched: Bool { self == .health }

  public var border: LotTileBorder {
    switch self {
    case .needsWater: .none
    case .threshold: LotTileBorder(style: .solid, width: 2, color: ColorTokens.borderStrong)
    case .health:
      LotTileBorder(style: .dashed, width: 1, color: ColorTokens.statusUnknownBorder)
    case .closed: LotTileBorder(style: .solid, width: 1, color: ColorTokens.borderSubtle)
    }
  }

  /// The icon, the eyebrow and the title.
  public var ink: ThemedColor {
    switch self {
    case .needsWater: ColorTokens.statusWaterInk
    case .threshold, .health: ColorTokens.textPrimary
    case .closed: ColorTokens.textSecondary
    }
  }
}

/// What a row says is wrong, as the core named it. `unknown` is a kind, side or quantity the
/// core does not know: a Health row that names only the Lot.
public enum AlertConditionKind: String, CaseIterable, Sendable {
  case needsWater
  case tooWet
  case tooLow
  case tooHigh
  case silent
  case battery
  case uncalibrated
  case unknown

  /// The title's catalogue entry; `tooLow` and `tooHigh` also take the quantity word.
  public var title: L10n {
    switch self {
    case .needsWater: .alertTitleNeedsWater
    case .tooWet: .alertTitleTooWet
    case .tooLow: .alertTitleTooLow
    case .tooHigh: .alertTitleTooHigh
    case .silent: .alertTitleSilent
    case .battery: .alertTitleBattery
    case .uncalibrated: .alertTitleUncalibrated
    case .unknown: .alertTitleUnknown
    }
  }

  var namesQuantity: Bool { self == .tooLow || self == .tooHigh }
}

/// The eyebrow over the title, as the core named it. Sentence case in the catalogue; uppercase
/// comes from the style.
public enum AlertEyebrowKind: String, CaseIterable, Sendable {
  case needsWater
  case belowLow
  case aboveHigh
  case health

  public var label: L10n {
    switch self {
    case .needsWater: .alertEyebrowNeedsWater
    case .belowLow: .alertEyebrowBelowLow
    case .aboveHigh: .alertEyebrowAboveHigh
    case .health: .alertEyebrowHealth
    }
  }
}

/// Where a tap on a row goes.
public enum AlertRowTarget: Equatable, Sendable {
  /// Lot detail of the Alert's Lot: Threshold and uncalibrated rows.
  case lot(id: String, name: String)
  /// The Devices tab: silent and battery rows, and a row the core could not read.
  case devices
}

/// One Alert row (UX-DR25, UX-DR26): an icon, an eyebrow and a title in `section` type. The core
/// decided its group, variant, condition, eyebrow, icon and target; this only words it. It shows
/// no value and no Threshold, and has no action but its tap.
public struct AlertRowPresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let group: AlertGroupKind
  public let variant: AlertRowVariantKind
  public let condition: AlertConditionKind
  public let eyebrow: AlertEyebrowKind
  public let icon: CarbonIcon
  /// The quantity as the contract names it (`soil_moisture`, …); empty when the core does not
  /// know it.
  public let quantity: String
  public let lotId: String
  public let lotName: String
  public let deviceId: String
  public let openedAt: Date
  /// Nil while the Alert is open.
  public let closedAt: Date?
  public let target: AlertRowTarget

  public init(
    id: String, group: AlertGroupKind, variant: AlertRowVariantKind,
    condition: AlertConditionKind, eyebrow: AlertEyebrowKind, icon: CarbonIcon, quantity: String,
    lotId: String, lotName: String, deviceId: String, openedAt: Date, closedAt: Date?,
    target: AlertRowTarget
  ) {
    self.id = id
    self.group = group
    self.variant = variant
    self.condition = condition
    self.eyebrow = eyebrow
    self.icon = icon
    self.quantity = quantity
    self.lotId = lotId
    self.lotName = lotName
    self.deviceId = deviceId
    self.openedAt = openedAt
    self.closedAt = closedAt
    self.target = target
  }

  public var isOpen: Bool { group != .closed }

  /// The quantity as a word inside a title: "temperature".
  private var quantityWord: L10n? {
    switch quantity {
    case "soil_moisture": .alertQuantitySoilMoisture
    case "air_temperature": .alertQuantityAirTemperature
    case "relative_humidity": .alertQuantityRelativeHumidity
    case "gas_resistance": .alertQuantityGasResistance
    default: nil
    }
  }

  /// "Tomatoes needs water", "Herbs too wet", "Beans temperature too low".
  public func titleText(_ context: CopyContext) -> String {
    guard condition.namesQuantity else { return context.text(condition.title, .text(lotName)) }
    // The core names a quantity with these conditions; without its word only the Lot is named.
    guard let quantityWord else { return context.text(.alertTitleUnknown, .text(lotName)) }
    return context.text(condition.title, .text(lotName), .text(context.text(quantityWord)))
  }

  /// "Needs water · 05:45", "Threshold Alert · above high · 05:45", a bare "Health Alert", and
  /// on a closed row "… · closed 06:40". Sentence case; the style makes it uppercase.
  public func eyebrowText(_ context: CopyContext) -> String {
    let kind = context.text(eyebrow.label)
    if let closedAt {
      return context.text(.alertEyebrowClosed, .text(kind), .text(context.when(closedAt)))
    }
    // An open Health Alert only names itself.
    if eyebrow == .health { return kind }
    return context.text(.alertEyebrowOpen, .text(kind), .text(context.when(openedAt)))
  }

  /// The one label of the row for VoiceOver (UX-DR98): the condition and when it started,
  /// "Tomatoes needs water, since 05:45"; a closed row adds "closed 06:40".
  public func spokenText(_ context: CopyContext) -> String {
    let title = titleText(context)
    let since = context.when(openedAt)
    if let closedAt {
      return context.text(
        .alertSpokenClosed, .text(title), .text(since), .text(context.when(closedAt)))
    }
    return context.text(.alertSpokenOpen, .text(title), .text(since))
  }

  /// The whole row is one button.
  public var traits: Set<ControlTrait> { [.button] }
}

/// One section of the Alerts surface with its rows, in the Server's order.
public struct AlertGroupPresentation: Equatable, Sendable, Identifiable {
  public let kind: AlertGroupKind
  public let rows: [AlertRowPresentation]

  public var id: AlertGroupKind { kind }
}

/// What the Alerts tab shows.
public enum AlertsSurface: Equatable, Sendable {
  /// Signed out, no current Site, or the Alerts are being read for the first time: no rows.
  case waiting
  /// The Alerts could not be read: the notice, and no rows.
  case failed(AlertsNoticeKind)
  /// The Alerts of the current Site were read.
  case ready
}

/// The Swift mirror of the core's `AlertsState`, built from the flattened `AlertsSnapshot`.
public struct AlertsPresentation: Equatable, Sendable {
  public let surface: AlertsSurface
  /// The current Site's name, for the Lot detail a row opens.
  public let siteName: String?
  /// Every open Alert of the Site as the Server counted them, for the tab label; 0 unless ready,
  /// so a failed load shows no count.
  public let openCount: Int
  /// A pull-to-refresh is under way; the rows stay.
  public let refreshing: Bool
  /// The Alerts in the Server's order: open newest first, then closed newest first. Empty unless
  /// ready.
  public let rows: [AlertRowPresentation]

  public init(
    surface: AlertsSurface, siteName: String? = nil, openCount: Int = 0, refreshing: Bool = false,
    rows: [AlertRowPresentation] = []
  ) {
    self.surface = surface
    self.siteName = siteName
    if surface == .ready {
      self.openCount = max(0, openCount)
      self.refreshing = refreshing
      self.rows = rows
    } else {
      self.openCount = 0
      self.refreshing = false
      self.rows = []
    }
  }

  public static let waiting = AlertsPresentation(surface: .waiting)

  /// Mirrors the flat snapshot: one entry per Alert in the Server's order, every list named as
  /// the core names it. `openedAts` and `closedAts` hold Unix milliseconds in decimal,
  /// `closedAts` an empty string for an open Alert. `surface` is `idle`, `loading`, `failed` or
  /// `ready`; anything else waits. A name this client does not know is read the careful way: a
  /// Health row (Closed when it has a closed time) with the `help` icon that opens Devices.
  public init(
    surface: String, notice: String?, siteId: String?, siteName: String?, openCount: Int,
    refreshing: Bool, ids: [String], groups: [String], variants: [String], conditions: [String],
    eyebrows: [String], icons: [String], quantities: [String], lotIds: [String],
    lotNames: [String], deviceIds: [String], openedAts: [String], closedAts: [String],
    targets: [String]
  ) {
    switch surface {
    case "failed":
      self.init(
        surface: .failed(notice.flatMap(AlertsNoticeKind.init(rawValue:)) ?? .unreachable),
        siteName: siteName)
    case "ready":
      guard siteId != nil else {
        self.init(surface: .waiting)
        return
      }
      let count =
        [
          ids.count, groups.count, variants.count, conditions.count, eyebrows.count, icons.count,
          quantities.count, lotIds.count, lotNames.count, deviceIds.count, openedAts.count,
          closedAts.count, targets.count,
        ].min() ?? 0
      let rows = (0..<count).compactMap { index -> AlertRowPresentation? in
        // A row without the time it opened cannot say when it started.
        guard let openedAt = Self.date(openedAts[index]) else { return nil }
        let closedAt = Self.date(closedAts[index])
        return AlertRowPresentation(
          id: ids[index],
          group: AlertGroupKind(rawValue: groups[index]) ?? (closedAt == nil ? .health : .closed),
          variant: AlertRowVariantKind(rawValue: variants[index])
            ?? (closedAt == nil ? .health : .closed),
          condition: AlertConditionKind(rawValue: conditions[index]) ?? .unknown,
          eyebrow: AlertEyebrowKind(rawValue: eyebrows[index]) ?? .health,
          icon: CarbonIcon(rawValue: icons[index]) ?? .help, quantity: quantities[index],
          lotId: lotIds[index], lotName: lotNames[index], deviceId: deviceIds[index],
          openedAt: openedAt, closedAt: closedAt,
          target: targets[index] == "lot"
            ? .lot(id: lotIds[index], name: lotNames[index]) : .devices)
      }
      self.init(
        surface: .ready, siteName: siteName, openCount: openCount, refreshing: refreshing,
        rows: rows)
    default:
      self.init(surface: .waiting)
    }
  }

  private static func date(_ milliseconds: String) -> Date? {
    Int64(milliseconds).map { Date(timeIntervalSince1970: Double($0) / 1000) }
  }

  /// Threshold, then Health, then Closed; a section without rows is left out. The Server's order
  /// is kept inside each: nothing here sorts.
  public var groups: [AlertGroupPresentation] {
    AlertGroupKind.allCases.compactMap { kind in
      let inGroup = rows.filter { $0.group == kind }
      return inGroup.isEmpty ? nil : AlertGroupPresentation(kind: kind, rows: inGroup)
    }
  }

  /// The Alerts were read and none is open: "No open Alerts.", followed by Closed when it has
  /// rows.
  public var showsEmpty: Bool {
    surface == .ready && !rows.contains { $0.isOpen }
  }

  /// The failure that replaces the rows.
  public var failure: AlertsNoticeKind? {
    if case .failed(let notice) = surface { return notice }
    return nil
  }
}

/// The Alerts half of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosAlerts` of `ColdframeCore`; no token or URL crosses this boundary. The core follows the
/// current Site of the Sites half and reads its Alerts when a Site becomes current.
@MainActor
public protocol AlertsService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (AlertsPresentation) -> Void)
  /// Reads the Alerts again: on every entry of the Alerts tab, and for Try again.
  func load()
  /// Pull-to-refresh, and every time the app comes to the front, so the tab count is current on
  /// any tab. Shown rows stay meanwhile.
  func refresh()
}
