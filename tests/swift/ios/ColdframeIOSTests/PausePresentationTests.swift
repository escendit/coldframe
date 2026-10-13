import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context
private let utc = TimeZone(identifier: "UTC") ?? .gmt

@Test("UX-DR46 from the Site menu the Pause sheet offers Whole Site alone and names the Site")
func pauseSheetFromSiteMenu() throws {
  let sheet = try #require(PauseFixture.forSite.build().sheet)

  // There is no Device in that context: the scope that cannot be chosen is not listed.
  #expect(sheet.scopes == [.site])
  #expect(sheet.segments.map(\.value) == [.site])
  #expect(sheet.segments.map(\.isSelected) == [true])
  #expect(sheet.segments.map { context.text($0.label) } == ["Whole Site"])
  #expect(context.resolve(sheet.primaryLabel) == "Pause Home garden")
  #expect(sheet.deviceId == nil)
  #expect(sheet.isEnabled)
}

@Test(
  "UX-DR46 UX-DR70 from a Device the sheet selects This Device and the primary follows the scope")
func pauseSheetFromDevice() throws {
  let device = try #require(PauseFixture.forDevice.build().sheet)
  let site = try #require(PauseFixture.forDeviceWholeSite.build().sheet)

  #expect(device.scopes == [.device, .site])
  #expect(device.segments.map { context.text($0.label) } == ["This Device", "Whole Site"])
  #expect(device.segments.map(\.isSelected) == [true, false])
  // Selection is a trait and a checkmark, never colour alone.
  #expect(device.segments.map(\.showsCheckmark) == [true, false])
  #expect(context.resolve(device.primaryLabel) == "Pause 7C19")
  #expect(device.deviceId == PauseFixture.nodeId)
  // Whole Site from a Device: the core names the Site, and the sheet is still that Device's.
  #expect(site.segments.map(\.isSelected) == [false, true])
  #expect(context.resolve(site.primaryLabel) == "Pause Home garden")
  #expect(site.id == device.id)
}

@Test("UX-DR46 Until is optional: empty it says No end date and how long the Pause then lasts")
func pauseSheetWithoutDate() throws {
  let sheet = try #require(PauseFixture.forSite.build().sheet)

  #expect(sheet.until == nil)
  #expect(sheet.untilText(context) == "No end date")
  #expect(context.text(sheet.untilHelper) == "Leave empty to pause until someone resumes.")
  #expect(sheet.untilSpoken(context) == "Until, No end date")
  #expect(!sheet.showsClearDate)
  #expect(!sheet.dateRefused)
  #expect(sheet.notice == nil)
}

@Test("UX-DR46 a picked date is shown as the calendar date it is, with Clear date")
func pauseSheetWithDate() throws {
  let sheet = try #require(PauseFixture.forDeviceWholeSite.build().sheet)

  #expect(sheet.until == PauseDay(year: 2027, month: 3, day: 1))
  #expect(sheet.untilText(context) == "1 Mar 2027")
  #expect(sheet.untilSpoken(context) == "Until, 1 Mar 2027")
  #expect(sheet.showsClearDate)
  // The words are the same wherever the phone is: no zone moves the day.
  for zone in ["Pacific/Kiritimati", "Pacific/Pago_Pago", "Europe/Zurich"] {
    let there = CopyContext(
      now: context.now, timeZone: TimeZone(identifier: zone) ?? .gmt, locale: context.locale,
      resolve: Catalogue.resolve)
    #expect(sheet.untilText(there) == "1 Mar 2027", "\(zone)")
  }
}

@Test("UX-DR46 the date sent is the calendar date the picker gave, in whatever zone the phone is")
func pauseDayIsACalendarDate() throws {
  for identifier in ["UTC", "Pacific/Kiritimati", "Pacific/Pago_Pago", "America/Sao_Paulo"] {
    let zone = TimeZone(identifier: identifier) ?? .gmt
    let day = PauseDay(year: 2027, month: 3, day: 1)
    let date = try #require(day.date(timeZone: zone))

    // What the picker shows for the day is read back as that day.
    #expect(PauseDay(date, timeZone: zone) == day, "\(identifier)")
  }
  // A day the calendar does not have, and the core's "no date", are no date here either.
  #expect(PauseDay(year: 2027, month: 2, day: 30).date(timeZone: utc) == nil)
  #expect(PauseDay(year: 0, month: 0, day: 0).date(timeZone: utc) == nil)
  // The picker opens on the picked day, or on today when nothing is picked.
  let picked = try #require(PauseFixture.forDeviceWholeSite.build().sheet)
  let empty = try #require(PauseFixture.forSite.build().sheet)
  #expect(
    PauseDay(picked.pickerDate(now: context.now, timeZone: utc), timeZone: utc)
      == PauseDay(year: 2027, month: 3, day: 1))
  #expect(empty.pickerDate(now: context.now, timeZone: utc) == context.now)
}

@Test("UX-DR46 opening the calendar on an empty Until picks tomorrow; a picked date is left alone")
func pauseSheetOpeningTheCalendar() throws {
  let empty = try #require(PauseFixture.forSite.build().sheet)
  let picked = try #require(PauseFixture.forDeviceWholeSite.build().sheet)
  let kiritimati = TimeZone(identifier: "Pacific/Kiritimati") ?? .gmt

  // 6 October 2026, 10:00 UTC: tomorrow is the 7th, and the 8th where it is already the 7th.
  #expect(
    empty.dayOnOpening(now: context.now, timeZone: utc) == PauseDay(year: 2026, month: 10, day: 7))
  #expect(
    empty.dayOnOpening(now: context.now, timeZone: kiritimati)
      == PauseDay(year: 2026, month: 10, day: 8))
  #expect(picked.dayOnOpening(now: context.now, timeZone: utc) == nil)
}

@Test("UX-DR46 a date the Server refused keeps the sheet open and marks the date field")
func pauseSheetDateRefused() throws {
  let sheet = try #require(PauseFixture.refused.build().sheet)

  #expect(sheet.dateRefused)
  #expect(context.text(sheet.untilHelper) == "Choose a date after today.")
  // What was entered stays; the refusal marks its field and is not a second notice.
  #expect(sheet.until == PauseDay(year: 2026, month: 10, day: 6))
  #expect(sheet.untilText(context) == "6 Oct 2026")
  #expect(sheet.notice == nil)
  #expect(sheet.isEnabled)
}

@Test("UX-DR84 UX-DR46 a Role lost meanwhile says You can't change this on the Site, in the sheet")
func pauseSheetForbidden() throws {
  let sheet = try #require(PauseFixture.notSaved("forbidden").build().sheet)
  let notice = try #require(sheet.notice)

  #expect(notice == .forbidden)
  #expect(notice.takesSiteName)
  #expect(
    context.text(notice.message(unreachable: .noticeUnreachable), .text(sheet.siteName))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
  #expect(!sheet.noticeTryAgain)
  // The entries are kept.
  #expect(sheet.scope == .site)
  #expect(sheet.until == PauseDay(year: 2027, month: 3, day: 1))
}

@Test(
  "UX-DR46 an unreachable Server or a Device that is gone uses the surface's notice and keeps the entries"
)
func pauseSheetOtherRefusals() throws {
  let unreachable = try #require(PauseFixture.notSaved("unreachable", tryAgain: true).build().sheet)
  let gone = try #require(PauseFixture.notSaved("notFound").build().sheet)
  let certificate = try #require(PauseFixture.notSaved("certificate").build().sheet)
  let other = try #require(PauseFixture.notSaved("unexpected", tryAgain: true).build().sheet)
  let unknown = try #require(PauseFixture.notSaved("somethingNew").build().sheet)

  // The "can't reach" copy is the one of the surface the sheet was opened on.
  #expect(unreachable.notice?.message(unreachable: .noticeUnreachable) == .noticeUnreachable)
  #expect(unreachable.notice?.message(unreachable: .devicesUnreachable) == .devicesUnreachable)
  #expect(unreachable.noticeTryAgain)
  #expect(unreachable.until == PauseDay(year: 2027, month: 3, day: 1))
  #expect(gone.notice == .notFound)
  #expect(
    context.text(PauseNoticeKind.notFound.message(unreachable: .noticeUnreachable))
      == "That Device is gone.")
  #expect(!gone.noticeTryAgain)
  // A certificate failure is never retried insecurely.
  #expect(certificate.notice?.message(unreachable: .noticeUnreachable) == .noticeCertificate)
  #expect(!certificate.noticeTryAgain)
  #expect(
    context.text(PauseNoticeKind.unexpected.message(unreachable: .noticeUnreachable))
      == "That didn't work. Try again.")
  #expect(other.noticeTryAgain)
  // A notice this client does not know still says that nothing changed.
  #expect(unknown.notice == .unexpected)
  // A 404 on the Site is the Site that is gone, in the copy the app already has for it.
  #expect(PauseNoticeKind(rawValue: "siteGone") == .siteGone)
  #expect(
    PauseNoticeKind.siteGone.message(unreachable: .noticeUnreachable)
      == .notificationsNoticeSiteGone)
  #expect(PauseNoticeKind.allCases.filter(\.takesSiteName) == [.forbidden, .siteGone])
}

@Test(
  "UX-DR46 UX-DR34 while the Pause is sent the primary reads Pausing… and nothing can be changed")
func pauseSheetWorking() throws {
  var fixture = PauseFixture.forDevice
  fixture.working = true
  let pause = fixture.build()
  let sheet = try #require(pause.sheet)

  #expect(context.resolve(sheet.primaryLabel) == "Pausing…")
  #expect(!sheet.isEnabled)
  #expect(pause.working)
  #expect(pause.workingOn(PauseFixture.nodeId))
  #expect(!pause.workingOn(PauseFixture.hubId))
  // Whole Site chosen from a Device: the Device itself is not what is being paused.
  var wholeSite = PauseFixture.forDeviceWholeSite
  wholeSite.working = true
  #expect(wholeSite.build().working)
  #expect(!wholeSite.build().workingOn(PauseFixture.nodeId))
}

@Test(
  "UX-DR46 the sheet is the core's: closed when the core closes it, absent when it cannot be read")
func pauseSheetFollowsTheCore() {
  #expect(PauseFixture.closed.build().sheet == nil)
  #expect(PauseFixture.closed.build() == .none)
  #expect(!PauseFixture.closed.build().working)
  // A scope this client does not know cannot be named on the primary button: no sheet.
  var unknownScope = PauseFixture.forSite
  unknownScope.scope = "garden"
  #expect(unknownScope.build().sheet == nil)
  // An unknown segment is left out; the known ones stay.
  var extraScope = PauseFixture.forDevice
  extraScope.scopes = ["device", "garden", "site"]
  #expect(extraScope.build().sheet?.scopes == [.device, .site])
  // Closed means closed, whatever else the lists still hold.
  var closed = PauseFixture.forDevice
  closed.sheetOpen = false
  #expect(closed.build().sheet == nil)
}

@Test("UX-DR70 Resume is one tap: the Device or the Site being resumed is the only thing working")
func pauseResuming() {
  let device = PauseFixture(resumingScope: "device", resumingDeviceId: PauseFixture.nodeId).build()
  let site = PauseFixture(resumingScope: "site").build()

  #expect(device.sheet == nil)
  #expect(device.working)
  #expect(device.workingOn(PauseFixture.nodeId))
  #expect(!device.workingOn(PauseFixture.hubId))
  #expect(site.working)
  #expect(!site.workingOn(PauseFixture.nodeId))
  #expect(device.failure == nil)
}

@Test(
  "UX-DR70 UX-DR84 a Resume that did not happen says why on its surface, with Try again where it can help"
)
func pauseResumeFailed() throws {
  let unreachable = PauseFixture.resumeFailed(
    "unreachable", deviceId: PauseFixture.nodeId, tryAgain: true
  ).build()
  let forbidden = PauseFixture.resumeFailed("forbidden").build()
  let failure = try #require(unreachable.failure)
  let refused = try #require(forbidden.failure)

  #expect(failure.message(unreachable: .devicesUnreachable) == .devicesUnreachable)
  #expect(failure.message(unreachable: .noticeUnreachable) == .noticeUnreachable)
  #expect(failure.tryAgain)
  #expect(failure.subject == nil)
  // It belongs to the Device it was asked for, and to no other row.
  #expect(unreachable.failure(for: PauseFixture.nodeId) == failure)
  #expect(unreachable.failure(for: PauseFixture.hubId) == nil)
  #expect(refused.scope == .site)
  #expect(refused.deviceId == nil)
  #expect(refused.subject == "Home garden")
  #expect(!refused.tryAgain)
  #expect(
    context.text(refused.message(unreachable: .noticeUnreachable), .text("Home garden"))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
  // Nothing failed: nothing is said.
  #expect(PauseFixture.closed.build().failure == nil)
  #expect(PauseFixture(failureScope: "device", failureDeviceId: "x").build().failure == nil)
}

@Test("UX-DR46 the Pause sheet is a native sheet with Cancel and one modal level at a time")
func pauseSheetIsNative() throws {
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/PauseViews.swift")
  let devices = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/DevicesViews.swift")
  let garden = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")

  #expect(views.contains("func pauseSheet("))
  #expect(views.contains("item: Binding("))
  #expect(views.contains(".modalCancel"))
  #expect(views.contains("SegmentedChoice("))
  // The calendar opens inside the sheet: no alert, dialog or cover is put on top of it.
  #expect(!views.contains(".alert("))
  #expect(!views.contains("confirmationDialog"))
  #expect(!views.contains("fullScreenCover"))
  // A surface never opens it over a sheet of its own.
  #expect(devices.contains("blocked: moving != nil || window != nil"))
  #expect(garden.contains("blocked: switching"))
  // While the request runs the sheet cannot be swiped away.
  #expect(views.contains("interactiveDismissDisabled(form.working)"))
}

@Test("UX-DR70 UX-DR113 Pause and Resume are taps: no swipe action on a Device row")
func pauseHasNoSwipe() throws {
  for file in Repo.swiftSources("apps/swift/ios") {
    let text = try String(contentsOf: file, encoding: .utf8)
    #expect(!text.contains("swipeActions"), "\(file.lastPathComponent)")
  }
}
