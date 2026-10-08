import ColdframeDesignTokens
import Foundation

/// What a Sensor measures, as the core named it (the camel-case key of the contract value).
public enum SensorQuantityKind: String, CaseIterable, Hashable, Sendable {
  case soilMoisture
  case airTemperature
  case relativeHumidity
  case gasResistance

  public var label: L10n {
    switch self {
    case .soilMoisture: .lotDetailQuantitySoilMoisture
    case .airTemperature: .lotDetailQuantityAirTemperature
    case .relativeHumidity: .lotDetailQuantityRelativeHumidity
    case .gasResistance: .lotDetailQuantityGasResistance
    }
  }

  /// Soil moisture is drawn as a daily low only; the others also read the day's min and max.
  public var showsRange: Bool { self != .soilMoisture }
}

/// The unit of a converted value, as the core named it. `raw` is the uncalibrated soil count and
/// is never a percentage.
public enum SensorUnitKind: String, CaseIterable, Sendable {
  case raw
  case celsius
  case percent
  case kiloOhm

  var entry: L10n {
    switch self {
    case .raw: .lotDetailValueRaw
    case .celsius: .lotDetailValueCelsius
    case .percent: .lotDetailValuePercent
    case .kiloOhm: .lotDetailValueKiloohm
    }
  }

  /// `raw 1840`, `14 °C`, `78 %`, `142 kΩ`: the Server's converted number with its unit.
  public func copy(_ number: String) -> Copy { Copy(entry, .text(number)) }
}

/// The charger state of a Node at its last report.
public enum ChargeKind: String, CaseIterable, Sendable {
  case charging
  case notCharging

  public var label: L10n { self == .charging ? .devicesCharging : .devicesNotCharging }
}

/// What the hero's big value is, as the core named it.
public enum HeroValueKindPresentation: String, CaseIterable, Sendable {
  case raw
  case percent
  case none
}

/// The status-specific line under the hero (UX-DR78), as the core named it.
public enum HeroNoteKind: String, CaseIterable, Sendable {
  case none
  case noPercentUntilCalibrated
  case checkPowerOrRange
  case hubSilent
  case pausedUntil
  case paused
  case pausedWithSite
}

/// The Lot detail hero (UX-DR27, UX-DR78): the Lot name, the status icon and label with "since
/// ‹time›", the big value, the Reading and low Threshold when the Server sent them, and the
/// status note. It has the tile's treatment of the status; only needs water is orange. The core
/// decided the variant, value kind and note; nothing here computes a status.
public struct LotDetailHeroPresentation: Equatable, Sendable {
  public let name: String
  public let variant: LotTileVariantKind
  public let label: LotTileLabelKind
  public let statusSince: Date?
  public let value: HeroValueKindPresentation
  public let rawNumber: String
  public let soilPercent: Int?
  public let lowPercent: Int?
  public let readingAt: Date?
  public let note: HeroNoteKind
  public let pausedUntil: Date?
  /// "Resume the Site to resume this Node": Admin+ on a Lot paused by the Site.
  public let resumeSiteHint: Bool
  public let needsWaterFill: Bool
  public let duration: LotDurationPresentation?

  public init(
    name: String, variant: LotTileVariantKind, label: LotTileLabelKind, statusSince: Date? = nil,
    value: HeroValueKindPresentation = .none, rawNumber: String = "", soilPercent: Int? = nil,
    lowPercent: Int? = nil, readingAt: Date? = nil, note: HeroNoteKind = .none,
    pausedUntil: Date? = nil, resumeSiteHint: Bool = false, needsWaterFill: Bool = false,
    duration: LotDurationPresentation? = nil
  ) {
    self.name = name
    self.variant = variant
    self.label = label
    self.statusSince = statusSince
    self.value = value
    self.rawNumber = rawNumber
    self.soilPercent = soilPercent
    self.lowPercent = lowPercent
    self.readingAt = readingAt
    self.note = note
    self.pausedUntil = pausedUntil
    self.resumeSiteHint = resumeSiteHint
    self.needsWaterFill = needsWaterFill
    self.duration = duration
  }

  public var isStale: Bool { variant == .stale }
  public var icon: CarbonIcon { variant.icon }
  public var statusLabel: L10n { isStale ? label.was : label.live }

  /// "since 05:45"; nil without a status time.
  public func since(_ context: CopyContext) -> String? {
    statusSince.map { context.text(.lotDetailSince, .text(context.when($0))) }
  }

  /// The big value: `raw 1840`, `~35`, or `—`; the silence of an unknown Lot ("6 h") takes its
  /// place on the hero; nothing on a stale hero.
  public func valueText(_ context: CopyContext) -> String? {
    if isStale { return nil }
    if let duration { return context.resolve(duration.short) }
    switch value {
    case .raw: return context.text(.lotDetailValueRaw, .text(rawNumber))
    case .percent:
      return soilPercent.map {
        context.text(.soilApprox, .text(Formats.number($0, locale: context.locale)))
      } ?? context.text(.lotTileValuePaused)
    case .none: return context.text(.lotTileValuePaused)
    }
  }

  /// "±5 % · 07:02" beside a percentage, else "last Reading 07:02"; nil without a Reading time.
  public func readingText(_ context: CopyContext) -> String? {
    guard let readingAt else { return nil }
    let time = context.when(readingAt)
    return value == .percent
      ? context.text(.lotDetailReading, .text(time))
      : context.text(.lotTileFootLastReading, .text(time))
  }

  /// "low 30 %" when the Server sent a low Threshold.
  public func lowText(_ context: CopyContext) -> String? {
    lowPercent.map { context.text(.lotTileFootLow, .text(context.percent($0))) }
  }

  /// The status-specific line (UX-DR78); nil for none.
  public func noteText(_ context: CopyContext) -> String? {
    switch note {
    case .none: return nil
    case .noPercentUntilCalibrated: return context.text(.lotTileFootUncalibrated)
    case .checkPowerOrRange: return context.text(.lotDetailNodeSilent)
    case .hubSilent: return context.text(.lotDetailHubSilent)
    case .pausedUntil:
      return pausedUntil.map { context.text(.lotDetailPausedUntil, .text(context.day($0))) }
        ?? context.text(.lotDetailPaused)
    case .paused: return context.text(.lotDetailPaused)
    case .pausedWithSite: return context.text(.lotDetailPausedWithSite)
    }
  }

  /// "Resume the Site to resume this Node" for Admin+ on a Lot paused by the Site.
  public func resumeSiteText(_ context: CopyContext) -> String? {
    resumeSiteHint ? context.text(.lotDetailResumeSite) : nil
  }

  /// The one label VoiceOver speaks for the hero (UX-DR98): the Lot, its status, since, the
  /// value when there is one, and the note.
  public func spokenText(_ context: CopyContext) -> String {
    let parts: [String?] = [
      name, context.text(statusLabel), since(context),
      isStale || (value == .none && duration == nil) ? nil : valueText(context), noteText(context),
      resumeSiteText(context),
    ]
    return context.joined(.listComma, parts) ?? name
  }
}

/// One Sensor cell (UX-DR28): a helper label over a `title` value, with the Reading's time. A
/// watched Sensor shows its value only.
public struct SensorCellPresentation: Equatable, Sendable, Identifiable {
  public let quantity: SensorQuantityKind
  public let number: String
  public let unit: SensorUnitKind
  public let measuredAt: Date?

  public init(
    quantity: SensorQuantityKind, number: String, unit: SensorUnitKind, measuredAt: Date?
  ) {
    self.quantity = quantity
    self.number = number
    self.unit = unit
    self.measuredAt = measuredAt
  }

  public var id: SensorQuantityKind { quantity }
  public var label: L10n { quantity.label }

  /// `raw 1840`, `14 °C`, `78 %`, `142 kΩ`.
  public func valueText(_ context: CopyContext) -> String { context.resolve(unit.copy(number)) }

  public func timeText(_ context: CopyContext) -> String? { measuredAt.map(context.when) }

  /// One element for VoiceOver: the quantity, its value, then when it was read.
  public func spokenText(_ context: CopyContext) -> String {
    context.joined(
      .listComma, [context.text(label), valueText(context), timeText(context)])
      ?? valueText(context)
  }
}

/// The two Device cells (UX-DR29): the Node's battery with charging, and last seen with its
/// cadence. Battery below 20 % shows `battery--low` (the core decided).
public struct DeviceCellsPresentation: Equatable, Sendable {
  public let nodeId: String
  public let battery: Int?
  public let batteryLow: Bool
  public let charging: ChargeKind?
  public let lastSeen: Date?

  public init(
    nodeId: String, battery: Int?, batteryLow: Bool, charging: ChargeKind?, lastSeen: Date?
  ) {
    self.nodeId = nodeId
    self.battery = battery
    self.batteryLow = batteryLow
    self.charging = charging
    self.lastSeen = lastSeen
  }

  public var batteryIcon: CarbonIcon? { batteryLow ? .batteryLow : nil }

  /// "62 %", or "—" when the Node's battery is not known.
  public func batteryText(_ context: CopyContext) -> String {
    battery.map { context.text(.lotDetailValuePercent, .text(String($0))) }
      ?? context.text(.lotTileValuePaused)
  }

  public func chargingText(_ context: CopyContext) -> String? {
    charging.map { context.text($0.label) }
  }

  /// "07:02", or "—" for a Node that has not reported.
  public func lastSeenText(_ context: CopyContext) -> String {
    lastSeen.map(context.when) ?? context.text(.lotTileValuePaused)
  }

  public var cadence: L10n { .lotDetailEveryFifteen }
}

/// One of the chart's 30 days. A day without Readings is a gap, never a zero.
public struct ChartBarPresentation: Equatable, Sendable, Identifiable {
  /// `yyyy-MM-dd`, a UTC date.
  public let day: String
  public let dayStart: Date
  public let present: Bool
  public let low: String
  public let high: String
  public let readingCount: Int
  /// 0...1 of the axis, as the core scaled it; 0 for a gap.
  public let fraction: Double

  public init(
    day: String, dayStart: Date, present: Bool, low: String, high: String, readingCount: Int,
    fraction: Double
  ) {
    self.day = day
    self.dayStart = dayStart
    self.present = present
    self.low = low
    self.high = high
    self.readingCount = readingCount
    self.fraction = fraction
  }

  public var id: String { day }
}

/// The History chart (UX-DR32, UX-DR33): 30 daily bars of the picked quantity's daily low,
/// outlined; no Threshold exists before Epic 5, so every bar has the normal style. Gaps stay
/// gaps. The text summary is the chart's accessibility label, and tapping or dragging selects a
/// day for its readout.
public struct HistoryChartPresentation: Equatable, Sendable {
  public let quantity: SensorQuantityKind
  public let unit: SensorUnitKind
  public let bars: [ChartBarPresentation]
  public let daysWithReadings: Int
  public let lowest: String
  public let lowestDay: String
  public let highest: String
  public let highestDay: String

  public init(
    quantity: SensorQuantityKind, unit: SensorUnitKind, bars: [ChartBarPresentation],
    daysWithReadings: Int, lowest: String, lowestDay: String, highest: String = "",
    highestDay: String = ""
  ) {
    self.quantity = quantity
    self.unit = unit
    self.bars = bars
    self.daysWithReadings = daysWithReadings
    self.lowest = lowest
    self.lowestDay = lowestDay
    self.highest = highest
    self.highestDay = highestDay
  }

  /// The UTC calendar day as "6 Oct" (history days are UTC dates and are written as such).
  public func dayText(_ date: Date, _ context: CopyContext) -> String {
    Formats.day(date, timeZone: .gmt, locale: context.locale)
  }

  /// The bar under a horizontal position, 0...1 of the chart's width; nil without bars.
  public func index(atFraction x: Double) -> Int? {
    guard !bars.isEmpty else { return nil }
    return min(bars.count - 1, max(0, Int(x * Double(bars.count))))
  }

  /// The selected day: "5 Oct: lowest 1790" for soil moisture, "5 Oct: 6 °C to 22 °C" for the
  /// others. A day without Readings reads as its date only.
  public func readout(at index: Int, _ context: CopyContext) -> String? {
    guard bars.indices.contains(index) else { return nil }
    let bar = bars[index]
    let day = dayText(bar.dayStart, context)
    guard bar.present else { return day }
    let low = context.resolve(unit.copy(bar.low))
    if quantity.showsRange {
      return context.text(
        .lotDetailChartReadoutRange, .text(day), .text(low),
        .text(context.resolve(unit.copy(bar.high))))
    }
    return context.text(.lotDetailChartReadoutLow, .text(day), .text(low))
  }

  /// The chart's text alternative (UX-DR98): the quantity, the lowest day and how many of the 30
  /// days have Readings.
  public func summary(_ context: CopyContext) -> String {
    let name = context.text(quantity.label)
    guard daysWithReadings > 0, !lowest.isEmpty,
      let lowestBar = bars.first(where: { $0.day == lowestDay })
    else { return context.text(.lotDetailChartSummaryEmpty, .text(name)) }
    return context.text(
      .lotDetailChartSummary, .text(name), .text(context.resolve(unit.copy(lowest))),
      .text(dayText(lowestBar.dayStart, context)), .number(daysWithReadings))
  }
}

/// Why Lot detail could not be read and there is nothing to show, as the core named it.
public enum LotDetailNoticeKind: String, CaseIterable, Sendable {
  case notFound
  case forbidden
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .notFound: .lotDetailNotFound
    case .forbidden: .lotDetailForbidden
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .lotDetailUnexpected
    }
  }

  /// The two notices that name the Site (`%@`).
  public var takesSite: Bool { self == .notFound || self == .forbidden }

  /// The certificate notice has no action: it is never retried insecurely (AD-13).
  public var offersTryAgain: Bool { self == .unreachable || self == .unexpected }
}

/// What Lot detail shows.
public enum LotDetailSurface: Equatable, Sendable {
  /// No Lot is open.
  case idle
  /// The first read, with nothing stored: the Lot's name and an outline, no value.
  case loading(name: String)
  case failed(name: String, notice: LotDetailNoticeKind)
  case ready
}

/// The Swift mirror of the core's `LotDetail`, built from the flattened `LotDetailSnapshot`
/// (UX-DR63): hero, Sensor cells, History chart with its Sensor picker, Device cells. While
/// stale there are no Sensor or Device cells and no value on the hero. A *no Node* Lot is the
/// empty detail. Nothing here converts, aggregates or derives a status.
public struct LotDetailPresentation: Equatable, Sendable {
  public let surface: LotDetailSurface
  public let lotId: String?
  public let hero: LotDetailHeroPresentation?
  public let stale: Bool
  public let refreshing: Bool
  public let fetchedAt: Date?
  public let staleAge: StaleAgePresentation
  public let noNode: Bool
  /// Add a Node on the empty detail, for Administrators and Owners.
  public let canAddNode: Bool
  /// Calibrate shows: Administrators and Owners on a live Lot with a Sensor that can be calibrated.
  public let canCalibrate: Bool
  /// Nil while stale or without a Node; empty is "No Readings yet."
  public let sensors: [SensorCellPresentation]?
  public let device: DeviceCellsPresentation?
  /// The Sensors the Lot has, in the Server's order; the picker shows with more than one.
  public let quantities: [SensorQuantityKind]
  public let picked: SensorQuantityKind?
  public let historyUnavailable: Bool
  public let chart: HistoryChartPresentation?

  public init(
    surface: LotDetailSurface, lotId: String? = nil, hero: LotDetailHeroPresentation? = nil,
    stale: Bool = false, refreshing: Bool = false, fetchedAt: Date? = nil,
    staleAge: StaleAgePresentation = StaleAgePresentation(days: 0, hours: 0, minutes: 0),
    noNode: Bool = false, canAddNode: Bool = false, canCalibrate: Bool = false,
    sensors: [SensorCellPresentation]? = nil,
    device: DeviceCellsPresentation? = nil, quantities: [SensorQuantityKind] = [],
    picked: SensorQuantityKind? = nil, historyUnavailable: Bool = false,
    chart: HistoryChartPresentation? = nil
  ) {
    self.surface = surface
    self.lotId = lotId
    self.hero = hero
    self.stale = stale
    self.refreshing = refreshing
    self.fetchedAt = fetchedAt
    self.staleAge = staleAge
    self.noNode = noNode
    self.canAddNode = canAddNode
    self.canCalibrate = canCalibrate
    self.sensors = sensors
    self.device = device
    self.quantities = quantities
    self.picked = picked
    self.historyUnavailable = historyUnavailable
    self.chart = chart
  }

  public static let idle = LotDetailPresentation(surface: .idle)

  /// The Sensor picker shows with more than one Sensor.
  public var showsPicker: Bool { quantities.count > 1 && picked != nil }

  /// The picker's segments; selection is a trait and a checkmark, never colour alone.
  public var pickerSegments: [SegmentPresentation<SensorQuantityKind>] {
    quantities.map { SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == picked) }
  }

  /// The stale header (UX-DR24) of this detail; nil when live.
  public func staleHeader(siteName: String) -> StaleHeaderPresentation? {
    guard stale, let fetchedAt else { return nil }
    return StaleHeaderPresentation(siteName: siteName, fetchedAt: fetchedAt, age: staleAge)
  }

  /// One column from Accessibility 1: the Sensor and Device cells go 1-up instead of 3-up and
  /// 2-up (UX-DR63, UX-DR97).
  public static func sensorColumns(accessibilitySize: Bool) -> Int { accessibilitySize ? 1 : 3 }
  public static func deviceColumns(accessibilitySize: Bool) -> Int { accessibilitySize ? 1 : 2 }

  /// Mirrors the flat snapshot field for field. Times are Unix milliseconds in decimal; an
  /// optional number is a decimal string that is empty when absent; an unknown quantity, unit or
  /// variant is left out or drawn as unknown, never as fine.
  public init(
    surface: String, notice: String?, lotId: String?, lotName: String?, stale: Bool,
    refreshing: Bool, fetchedAtEpochMs: Int64, staleAgeDays: Int, staleAgeHours: Int,
    staleAgeMinutes: Int, heroVariant: String?, heroLabel: String?, heroStatusSince: String,
    heroValue: String?, heroRawNumber: String, heroSoilPercent: String, heroLowPercent: String,
    heroReadingAt: String, heroNote: String?, heroPausedUntil: String, heroResumeSiteHint: Bool,
    heroNeedsWaterFill: Bool, heroDurationValue: String, heroDurationUnit: String, noNode: Bool,
    canAddNode: Bool, canCalibrate: Bool = false, hasSensors: Bool, sensorQuantities: [String],
    sensorNumbers: [String],
    sensorUnits: [String], sensorMeasuredAts: [String], hasDevice: Bool, deviceNodeId: String,
    deviceBattery: String, deviceBatteryLow: Bool, deviceCharging: String, deviceLastSeen: String,
    quantities: [String], picked: String?, historyUnavailable: Bool, hasChart: Bool,
    chartQuantity: String?, chartUnit: String?, barDays: [String], barDayEpochMs: [Int64],
    barPresent: [Bool], barLows: [String], barHighs: [String], barCounts: [Int],
    barFractions: [Double], chartDaysWithReadings: Int, chartLowest: String,
    chartLowestDay: String, chartHighest: String, chartHighestDay: String
  ) {
    let name = lotName ?? ""
    switch surface {
    case "loading":
      self.init(surface: .loading(name: name), lotId: lotId)
      return
    case "failed":
      self.init(
        surface: .failed(
          name: name, notice: notice.flatMap(LotDetailNoticeKind.init(rawValue:)) ?? .unexpected),
        lotId: lotId)
      return
    case "ready": break
    default:
      self.init(surface: .idle)
      return
    }
    // A tile or label the core did not describe is drawn as unknown, never as fine.
    let variant = heroVariant.flatMap(LotTileVariantKind.init(rawValue:)) ?? .unknown
    let hero = LotDetailHeroPresentation(
      name: name, variant: variant,
      label: heroLabel.flatMap(LotTileLabelKind.init(rawValue:)) ?? .silent,
      statusSince: Self.date(heroStatusSince),
      value: heroValue.flatMap(HeroValueKindPresentation.init(rawValue:)) ?? .none,
      rawNumber: heroRawNumber, soilPercent: Int(heroSoilPercent), lowPercent: Int(heroLowPercent),
      readingAt: Self.date(heroReadingAt),
      note: heroNote.flatMap(HeroNoteKind.init(rawValue:)) ?? .none,
      pausedUntil: Self.date(heroPausedUntil), resumeSiteHint: heroResumeSiteHint,
      needsWaterFill: heroNeedsWaterFill,
      duration: Int(heroDurationValue).flatMap { value in
        LotDurationPresentation.Unit(rawValue: heroDurationUnit).map {
          LotDurationPresentation(value: value, unit: $0)
        }
      })
    let cellCount = min(
      sensorQuantities.count, sensorNumbers.count, sensorUnits.count, sensorMeasuredAts.count)
    let sensors: [SensorCellPresentation]? =
      hasSensors
      ? (0..<cellCount).compactMap { index in
        guard let quantity = SensorQuantityKind(rawValue: sensorQuantities[index]),
          let unit = SensorUnitKind(rawValue: sensorUnits[index])
        else { return nil }
        return SensorCellPresentation(
          quantity: quantity, number: sensorNumbers[index], unit: unit,
          measuredAt: Self.date(sensorMeasuredAts[index]))
      } : nil
    let device: DeviceCellsPresentation? =
      hasDevice
      ? DeviceCellsPresentation(
        nodeId: deviceNodeId, battery: Int(deviceBattery), batteryLow: deviceBatteryLow,
        charging: ChargeKind(rawValue: deviceCharging), lastSeen: Self.date(deviceLastSeen))
      : nil
    var chart: HistoryChartPresentation?
    if hasChart, let quantity = chartQuantity.flatMap(SensorQuantityKind.init(rawValue:)),
      let unit = chartUnit.flatMap(SensorUnitKind.init(rawValue:))
    {
      let barCount = min(
        barDays.count, barDayEpochMs.count, barPresent.count, barLows.count, barHighs.count,
        barCounts.count, barFractions.count)
      chart = HistoryChartPresentation(
        quantity: quantity, unit: unit,
        bars: (0..<barCount).map {
          ChartBarPresentation(
            day: barDays[$0],
            dayStart: Date(timeIntervalSince1970: Double(barDayEpochMs[$0]) / 1000),
            present: barPresent[$0], low: barLows[$0], high: barHighs[$0],
            readingCount: barCounts[$0], fraction: barFractions[$0])
        }, daysWithReadings: chartDaysWithReadings, lowest: chartLowest,
        lowestDay: chartLowestDay, highest: chartHighest, highestDay: chartHighestDay)
    }
    self.init(
      surface: .ready, lotId: lotId, hero: hero, stale: stale, refreshing: refreshing,
      fetchedAt: fetchedAtEpochMs > 0
        ? Date(timeIntervalSince1970: Double(fetchedAtEpochMs) / 1000) : nil,
      staleAge: StaleAgePresentation(
        days: staleAgeDays, hours: staleAgeHours, minutes: staleAgeMinutes),
      noNode: noNode, canAddNode: canAddNode, canCalibrate: canCalibrate, sensors: sensors,
      device: device,
      quantities: quantities.compactMap(SensorQuantityKind.init(rawValue:)),
      picked: picked.flatMap(SensorQuantityKind.init(rawValue:)),
      historyUnavailable: historyUnavailable, chart: chart)
  }

  private static func date(_ epochMs: String) -> Date? {
    Int64(epochMs).map { Date(timeIntervalSince1970: Double($0) / 1000) }
  }
}

/// The Lot detail half of the shared Kotlin core, as the SwiftUI shell sees it. The app target
/// adapts `IosLotDetail` of `ColdframeCore`; no token or URL crosses this boundary.
@MainActor
public protocol LotDetailService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (LotDetailPresentation) -> Void)
  /// Calls `onEvent` when stale mode is entered or left, for one polite announcement each.
  func observeEvents(_ onEvent: @escaping @MainActor (LotsEventPresentation) -> Void)
  /// Opens a Lot, shown under `name` until the Server answers.
  func open(lotId: String, name: String)
  func close()
  /// Pull-to-refresh, the app coming to the front, and Try again.
  func refresh()
  /// The one-minute tick: reads the core's snapshot again so the stale age moves.
  func tick()
  /// Shows the history of a Sensor of the picker.
  func pick(_ quantity: SensorQuantityKind)
}
