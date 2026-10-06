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
    return result
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
