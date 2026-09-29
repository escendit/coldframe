import Foundation
import Testing

@testable import ColdframeIOS

private func english(_ key: L10n, _ arguments: [Int]) -> String {
  let entries = (try? Catalogue.entries()) ?? [:]
  return Catalogue.format(entries[key.rawValue] ?? key.rawValue, arguments)
}

@Test("UX-DR127 durations use min under an hour, h under a day and d from a day")
func durations() {
  #expect(Formats.duration(seconds: -60, text: english) == "0 min")
  #expect(Formats.duration(seconds: 59 * 60, text: english) == "59 min")
  #expect(Formats.duration(seconds: 6.5 * 3600, text: english) == "6 h")
  #expect(Formats.duration(seconds: 23 * 3600 + 59 * 60, text: english) == "23 h")
  #expect(Formats.duration(seconds: 24 * 3600, text: english) == "1 d")
  #expect(Formats.duration(seconds: 3 * 86400 + 5 * 3600, text: english) == "3 d")
}

@Test("UX-DR127 the stale header adds minutes to hours")
func staleHeaderDuration() {
  #expect(
    Formats.duration(seconds: 2 * 3600 + 12 * 60, withMinutes: true, text: english) == "2 h 12 min")
  #expect(Formats.duration(seconds: 2 * 3600, withMinutes: true, text: english) == "2 h")
}

@Test("UX-DR127 today shows the clock in the locale's 12 or 24 hour format")
func clockTimes() {
  let zurich = TimeZone(identifier: "Europe/Zurich")!
  let reading = Date(timeIntervalSince1970: 1_790_571_720)  // 2026-09-28 05:02 UTC
  let now = Date(timeIntervalSince1970: 1_790_589_600)  // 2026-09-28 10:00 UTC

  #expect(
    Formats.when(reading, now: now, timeZone: zurich, locale: Locale(identifier: "de_CH"))
      == "07:02")
  #expect(
    Formats.when(reading, now: now, timeZone: zurich, locale: Locale(identifier: "en_US")).contains(
      "7:02"))
}

@Test("UX-DR127 earlier than today shows the weekday; older than 7 days the date")
func weekdaysAndDates() {
  let zurich = TimeZone(identifier: "Europe/Zurich")!
  let now = Date(timeIntervalSince1970: 1_790_589_600)
  let english = Locale(identifier: "en_GB")

  #expect(
    Formats.when(now.addingTimeInterval(-2 * 86400), now: now, timeZone: zurich, locale: english)
      == "Sat")
  #expect(
    Formats.when(now.addingTimeInterval(-18 * 86400), now: now, timeZone: zurich, locale: english)
      .contains("10"))
  #expect(
    Formats.when(now.addingTimeInterval(86400), now: now, timeZone: zurich, locale: english)
      .contains("29"))
}

@Test("UX-DR127 numbers and percent spacing follow the locale")
func numbersAndPercent() {
  #expect(Formats.number(1234, locale: Locale(identifier: "en_US")) == "1,234")
  #expect(Formats.percent(20, locale: Locale(identifier: "en_US")) == "20%")
  #expect(Formats.percent(20, locale: Locale(identifier: "de_DE")).hasPrefix("20"))
  #expect(Formats.percent(20, locale: Locale(identifier: "de_DE")) != "20%")
}
