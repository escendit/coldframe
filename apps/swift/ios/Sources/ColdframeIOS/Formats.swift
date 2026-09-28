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
