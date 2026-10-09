import Foundation
import Testing

@testable import ColdframeIOS

/// A `PushPresentation` as `CorePushService` builds it from the core's flat `PushSnapshot`.
enum PushFixture {
  static func push(
    permission: String = "granted", noticeVisible: Bool = false, promptDue: Bool = false,
    routeTarget: String? = nil, routeSiteId: String? = nil, routeLotId: String? = nil,
    routeLotName: String? = nil
  ) -> PushPresentation {
    PushPresentation(
      permission: permission, noticeVisible: noticeVisible, promptDue: promptDue,
      routeTarget: routeTarget, routeSiteId: routeSiteId, routeLotId: routeLotId,
      routeLotName: routeLotName)
  }

  static let granted = push()
  /// Signed in on a Site, never asked on this phone.
  static let promptDue = push(permission: "unknown", promptDue: true)
  static let denied = push(permission: "denied", noticeVisible: true)
  static let tappedAlert = push(
    routeTarget: "lotDetail", routeSiteId: "site-home", routeLotId: "lot-t",
    routeLotName: "Tomatoes")
  static let tappedSummary = push(routeTarget: "overview", routeSiteId: "site-home")
}

/// One example of `packages/asyncapi/fixtures`: what the Server sends to APNs, and where a tap
/// must lead.
private func apnsFixture(_ kind: String) throws -> (payload: [String: Any], route: [String: Any]) {
  let data = try Data(contentsOf: Repo.url("packages/asyncapi/fixtures/push.\(kind).json"))
  let json = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
  let apns = try #require(json["apns"] as? [String: Any])
  return (
    try #require(apns["payload"] as? [String: Any]), try #require(json["route"] as? [String: Any])
  )
}

private func offenders(_ pattern: String, in path: String) throws -> [String] {
  let expression = try NSRegularExpression(pattern: pattern)
  return try Repo.swiftSources(path).filter { file in
    let text = try String(contentsOf: file, encoding: .utf8)
    return expression.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil
  }.map(\.lastPathComponent)
}

// MARK: - The permission and its one prompt

@Test("UX-DR122 the first landing on a Site overview shows the one line of why, and Continue asks")
func whyLineOnTheFirstOverview() throws {
  let push = PushFixture.promptDue

  #expect(push.permission == .unknown)
  #expect(push.overviewNotice == .why)
  #expect(PushNoticeKind.why.message == .pushWhy)
  #expect(PushNoticeKind.why.actionLabel == .pushWhyContinue)
  #expect(PushNoticeKind.why.action == .ask)
  let entries = try Catalogue.entries()
  #expect(entries["push_why"] == "Coldframe tells you when a Lot needs water.")
  #expect(entries["push_why_continue"] == "Continue")
  // The line belongs to the overview: My notifications never asks.
  #expect(push.settingsNotice == nil)
}

@Test("UX-DR122 once the prompt was answered the why-line is gone, and nothing shows before")
func whyLineShowsOnce() {
  #expect(PushFixture.granted.overviewNotice == nil)
  #expect(PushPresentation.idle.overviewNotice == nil)
  #expect(PushPresentation.idle.settingsNotice == nil)
  #expect(PushPresentation.idle.permission == .unknown)
  #expect(PushPresentation.idle.route == nil)
  // A permission word this client does not know is not a denial.
  #expect(PushFixture.push(permission: "later").permission == .unknown)
  // While the core asks for the prompt, the line of why comes before any notice.
  #expect(
    PushFixture.push(permission: "unknown", noticeVisible: true, promptDue: true).overviewNotice
      == .why)
}

@Test("UX-DR122 UX-DR121 the prompt asks for alerts and sound only, and names the OS answer")
func permissionRequest() {
  #expect(PushDisplay.authorization == [.alert, .sound])
  #expect(
    OsNotificationPermission.allCases.map(\.rawValue) == ["notDetermined", "granted", "denied"])
  #expect(OsNotificationPermission(promptGranted: true) == .granted)
  #expect(OsNotificationPermission(promptGranted: false) == .denied)
}

// MARK: - Notifications off

@Test("UX-DR88 notifications off shows the notice in My notifications and above the overview tiles")
func notificationsOffNotice() throws {
  let push = PushFixture.denied

  #expect(push.permission == .denied)
  #expect(push.settingsNotice == .off)
  #expect(push.overviewNotice == .off)
  #expect(PushNoticeKind.off.message == .pushOff)
  #expect(PushNoticeKind.off.actionLabel == .pushOpenSettings)
  #expect(PushNoticeKind.off.action == .openSettings)
  let entries = try Catalogue.entries()
  #expect(
    entries["push_off"]
      == "Notifications are off for Coldframe on this phone. You won't get Alerts.")
  #expect(entries["push_open_settings"] == "Open Settings")
}

@Test("UX-DR88 both notices are gone at the next foreground that finds the permission granted")
func notificationsOnHasNoNotice() {
  #expect(PushFixture.granted.settingsNotice == nil)
  #expect(PushFixture.granted.overviewNotice == nil)
  // The core decides: a denied permission it does not want shown (signed out) shows nothing.
  #expect(PushFixture.push(permission: "denied").settingsNotice == nil)
}

@Test("UX-DR88 the notice stands above the tiles of the overview and outside the settings' surface")
func noticesInTheViews() throws {
  let sites = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/SitesViews.swift")
  let garden = try #require(
    sites.components(separatedBy: "public struct GardenView").last?
      .components(separatedBy: "private func header").first)
  let notice = try #require(garden.range(of: "push.overviewNotice"))
  let header = try #require(garden.range(of: "header(context)"))
  let tiles = try #require(garden.range(of: "\n          tiles\n"))
  #expect(header.lowerBound < notice.lowerBound)
  #expect(notice.lowerBound < tiles.lowerBound)

  // In My notifications it shows whatever the settings' own surface is, also after a failed load.
  let settings = try Repo.text(
    "apps/swift/ios/Sources/ColdframeIOS/UI/NotificationSettingsViews.swift")
  let screen = try #require(
    settings.components(separatedBy: "public struct MyNotificationsView").last)
  let off = try #require(screen.range(of: "push.settingsNotice"))
  let surface = try #require(screen.range(of: "switch presentation.surface"))
  #expect(off.lowerBound < surface.lowerBound)
  // Never dismissable: the notice component has one action and no way to close it.
  let views = try Repo.text("apps/swift/ios/Sources/ColdframeIOS/UI/PushViews.swift")
  #expect(views.contains("InlineNotice("))
  #expect(!views.lowercased().contains("dismiss"))
}

// MARK: - Where a tap leads

@Test("UX-DR120 a tapped Alert opens Lot detail on the Garden tab, from any tab")
func tappedAlertRoute() throws {
  let route = try #require(PushFixture.tappedAlert.route)

  #expect(route == .lotDetail(siteId: "site-home", lotId: "lot-t", lotName: "Tomatoes"))
  #expect(route.tab == .garden)
  #expect(route.gardenPath == GardenPath(lot: OpenedLot(id: "lot-t", name: "Tomatoes")))
  #expect(!route.gardenPath.siteSettings)
}

@Test("UX-DR120 a tapped summary opens the Site overview, closing what was open on Garden")
func tappedSummaryRoute() throws {
  let route = try #require(PushFixture.tappedSummary.route)

  #expect(route == .overview(siteId: "site-home"))
  #expect(route.tab == .garden)
  #expect(route.gardenPath == .root)
  #expect(GardenPath.root.lot == nil)
  #expect(!GardenPath.root.siteSettings)
}

@Test("UX-DR120 an unknown target or a Lot without an ID ends on the overview; no tap, no route")
func routeFallbacks() {
  #expect(PushFixture.granted.route == nil)
  #expect(
    PushFixture.push(routeTarget: "devices", routeSiteId: "site-home").route
      == .overview(siteId: "site-home"))
  #expect(
    PushFixture.push(routeTarget: "lotDetail", routeSiteId: "site-home").route
      == .overview(siteId: "site-home"))
  // A Lot the core could not name is still that Lot.
  #expect(
    PushFixture.push(routeTarget: "lotDetail", routeSiteId: "s", routeLotId: "lot-t").route
      == .lotDetail(siteId: "s", lotId: "lot-t", lotName: ""))
}

@Test(
  "UX-DR120 the routing keys handed to the core are the coldframe object of the APNs payload",
  arguments: ["alert", "reminder", "summary"])
func routingKeysFromThePayload(kind: String) throws {
  let fixture = try apnsFixture(kind)
  let routing = try #require(PushPayload.routing(userInfo: fixture.payload))

  #expect(routing["kind"] == kind)
  #expect(routing["siteId"] == fixture.route["siteId"] as? String)
  #expect(routing["lotId"] == fixture.route["lotId"] as? String)
  #expect(routing["collapseId"]?.count == 32)
  #expect(Set(routing.keys).isSubset(of: ["kind", "siteId", "lotId", "alertId", "collapseId"]))
  #expect((routing["alertId"] == nil) == (kind == "summary"))
}

@Test("UX-DR120 a notification that is not Coldframe's own leads nowhere")
func foreignPayload() {
  #expect(PushPayload.routing(userInfo: ["aps": ["alert": "Hello"]]) == nil)
  #expect(PushPayload.routing(userInfo: ["coldframe": "alert"]) == nil)
  #expect(PushPayload.routing(userInfo: ["coldframe": [String: String]()]) == nil)
  // Only strings are routing keys.
  #expect(
    PushPayload.routing(userInfo: ["coldframe": ["kind": "summary", "siteId": 7] as [String: Any]])
      == ["kind": "summary"])
}

// MARK: - Presentation, registration and what the shell must not do

@Test(
  "UX-DR121 in the foreground a notification shows as banner and in the list with sound, no badge")
func foregroundDisplay() throws {
  #expect(PushDisplay.foreground == [.banner, .list, .sound])
  #expect(PushDisplayOption.allCases.map(\.rawValue) == ["banner", "list", "sound"])
  #expect(PushAuthorizationOption.allCases.map(\.rawValue) == ["alert", "sound"])
  // The grouping, the interruption level and the absent badge are the Server's payload.
  for kind in ["alert", "reminder", "summary"] {
    let aps = try #require(try apnsFixture(kind).payload["aps"] as? [String: Any])
    #expect(aps["badge"] == nil)
    #expect(aps["category"] == nil)
    #expect(aps["interruption-level"] as? String == "active")
    #expect(aps["thread-id"] as? String == (try apnsFixture(kind).route["siteId"] as? String))
  }
}

@Test("UX-DR121 UX-DR119 the shell sets no badge, no category, no action and schedules nothing")
func noBadgeNoActions() throws {
  #expect(!Repo.swiftSources("apps/swift/ios/App").isEmpty)
  // No count on the app icon, asked for or set, in the files that hold the OS calls.
  let counts = #"\.badge\b|[Bb]adgeCount|BadgeNumber"#
  #expect(try offenders(counts, in: "apps/swift/ios/App") == [])
  let pattern =
    #"UNNotificationCategory|UNNotificationAction|setNotificationCategories|criticalAlert|"#
    + #"timeSensitive|UNMutableNotificationContent|UNNotificationRequest\(|UNTimeIntervalNotification"#
  #expect(try offenders(pattern, in: "apps/swift/ios") == [])
}

@Test(
  "UX-DR115 the device token goes to the core as lowercase hex with the build's APNs environment")
func deviceTokenForTheCore() {
  #expect(PushPayload.hex(Data([0x00, 0x0f, 0xa1, 0xff])) == "000fa1ff")
  #expect(PushPayload.hex(Data()) == "")
  #expect(PushEnvironment(entitlement: "development") == .sandbox)
  #expect(PushEnvironment(entitlement: "production") == .production)
  #expect(PushEnvironment(entitlement: nil) == .production)
  #expect(PushEnvironment(entitlement: "") == .production)
  #expect(PushEnvironment.allCases.map(\.rawValue) == ["production", "sandbox"])
}

@Test("UX-DR115 the shell registers through the core only: no request of its own to any Server")
func noRestCallFromTheShell() throws {
  #expect(try offenders(#"URLSession|URLRequest|NSURLConnection"#, in: "apps/swift/ios") == [])
  // Only the adapters in App/ know the core.
  #expect(try offenders(#"import ColdframeCore"#, in: "apps/swift/ios/Sources") == [])
  let adapter = try Repo.text("apps/swift/ios/App/CorePushService.swift")
  for call in [
    "core.reportPermission(", "core.promptAnswered(", "core.deviceToken(", "core.opened(",
    "core.routeHandled(",
  ] {
    #expect(adapter.contains(call), "\(call)")
  }
}

@Test("UX-DR115 push is off in a committed build: no entitlement, team, profile or key in the repo")
func noCredentialsCommitted() throws {
  let config = try Repo.text("apps/swift/ios/Config/Coldframe.xcconfig")
  #expect(config.contains("\nCOLDFRAME_PUSH = NO\n"))
  #expect(
    config.contains("CODE_SIGN_ENTITLEMENTS = $(COLDFRAME_PUSH_ENTITLEMENTS_$(COLDFRAME_PUSH))"))
  #expect(config.contains("\nCOLDFRAME_PUSH_ENTITLEMENTS_NO =\n"))
  #expect(!config.contains("\nDEVELOPMENT_TEAM"))
  #expect(!config.contains("PROVISIONING_PROFILE"))
  let entitlements = try Repo.text("apps/swift/ios/Config/Coldframe.entitlements")
  #expect(entitlements.contains("<key>aps-environment</key>"))
  #expect(entitlements.contains("<string>$(COLDFRAME_APS_ENVIRONMENT)</string>"))
  let project = try Repo.text("apps/swift/ios/project.yml")
  #expect(!project.contains("DEVELOPMENT_TEAM"))
  #expect(!project.contains("PROVISIONING_PROFILE"))
  let secrets: Set<String> = ["mobileprovision", "provisionprofile", "p8", "p12", "cer"]
  let walker = try #require(
    FileManager.default.enumerator(
      at: Repo.url("apps/swift"), includingPropertiesForKeys: nil))
  let found = walker.compactMap { $0 as? URL }.filter { secrets.contains($0.pathExtension) }
  #expect(found.map(\.lastPathComponent) == [])
  // Both Info.plists hand the entitlement's environment to the adapter.
  for plist in ["Info.plist", "Info-Debug.plist"] {
    let text = try Repo.text("apps/swift/ios/Config/\(plist)")
    #expect(text.contains("<string>$(COLDFRAME_APS_ENVIRONMENT)</string>"), "\(plist)")
  }
}
