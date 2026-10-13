import ColdframeDesignTokens
import Foundation

/// What a Pause or a Resume is about, as the core names it: one Device, or the whole Site.
public enum PauseScopeKind: String, CaseIterable, Sendable {
  case device
  case site

  /// "This Device" / "Whole Site" on its segment.
  public var label: L10n {
    switch self {
    case .device: .pauseScopeDevice
    case .site: .pauseScopeSite
    }
  }
}

/// Why a Pause or a Resume did not happen, named as the core names it. Nothing changed with any
/// of them. A date the Server refused is not one of these: it marks the date field of the sheet.
public enum PauseNoticeKind: String, CaseIterable, Sendable {
  /// 403: the Role is too low; it may have changed since the surface was read.
  case forbidden
  /// 404 on a Device: the Device is gone.
  case notFound
  /// 404 on the Site: it is no longer one of the User's Sites.
  case siteGone
  case unreachable
  case certificate
  case unexpected

  /// The words. `unreachable` is the "can't reach" copy of the surface the Pause was asked on.
  public func message(unreachable: L10n) -> L10n {
    switch self {
    case .forbidden: .siteSettingsForbidden
    case .notFound: .pauseNotFound
    case .siteGone: .notificationsNoticeSiteGone
    case .unreachable: unreachable
    case .certificate: .noticeCertificate
    case .unexpected: .pauseUnexpected
    }
  }

  /// "You can't change this on ‹Site›…" names the Site (UX-DR84), and so does "‹Site› is no
  /// longer one of your Sites."
  public var takesSiteName: Bool { self == .forbidden || self == .siteGone }
}

/// A calendar date as the User picked it in the Pause sheet. No time zone belongs to it: the
/// Server resolves the date. A zone is used only to show the day in the native picker and to
/// read the picked day back, the same zone both ways, so the day never moves.
public struct PauseDay: Equatable, Sendable {
  public let year: Int
  /// 1 to 12.
  public let month: Int
  public let day: Int

  public init(year: Int, month: Int, day: Int) {
    self.year = year
    self.month = month
    self.day = day
  }

  /// The calendar day `date` falls on in `timeZone`: what the picker showed when it was tapped.
  public init(_ date: Date, timeZone: TimeZone) {
    let parts = Self.calendar(timeZone: timeZone).dateComponents([.year, .month, .day], from: date)
    self.init(year: parts.year ?? 0, month: parts.month ?? 0, day: parts.day ?? 0)
  }

  /// The calendar of the picker: Gregorian, as the contract's `yyyy-MM-dd` is.
  public static func calendar(timeZone: TimeZone) -> Calendar {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = timeZone
    return calendar
  }

  /// Noon of this day in `timeZone`, for the picker and the formatter; nil for a day the
  /// calendar does not have, and for the core's "no date" (zeros).
  public func date(timeZone: TimeZone) -> Date? {
    guard year > 0, month > 0, day > 0 else { return nil }
    let calendar = Self.calendar(timeZone: timeZone)
    let parts = DateComponents(year: year, month: month, day: day, hour: 12)
    guard let date = calendar.date(from: parts), PauseDay(date, timeZone: timeZone) == self else {
      return nil
    }
    return date
  }

  /// "1 Mar 2027": the day with its year, in the locale's order.
  public func text(timeZone: TimeZone, locale: Locale) -> String? {
    date(timeZone: timeZone).map {
      let formatter = DateFormatter()
      formatter.calendar = Self.calendar(timeZone: timeZone)
      formatter.locale = locale
      formatter.timeZone = timeZone
      formatter.setLocalizedDateFormatFromTemplate("dMMMy")
      return formatter.string(from: $0)
    }
  }
}

/// The Pause sheet (UX-DR46), as the core holds it. Opened from the Site menu it has no Device
/// and Whole Site is its only scope; opened from Lot detail or a Devices row it offers This
/// Device, selected, and Whole Site. The core names what the primary button pauses, keeps what
/// was entered and says why a Pause did not happen; nothing here decides any of it.
public struct PauseSheetPresentation: Equatable, Sendable, Identifiable {
  /// The segments of the scope choice, in the core's order. A scope that cannot be chosen is
  /// not listed.
  public let scopes: [PauseScopeKind]
  public let scope: PauseScopeKind
  /// What the primary button names: "7C19" for This Device, "Home garden" for Whole Site.
  public let target: String
  public let siteName: String
  /// The Device the sheet was opened for; nil from the Site menu.
  public let deviceId: String?
  /// The picked date; nil pauses until someone resumes.
  public let until: PauseDay?
  /// The request is on its way.
  public let working: Bool
  /// The Server refused the date: the field is marked and says "Choose a date after today."
  public let dateRefused: Bool
  /// Any other reason the Pause did not happen; the sheet stays open with what was entered.
  public let notice: PauseNoticeKind?
  public let noticeTryAgain: Bool

  public init(
    scopes: [PauseScopeKind], scope: PauseScopeKind, target: String, siteName: String,
    deviceId: String? = nil, until: PauseDay? = nil, working: Bool = false,
    dateRefused: Bool = false, notice: PauseNoticeKind? = nil, noticeTryAgain: Bool = false
  ) {
    self.scopes = scopes
    self.scope = scope
    self.target = target
    self.siteName = siteName
    self.deviceId = deviceId
    self.until = until
    self.working = working
    self.dateRefused = dateRefused
    self.notice = notice
    self.noticeTryAgain = noticeTryAgain
  }

  /// One sheet per Device, or the Site's: choosing another scope is the same sheet.
  public var id: String { deviceId ?? "" }

  /// "This Device" / "Whole Site"; selection is a trait and a checkmark, never colour alone.
  public var segments: [SegmentPresentation<PauseScopeKind>] {
    scopes.map { SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == scope) }
  }

  /// The primary button names the result, "Pause Home garden" or "Pause 7C19", and reads
  /// "Pausing…" in place while the request runs, never a spinner.
  public var primaryLabel: Copy {
    working ? Copy(.pauseWorking) : Copy(.pausePrimary, .text(target))
  }

  /// Nothing in the sheet can be changed or sent again while the request runs.
  public var isEnabled: Bool { !working }

  /// The picked date, "1 Mar 2027", or "No end date".
  public func untilText(_ context: CopyContext) -> String {
    until?.text(timeZone: context.timeZone, locale: context.locale)
      ?? context.text(.pauseUntilEmpty)
  }

  /// The line under the date field: the helper, or why the Server refused the date.
  public var untilHelper: L10n { dateRefused ? .pauseUntilRefused : .pauseUntilHelper }

  /// The one label VoiceOver speaks for the date field: "Until, 1 Mar 2027".
  public func untilSpoken(_ context: CopyContext) -> String {
    context.text(.listComma, .text(context.text(.pauseUntil)), .text(untilText(context)))
  }

  /// "Clear date" shows only while a date is picked.
  public var showsClearDate: Bool { until != nil }

  /// The day the native picker stands on: the picked one, or today when none is.
  public func pickerDate(now: Date, timeZone: TimeZone) -> Date {
    until?.date(timeZone: timeZone) ?? now
  }

  /// What opening the calendar on an empty "Until" picks: tomorrow, the first day that can be
  /// an end. The native calendar always marks a day, so the field and the calendar must name the
  /// same one; "Clear date" empties both. Nil when a date is picked already. Whether the day is
  /// allowed stays the Server's rule.
  public func dayOnOpening(now: Date, timeZone: TimeZone) -> PauseDay? {
    guard until == nil else { return nil }
    let calendar = PauseDay.calendar(timeZone: timeZone)
    return calendar.date(byAdding: .day, value: 1, to: now).map {
      PauseDay($0, timeZone: timeZone)
    }
  }
}

/// The last Resume that did not happen, for the surface it was asked on. A Pause that did not
/// happen stays in its sheet instead.
public struct PauseFailurePresentation: Equatable, Sendable {
  public let scope: PauseScopeKind
  /// The Device that was to be resumed; nil for the Site.
  public let deviceId: String?
  public let notice: PauseNoticeKind
  public let tryAgain: Bool
  /// The Site of the `forbidden` copy.
  public let siteName: String

  public init(
    scope: PauseScopeKind, deviceId: String? = nil, notice: PauseNoticeKind, tryAgain: Bool,
    siteName: String
  ) {
    self.scope = scope
    self.deviceId = deviceId
    self.notice = notice
    self.tryAgain = tryAgain
    self.siteName = siteName
  }

  /// The words; `unreachable` is the "can't reach" copy of the surface.
  public func message(unreachable: L10n) -> L10n { notice.message(unreachable: unreachable) }

  /// The Site, where the message names one.
  public var subject: String? { notice.takesSiteName ? siteName : nil }
}

/// The Swift mirror of the core's `PauseSnapshot`, the same on the overview, Lot detail and
/// Devices (Story 8.2): the open Pause sheet, the Resume under way and the last Resume that did
/// not happen. Whether anything is paused is not here: that is the Server's answer on the
/// surface itself, and nothing in the shell computes or remembers it.
public struct PausePresentation: Equatable, Sendable {
  /// The open sheet; nil when the core has none open.
  public let sheet: PauseSheetPresentation?
  /// What a Resume under way is about; nil when none runs.
  public let resumingScope: PauseScopeKind?
  public let resumingDeviceId: String?
  public let failure: PauseFailurePresentation?

  public init(
    sheet: PauseSheetPresentation? = nil, resumingScope: PauseScopeKind? = nil,
    resumingDeviceId: String? = nil, failure: PauseFailurePresentation? = nil
  ) {
    self.sheet = sheet
    self.resumingScope = resumingScope
    self.resumingDeviceId = resumingScope == nil ? nil : resumingDeviceId
    self.failure = failure
  }

  /// Nothing open, nothing under way, nothing failed.
  public static let none = PausePresentation()

  /// Mirrors the flat snapshot. Enum values cross as `device` / `site` and as the notice keys;
  /// an absent value is an empty string or 0. `sheetUntilYear`, `sheetUntilMonth` and
  /// `sheetUntilDay` are the picked date in calendar numbers. A scope this client does not know
  /// leaves the sheet out, since its primary button could not say what it pauses; a notice it
  /// does not know still says that nothing changed.
  public init(
    sheetOpen: Bool, sheetScopes: [String], sheetScope: String, sheetTarget: String,
    sheetDeviceId: String, sheetSiteName: String, sheetUntilYear: Int, sheetUntilMonth: Int,
    sheetUntilDay: Int, sheetWorking: Bool, sheetDateRefused: Bool, sheetNotice: String,
    sheetNoticeTryAgain: Bool, resumingScope: String, resumingDeviceId: String,
    failureScope: String, failureDeviceId: String, failureNotice: String, failureTryAgain: Bool,
    failureSiteName: String
  ) {
    func notice(_ key: String) -> PauseNoticeKind? {
      key.isEmpty ? nil : PauseNoticeKind(rawValue: key) ?? .unexpected
    }
    var sheet: PauseSheetPresentation?
    if sheetOpen, let scope = PauseScopeKind(rawValue: sheetScope) {
      let day = PauseDay(year: sheetUntilYear, month: sheetUntilMonth, day: sheetUntilDay)
      let refusal = notice(sheetNotice)
      sheet = PauseSheetPresentation(
        scopes: sheetScopes.compactMap(PauseScopeKind.init(rawValue:)), scope: scope,
        target: sheetTarget, siteName: sheetSiteName,
        deviceId: sheetDeviceId.isEmpty ? nil : sheetDeviceId,
        until: day.year > 0 && day.month > 0 && day.day > 0 ? day : nil, working: sheetWorking,
        dateRefused: sheetDateRefused, notice: refusal,
        noticeTryAgain: refusal != nil && sheetNoticeTryAgain)
    }
    var failure: PauseFailurePresentation?
    if let failed = notice(failureNotice), let scope = PauseScopeKind(rawValue: failureScope) {
      failure = PauseFailurePresentation(
        scope: scope, deviceId: failureDeviceId.isEmpty ? nil : failureDeviceId, notice: failed,
        tryAgain: failureTryAgain, siteName: failureSiteName)
    }
    self.init(
      sheet: sheet, resumingScope: PauseScopeKind(rawValue: resumingScope),
      resumingDeviceId: resumingDeviceId.isEmpty ? nil : resumingDeviceId, failure: failure)
  }

  /// A request is on its way: one change at a time on a surface.
  public var working: Bool { resumingScope != nil || sheet?.working == true }

  /// The Device `deviceId` is being paused or resumed.
  public func workingOn(_ deviceId: String) -> Bool {
    if resumingDeviceId == deviceId { return true }
    guard let sheet else { return false }
    return sheet.working && sheet.scope == .device && sheet.deviceId == deviceId
  }

  /// The Resume of `deviceId` that did not happen, for the row of that Device and no other.
  public func failure(for deviceId: String) -> PauseFailurePresentation? {
    failure?.deviceId == deviceId ? failure : nil
  }
}

/// Pause and Resume of one surface of the shared Kotlin core, as the SwiftUI shell sees it. The
/// Lots, Lot detail and Devices services each have one; the app target adapts `IosPause` of
/// `ColdframeCore`. Only an Administrator or Owner does anything: the core ignores a Member's
/// call, and the controls do not exist for a Member.
@MainActor
public protocol PauseService: AnyObject {
  /// Pause in the Site menu: opens the sheet with Whole Site as its only scope.
  func openForSite()
  /// Pause on Lot detail or a Devices row: opens the sheet with This Device selected.
  func openForDevice(_ deviceId: String)
  func setScope(_ scope: PauseScopeKind)
  /// The calendar date the User picked, or nil for no end. No time zone is involved.
  func setUntil(_ day: PauseDay?)
  /// Closes the sheet and sends nothing. The core ignores it while the request runs.
  func cancel()
  /// The primary button, and Try again inside the sheet. Accepted, the core closes the sheet;
  /// refused, it stays open and says why.
  func save()
  /// Resume in the Site menu: one tap, no sheet.
  func resumeSite()
  /// Resume on Lot detail or a Devices row: lifts the Device's own Pause.
  func resumeDevice(_ deviceId: String)
  /// Try again after a Resume that did not happen.
  func retry()
}
