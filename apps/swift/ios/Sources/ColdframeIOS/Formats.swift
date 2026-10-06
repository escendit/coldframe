import Foundation

/// Locale-aware formats by the Voice rules (UX-DR127). Words come from the catalogue through
/// `text`; numbers, the clock and the calendar come from the locale.
public enum Formats {
  /// "min" under 1 h, "h" under 24 h ("h min" with `withMinutes`, for the stale header), "d"
  /// from 24 h. `text` formats a catalogue entry with its integer arguments.
  public static func duration(
    seconds: Double, withMinutes: Bool = false, text: (L10n, [Int]) -> String
  ) -> String {
    let total = Int(max(0, seconds))
    let minutes = total / 60
    let hours = minutes / 60
    if minutes < 60 { return text(.durationMinutes, [minutes]) }
    if hours < 24 {
      let rest = minutes % 60
      return withMinutes && rest > 0
        ? text(.durationHoursMinutes, [hours, rest]) : text(.durationHours, [hours])
    }
    return text(.durationDays, [hours / 24])
  }

  /// Clock time in the locale's 12 or 24 hour format.
  public static func time(_ date: Date, timeZone: TimeZone, locale: Locale) -> String {
    let formatter = DateFormatter()
    formatter.locale = locale
    formatter.timeZone = timeZone
    formatter.dateStyle = .none
    formatter.timeStyle = .short
    return formatter.string(from: date)
  }

  /// Today → clock time; 1 to 7 calendar days ago → weekday; otherwise (or a later day) → date.
  public static func when(_ date: Date, now: Date, timeZone: TimeZone, locale: Locale) -> String {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = timeZone
    let days =
      calendar.dateComponents(
        [.day], from: calendar.startOfDay(for: date), to: calendar.startOfDay(for: now)
      ).day ?? 0
    if days == 0 { return time(date, timeZone: timeZone, locale: locale) }
    let formatter = DateFormatter()
    formatter.locale = locale
    formatter.timeZone = timeZone
    formatter.setLocalizedDateFormatFromTemplate((1...7).contains(days) ? "EEE" : "dMMM")
    return formatter.string(from: date)
  }

  /// A calendar day without its year: "1 Nov", or "1 November" with `long`.
  public static func day(
    _ date: Date, timeZone: TimeZone, locale: Locale, long: Bool = false
  ) -> String {
    let formatter = DateFormatter()
    formatter.locale = locale
    formatter.timeZone = timeZone
    formatter.setLocalizedDateFormatFromTemplate(long ? "dMMMM" : "dMMM")
    return formatter.string(from: date)
  }

  /// A whole number in the locale's format.
  public static func number(_ value: Int, locale: Locale) -> String {
    let formatter = NumberFormatter()
    formatter.locale = locale
    formatter.numberStyle = .decimal
    return formatter.string(from: NSNumber(value: value)) ?? String(value)
  }

  /// A percentage (0–100) with the locale's spacing.
  public static func percent(_ value: Double, locale: Locale) -> String {
    let formatter = NumberFormatter()
    formatter.locale = locale
    formatter.numberStyle = .percent
    formatter.maximumFractionDigits = 0
    return formatter.string(from: NSNumber(value: value / 100)) ?? "\(Int(value))%"
  }
}

/// What turns structured copy into text: the catalogue (`resolve`), the clock the times are told
/// against, and the locale. The views pass the String Catalog; tests on Linux read the catalogue
/// file. Site time zones arrive with the Site clock; until then times are in the phone's zone.
public struct CopyContext: Sendable {
  public let now: Date
  public let timeZone: TimeZone
  public let locale: Locale
  public let resolve: @Sendable (Copy) -> String

  public init(
    now: Date, timeZone: TimeZone, locale: Locale, resolve: @escaping @Sendable (Copy) -> String
  ) {
    self.now = now
    self.timeZone = timeZone
    self.locale = locale
    self.resolve = resolve
  }

  public func text(_ key: L10n, _ arguments: CopyArgument...) -> String {
    resolve(Copy(key: key, arguments: arguments))
  }

  /// Today → clock time; earlier → weekday or date (UX-DR127).
  public func when(_ date: Date) -> String {
    Formats.when(date, now: now, timeZone: timeZone, locale: locale)
  }

  /// "1 Nov", or "1 November" with `long`.
  public func day(_ date: Date, long: Bool = false) -> String {
    Formats.day(date, timeZone: timeZone, locale: locale, long: long)
  }

  public func percent(_ value: Int) -> String {
    Formats.percent(Double(value), locale: locale)
  }

  /// The parts that are present, joined two at a time by `separator` ("%1$@ · %2$@"); nil when
  /// none is.
  public func joined(_ separator: L10n, _ parts: [String?]) -> String? {
    let present = parts.compactMap { $0 }
    guard let first = present.first else { return nil }
    return present.dropFirst().reduce(first) { text(separator, .text($0), .text($1)) }
  }
}
