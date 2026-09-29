import Foundation

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
