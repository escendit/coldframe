import Foundation

@testable import ColdframeIOS

/// Files of the repository, found from this source file (tests/swift/ios/ColdframeIOSTests).
enum Repo {
  static let root: URL = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .deletingLastPathComponent().deletingLastPathComponent()

  static func url(_ path: String) -> URL { root.appendingPathComponent(path) }

  static func text(_ path: String) throws -> String {
    try String(contentsOf: url(path), encoding: .utf8)
  }

  /// Swift sources below `path`.
  static func swiftSources(_ path: String) -> [URL] {
    guard
      let walker = FileManager.default.enumerator(
        at: url(path), includingPropertiesForKeys: nil)
    else { return [] }
    return walker.compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" }
  }
}

/// The English values of `Localizable.xcstrings`; plural forms as `key.form`.
enum Catalogue {
  static let path = "apps/swift/ios/Sources/ColdframeIOS/Resources/Localizable.xcstrings"

  static func entries() throws -> [String: String] {
    let data = try Data(contentsOf: Repo.url(path))
    let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
    let strings = json?["strings"] as? [String: Any] ?? [:]
    var entries: [String: String] = [:]
    for (key, entry) in strings {
      let english =
        ((entry as? [String: Any])?["localizations"] as? [String: Any])?["en"] as? [String: Any]
      if let plural = (english?["variations"] as? [String: Any])?["plural"] as? [String: Any] {
        for (form, unit) in plural {
          entries["\(key).\(form)"] = value(of: unit)
        }
      } else if let english {
        entries[key] = value(of: english)
      }
    }
    return entries
  }

  private static func value(of unit: Any) -> String? {
    ((unit as? [String: Any])?["stringUnit"] as? [String: Any])?["value"] as? String
  }

  /// Formats an entry the way `String(format:)` does for these integer placeholders.
  static func format(_ template: String, _ arguments: [Int]) -> String {
    var result = template
    for (index, argument) in arguments.enumerated() {
      result = result.replacingOccurrences(of: "%\(index + 1)$lld", with: String(argument))
    }
    if let first = arguments.first {
      result = result.replacingOccurrences(of: "%lld", with: String(first))
    }
    return result
  }
}

extension Catalogue {
  private static let english: [String: String] = (try? entries()) ?? [:]

  /// The English text of `copy`, as the String Catalog resolves it on a device: the plural form
  /// by the first number (`one` for 1, else `other`), then `%@` and `%lld`, positional or in
  /// order. An entry without arguments is returned as written, as `Copy.string` does.
  static func resolve(_ copy: Copy) -> String {
    let key = copy.key.rawValue
    let numbers = copy.arguments.compactMap { argument -> Int? in
      if case .number(let number) = argument { return number }
      return nil
    }
    let form = numbers.first == 1 ? "one" : "other"
    guard var result = english[key] ?? english["\(key).\(form)"] else { return key }
    let values = copy.arguments.map { argument -> String in
      switch argument {
      case .text(let text): text
      case .number(let number): String(number)
      }
    }
    for (index, value) in values.enumerated() {
      for placeholder in ["%\(index + 1)$@", "%\(index + 1)$lld"] {
        result = result.replacingOccurrences(of: placeholder, with: value)
      }
    }
    for value in values {
      let next = ["%@", "%lld"].compactMap { result.range(of: $0) }
        .min { $0.lowerBound < $1.lowerBound }
      if let next { result.replaceSubrange(next, with: value) }
    }
    // `%%` is a literal percent sign in a format string.
    return result.replacingOccurrences(of: "%%", with: "%")
  }
}

/// Fixed clock and catalogue for the Site overview: 6 October 2026, 10:00 UTC, British English.
enum Overview {
  static let nowMs: Int64 = 1_791_280_800_000
  /// 07:02 today.
  static let readingMs: Int64 = 1_791_270_120_000
  /// 01:05 today.
  static let earlierMs: Int64 = 1_791_248_700_000
  /// 1 November 2026.
  static let novemberMs: Int64 = 1_793_534_400_000

  static let context = CopyContext(
    now: Date(timeIntervalSince1970: Double(nowMs) / 1000),
    timeZone: TimeZone(identifier: "UTC") ?? .gmt, locale: Locale(identifier: "en_GB"),
    resolve: Catalogue.resolve)

  /// One Lot as the core flattens it into the `lot…` lists of `LotsSnapshot`.
  struct Lot {
    var id: String
    var name: String
    var status: String
    var variant: String
    var label: String
    var value: String = "none"
    var foot: String = "none"
    var spoken: String
    var soil: String = ""
    var low: String = ""
    var readingAt: String = ""
    var durationValue: String = ""
    var durationUnit: String = ""
    var pausedBySite: Bool = false
    var pausedUntil: String = ""
    var opensAddNode: Bool = false
    var opensCalibrate: Bool = false

    /// The same Lot in stale mode, as the core flattens it: no value, "as of".
    var stale: Lot {
      Lot(
        id: id, name: name, status: status, variant: "stale", label: label, foot: "asOf",
        spoken: "stale", pausedBySite: pausedBySite)
    }
  }

  static let needsWater = Lot(
    id: "t", name: "Tomatoes", status: "needsWater", variant: "needsWater", label: "needsWater",
    value: "soil", foot: "reading", spoken: "needsWater", soil: "20", low: "30",
    readingAt: String(readingMs))
  static let needsCalibration = Lot(
    id: "c", name: "Carrots", status: "needsCalibration", variant: "needsCalibration",
    label: "needsCalibration", value: "raw", foot: "noPercentUntilCalibrated",
    spoken: "needsCalibration")
  static let nodeSilent = Lot(
    id: "b", name: "Beans", status: "unknown", variant: "unknown", label: "silent",
    value: "duration", foot: "wasPercentAt", spoken: "nodeSilent", soil: "40",
    readingAt: String(earlierMs), durationValue: "6", durationUnit: "hours")
  static let hubSilent = Lot(
    id: "h", name: "Herbs", status: "unknown", variant: "unknown", label: "hubSilent",
    value: "duration", foot: "lastReading", spoken: "hubSilent", readingAt: String(readingMs),
    durationValue: "12", durationUnit: "minutes")
  static let neverReported = Lot(
    id: "n", name: "Nasturtiums", status: "unknown", variant: "unknown", label: "silent",
    value: "duration", foot: "noReadingsYet", spoken: "nodeSilent", durationValue: "2",
    durationUnit: "days")
  static let ok = Lot(
    id: "p", name: "Peppers", status: "ok", variant: "ok", label: "ok", value: "soil",
    foot: "reading", spoken: "ok", soil: "35", low: "25", readingAt: String(readingMs))
  /// OK without a percentage or a low Threshold: only the Reading time is present.
  static let okBare = Lot(
    id: "o", name: "Onions", status: "ok", variant: "ok", label: "ok", value: "none",
    foot: "reading", spoken: "ok", readingAt: String(readingMs))
  static let pausedUntil = Lot(
    id: "s", name: "Squash", status: "paused", variant: "paused", label: "paused", value: "dash",
    foot: "pausedUntil", spoken: "pausedUntil", pausedUntil: String(novemberMs))
  static let pausedBySite = Lot(
    id: "l", name: "Leeks", status: "paused", variant: "paused", label: "pausedBySite",
    value: "dash", foot: "paused", spoken: "pausedWithSite", pausedBySite: true)
  static let paused = Lot(
    id: "k", name: "Kale", status: "paused", variant: "paused", label: "paused", value: "dash",
    foot: "paused", spoken: "paused")
  static let noNode = Lot(
    id: "z", name: "Zucchini", status: "noNode", variant: "noNode", label: "noNode",
    value: "plus", foot: "addNode", spoken: "noNode", opensAddNode: true)

  /// One Lot per variant and label, in the Server's order.
  static let everyLot = [
    needsWater, needsCalibration, nodeSilent, hubSilent, neverReported, ok, okBare, pausedUntil,
    pausedBySite, paused, noNode,
  ]

  /// A `LotsPresentation` as `CoreLotsService` builds it from a `LotsSnapshot` of an Owner.
  static func lots(
    surface: String = "ready", notice: String? = nil, siteName: String? = "Home garden",
    _ lots: [Lot] = everyLot, stale: Bool = false, refreshing: Bool = false,
    fetchedAtMs: Int64 = nowMs, staleAge: (days: Int, hours: Int, minutes: Int) = (0, 0, 0),
    headline: String? = "needsWater", headlineCount: Int = 1,
    headlineLotName: String? = "Tomatoes", headlinePausedUntil: String = "",
    headlinePausedInk: Bool = false,
    counts: [(status: String, count: Int)] = [
      ("needsCalibration", 1), ("unknown", 3), ("ok", 2), ("paused", 3), ("noNode", 1),
    ],
    menuItems: [String] = ["siteSettings"], menuEnabled: Bool = true
  ) -> LotsPresentation {
    LotsPresentation(
      surface: surface, notice: notice, siteId: "a", siteName: siteName, role: "owner",
      canRenameSite: true, canEditLots: true, readOnlyNotice: false,
      siteNameDraft: siteName ?? "", siteNameError: nil, siteRenameWorking: false,
      lotIds: lots.map(\.id), lotNames: lots.map(\.name), lotStatuses: lots.map(\.status),
      newLotName: "", newLotNameError: nil, createWorking: false,
      renamingLotId: nil, renameDraft: "", renameError: nil, renameWorking: false,
      removingLotId: nil, removingLotName: nil, removeWorking: false,
      actionNotice: nil, actionNoticeSubject: nil,
      stale: stale, refreshing: refreshing, fetchedAtEpochMs: fetchedAtMs,
      staleAgeDays: staleAge.days, staleAgeHours: staleAge.hours,
      staleAgeMinutes: staleAge.minutes,
      headline: headline, headlineCount: headlineCount, headlineLotName: headlineLotName,
      headlinePausedUntil: headlinePausedUntil, headlinePausedInk: headlinePausedInk,
      countStatuses: counts.map(\.status), countValues: counts.map(\.count),
      lotVariants: lots.map(\.variant), lotLabels: lots.map(\.label),
      lotValues: lots.map(\.value), lotFoots: lots.map(\.foot), lotSpokens: lots.map(\.spoken),
      lotSoilPercents: lots.map(\.soil), lotLowPercents: lots.map(\.low),
      lotReadingAts: lots.map(\.readingAt), lotDurationValues: lots.map(\.durationValue),
      lotDurationUnits: lots.map(\.durationUnit), lotPausedBySite: lots.map(\.pausedBySite),
      lotPausedUntils: lots.map(\.pausedUntil), lotOpensAddNode: lots.map(\.opensAddNode),
      lotOpensCalibrate: lots.map(\.opensCalibrate),
      menuItems: menuItems, menuEnabled: menuEnabled)
  }

  /// Every Lot in stale mode: the Server was last read at 07:02, 2 h 58 min ago.
  static func staleLots(_ lots: [Lot] = everyLot) -> LotsPresentation {
    Overview.lots(
      lots.map(\.stale), stale: true, fetchedAtMs: readingMs, staleAge: (0, 2, 58),
      menuEnabled: false)
  }

  /// A cold start without kept Lots.
  static let loading = lots(surface: "loading", [])

  static func tile(_ lot: Lot) -> LotTilePresentation {
    lots([lot]).tiles[0]
  }

  static func staleTile(_ lot: Lot) -> LotTilePresentation {
    staleLots([lot]).tiles[0]
  }
}

/// Lot detail as the core flattens it into a `LotDetailSnapshot`, at the clock of `Overview`
/// (6 October 2026, 10:00 UTC). Defaults are a live Lot that needs calibration, the Peppers on
/// Node 7c19 with a soil Sensor; each scenario overrides what differs.
struct LotDetailFixture {
  var surface = "ready"
  var notice: String?
  var lotName = "Peppers"
  var stale = false
  var fetchedAtMs: Int64 = Overview.nowMs
  var staleAge: (days: Int, hours: Int, minutes: Int) = (0, 0, 0)
  var heroVariant = "needsCalibration"
  var heroLabel = "needsCalibration"
  var heroStatusSince = String(Overview.readingMs)
  var heroValue = "raw"
  var heroRawNumber = "1840"
  var heroSoilPercent = ""
  var heroLowPercent = ""
  var heroReadingAt = String(Overview.readingMs)
  var heroNote = "noPercentUntilCalibrated"
  var heroPausedUntil = ""
  var heroResumeSiteHint = false
  var heroDuration: (value: String, unit: String) = ("", "")
  var noNode = false
  var canAddNode = true
  var canCalibrate = false
  var canSetThresholds = false
  var canViewThresholds = false
  var thresholdLowPercent = ""
  var thresholdHighPercent = ""
  var sensorIds: [String] = []
  /// The Threshold band; `nil` is no band. Days back from today whose low is under the low line.
  var band: (low: String, high: String, lowFraction: Double, highFraction: Double)?
  var belowLowDays: Set<Int> = []
  var hasSensors = true
  var sensors: [(quantity: String, number: String, unit: String)] = [
    ("soilMoisture", "1840", "raw"), ("airTemperature", "14", "celsius"),
    ("relativeHumidity", "78", "percent"), ("gasResistance", "142", "kiloOhm"),
  ]
  var hasDevice = true
  var battery = "62"
  var batteryLow = false
  var charging = "charging"
  var lastSeen = String(Overview.readingMs)
  var picked: String? = "soilMoisture"
  var historyUnavailable = false
  var chartQuantity: String? = "soilMoisture"
  var chartUnit: String? = "raw"
  /// Days back from today (0 = today) with their low, high, Reading count and axis fraction.
  var days: [Int: (low: String, high: String, count: Int, fraction: Double)] = [
    0: ("1790", "2050", 30, 0.5), 1: ("1840", "2210", 96, 0.8), 3: ("2100", "2300", 96, 1.0),
  ]
  var chartLowest = "1790"
  var chartLowestDay = ""

  /// Midnight UTC of today.
  static let todayMs: Int64 = Overview.nowMs / 86_400_000 * 86_400_000

  static func dayText(back: Int) -> String {
    let formatter = DateFormatter()
    formatter.dateFormat = "yyyy-MM-dd"
    formatter.timeZone = TimeZone(identifier: "UTC")
    formatter.locale = Locale(identifier: "en_US_POSIX")
    return formatter.string(
      from: Date(timeIntervalSince1970: Double(todayMs - Int64(back) * 86_400_000) / 1000))
  }

  func build() -> LotDetailPresentation {
    let order = Array((0..<30).reversed())
    let lowestDay = chartLowestDay.isEmpty ? Self.dayText(back: 0) : chartLowestDay
    return LotDetailPresentation(
      surface: surface, notice: notice, lotId: "p", lotName: lotName, stale: stale,
      refreshing: false, fetchedAtEpochMs: fetchedAtMs, staleAgeDays: staleAge.days,
      staleAgeHours: staleAge.hours, staleAgeMinutes: staleAge.minutes, heroVariant: heroVariant,
      heroLabel: heroLabel, heroStatusSince: heroStatusSince, heroValue: heroValue,
      heroRawNumber: heroRawNumber, heroSoilPercent: heroSoilPercent,
      heroLowPercent: heroLowPercent, heroReadingAt: heroReadingAt, heroNote: heroNote,
      heroPausedUntil: heroPausedUntil, heroResumeSiteHint: heroResumeSiteHint,
      heroNeedsWaterFill: heroVariant == "needsWater", heroDurationValue: heroDuration.value,
      heroDurationUnit: heroDuration.unit, noNode: noNode, canAddNode: canAddNode,
      canCalibrate: canCalibrate, hasSensors: hasSensors, sensorQuantities: sensors.map(\.quantity),
      sensorNumbers: sensors.map(\.number), sensorUnits: sensors.map(\.unit),
      sensorMeasuredAts: sensors.map { _ in String(Overview.readingMs) }, hasDevice: hasDevice,
      deviceNodeId: "7c19", deviceBattery: battery, deviceBatteryLow: batteryLow,
      deviceCharging: charging, deviceLastSeen: lastSeen,
      quantities: sensors.map(\.quantity), picked: picked, historyUnavailable: historyUnavailable,
      hasChart: chartQuantity != nil, chartQuantity: chartQuantity, chartUnit: chartUnit,
      barDays: order.map(Self.dayText(back:)),
      barDayEpochMs: order.map { Self.todayMs - Int64($0) * 86_400_000 },
      barPresent: order.map { days[$0] != nil }, barLows: order.map { days[$0]?.low ?? "" },
      barHighs: order.map { days[$0]?.high ?? "" }, barCounts: order.map { days[$0]?.count ?? 0 },
      barFractions: order.map { days[$0]?.fraction ?? 0 }, chartDaysWithReadings: days.count,
      chartLowest: chartLowest, chartLowestDay: lowestDay, chartHighest: "2300",
      chartHighestDay: Self.dayText(back: 3), canSetThresholds: canSetThresholds,
      canViewThresholds: canViewThresholds, thresholdLowPercent: thresholdLowPercent,
      thresholdHighPercent: thresholdHighPercent, sensorIds: sensorIds,
      chartHasBand: band != nil, chartBandLowPercent: band?.low ?? "",
      chartBandHighPercent: band?.high ?? "", chartBandLowFraction: band?.lowFraction ?? 0,
      chartBandHighFraction: band?.highFraction ?? 0,
      barBelowLow: order.map { belowLowDays.contains($0) },
      chartBelowLowDays: order.filter { belowLowDays.contains($0) }.map(Self.dayText(back:)))
  }

  /// Calibrated soil with a 30 % low and a 70 % high, a day 3 back under the low line.
  static var thresholds: LotDetailFixture {
    var detail = ok
    detail.canSetThresholds = true
    detail.canViewThresholds = true
    detail.thresholdLowPercent = "30"
    detail.thresholdHighPercent = "70"
    detail.sensorIds = ["s-soil", "s-air", "s-hum", "s-gas"]
    detail.chartUnit = "percent"
    detail.days = [0: ("45", "60", 30, 0.45), 1: ("50", "62", 96, 0.5), 3: ("20", "40", 96, 0.2)]
    detail.chartLowest = "20"
    detail.chartLowestDay = dayText(back: 3)
    detail.band = ("30", "70", 0.3, 0.7)
    detail.belowLowDays = [3]
    return detail
  }

  static let needsCalibration = LotDetailFixture()

  static var needsWater: LotDetailFixture {
    var detail = LotDetailFixture()
    detail.lotName = "Tomatoes"
    detail.heroVariant = "needsWater"
    detail.heroLabel = "needsWater"
    detail.heroValue = "percent"
    detail.heroSoilPercent = "20"
    detail.heroLowPercent = "30"
    detail.heroNote = "none"
    detail.batteryLow = true
    detail.battery = "14"
    detail.charging = "notCharging"
    return detail
  }

  static var ok: LotDetailFixture {
    var detail = needsWater
    detail.lotName = "Herbs"
    detail.heroVariant = "ok"
    detail.heroLabel = "ok"
    detail.heroSoilPercent = "35"
    detail.heroLowPercent = "25"
    detail.batteryLow = false
    detail.battery = "62"
    detail.charging = "charging"
    return detail
  }

  static var nodeSilent: LotDetailFixture {
    var detail = LotDetailFixture()
    detail.lotName = "Beans"
    detail.heroVariant = "unknown"
    detail.heroLabel = "silent"
    detail.heroValue = "none"
    detail.heroRawNumber = ""
    detail.heroNote = "checkPowerOrRange"
    detail.heroReadingAt = String(Overview.earlierMs)
    detail.heroDuration = ("6", "hours")
    return detail
  }

  static var hubSilent: LotDetailFixture {
    var detail = nodeSilent
    detail.heroLabel = "hubSilent"
    detail.heroNote = "hubSilent"
    detail.heroDuration = ("12", "minutes")
    return detail
  }

  static var pausedUntil: LotDetailFixture {
    var detail = LotDetailFixture()
    detail.lotName = "Squash"
    detail.heroVariant = "paused"
    detail.heroLabel = "paused"
    detail.heroValue = "none"
    detail.heroNote = "pausedUntil"
    detail.heroPausedUntil = String(Overview.novemberMs)
    return detail
  }

  static var pausedBySite: LotDetailFixture {
    var detail = pausedUntil
    detail.lotName = "Leeks"
    detail.heroLabel = "pausedBySite"
    detail.heroNote = "pausedWithSite"
    detail.heroPausedUntil = ""
    detail.heroResumeSiteHint = true
    return detail
  }

  static var noNodeLot: LotDetailFixture {
    var detail = LotDetailFixture()
    detail.lotName = "Zucchini"
    detail.heroVariant = "noNode"
    detail.heroLabel = "noNode"
    detail.heroValue = "none"
    detail.heroRawNumber = ""
    detail.heroNote = "none"
    detail.heroReadingAt = ""
    detail.noNode = true
    detail.hasSensors = false
    detail.sensors = []
    detail.hasDevice = false
    detail.picked = nil
    detail.chartQuantity = nil
    return detail
  }

  /// Any of the above in stale mode, as the core flattens it: no value, no cells, "as of".
  var asStale: LotDetailFixture {
    var detail = self
    detail.stale = true
    detail.fetchedAtMs = Overview.readingMs
    detail.staleAge = (0, 2, 58)
    detail.heroVariant = "stale"
    detail.heroValue = "none"
    detail.heroRawNumber = ""
    detail.heroSoilPercent = ""
    detail.heroLowPercent = ""
    detail.heroReadingAt = ""
    detail.heroNote = "none"
    detail.heroDuration = ("", "")
    detail.heroResumeSiteHint = false
    detail.hasSensors = false
    detail.hasDevice = false
    return detail
  }
}
