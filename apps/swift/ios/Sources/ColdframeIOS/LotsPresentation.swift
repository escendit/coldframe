import ColdframeDesignTokens
import Foundation

/// A Lot status as the Server computed it (AD-14); the client never computes or re-sorts it.
public enum LotStatusKind: String, CaseIterable, Sendable {
  case needsWater
  case needsCalibration
  case unknown
  case ok
  case paused
  case noNode
}

/// How a tile is filled, with colour removed (DESIGN.md Shapes).
public enum LotTileFillKind: String, CaseIterable, Sendable {
  /// Transparent: no Node, stale.
  case none
  /// Solid water fill with the level and its edge: needs water.
  case water
  /// Soil fill with the level and its edge: OK.
  case soil
  /// The hatch, with the text on a solid plate: unknown, needs calibration.
  case hatch
  /// Flat paused fill.
  case paused
}

public enum LotTileBorderStyle: String, CaseIterable, Sendable {
  case none
  case solid
  case dashed
  case dotted
}

/// A tile's outline: its style, its width in points and its colour token.
public struct LotTileBorder: Equatable, Sendable {
  public let style: LotTileBorderStyle
  public let width: Double
  public let color: ThemedColor?

  public static let none = LotTileBorder(style: .none, width: 0, color: nil)
}

/// How a tile is drawn (UX-DR18, UX-DR19), as the core named it: one of the six statuses, or
/// stale whatever the status was. Every variant differs from the others in shape and icon, so a
/// status is never told by colour alone (UX-DR99).
public enum LotTileVariantKind: String, CaseIterable, Sendable {
  case needsWater
  case ok
  case unknown
  case needsCalibration
  case paused
  case noNode
  case stale

  /// The Carbon icon beside the status label.
  public var icon: CarbonIcon {
    switch self {
    case .needsWater: .rainDrop
    case .ok: .checkmarkOutline
    case .unknown: .help
    case .needsCalibration: .tools
    case .paused: .pauseOutline
    case .noNode: .add
    case .stale: .cloudOffline
    }
  }

  public var fill: LotTileFillKind {
    switch self {
    case .needsWater: .water
    case .ok: .soil
    case .unknown, .needsCalibration: .hatch
    case .paused: .paused
    case .noNode, .stale: .none
    }
  }

  public var border: LotTileBorder {
    switch self {
    case .needsWater: .none
    case .ok: LotTileBorder(style: .solid, width: 1, color: ColorTokens.statusOkBorder)
    case .unknown: LotTileBorder(style: .dashed, width: 1, color: ColorTokens.statusUnknownBorder)
    case .needsCalibration:
      LotTileBorder(style: .dashed, width: 2, color: ColorTokens.statusCalibrationBorder)
    case .paused: LotTileBorder(style: .solid, width: 2, color: ColorTokens.statusPausedBorder)
    case .noNode: LotTileBorder(style: .dotted, width: 1, color: ColorTokens.statusNoNodeBorder)
    case .stale: LotTileBorder(style: .solid, width: 1, color: ColorTokens.staleBorder)
    }
  }

  /// Hatched tiles put their name, label, value and foot on a solid plate (UX-DR12, UX-DR17).
  public var isHatched: Bool { fill == .hatch }

  /// The soil level, its 2 pt edge and the low Threshold tick are drawn only on these.
  public var showsLevel: Bool { fill == .water || fill == .soil }

  /// The flat background, or nil for a transparent or hatched tile.
  public var background: ThemedColor? {
    switch self {
    case .needsWater: ColorTokens.statusWaterFill
    case .ok: ColorTokens.statusOkFill
    case .paused: ColorTokens.statusPausedFill
    case .unknown, .needsCalibration, .noNode, .stale: nil
    }
  }

  /// The part of the tile below the Reading's percentage.
  public var level: ThemedColor? {
    switch self {
    case .needsWater: ColorTokens.statusWaterLevel
    case .ok: ColorTokens.statusOkLevel
    default: nil
    }
  }

  /// The 2 pt edge on top of the level, which carries the contrast.
  public var levelEdge: ThemedColor? {
    switch self {
    case .needsWater: ColorTokens.statusWaterInk
    case .ok: ColorTokens.statusLevelEdge
    default: nil
    }
  }

  /// The 12 pt tick at the low Threshold.
  public var lowMarker: ThemedColor? {
    switch self {
    case .needsWater: ColorTokens.statusWaterInk
    case .ok: ColorTokens.statusLowMarker
    default: nil
    }
  }

  /// The name and the big value.
  public var ink: ThemedColor {
    switch self {
    case .needsWater: ColorTokens.statusWaterInk
    case .ok, .unknown, .needsCalibration: ColorTokens.textPrimary
    case .paused: ColorTokens.statusPausedInk
    case .noNode: ColorTokens.statusNoNodeInk
    case .stale: ColorTokens.textSecondary
    }
  }

  /// The icon and the status label.
  public var labelInk: ThemedColor {
    self == .needsCalibration ? ColorTokens.statusCalibrationInk : ink
  }

  /// The foot line; a stale tile's "as of" is in `stale-ink`.
  public var footInk: ThemedColor {
    switch self {
    case .ok, .unknown, .needsCalibration: ColorTokens.textSecondary
    case .stale: ColorTokens.staleInk
    case .needsWater, .paused, .noNode: ink
    }
  }
}

/// The status label under the Lot name, as the core named it from `unknownCause` and `pausedBy`.
/// Sentence case in the catalogue; uppercase comes from the style.
public enum LotTileLabelKind: String, CaseIterable, Sendable {
  case needsWater
  case ok
  case silent
  case hubSilent
  case needsCalibration
  case paused
  case pausedBySite
  case noNode

  public var live: L10n {
    switch self {
    case .needsWater: .lotTileLabelNeedsWater
    case .ok: .lotTileLabelOk
    case .silent: .lotTileLabelUnknownNode
    case .hubSilent: .lotTileLabelUnknownHub
    case .needsCalibration: .lotTileLabelNeedsCalibration
    case .paused: .lotTileLabelPaused
    case .pausedBySite: .lotTileLabelPausedBySite
    case .noNode: .lotTileNoNode
    }
  }

  /// "Was ‹status label›" on a stale tile.
  public var was: L10n {
    switch self {
    case .needsWater: .lotTileWasNeedsWater
    case .ok: .lotTileWasOk
    case .silent: .lotTileWasUnknownNode
    case .hubSilent: .lotTileWasUnknownHub
    case .needsCalibration: .lotTileWasNeedsCalibration
    case .paused: .lotTileWasPaused
    case .pausedBySite: .lotTileWasPausedBySite
    case .noNode: .lotTileWasNoNode
    }
  }
}

/// The big value of a tile, as the core named it.
public enum LotTileValueKind: String, CaseIterable, Sendable {
  case soil
  case duration
  case raw
  case dash
  case plus
  case none
}

/// The foot line of a tile, as the core named it.
public enum LotTileFootKind: String, CaseIterable, Sendable {
  case reading
  case wasPercentAt
  case lastReading
  case noReadingsYet
  case noPercentUntilCalibrated
  case pausedUntil
  case paused
  case addNode
  case asOf
  case none
}

/// Which spoken label a tile gets, as the core named it.
public enum LotTileSpokenKind: String, CaseIterable, Sendable {
  case needsWater
  case ok
  case nodeSilent
  case hubSilent
  case needsCalibration
  case pausedUntil
  case pausedWithSite
  case paused
  case noNode
  case stale
}

/// A duration the core measured: "6 h" on the tile, "6 hours" when spoken.
public struct LotDurationPresentation: Equatable, Sendable {
  public enum Unit: String, CaseIterable, Sendable {
    case minutes
    case hours
    case days
  }

  public let value: Int
  public let unit: Unit

  public init(value: Int, unit: Unit) {
    self.value = value
    self.unit = unit
  }

  /// "6 h".
  public var short: Copy {
    switch unit {
    case .minutes: Copy(.durationMinutes, .number(value))
    case .hours: Copy(.durationHours, .number(value))
    case .days: Copy(.durationDays, .number(value))
    }
  }

  /// "6 hours", by the catalogue's plural rules.
  public var spoken: Copy {
    switch unit {
    case .minutes: Copy(.durationSpokenMinutes, .number(value))
    case .hours: Copy(.durationSpokenHours, .number(value))
    case .days: Copy(.durationSpokenDays, .number(value))
    }
  }
}

/// One Lot tile on the Site overview (UX-DR17 to UX-DR20, UX-DR77). The core decided the
/// variant, the label, which value and foot it shows, which spoken label it gets and every
/// number; this maps them to the catalogue and to tokens. Nothing here computes a status, an
/// order or a duration. A part the core left out is not shown.
public struct LotTilePresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  /// The Server's status (AD-14); a stale tile says what it was.
  public let status: LotStatusKind
  public let variant: LotTileVariantKind
  public let label: LotTileLabelKind
  public let value: LotTileValueKind
  public let foot: LotTileFootKind
  public let spoken: LotTileSpokenKind
  /// Soil moisture, already rounded to 5 by the core.
  public let soilPercent: Int?
  /// The low Threshold.
  public let lowPercent: Int?
  public let readingAt: Date?
  /// How long the Node or Hub has been silent, as the core measured it.
  public let duration: LotDurationPresentation?
  public let pausedBySite: Bool
  public let pausedUntil: Date?
  /// When the shown data was read from the Server; set on a stale tile.
  public let asOf: Date?
  /// The one tap target a tile has: a live *no Node* tile starts Add a Node, for
  /// Administrators and Owners, as the core decided.
  public let opensAddNode: Bool
  /// A live *needs calibration* tile has a Calibrate control beside it, for Administrators and
  /// Owners, as the core decided. The tile itself still opens Lot detail.
  public let opensCalibrate: Bool

  public init(
    id: String, name: String, status: LotStatusKind, variant: LotTileVariantKind,
    label: LotTileLabelKind, value: LotTileValueKind = .none, foot: LotTileFootKind = .none,
    spoken: LotTileSpokenKind, soilPercent: Int? = nil, lowPercent: Int? = nil,
    readingAt: Date? = nil, duration: LotDurationPresentation? = nil, pausedBySite: Bool = false,
    pausedUntil: Date? = nil, asOf: Date? = nil, opensAddNode: Bool = false,
    opensCalibrate: Bool = false
  ) {
    self.id = id
    self.name = name
    self.status = status
    self.variant = variant
    self.label = label
    self.value = value
    self.foot = foot
    self.spoken = spoken
    self.soilPercent = soilPercent
    self.lowPercent = lowPercent
    self.readingAt = readingAt
    self.duration = duration
    self.pausedBySite = pausedBySite
    self.pausedUntil = pausedUntil
    self.asOf = asOf
    self.opensAddNode = opensAddNode
    self.opensCalibrate = opensCalibrate
  }

  public var isStale: Bool { variant == .stale }

  public var icon: CarbonIcon { variant.icon }

  /// The status label under the name: "Needs water", or "Was needs water" on a stale tile.
  public var statusLabel: L10n { isStale ? label.was : label.live }

  /// How high the soil level stands, in percent of the tile; only where the variant draws one.
  public var level: Int? { variant.showsLevel ? soilPercent.map { min(100, max(0, $0)) } : nil }

  /// How high the low Threshold tick sits, in percent of the tile.
  public var lowMarker: Int? {
    variant.showsLevel ? lowPercent.map { min(100, max(0, $0)) } : nil
  }

  /// The big value: "~20", "6 h", "raw", "—", "+", or nothing: a stale tile, and an OK tile
  /// whose percentage the Server did not send.
  public func valueText(_ context: CopyContext) -> String? {
    switch value {
    case .soil:
      return soilPercent.map {
        context.text(.soilApprox, .text(Formats.number($0, locale: context.locale)))
      }
    case .duration: return duration.map { context.resolve($0.short) }
    case .raw: return context.text(.lotTileValueRaw)
    case .dash: return context.text(.lotTileValuePaused)
    case .plus: return context.text(.lotTileNoNodeValue)
    case .none: return nil
    }
  }

  /// The foot line, from the parts that are present.
  public func footText(_ context: CopyContext) -> String? {
    switch foot {
    case .reading:
      return context.joined(
        .listDot,
        [
          readingAt.map(context.when),
          lowPercent.map { context.text(.lotTileFootLow, .text(context.percent($0))) },
        ])
    case .wasPercentAt:
      guard let soilPercent, let readingAt else { return lastReadingFoot(context) }
      return context.text(
        .lotTileFootWas, .text(context.text(.soilApprox, .text(context.percent(soilPercent)))),
        .text(context.when(readingAt)))
    case .lastReading: return lastReadingFoot(context)
    case .noReadingsYet: return context.text(.lotTileFootNoReadings)
    case .noPercentUntilCalibrated: return context.text(.lotTileFootUncalibrated)
    case .pausedUntil:
      return pausedUntil.map { context.text(.lotTileFootUntil, .text(context.day($0))) }
        ?? context.text(.lotTileFootPaused)
    case .paused: return context.text(.lotTileFootPaused)
    case .addNode: return context.text(.lotTileAddNode)
    case .asOf: return asOf.map { context.text(.lotTileAsOf, .text(context.when($0))) }
    case .none: return nil
    }
  }

  private func lastReadingFoot(_ context: CopyContext) -> String {
    readingAt.map { context.text(.lotTileFootLastReading, .text(context.when($0))) }
      ?? context.text(.lotTileFootNoReadings)
  }

  /// The one label VoiceOver speaks for the whole tile (UX-DR98): the Lot, its status and the
  /// parts that are present, as in the State Patterns table. A stale tile says what the status
  /// was, "not live" and as of when.
  public func spokenText(_ context: CopyContext) -> String {
    if spoken == .noNode { return context.text(.lotTileDescriptionNoNode, .text(name)) }
    return context.joined(.listComma, [name] + spokenParts(context)) ?? name
  }

  private func spokenParts(_ context: CopyContext) -> [String?] {
    switch spoken {
    case .needsWater:
      return [
        context.text(.lotTileSpokenNeedsWater), spokenPercent(context), spokenLow(context),
        readingAt.map { context.text(.lotTileSpokenReading, .text(context.when($0))) },
      ]
    case .ok:
      // An OK Lot is not read with its Reading time.
      return [context.text(.lotTileSpokenOk), spokenPercent(context), spokenLow(context)]
    case .nodeSilent, .hubSilent:
      let silent: L10n = spoken == .hubSilent ? .lotTileSpokenHubSilent : .lotTileSpokenNodeSilent
      return [
        context.text(.lotTileSpokenUnknown),
        duration.map { context.text(silent, .text(context.resolve($0.spoken))) },
        spokenLast(context),
      ]
    case .needsCalibration:
      return [
        context.text(.lotTileSpokenNeedsCalibration), context.text(.lotTileSpokenUncalibrated),
      ]
    case .pausedUntil:
      return [
        pausedUntil.map {
          context.text(.lotTileSpokenPausedUntil, .text(context.day($0, long: true)))
        } ?? context.text(.lotTileSpokenPaused)
      ]
    case .pausedWithSite: return [context.text(.lotTileSpokenPausedSite)]
    case .paused: return [context.text(.lotTileSpokenPaused)]
    case .noNode: return []
    case .stale:
      return [
        context.text(status.spokenWas), context.text(.lotTileSpokenNotLive),
        asOf.map { context.text(.lotTileAsOf, .text(context.when($0))) },
      ]
    }
  }

  private func spokenPercent(_ context: CopyContext) -> String? {
    soilPercent.map { context.text(.lotTileSpokenPercent, .number($0)) }
  }

  private func spokenLow(_ context: CopyContext) -> String? {
    lowPercent.map { context.text(.lotTileSpokenLow, .number($0)) }
  }

  /// What an unknown Lot last said, following its foot line.
  private func spokenLast(_ context: CopyContext) -> String? {
    switch foot {
    case .noReadingsYet: return context.text(.lotTileSpokenNoReadings)
    case .wasPercentAt, .lastReading:
      guard let readingAt else { return context.text(.lotTileSpokenNoReadings) }
      let time = context.when(readingAt)
      if foot == .wasPercentAt, let soilPercent {
        return context.text(.lotTileSpokenLastPercent, .number(soilPercent), .text(time))
      }
      return context.text(.lotTileSpokenLastReading, .text(time))
    default: return nil
    }
  }

  /// Every tile is a button (UX-DR63): it opens Lot detail, except a live *no Node* tile for
  /// Administrators and Owners, which keeps starting Add a Node, as the core decided.
  public var isTappable: Bool { true }

  /// Opens Lot detail: every tile that does not start Add a Node.
  public var opensLotDetail: Bool { !opensAddNode }

  public var traits: Set<ControlTrait> { [.button] }

  /// Two columns, one from Accessibility 1 (UX-DR97, UX-DR107).
  public static func columns(accessibilitySize: Bool) -> Int { accessibilitySize ? 1 : 2 }

  /// In one column the value sits directly under the status label and the tile grows downward;
  /// in two it sits at the bottom of the square (UX-DR17, UX-DR97).
  public static func valueFollowsLabel(columns: Int) -> Bool { columns <= 1 }

  /// Skeleton tiles shown while the Lots of a Site are read for the first time (UX-DR19,
  /// UX-DR80): outlines only, with no icon, label or value, hidden from VoiceOver.
  public static let skeletonCount = 4
}

extension LotStatusKind {
  /// "was needs water", the status of a stale tile when spoken.
  public var spokenWas: L10n {
    switch self {
    case .needsWater: .lotTileSpokenWasNeedsWater
    case .ok: .lotTileSpokenWasOk
    case .unknown: .lotTileSpokenWasUnknown
    case .needsCalibration: .lotTileSpokenWasNeedsCalibration
    case .paused: .lotTileSpokenWasPaused
    case .noNode: .lotTileSpokenWasNoNode
    }
  }

  /// "2 unknown", one part of the counts subline; the headline says how many need water.
  public func countCopy(_ count: Int) -> Copy? {
    switch self {
    case .needsWater: nil
    case .needsCalibration: Copy(.gardenCountNeedsCalibration, .number(count))
    case .unknown: Copy(.gardenCountUnknown, .number(count))
    case .ok: Copy(.gardenCountOk, .number(count))
    case .paused: Copy(.gardenCountPaused, .number(count))
    case .noNode: Copy(.gardenCountNoNode, .number(count))
    }
  }
}

/// Which headline sentence the Site gets (UX-DR129), as the core chose it.
public enum SiteHeadlineKind: String, CaseIterable, Sendable {
  case noReadings
  case needsWater
  case cantBeRead
  case paused
  case nothingNeedsWater
}

/// One part of the counts subline.
public struct LotCountPresentation: Equatable, Sendable {
  public let status: LotStatusKind
  public let count: Int

  public init(status: LotStatusKind, count: Int) {
    self.status = status
    self.count = count
  }
}

/// The Site summary (UX-DR21, UX-DR129): the headline sentence, exposed as a heading, and the
/// counts subline. The core chose the sentence and counted; this writes them.
public struct SiteSummaryPresentation: Equatable, Sendable {
  public let kind: SiteHeadlineKind
  /// How many Lots the sentence is about (needs water, can't be read).
  public let count: Int
  /// The one Lot that needs water.
  public let lotName: String?
  /// The end every Site-paused Lot shares.
  public let pausedUntil: Date?
  /// A paused Site's headline is in `status-paused-ink`.
  public let pausedInk: Bool
  /// The non-zero counts in the Server's order, without needs water.
  public let counts: [LotCountPresentation]

  public init(
    kind: SiteHeadlineKind, count: Int = 0, lotName: String? = nil, pausedUntil: Date? = nil,
    pausedInk: Bool = false, counts: [LotCountPresentation] = []
  ) {
    self.kind = kind
    self.count = count
    self.lotName = lotName
    self.pausedUntil = pausedUntil
    self.pausedInk = pausedInk
    self.counts = counts
  }

  /// No Lot has a Node, or the Lots are not known: never "Nothing needs water".
  public static let noReadings = SiteSummaryPresentation(kind: .noReadings)

  public var headlineInk: ThemedColor {
    pausedInk ? ColorTokens.statusPausedInk : ColorTokens.textPrimary
  }

  public func headline(_ context: CopyContext) -> String {
    switch kind {
    case .noReadings: return context.text(.gardenNoReadings)
    case .needsWater:
      if let lotName { return context.text(.gardenHeadlineLotNeedsWater, .text(lotName)) }
      return context.text(.countLotsNeedWater, .number(count))
    case .cantBeRead: return context.text(.gardenHeadlineCantRead, .number(count))
    case .paused:
      return pausedUntil.map { context.text(.gardenHeadlinePausedUntil, .text(context.day($0))) }
        ?? context.text(.gardenHeadlinePaused)
    case .nothingNeedsWater: return context.text(.gardenHeadlineNothing)
    }
  }

  /// "2 unknown · 2 OK · 1 paused · 1 without Node"; the first-run detail line under "No
  /// Readings yet"; nothing when there is nothing to count.
  public func subline(_ context: CopyContext) -> String? {
    if kind == .noReadings { return context.text(.gardenNoReadingsDetail) }
    return context.joined(
      .listDot, counts.map { $0.status.countCopy($0.count).map(context.resolve) })
  }
}

/// The age of the shown data, as the core measured it for this minute.
public struct StaleAgePresentation: Equatable, Sendable {
  public let days: Int
  public let hours: Int
  public let minutes: Int

  public init(days: Int, hours: Int, minutes: Int) {
    self.days = days
    self.hours = hours
    self.minutes = minutes
  }

  /// "12 min" under 1 h, "2 h 12 min" under 24 h, "3 d" from 24 h (UX-DR127).
  public var copy: Copy {
    if days > 0 { return Copy(.durationDays, .number(days)) }
    if hours > 0 {
      return minutes > 0
        ? Copy(.durationHoursMinutes, .number(hours), .number(minutes))
        : Copy(.durationHours, .number(hours))
    }
    return Copy(.durationMinutes, .number(minutes))
  }
}

/// The stale header (UX-DR24), which replaces the summary while the Server can't be reached:
/// `cloud--offline`, "‹Site› · can't reach your Server", the age in `headline` and `stale-ink`,
/// and one line that says when the data is from. The age moves with the minute tick and is
/// never announced.
public struct StaleHeaderPresentation: Equatable, Sendable {
  public let siteName: String
  /// The last successful refresh: "Last data ‹t›".
  public let fetchedAt: Date
  public let age: StaleAgePresentation

  public init(siteName: String, fetchedAt: Date, age: StaleAgePresentation) {
    self.siteName = siteName
    self.fetchedAt = fetchedAt
    self.age = age
  }

  public let icon: CarbonIcon = .cloudOffline
  public let ageInk: ThemedColor = ColorTokens.staleInk
  /// A tick of the age is a text change only.
  public let announcesAge = false

  public func title(_ context: CopyContext) -> String {
    context.text(.staleTitle, .text(siteName))
  }

  /// "2 h 12 min old".
  public func ageText(_ context: CopyContext) -> String {
    context.text(.staleAge, .text(context.resolve(age.copy)))
  }

  public func detail(_ context: CopyContext) -> String {
    context.text(.staleDetail, .text(context.when(fetchedAt)))
  }
}

/// The Site overview as the core built it for one `now`: live or stale, the summary and the
/// Site menu. Nil fields the core did not send are left out.
public struct SiteOverviewPresentation: Equatable, Sendable {
  /// Stale mode: the Server could not be reached, or the Lots are the ones kept on this phone
  /// until the first refresh lands (UX-DR79, UX-DR80).
  public let stale: Bool
  /// A read is on its way; the shown Lots stay.
  public let refreshing: Bool
  /// The last successful refresh.
  public let fetchedAt: Date?
  public let staleAge: StaleAgePresentation
  public let summary: SiteSummaryPresentation
  public let menuItems: [SiteMenuItem]
  /// False in stale mode: every item then shows "Needs your Server".
  public let menuEnabled: Bool

  public init(
    stale: Bool, refreshing: Bool = false, fetchedAt: Date? = nil,
    staleAge: StaleAgePresentation = StaleAgePresentation(days: 0, hours: 0, minutes: 0),
    summary: SiteSummaryPresentation, menuItems: [SiteMenuItem], menuEnabled: Bool
  ) {
    self.stale = stale
    self.refreshing = refreshing
    self.fetchedAt = fetchedAt
    self.staleAge = staleAge
    self.summary = summary
    self.menuItems = menuItems
    self.menuEnabled = menuEnabled
  }
}

/// Stale mode was entered or left (UX-DR106), as the core reported it: one polite announcement
/// each. A first load, an unchanged refresh and the minute tick report nothing.
public struct LotsEventPresentation: Equatable, Sendable {
  public enum Kind: String, CaseIterable, Sendable {
    case enteredStale
    case leftStale
  }

  public let kind: Kind
  public let siteId: String
  /// The last successful refresh, when stale mode is entered.
  public let fetchedAt: Date?

  /// Nil for an event this client does not know: nothing is announced.
  public init?(kind: String, siteId: String, fetchedAtEpochMs: Int64) {
    guard let kind = Kind(rawValue: kind) else { return nil }
    self.kind = kind
    self.siteId = siteId
    self.fetchedAt =
      kind == .enteredStale && fetchedAtEpochMs > 0
      ? Date(timeIntervalSince1970: Double(fetchedAtEpochMs) / 1000) : nil
  }

  public let announcement: Announcement = .polite

  /// "Can't reach your Server. Showing data from 07:02." or "Live again."
  /// Nil when the time is missing: the sentence cannot be said without it.
  public func text(_ context: CopyContext) -> String? {
    switch kind {
    case .enteredStale:
      return fetchedAt.map { context.text(.staleEntered, .text(context.when($0))) }
    case .leftStale: return context.text(.staleLeft)
    }
  }
}

/// The result of a Site settings action that did not change anything, named as the core names it.
public enum LotsActionNoticeKind: String, CaseIterable, Sendable {
  case forbidden
  case lotClaimed
  case lotNotFound
  case renameSiteUnavailable
  case keyReused
  case reminderCadenceNotSaved
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .forbidden: .siteSettingsForbidden
    case .lotClaimed: .siteSettingsLotClaimed
    case .lotNotFound: .siteSettingsLotNotFound
    case .renameSiteUnavailable: .siteSettingsRenameSiteUnavailable
    case .keyReused: .siteSettingsKeyReused
    case .reminderCadenceNotSaved: .siteSettingsReminderCadenceNotSaved
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .siteSettingsUnexpected
    }
  }

  /// Forbidden names the Site and Lot claimed names the Lot (`%@`).
  public var takesSubject: Bool { self == .forbidden || self == .lotClaimed }

  public var announcement: Announcement { .assertive }
}

/// A name the client refused before sending it.
extension NameErrorKind {
  /// The reason under the Lot name field; too long shares the Site copy.
  public var lotMessage: L10n {
    switch self {
    case .blank: .siteSettingsLotNameBlank
    case .tooLong: .createSiteNameTooLong
    }
  }
}

/// One Lot row of Site settings.
public struct LotRowPresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String

  public init(id: String, name: String) {
    self.id = id
    self.name = name
  }
}

/// Rename Lot, shown in an alert with a field while the core has a Lot to rename.
public struct LotRenamePresentation: Equatable, Sendable {
  public let lotId: String
  public let draft: String
  public let error: NameErrorKind?
  public let working: Bool

  public init(lotId: String, draft: String, error: NameErrorKind?, working: Bool) {
    self.lotId = lotId
    self.draft = draft
    self.error = error
    self.working = working
  }

  public var buttonLabel: L10n { working ? .siteSettingsRenamingLot : .siteSettingsRenameLot }
  public var isButtonEnabled: Bool { !working }
}

/// The destructive confirmation naming the Lot (UX-DR74): "Remove Lot {lot}?".
public struct LotRemovalPresentation: Equatable, Sendable {
  public let lotId: String
  public let lotName: String
  public let working: Bool

  public init(lotId: String, lotName: String, working: Bool) {
    self.lotId = lotId
    self.lotName = lotName
    self.working = working
  }

  public let title: L10n = .siteSettingsRemoveLotQuestion
  public let message: L10n = .siteSettingsRemoveLotDetail
  public var confirm: L10n { working ? .siteSettingsRemovingLot : .siteSettingsRemoveLot }
  public let cancel: L10n = .siteSettingsCancel
}

/// The Reminders section of Site settings (UX-DR50): the Site's Reminder cadence, "Daily" or
/// "Every 2 days" and never "Never". An Owner or Administrator picks it and it applies at once;
/// a Member reads it as text (UX-DR84).
public struct SiteRemindersPresentation: Equatable, Sendable {
  /// The Site's cadence, already naming a pick on its way.
  public let cadence: ReminderCadenceKind
  public let canEdit: Bool
  public let working: Bool

  public init(cadence: ReminderCadenceKind, canEdit: Bool, working: Bool = false) {
    self.cadence = cadence
    self.canEdit = canEdit
    self.working = working
  }

  /// "Daily" / "Every 2 days".
  public var segments: [SegmentPresentation<ReminderCadenceKind>] {
    ReminderCadenceKind.allCases.map {
      SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == cadence)
    }
  }
}

/// Site settings as one surface (UX-DR74): the Site name, renamed only by an Owner, the Lots,
/// created, renamed and removed by Owners and Administrators, and the Reminders, whose cadence
/// Owners and Administrators pick. A Member sees all three read-only with one notice; controls a
/// Role cannot use are hidden, not disabled (UX-DR84).
public struct SiteSettingsPresentation: Equatable, Sendable {
  public let siteName: String
  public let role: SiteRoleKind
  public let canRenameSite: Bool
  public let canEditLots: Bool
  public let showsReadOnlyNotice: Bool
  public let siteNameDraft: String
  public let siteNameError: NameErrorKind?
  public let siteRenameWorking: Bool
  public let lots: [LotRowPresentation]
  public let newLotName: String
  public let newLotNameError: NameErrorKind?
  public let createWorking: Bool
  public let renaming: LotRenamePresentation?
  public let removing: LotRemovalPresentation?
  public let notice: LotsActionNoticeKind?
  public let noticeSubject: String?
  public let noticeTryAgain: Bool
  /// Nil until the Server has answered with the Site's cadence: then there is no section.
  public let reminders: SiteRemindersPresentation?

  public init(
    siteName: String, role: SiteRoleKind, canRenameSite: Bool, canEditLots: Bool,
    showsReadOnlyNotice: Bool, siteNameDraft: String, siteNameError: NameErrorKind?,
    siteRenameWorking: Bool, lots: [LotRowPresentation], newLotName: String,
    newLotNameError: NameErrorKind?, createWorking: Bool, renaming: LotRenamePresentation?,
    removing: LotRemovalPresentation?, notice: LotsActionNoticeKind?, noticeSubject: String?,
    noticeTryAgain: Bool = false, reminders: SiteRemindersPresentation? = nil
  ) {
    self.siteName = siteName
    self.role = role
    self.canRenameSite = canRenameSite
    self.canEditLots = canEditLots
    self.showsReadOnlyNotice = showsReadOnlyNotice
    self.siteNameDraft = siteNameDraft
    self.siteNameError = siteNameError
    self.siteRenameWorking = siteRenameWorking
    self.lots = lots
    self.newLotName = newLotName
    self.newLotNameError = newLotNameError
    self.createWorking = createWorking
    self.renaming = renaming
    self.removing = removing
    self.notice = notice
    self.noticeSubject = noticeSubject
    self.noticeTryAgain = noticeTryAgain
    self.reminders = reminders
  }

  /// The notice above the Lots: every one but the Reminder cadence's, which sits with its
  /// control in Reminders.
  public var generalNotice: LotsActionNoticeKind? {
    notice == .reminderCadenceNotSaved ? nil : notice
  }

  /// "The Reminder cadence was not saved. Try again.", shown in Reminders.
  public var remindersNotice: LotsActionNoticeKind? {
    notice == .reminderCadenceNotSaved ? notice : nil
  }

  public var remindersNoticeTryAgain: Bool { remindersNotice != nil && noticeTryAgain }

  /// "Rename Site", "Renaming Site…" in place while working.
  public var renameSiteLabel: L10n {
    siteRenameWorking ? .siteSettingsRenamingSite : .siteSettingsRenameSite
  }

  public var siteNameHelper: L10n { siteNameError?.message ?? .siteSettingsSiteNameHelper }

  /// "Create Lot", "Creating Lot…" in place while working.
  public var createLotLabel: L10n {
    createWorking ? .siteSettingsCreatingLot : .siteSettingsCreateLot
  }

  public var lotNameHelper: L10n { newLotNameError?.lotMessage ?? .siteSettingsLotNameHelper }

  /// "Only Owners and Administrators can change Lots." for Members only.
  public var readOnlyNotice: L10n? { showsReadOnlyNotice ? .siteSettingsReadOnly : nil }

  public var emptyLots: L10n? { lots.isEmpty ? .siteSettingsNoLots : nil }
}

/// Which Lots surface shows.
public enum LotsSurface: Equatable, Sendable {
  /// Signed out or no current Site.
  case waiting
  /// The Lots of a Site are read for the first time and none are kept on this phone: skeleton
  /// tiles and "Loading ‹Site›" (UX-DR80).
  case loading(siteName: String)
  /// The Lots could not be read: the Sites load notices (Unreachable, Certificate, …).
  case failed(SitesNoticeKind)
  case ready(SiteSettingsPresentation, tiles: [LotTilePresentation])
}

/// The Swift mirror of the core's `LotsState`, built from the flattened `LotsSnapshot`.
public struct LotsPresentation: Equatable, Sendable {
  public let surface: LotsSurface
  /// The Site overview; nil unless ready.
  public let overview: SiteOverviewPresentation?

  public init(surface: LotsSurface, overview: SiteOverviewPresentation? = nil) {
    self.surface = surface
    self.overview = overview
  }

  public static let waiting = LotsPresentation(surface: .waiting)

  /// Mirrors the flat snapshot field for field; unknown names fall back to the safest reading:
  /// a tile the core did not describe is drawn as unknown, never as fine. Times are Unix
  /// milliseconds; an optional number is a decimal string that is empty when absent.
  public init(
    surface: String, notice: String?, siteId: String?, siteName: String?, role: String?,
    canRenameSite: Bool, canEditLots: Bool, readOnlyNotice: Bool,
    siteNameDraft: String, siteNameError: String?, siteRenameWorking: Bool,
    lotIds: [String], lotNames: [String], lotStatuses: [String],
    newLotName: String, newLotNameError: String?, createWorking: Bool,
    renamingLotId: String?, renameDraft: String, renameError: String?, renameWorking: Bool,
    removingLotId: String?, removingLotName: String?, removeWorking: Bool,
    actionNotice: String?, actionNoticeSubject: String?,
    actionNoticeTryAgain: Bool = false, canSetReminderCadence: Bool = false,
    reminderCadence: String = "", reminderCadenceWorking: Bool = false,
    stale: Bool = false, refreshing: Bool = false, fetchedAtEpochMs: Int64 = 0,
    staleAgeDays: Int = 0, staleAgeHours: Int = 0, staleAgeMinutes: Int = 0,
    headline: String? = nil, headlineCount: Int = 0, headlineLotName: String? = nil,
    headlinePausedUntil: String = "", headlinePausedInk: Bool = false,
    countStatuses: [String] = [], countValues: [Int] = [],
    lotVariants: [String] = [], lotLabels: [String] = [], lotValues: [String] = [],
    lotFoots: [String] = [], lotSpokens: [String] = [], lotSoilPercents: [String] = [],
    lotLowPercents: [String] = [], lotReadingAts: [String] = [],
    lotDurationValues: [String] = [], lotDurationUnits: [String] = [],
    lotPausedBySite: [Bool] = [], lotPausedUntils: [String] = [],
    lotOpensAddNode: [Bool] = [], lotOpensCalibrate: [Bool] = [], menuItems: [String] = [],
    menuEnabled: Bool = true
  ) {
    switch surface {
    case "loading":
      self.overview = nil
      self.surface = siteName.map { .loading(siteName: $0) } ?? .waiting
    case "failed":
      self.overview = nil
      self.surface = .failed(notice.flatMap(SitesNoticeKind.init(rawValue:)) ?? .unexpected)
    case "ready":
      guard siteId != nil, let siteName else {
        self.overview = nil
        self.surface = .waiting
        return
      }
      let fetchedAt = Self.date(epochMs: fetchedAtEpochMs)
      let count = min(lotIds.count, lotNames.count, lotStatuses.count)
      // The Server's order, never re-sorted (UX-DR20). An unknown status or variant is drawn as
      // unknown, never as *no Node* or as fine.
      let tiles = (0..<count).map { index in
        let variant =
          Self.entry(lotVariants, index).flatMap(LotTileVariantKind.init(rawValue:)) ?? .unknown
        let duration = Self.entry(lotDurationValues, index).flatMap { Int($0) }.flatMap { value in
          Self.entry(lotDurationUnits, index)
            .flatMap(LotDurationPresentation.Unit.init(rawValue:))
            .map { LotDurationPresentation(value: value, unit: $0) }
        }
        return LotTilePresentation(
          id: lotIds[index], name: lotNames[index],
          status: LotStatusKind(rawValue: lotStatuses[index]) ?? .unknown,
          variant: variant,
          label: Self.entry(lotLabels, index).flatMap(LotTileLabelKind.init(rawValue:)) ?? .silent,
          value: Self.entry(lotValues, index).flatMap(LotTileValueKind.init(rawValue:)) ?? .none,
          foot: Self.entry(lotFoots, index).flatMap(LotTileFootKind.init(rawValue:)) ?? .none,
          spoken: Self.entry(lotSpokens, index).flatMap(LotTileSpokenKind.init(rawValue:))
            ?? (variant == .stale ? .stale : .nodeSilent),
          soilPercent: Self.entry(lotSoilPercents, index).flatMap { Int($0) },
          lowPercent: Self.entry(lotLowPercents, index).flatMap { Int($0) },
          readingAt: Self.entry(lotReadingAts, index).flatMap(Self.date(text:)),
          duration: duration,
          pausedBySite: Self.entry(lotPausedBySite, index) ?? false,
          pausedUntil: Self.entry(lotPausedUntils, index).flatMap(Self.date(text:)),
          asOf: variant == .stale ? fetchedAt : nil,
          opensAddNode: Self.entry(lotOpensAddNode, index) ?? false,
          opensCalibrate: Self.entry(lotOpensCalibrate, index) ?? false)
      }
      let role = role.flatMap(SiteRoleKind.init(rawValue:)) ?? .member
      let notice = actionNotice.map { LotsActionNoticeKind(rawValue: $0) ?? .unexpected }
      let renaming = renamingLotId.map { lotId in
        LotRenamePresentation(
          lotId: lotId, draft: renameDraft,
          error: renameError.flatMap(NameErrorKind.init(rawValue:)), working: renameWorking)
      }
      let removing = removingLotId.map { lotId in
        LotRemovalPresentation(
          lotId: lotId,
          lotName: removingLotName ?? tiles.first { $0.id == lotId }?.name ?? "",
          working: removeWorking)
      }
      let settings = SiteSettingsPresentation(
        siteName: siteName, role: role,
        canRenameSite: canRenameSite, canEditLots: canEditLots,
        showsReadOnlyNotice: readOnlyNotice,
        siteNameDraft: siteNameDraft,
        siteNameError: siteNameError.flatMap(NameErrorKind.init(rawValue:)),
        siteRenameWorking: siteRenameWorking,
        lots: tiles.map { LotRowPresentation(id: $0.id, name: $0.name) },
        newLotName: newLotName,
        newLotNameError: newLotNameError.flatMap(NameErrorKind.init(rawValue:)),
        createWorking: createWorking,
        renaming: canEditLots ? renaming : nil,
        removing: canEditLots ? removing : nil,
        notice: notice,
        noticeSubject: notice?.takesSubject == true ? actionNoticeSubject : nil,
        noticeTryAgain: actionNoticeTryAgain,
        // Empty until the Server answered; a cadence this client does not know is not worded.
        reminders: ReminderCadenceKind(rawValue: reminderCadence).map {
          SiteRemindersPresentation(
            cadence: $0, canEdit: canSetReminderCadence, working: reminderCadenceWorking)
        })
      let counts = zip(countStatuses, countValues).compactMap { status, value in
        LotStatusKind(rawValue: status).map { LotCountPresentation(status: $0, count: value) }
      }
      // A headline this client does not know never reads as "Nothing needs water".
      let summary = SiteSummaryPresentation(
        kind: headline.flatMap(SiteHeadlineKind.init(rawValue:)) ?? .noReadings,
        count: headlineCount, lotName: headlineLotName,
        pausedUntil: Self.date(text: headlinePausedUntil), pausedInk: headlinePausedInk,
        counts: counts)
      self.overview = SiteOverviewPresentation(
        stale: stale, refreshing: refreshing, fetchedAt: fetchedAt,
        staleAge: StaleAgePresentation(
          days: staleAgeDays, hours: staleAgeHours, minutes: staleAgeMinutes),
        summary: summary, menuItems: menuItems.compactMap(SiteMenuItem.init(rawValue:)),
        menuEnabled: menuEnabled)
      self.surface = .ready(settings, tiles: tiles)
    default:
      self.overview = nil
      self.surface = .waiting
    }
  }

  private static func entry<Value>(_ list: [Value], _ index: Int) -> Value? {
    index < list.count ? list[index] : nil
  }

  /// Unix milliseconds; 0 is "none".
  private static func date(epochMs: Int64) -> Date? {
    epochMs > 0 ? Date(timeIntervalSince1970: Double(epochMs) / 1000) : nil
  }

  /// Unix milliseconds in decimal, or an empty string.
  private static func date(text: String) -> Date? {
    Int64(text).flatMap { date(epochMs: $0) }
  }

  /// The Lot tiles for Garden, in the Server's order; empty unless ready.
  public var tiles: [LotTilePresentation] {
    if case .ready(_, let tiles) = surface { return tiles }
    return []
  }

  /// The Site whose Lots are being read for the first time: skeleton tiles.
  public var loadingSiteName: String? {
    if case .loading(let siteName) = surface { return siteName }
    return nil
  }

  /// Site settings; nil unless ready.
  public var siteSettings: SiteSettingsPresentation? {
    if case .ready(let settings, _) = surface { return settings }
    return nil
  }

  /// The load failure that replaces the Lot grid on Garden.
  public var failure: SitesNoticeKind? {
    if case .failed(let notice) = surface { return notice }
    return nil
  }
}

/// The Lots half of the shared Kotlin core, as the SwiftUI shell sees it. The app target adapts
/// `IosLots` of `ColdframeCore`; no token or URL crosses this boundary. The core follows the
/// current Site of the Sites half and reloads after every change.
@MainActor
public protocol LotsService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (LotsPresentation) -> Void)
  /// Calls `onEvent` when stale mode is entered or left, for one polite announcement each.
  func observeEvents(_ onEvent: @escaping @MainActor (LotsEventPresentation) -> Void)
  /// Try again after a failed load.
  func load()
  /// Pull-to-refresh, and every time the app comes to the front. The shown Lots stay.
  func refresh()
  /// The one-minute tick: reads the core's snapshot again, so the stale age and the tiles'
  /// durations move. Nothing is fetched and nothing is announced.
  func tick()
  func setSiteName(_ name: String)
  func renameSite()
  func setNewLotName(_ name: String)
  func createLot()
  func startRename(lotId: String)
  func setRename(_ name: String)
  func rename()
  func cancelRename()
  func askRemove(lotId: String)
  func confirmRemove()
  func cancelRemove()
  /// Picks the Site's Reminder cadence (Owner or Administrator); applies at once.
  func setReminderCadence(_ cadence: ReminderCadenceKind)
  /// Try again after the Reminder cadence was not saved.
  func retryReminderCadence()
}
