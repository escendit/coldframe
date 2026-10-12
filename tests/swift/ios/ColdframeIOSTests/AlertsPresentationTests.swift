import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

/// Alerts as the core flattens them into an `AlertsSnapshot`, at the clock of `Overview`
/// (6 October 2026, 10:00 UTC). Times are Unix milliseconds in decimal, empty when absent.
enum AlertsFixture {
  /// 05:45 today.
  static let openedMs: Int64 = 1_791_265_500_000
  /// 06:40 today.
  static let closedMs: Int64 = 1_791_268_800_000

  /// One Alert as the core flattens it into the `alert…` lists of `AlertsSnapshot`.
  struct Alert {
    var id: String
    var group = "threshold"
    var variant = "threshold"
    var condition: String
    var eyebrow: String
    var icon: String
    var quantity = "soil_moisture"
    var lotId = "lot-t"
    var lotName = "Tomatoes"
    var deviceId = "7c19aa01bb02cc03"
    var openedAt = String(AlertsFixture.openedMs)
    var closedAt = ""
    var target = "lot"
    /// The title sentence the core names (Story 7.2), with its parts; empty when it named none.
    var title = ""
    var deviceKind = ""
    var deviceName = ""
    var silentSince = ""
    var durationValue = ""
    var durationUnit = ""

    /// The same Alert once closed at 06:40, as the core flattens it: only the group, the variant
    /// and the time change.
    func closed(id: String) -> Alert {
      var alert = self
      alert.id = id
      alert.group = "closed"
      alert.variant = "closed"
      alert.closedAt = String(AlertsFixture.closedMs)
      // A closed row has no " for …".
      alert.durationValue = ""
      alert.durationUnit = ""
      return alert
    }
  }

  static let needsWater = Alert(
    id: "a1", variant: "needsWater", condition: "needsWater", eyebrow: "needsWater",
    icon: "rain-drop", title: "needsWater")
  static let tooWet = Alert(
    id: "a2", condition: "tooWet", eyebrow: "aboveHigh", icon: "arrow--up", lotId: "lot-h",
    lotName: "Herbs", title: "tooWet")
  static let tooCold = Alert(
    id: "a3", condition: "tooLow", eyebrow: "belowLow", icon: "arrow--down",
    quantity: "air_temperature", lotId: "lot-b", lotName: "Beans", title: "tooLow")
  static let tooHumid = Alert(
    id: "a4", condition: "tooHigh", eyebrow: "aboveHigh", icon: "arrow--up",
    quantity: "relative_humidity", lotId: "lot-b", lotName: "Beans", title: "tooHigh")
  /// A silent Alert from before the Server recorded a Device kind and a silent-since.
  static let silent = Alert(
    id: "h1", group: "health", variant: "health", condition: "silent", eyebrow: "health",
    icon: "help", quantity: "", lotId: "lot-p", lotName: "Peppers", target: "devices",
    title: "nodeOnLotSilent", deviceName: "7C19")
  static let battery = Alert(
    id: "h2", group: "health", variant: "health", condition: "battery", eyebrow: "health",
    icon: "battery--low", quantity: "", lotId: "lot-p", lotName: "Peppers", target: "devices",
    title: "battery", deviceKind: "node", deviceName: "7C19")
  static let uncalibrated = Alert(
    id: "h3", group: "health", variant: "health", condition: "uncalibrated", eyebrow: "health",
    icon: "tools", quantity: "", lotId: "lot-c", lotName: "Carrots", title: "uncalibrated",
    deviceKind: "node", deviceName: "7C19")
  /// A kind the core does not know.
  static let unknownKind = Alert(
    id: "h4", group: "health", variant: "health", condition: "unknown", eyebrow: "health",
    icon: "help", quantity: "", lotId: "lot-c", lotName: "Carrots", target: "devices",
    title: "healthOnLot", deviceName: "7C19")
  /// 01:05 today: when the silent Node was last heard, 6 h before the Alert opened at 07:05.
  static let silentSinceMs: Int64 = Overview.earlierMs
  /// Story 7.2: a silent Node on Lot "Beans", last heard at 01:05.
  static let silentNode = Alert(
    id: "s1", group: "health", variant: "health", condition: "silent", eyebrow: "health",
    icon: "help", quantity: "", lotId: "lot-b", lotName: "Beans", deviceId: "7c19aa01bb02cc03",
    target: "devices", title: "nodeOnLotSilent", deviceKind: "node", deviceName: "7C19",
    silentSince: String(silentSinceMs), durationValue: "6", durationUnit: "hours")
  /// A silent Hub: no Lot, named by its short name.
  static let silentHub = Alert(
    id: "s2", group: "health", variant: "health", condition: "silent", eyebrow: "health",
    icon: "help", quantity: "", lotId: "", lotName: "", deviceId: "3f2a9c0d1e4b5a67",
    target: "devices", title: "hubSilent", deviceKind: "hub", deviceName: "3F2A",
    silentSince: String(Overview.nowMs - 5 * 60_000), durationValue: "5", durationUnit: "minutes")
  /// A silent Node on no Lot, named by its short name.
  static let silentSpareNode = Alert(
    id: "s3", group: "health", variant: "health", condition: "silent", eyebrow: "health",
    icon: "help", quantity: "", lotId: "", lotName: "", deviceId: "9c0d0e0f10111213",
    target: "devices", title: "nodeSilent", deviceKind: "node", deviceName: "9C0D",
    silentSince: String(silentSinceMs), durationValue: "6", durationUnit: "hours")
  static let closedSilentHub = silentHub.closed(id: "s4")

  /// The Health rows of silent Devices, in the Server's order: open, then closed.
  static let silentDevices = [silentNode, silentHub, silentSpareNode, closedSilentHub]
  static let closedNeedsWater: Alert = {
    var alert = needsWater.closed(id: "c1")
    alert.lotId = "lot-s"
    alert.lotName = "Squash"
    return alert
  }()
  static let closedTooWet = tooWet.closed(id: "c2")
  static let closedSilent = silent.closed(id: "c3")

  /// One Alert per row variant and Health cause, in the Server's order: open, then closed.
  static let every = [
    needsWater, tooWet, tooCold, tooHumid, silent, battery, uncalibrated, unknownKind,
    closedNeedsWater, closedTooWet, closedSilent,
  ]

  static func alerts(
    _ alerts: [Alert] = every, surface: String = "ready", notice: String? = nil,
    siteId: String? = "a", siteName: String? = "Home garden", openCount: Int? = nil,
    refreshing: Bool = false
  ) -> AlertsPresentation {
    AlertsPresentation(
      surface: surface, notice: notice, siteId: siteId, siteName: siteName,
      openCount: openCount ?? alerts.filter(\.closedAt.isEmpty).count, refreshing: refreshing,
      ids: alerts.map(\.id), groups: alerts.map(\.group), variants: alerts.map(\.variant),
      conditions: alerts.map(\.condition), eyebrows: alerts.map(\.eyebrow),
      icons: alerts.map(\.icon), quantities: alerts.map(\.quantity),
      lotIds: alerts.map(\.lotId), lotNames: alerts.map(\.lotName),
      deviceIds: alerts.map(\.deviceId), openedAts: alerts.map(\.openedAt),
      closedAts: alerts.map(\.closedAt), targets: alerts.map(\.target),
      titles: alerts.map(\.title), deviceKinds: alerts.map(\.deviceKind),
      deviceNames: alerts.map(\.deviceName), silentSinces: alerts.map(\.silentSince),
      durationValues: alerts.map(\.durationValue), durationUnits: alerts.map(\.durationUnit))
  }

  static func row(_ alert: Alert) throws -> AlertRowPresentation {
    try #require(alerts([alert]).rows.first)
  }
}

private let context = Overview.context

@Test("UX-DR64 open Alerts group as Threshold then Health, newest first, then Closed")
func alertsGroups() {
  let alerts = AlertsFixture.alerts()

  #expect(alerts.groups.map(\.kind) == [.threshold, .health, .closed])
  #expect(alerts.groups[0].rows.map(\.id) == ["a1", "a2", "a3", "a4"])
  #expect(alerts.groups[1].rows.map(\.id) == ["h1", "h2", "h3", "h4"])
  #expect(alerts.groups[2].rows.map(\.id) == ["c1", "c2", "c3"])
  #expect(
    alerts.groups.map(\.kind.title) == [
      .alertsGroupThreshold, .alertsGroupHealth, .alertsGroupClosed,
    ])
  #expect(!alerts.showsEmpty)
}

@Test("UX-DR64 the Server's order is kept inside a group: nothing sorts")
func alertsKeepTheServersOrder() {
  let alerts = AlertsFixture.alerts([
    AlertsFixture.silent, AlertsFixture.tooWet, AlertsFixture.battery, AlertsFixture.needsWater,
  ])

  #expect(alerts.groups.map(\.kind) == [.threshold, .health])
  #expect(alerts.groups[0].rows.map(\.id) == ["a2", "a1"])
  #expect(alerts.groups[1].rows.map(\.id) == ["h1", "h2"])
}

@Test("UX-DR64 UX-DR82 a Site without Alerts reads No open Alerts. and has no group")
func alertsEmpty() throws {
  let alerts = AlertsFixture.alerts([])

  #expect(alerts.showsEmpty)
  #expect(alerts.groups.isEmpty)
  #expect(alerts.openCount == 0)
  let entries = try Catalogue.entries()
  #expect(entries[L10n.alertsEmpty.rawValue] == "No open Alerts.")
}

@Test("UX-DR82 with only closed Alerts, No open Alerts. is followed by Closed with its rows")
func alertsOnlyClosed() {
  let alerts = AlertsFixture.alerts([AlertsFixture.closedNeedsWater])

  #expect(alerts.showsEmpty)
  #expect(alerts.groups.map(\.kind) == [.closed])
  #expect(alerts.groups[0].rows.map(\.id) == ["c1"])
  #expect(alerts.openCount == 0)
}

@Test("UX-DR25 only an open low-side soil-moisture Threshold Alert is the orange needs-water row")
func alertRowNeedsWater() throws {
  let row = try AlertsFixture.row(AlertsFixture.needsWater)

  #expect(row.variant == .needsWater)
  #expect(row.variant.background == ColorTokens.statusWaterFill)
  #expect(row.variant.ink == ColorTokens.statusWaterInk)
  #expect(row.variant.border == .none)
  #expect(!row.variant.isHatched)
  #expect(row.icon == .rainDrop)
  #expect(row.titleText(context) == "Tomatoes needs water")
  #expect(row.eyebrowText(context) == "Needs water · 05:45")
  // Every other row is not orange.
  for alert in AlertsFixture.every where alert.id != "a1" {
    let other = try AlertsFixture.row(alert)
    #expect(other.variant != .needsWater, "\(alert.id)")
    #expect(other.variant.background != ColorTokens.statusWaterFill, "\(alert.id)")
  }
}

@Test("UX-DR25 too wet is the neutral Threshold row with arrow up, never orange")
func alertRowTooWet() throws {
  let row = try AlertsFixture.row(AlertsFixture.tooWet)

  #expect(row.variant == .threshold)
  #expect(row.variant.background == ColorTokens.layer01)
  #expect(row.variant.ink == ColorTokens.textPrimary)
  #expect(
    row.variant.border
      == LotTileBorder(style: .solid, width: 2, color: ColorTokens.borderStrong))
  #expect(row.icon == .arrowUp)
  #expect(row.titleText(context) == "Herbs too wet")
  #expect(row.eyebrowText(context) == "Threshold Alert · above high · 05:45")
}

@Test("UX-DR25 another quantity names itself and the side, with arrow down or arrow up")
func alertRowOtherQuantities() throws {
  let cold = try AlertsFixture.row(AlertsFixture.tooCold)
  let humid = try AlertsFixture.row(AlertsFixture.tooHumid)

  #expect(cold.variant == .threshold)
  #expect(cold.icon == .arrowDown)
  #expect(cold.titleText(context) == "Beans temperature too low")
  #expect(cold.eyebrowText(context) == "Threshold Alert · below low · 05:45")
  #expect(humid.variant == .threshold)
  #expect(humid.icon == .arrowUp)
  #expect(humid.titleText(context) == "Beans humidity too high")
  #expect(humid.eyebrowText(context) == "Threshold Alert · above high · 05:45")
}

@Test("UX-DR26 Health rows are hatched and dashed with text on a plate and an icon by cause")
func alertRowHealth() throws {
  let silent = try AlertsFixture.row(AlertsFixture.silent)
  let battery = try AlertsFixture.row(AlertsFixture.battery)
  let uncalibrated = try AlertsFixture.row(AlertsFixture.uncalibrated)

  for row in [silent, battery, uncalibrated] {
    #expect(row.variant == .health)
    #expect(row.variant.isHatched)
    #expect(row.variant.background == nil)
    #expect(
      row.variant.border
        == LotTileBorder(style: .dashed, width: 1, color: ColorTokens.statusUnknownBorder))
    #expect(row.eyebrowText(context) == "Health Alert")
  }
  #expect(silent.icon == .help)
  #expect(battery.icon == .batteryLow)
  #expect(uncalibrated.icon == .tools)
  #expect(silent.titleText(context) == "Node on Lot 'Peppers' silent")
  #expect(battery.titleText(context) == "Node battery low on Lot 'Peppers'")
  #expect(uncalibrated.titleText(context) == "Soil Sensor on Lot 'Carrots' needs Calibration")
}

@Test("UX-DR26 a kind the core does not know is a Health row with the help icon")
func alertRowUnknownKind() throws {
  let row = try AlertsFixture.row(AlertsFixture.unknownKind)

  #expect(row.group == .health)
  #expect(row.variant == .health)
  #expect(row.condition == .unknown)
  #expect(row.icon == .help)
  #expect(row.eyebrowText(context) == "Health Alert")
  #expect(row.titleText(context) == "Health Alert on Lot 'Carrots'")
}

@Test("UX-DR25 UX-DR26 a closed row is an outline in text-secondary and says when it closed")
func alertRowClosed() throws {
  let water = try AlertsFixture.row(AlertsFixture.closedNeedsWater)
  let wet = try AlertsFixture.row(AlertsFixture.closedTooWet)
  let silent = try AlertsFixture.row(AlertsFixture.closedSilent)

  for row in [water, wet, silent] {
    #expect(row.group == .closed)
    #expect(row.variant == .closed)
    #expect(row.variant.background == nil)
    #expect(!row.variant.isHatched)
    #expect(row.variant.ink == ColorTokens.textSecondary)
    #expect(
      row.variant.border
        == LotTileBorder(style: .solid, width: 1, color: ColorTokens.borderSubtle))
  }
  #expect(water.titleText(context) == "Squash needs water")
  #expect(water.eyebrowText(context) == "Needs water · closed 06:40")
  #expect(wet.eyebrowText(context) == "Threshold Alert · above high · closed 06:40")
  #expect(silent.eyebrowText(context) == "Health Alert · closed 06:40")
}

@Test("UX-DR99 the four row variants differ in fill and border, not by colour alone")
func alertRowVariantsDiffer() {
  let shapes = AlertRowVariantKind.allCases.map {
    "\($0.background == nil)-\($0.isHatched)-\($0.border.style)-\($0.border.width)"
  }
  #expect(AlertRowVariantKind.allCases.count == 4)
  #expect(Set(shapes).count == 4)
}

@Test("UX-DR98 a row is one element whose label states the condition and when it started")
func alertRowSpokenLabel() throws {
  let open = try AlertsFixture.row(AlertsFixture.needsWater)
  let closed = try AlertsFixture.row(AlertsFixture.closedNeedsWater)
  let health = try AlertsFixture.row(AlertsFixture.silent)

  #expect(open.spokenText(context) == "Tomatoes needs water, since 05:45")
  #expect(closed.spokenText(context) == "Squash needs water, since 05:45, closed 06:40")
  #expect(health.spokenText(context) == "Node on Lot 'Peppers' silent, since 05:45")
  #expect(open.traits == [.button])
}

@Test("UX-DR98 times follow the when rule: today the clock, earlier the weekday, older the date")
func alertRowTimes() throws {
  var yesterday = AlertsFixture.needsWater
  yesterday.openedAt = String(AlertsFixture.openedMs - 86_400_000)
  var older = AlertsFixture.needsWater
  older.openedAt = String(AlertsFixture.openedMs - 10 * 86_400_000)

  #expect(try AlertsFixture.row(yesterday).eyebrowText(context) == "Needs water · Mon")
  // 26 September: a date, as the locale writes it, and no clock time.
  let date = context.day(
    Date(timeIntervalSince1970: Double(AlertsFixture.openedMs) / 1000 - 864_000))
  #expect(date.hasPrefix("26 Sep"))
  #expect(try AlertsFixture.row(older).spokenText(context) == "Tomatoes needs water, since \(date)")
}

@Test("UX-DR64 Threshold and uncalibrated rows open Lot detail; silent and battery open Devices")
func alertRowTargets() throws {
  #expect(
    try AlertsFixture.row(AlertsFixture.needsWater).target == .lot(id: "lot-t", name: "Tomatoes"))
  #expect(try AlertsFixture.row(AlertsFixture.tooWet).target == .lot(id: "lot-h", name: "Herbs"))
  #expect(
    try AlertsFixture.row(AlertsFixture.uncalibrated).target
      == .lot(id: "lot-c", name: "Carrots"))
  #expect(
    try AlertsFixture.row(AlertsFixture.closedNeedsWater).target
      == .lot(id: "lot-s", name: "Squash"))
  #expect(try AlertsFixture.row(AlertsFixture.silent).target == .devices)
  #expect(try AlertsFixture.row(AlertsFixture.battery).target == .devices)
  #expect(try AlertsFixture.row(AlertsFixture.closedSilent).target == .devices)
  #expect(try AlertsFixture.row(AlertsFixture.unknownKind).target == .devices)
}

@Test("UX-DR64 a failed load shows the notice with Try again, no rows and no count")
func alertsFailed() {
  let failed = AlertsFixture.alerts(surface: "failed", notice: "unreachable", openCount: 4)

  #expect(failed.surface == .failed(.unreachable))
  #expect(failed.failure == .unreachable)
  #expect(failed.failure?.message == .alertsUnreachable)
  #expect(failed.failure?.offersTryAgain == true)
  #expect(failed.rows.isEmpty)
  #expect(failed.groups.isEmpty)
  #expect(!failed.showsEmpty)
  #expect(failed.openCount == 0)

  let certificate = AlertsFixture.alerts(surface: "failed", notice: "certificate")
  #expect(certificate.failure == .certificate)
  #expect(certificate.failure?.offersTryAgain == false)
  // A notice this client does not know reads as unreachable.
  #expect(AlertsFixture.alerts(surface: "failed", notice: "later").failure == .unreachable)
}

@Test("UX-DR64 nothing is shown while the Alerts are read, signed out or without a current Site")
func alertsWaiting() {
  for surface in ["idle", "loading", "later"] {
    let alerts = AlertsFixture.alerts(surface: surface)
    #expect(alerts.surface == .waiting, "\(surface)")
    #expect(alerts.rows.isEmpty)
    #expect(!alerts.showsEmpty)
    #expect(alerts.openCount == 0)
  }
  #expect(AlertsFixture.alerts(siteId: nil).surface == .waiting)
  #expect(AlertsPresentation.waiting.surface == .waiting)
}

@Test("UX-DR64 the open count is the Server's, also when it is more than the rows read")
func alertsOpenCount() {
  #expect(AlertsFixture.alerts().openCount == 8)
  #expect(AlertsFixture.alerts([AlertsFixture.needsWater], openCount: 250).openCount == 250)
  #expect(AlertsFixture.alerts([AlertsFixture.needsWater], openCount: -1).openCount == 0)
}

@Test("UX-DR64 ragged lists are cut to the shortest, and a row without an opened time is dropped")
func alertsRaggedSnapshot() {
  let three = [AlertsFixture.needsWater, AlertsFixture.tooWet, AlertsFixture.tooCold]
  let ragged = AlertsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home garden", openCount: 2,
    refreshing: false, ids: three.map(\.id), groups: three.map(\.group),
    variants: three.map(\.variant), conditions: three.map(\.condition),
    eyebrows: three.map(\.eyebrow), icons: three.map(\.icon),
    quantities: ["soil_moisture", "soil_moisture"], lotIds: three.map(\.lotId),
    lotNames: three.map(\.lotName), deviceIds: three.map(\.deviceId),
    openedAts: [String(AlertsFixture.openedMs), "soon", "1"], closedAts: ["", "", ""],
    targets: three.map(\.target))

  #expect(ragged.rows.map(\.id) == ["a1"])
}

@Test("UX-DR26 a name the core added later is read the careful way: Health, help, Devices")
func alertsUnknownNames() throws {
  var later = AlertsFixture.needsWater
  later.group = "frost"
  later.variant = "frost"
  later.condition = "frost"
  later.eyebrow = "frost"
  later.icon = "snowflake"
  later.target = "weather"
  later.title = "frost"
  let open = try AlertsFixture.row(later)
  later.closedAt = String(AlertsFixture.closedMs)
  let closed = try AlertsFixture.row(later)

  #expect(open.group == .health)
  #expect(open.variant == .health)
  #expect(open.condition == .unknown)
  #expect(open.eyebrow == .health)
  #expect(open.icon == .help)
  #expect(open.target == .devices)
  #expect(open.title == .healthOnLot)
  #expect(open.titleText(context) == "Health Alert on Lot 'Tomatoes'")
  #expect(closed.group == .closed)
  #expect(closed.variant == .closed)
  // A quantity without a word names only the Lot.
  var noWord = AlertsFixture.tooCold
  noWord.quantity = ""
  #expect(try AlertsFixture.row(noWord).titleText(context) == "Health Alert on Lot 'Beans'")
}

@Test("UX-DR14 every Alerts string is in the catalogue, sentence case, with its placeholders")
func alertsCopy() throws {
  let entries = try Catalogue.entries()

  #expect(entries["alerts_empty"] == "No open Alerts.")
  #expect(entries["alerts_group_closed"] == "Closed")
  #expect(entries["alert_eyebrow_needs_water"] == "Needs water")
  #expect(entries["alert_eyebrow_health"] == "Health Alert")
  #expect(entries["alert_eyebrow_below_low"] == "Threshold Alert · below low")
  #expect(entries["alert_eyebrow_above_high"] == "Threshold Alert · above high")
  #expect(entries["alert_eyebrow_closed"] == "%1$@ · closed %2$@")
  #expect(entries["nav_alerts_count"] == "Alerts · %1$lld")
  // Uppercase comes from the style, and the row never says "All good".
  for (key, value) in entries where key.hasPrefix("alert") {
    #expect(value.range(of: #"\p{Lu}{2,}"#, options: .regularExpression) == nil, "\(key)")
    #expect(!value.lowercased().contains("all good"), "\(key)")
  }
  #expect(Typography.statusLabel.uppercase)
}

// MARK: - Health rows of silent Devices (Story 7.2)

@Test("UX-DR117 a silent Node is named by its Lot, a Hub and a Node on no Lot by their short name")
func alertTitlesOfSilentDevices() throws {
  let node = try AlertsFixture.row(AlertsFixture.silentNode)
  let hub = try AlertsFixture.row(AlertsFixture.silentHub)
  let spare = try AlertsFixture.row(AlertsFixture.silentSpareNode)

  #expect(node.titleText(context) == "Node on Lot 'Beans' silent for 6 h")
  #expect(hub.titleText(context) == "Hub 3F2A silent for 5 min")
  #expect(spare.titleText(context) == "Node 9C0D silent for 6 h")
  #expect(node.title == .nodeOnLotSilent)
  #expect(hub.title == .hubSilent)
  #expect(spare.title == .nodeSilent)
  #expect(node.deviceKind == .node)
  #expect(hub.deviceKind == .hub)
  #expect(hub.deviceName == "3F2A")
  #expect(node.silentSince == Date(timeIntervalSince1970: Double(Overview.earlierMs) / 1000))
  #expect(node.duration == LotDurationPresentation(value: 6, unit: .hours))
  // They are Health rows like any other: hatched, the help icon, a bare eyebrow.
  for row in [node, hub, spare] {
    #expect(row.group == .health)
    #expect(row.variant == .health)
    #expect(row.icon == .help)
    #expect(row.eyebrowText(context) == "Health Alert")
  }
  #expect(hub.spokenText(context) == "Hub 3F2A silent for 5 min, since 05:45")
}

@Test("UX-DR117 the silence is the core's, in its unit: minutes, hours or days")
func alertTitleDurations() throws {
  var days = AlertsFixture.silentHub
  days.durationValue = "2"
  days.durationUnit = "days"
  var hour = AlertsFixture.silentSpareNode
  hour.durationValue = "1"
  hour.durationUnit = "hours"

  #expect(try AlertsFixture.row(days).titleText(context) == "Hub 3F2A silent for 2 d")
  #expect(try AlertsFixture.row(hour).titleText(context) == "Node 9C0D silent for 1 h")
}

@Test("UX-DR117 a closed row and an open one without a silent-since drop the duration")
func alertTitlesWithoutDuration() throws {
  let closed = try AlertsFixture.row(AlertsFixture.closedSilentHub)
  #expect(closed.titleText(context) == "Hub 3F2A silent")
  #expect(closed.eyebrowText(context) == "Health Alert · closed 06:40")
  #expect(closed.spokenText(context) == "Hub 3F2A silent, since 05:45, closed 06:40")

  var hub = AlertsFixture.silentHub
  var spare = AlertsFixture.silentSpareNode
  var node = AlertsFixture.silentNode
  for index in 0..<3 {
    // The core leaves both empty; one without the other, or a unit this client does not know,
    // reads the same way.
    hub.durationValue = index == 1 ? "5" : ""
    hub.durationUnit = index == 0 ? "" : index == 1 ? "fortnights" : "minutes"
    #expect(try AlertsFixture.row(hub).titleText(context) == "Hub 3F2A silent", "\(index)")
  }
  spare.silentSince = ""
  spare.durationValue = ""
  spare.durationUnit = ""
  node.silentSince = ""
  node.durationValue = ""
  node.durationUnit = ""
  #expect(try AlertsFixture.row(spare).titleText(context) == "Node 9C0D silent")
  #expect(try AlertsFixture.row(node).titleText(context) == "Node on Lot 'Beans' silent")
  // The Alert of before this story: no Device kind, no silent-since, named by its Lot.
  #expect(
    try AlertsFixture.row(AlertsFixture.silent).titleText(context)
      == "Node on Lot 'Peppers' silent")
}

@Test(
  "UX-DR117 UX-DR26 a Health kind the core does not know is the neutral row, with or without a Lot"
)
func alertTitlesNeutral() throws {
  var onLot = AlertsFixture.unknownKind
  onLot.title = "healthOnLot"
  var bare = AlertsFixture.unknownKind
  bare.title = "health"
  bare.lotId = ""
  bare.lotName = ""
  // A title this client does not know yet: the neutral title that fits what the row has.
  var laterOnLot = AlertsFixture.silentNode
  laterOnLot.title = "frozen"
  var laterBare = AlertsFixture.silentHub
  laterBare.title = "frozen"

  #expect(try AlertsFixture.row(onLot).titleText(context) == "Health Alert on Lot 'Carrots'")
  #expect(try AlertsFixture.row(bare).titleText(context) == "Health Alert")
  #expect(try AlertsFixture.row(laterOnLot).titleText(context) == "Health Alert on Lot 'Beans'")
  #expect(try AlertsFixture.row(laterBare).titleText(context) == "Health Alert")
  #expect(try AlertsFixture.row(bare).icon == .help)
  #expect(try AlertsFixture.row(bare).variant == .health)
}

@Test("UX-DR117 a snapshot without titles reads as before: each condition's own sentence")
func alertTitlesFallBackToTheCondition() throws {
  let alerts = AlertsFixture.every
  let untitled = AlertsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home garden", openCount: 8,
    refreshing: false, ids: alerts.map(\.id), groups: alerts.map(\.group),
    variants: alerts.map(\.variant), conditions: alerts.map(\.condition),
    eyebrows: alerts.map(\.eyebrow), icons: alerts.map(\.icon),
    quantities: alerts.map(\.quantity), lotIds: alerts.map(\.lotId),
    lotNames: alerts.map(\.lotName), deviceIds: alerts.map(\.deviceId),
    openedAts: alerts.map(\.openedAt), closedAts: alerts.map(\.closedAt),
    targets: alerts.map(\.target))
  let titled = AlertsFixture.alerts()

  #expect(untitled.rows.count == alerts.count)
  #expect(untitled.rows.allSatisfy { $0.title == nil && $0.duration == nil })
  #expect(untitled.rows.map { $0.titleText(context) } == titled.rows.map { $0.titleText(context) })
  #expect(untitled.rows[4].titleText(context) == "Node on Lot 'Peppers' silent")
}

@Test("UX-DR64 UX-DR117 every silent row opens Devices, and so does any row without a Lot")
func alertTargetsOfSilentDevices() throws {
  for alert in AlertsFixture.silentDevices {
    #expect(try AlertsFixture.row(alert).target == .devices, "\(alert.id)")
  }
  // The core sends a row without a Lot to Devices; a target that says otherwise is not followed.
  var odd = AlertsFixture.silentHub
  odd.target = "lot"
  #expect(try AlertsFixture.row(odd).target == .devices)
  // Health rows sit in the Health group after the Threshold Alerts; the closed one under Closed.
  let alerts = AlertsFixture.alerts([AlertsFixture.needsWater] + AlertsFixture.silentDevices)
  #expect(alerts.groups.map(\.kind) == [.threshold, .health, .closed])
  #expect(alerts.groups[1].rows.map(\.id) == ["s1", "s2", "s3"])
  #expect(alerts.openCount == 4)
}

@Test("UX-DR117 the titles of silent Devices are in the catalogue, sentence case, with their parts")
func alertTitleCopy() throws {
  let entries = try Catalogue.entries()

  #expect(entries["alert_title_silent"] == "Node on Lot '%1$@' silent")
  #expect(entries["alert_title_silent_for"] == "Node on Lot '%1$@' silent for %2$@")
  #expect(entries["alert_title_hub_silent"] == "Hub %@ silent")
  #expect(entries["alert_title_hub_silent_for"] == "Hub %1$@ silent for %2$@")
  #expect(entries["alert_title_node_silent"] == "Node %@ silent")
  #expect(entries["alert_title_node_silent_for"] == "Node %1$@ silent for %2$@")
  #expect(entries["alert_title_health"] == "Health Alert")
}

// `AlertsActions` lives with the views, which exist only where SwiftUI does.
#if canImport(SwiftUI)
  @Test("UX-DR98 the actions reach the service one for one")
  @MainActor
  func alertsActionsReachTheService() {
    final class Spy: AlertsService {
      var calls: [String] = []
      func observe(_ onChange: @escaping @MainActor (AlertsPresentation) -> Void) {}
      func load() { calls.append("load") }
      func refresh() { calls.append("refresh") }
      func tick() { calls.append("tick") }
    }
    let spy = Spy()
    let actions = AlertsActions(service: spy)

    actions.load()
    #expect(spy.calls == ["load"])
    actions.refresh()
    actions.load()
    actions.tick()

    #expect(spy.calls == ["load", "refresh", "load", "tick"])
  }
#endif
