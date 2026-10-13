import ColdframeDesignTokens
import Foundation
import Testing

@testable import ColdframeIOS

/// A snapshot as the core flattens it: an Owner of Site "a" with one online Hub seen at 07:02 UTC.
private func devices(
  surface: String = "ready", notice: String? = nil, siteId: String? = "a",
  canAddHub: Bool = true, canAddNode: Bool = true,
  hubIds: [String] = ["3f2a9c0d1e4b5a67"], hubStatuses: [String] = ["online"],
  hubLastSeen: [String] = ["1791270120000"], nodes: [NodeFixture] = [],
  canManageNodes: Bool = false, lots: [LotFixture] = [], workingNodeId: String = "",
  failedNodeId: String = "", actionNotice: String = "", siteName: String? = "Home garden",
  hubSilence: [SilenceFixture.Device] = [], nodeSilence: [SilenceFixture.Device] = [],
  canSetSilenceWindow: Bool = false, silenceWindowWorkingId: String = "",
  silenceWindowFailedId: String = "", silenceWindowNotice: String = ""
) -> DevicesPresentation {
  DevicesPresentation(
    surface: surface, notice: notice, siteId: siteId, canAddHub: canAddHub,
    canAddNode: canAddNode, hubIds: hubIds, hubStatuses: hubStatuses, hubLastSeen: hubLastSeen,
    nodeIds: nodes.map(\.id), nodeLotNames: nodes.map(\.lot), nodeBatteries: nodes.map(\.battery),
    nodeBatteryLow: nodes.map(\.low), nodeCharging: nodes.map(\.charging),
    nodeLastSeen: nodes.map(\.lastSeen), canManageNodes: canManageNodes,
    nodeLotIds: nodes.map(\.lotId), lotIds: lots.map(\.id), lotNames: lots.map(\.name),
    lotHasNode: lots.map(\.hasNode), workingNodeId: workingNodeId, failedNodeId: failedNodeId,
    actionNotice: actionNotice, siteName: siteName, hubSilence: SilenceFixture.lists(hubSilence),
    nodeSilence: SilenceFixture.lists(nodeSilence), canSetSilenceWindow: canSetSilenceWindow,
    silenceWindowWorkingId: silenceWindowWorkingId, silenceWindowFailedId: silenceWindowFailedId,
    silenceWindowNotice: silenceWindowNotice)
}

/// Silence as the core flattens it into the `hub…` and `node…` silence lists of a
/// `DevicesSnapshot` (Story 7.2), at the clock of `Overview` (6 October 2026, 10:00 UTC): Hub
/// 3f2a… silent for 12 min on the default window, Hub 1b00… heard with its own window of 10 min,
/// the Beans Node silent for 6 h on the default window, the Tomatoes Node heard with its own
/// window of 8 h.
enum SilenceFixture {
  /// One Device's entries of the silence lists; empty is "the core sent none".
  struct Device {
    var shortName = ""
    var silentSince = ""
    var silenceValue = ""
    var silenceUnit = ""
    var windowSeconds = ""
    var windowValue = ""
    var windowUnit = ""
    var overridden = false
  }

  static func lists(_ devices: [Device]) -> DevicesPresentation.SilenceLists {
    DevicesPresentation.SilenceLists(
      shortNames: devices.map(\.shortName), silentSince: devices.map(\.silentSince),
      silenceValues: devices.map(\.silenceValue), silenceUnits: devices.map(\.silenceUnit),
      windowSeconds: devices.map(\.windowSeconds), windowValues: devices.map(\.windowValue),
      windowUnits: devices.map(\.windowUnit), windowOverridden: devices.map(\.overridden))
  }

  static let silentHubId = "3f2a9c0d1e4b5a67"
  static let heardHubId = "1b00aa11bb22cc33"
  static let silentNodeId = "1b2c3d4e5f607182"
  static let heardNodeId = "7c19aa01bb02cc03"

  static let silentHub = Device(
    shortName: "3F2A", silentSince: String(Overview.nowMs - 12 * 60_000), silenceValue: "12",
    silenceUnit: "minutes", windowSeconds: "300", windowValue: "5", windowUnit: "minutes")
  static let heardHub = Device(
    shortName: "1B00", windowSeconds: "600", windowValue: "10", windowUnit: "minutes",
    overridden: true)
  static let silentNode = Device(
    shortName: "1B2C", silentSince: String(Overview.earlierMs), silenceValue: "6",
    silenceUnit: "hours", windowSeconds: "21600", windowValue: "6", windowUnit: "hours")
  static let heardNode = Device(
    shortName: "7C19", windowSeconds: "28800", windowValue: "8", windowUnit: "hours",
    overridden: true)

  /// The Devices of Site "Home garden" for an Administrator or Owner, or for a Member.
  static func devices(
    canSetSilenceWindow: Bool = true, workingId: String = "", failedId: String = "",
    notice: String = ""
  ) -> DevicesPresentation {
    DevicesPresentation(
      surface: "ready", notice: nil, siteId: "a", canAddHub: canSetSilenceWindow,
      canAddNode: canSetSilenceWindow, hubIds: [silentHubId, heardHubId],
      hubStatuses: ["offline", "online"],
      hubLastSeen: [String(Overview.nowMs - 12 * 60_000), String(Overview.nowMs - 30_000)],
      nodeIds: [silentNodeId, heardNodeId], nodeLotNames: ["Beans", "Tomatoes"],
      nodeBatteries: ["14", "62"], nodeBatteryLow: [true, false],
      nodeCharging: ["notCharging", "charging"],
      nodeLastSeen: [String(Overview.earlierMs), String(Overview.readingMs)],
      canManageNodes: canSetSilenceWindow, nodeLotIds: ["lot-b", "lot-t"],
      lotIds: ["lot-b", "lot-t"], lotNames: ["Beans", "Tomatoes"], lotHasNode: [true, true],
      siteName: "Home garden", hubSilence: lists([silentHub, heardHub]),
      nodeSilence: lists([silentNode, heardNode]), canSetSilenceWindow: canSetSilenceWindow,
      silenceWindowWorkingId: workingId, silenceWindowFailedId: failedId,
      silenceWindowNotice: notice)
  }
}

/// A Lot as the core flattens it into the `lot…` lists of `DevicesSnapshot`.
private struct LotFixture {
  var id: String
  var name: String
  var hasNode: Bool
}

/// A Node as the core flattens it into the `node…` lists of `DevicesSnapshot`.
private struct NodeFixture {
  var id: String
  var lot = ""
  var lotId = ""
  var battery = ""
  var low = false
  var charging = ""
  var lastSeen = ""
}

private let tomatoes = NodeFixture(
  id: "7c19aa01bb02cc03", lot: "Tomatoes", lotId: "lot-t", battery: "62", charging: "charging",
  lastSeen: "1791270120000")
private let beans = NodeFixture(
  id: "1b2c3d4e5f607182", lot: "Beans", lotId: "lot-b", battery: "14", low: true,
  charging: "notCharging", lastSeen: "1791248700000")
private let unassigned = NodeFixture(id: "0a0b0c0d0e0f1011")

/// 2026-10-06 07:04 UTC, two minutes after the Hub was seen.
private let now = Date(timeIntervalSince1970: 1_791_270_240)
private let utc = TimeZone(identifier: "UTC") ?? .gmt
private let zurich = TimeZone(identifier: "Europe/Zurich") ?? .gmt
private let british = Locale(identifier: "en_GB")

@Test("UX-DR30 UX-DR65 an online Hub is a row with its full Device ID, Online and last seen")
func devicesOnlineHub() throws {
  let hub = try #require(devices().hubs.first)

  #expect(hub.id == "3f2a9c0d1e4b5a67")
  #expect(hub.isOnline)
  #expect(hub.status == .devicesOnline)
  #expect(hub.icon == .checkmarkOutline)
  #expect(hub.lastSeen == Date(timeIntervalSince1970: 1_791_270_120))
  #expect(
    hub.lastSeenCopy(now: now, timeZone: utc, locale: british)
      == Copy(.devicesLastSeen, .text("07:02")))
  let entries = try Catalogue.entries()
  #expect(entries["devices_hubs"] == "Hubs")
  #expect(entries["devices_online"] == "Online")
  #expect(entries["devices_last_seen"] == "Last seen %@")
}

@Test("UX-DR30 a Hub whose heartbeat stopped reads Offline with the unchanged last-seen time")
func devicesHeartbeatStopped() throws {
  let hub = try #require(devices(hubStatuses: ["offline"]).hubs.first)

  #expect(!hub.isOnline)
  #expect(hub.status == .devicesOffline)
  #expect(hub.icon == .help)
  #expect(
    hub.lastSeenCopy(now: now, timeZone: utc, locale: british)
      == Copy(.devicesLastSeen, .text("07:02")))
  #expect(try Catalogue.entries()["devices_offline"] == "Offline")
}

@Test("UX-DR30 the status is the Server's word: only online reads as Online, whatever the time")
func devicesStatusIsNeverComputed() throws {
  // Seen this very second and still offline, because the core said so.
  let fresh = try #require(
    devices(hubStatuses: ["offline"], hubLastSeen: ["1791270240000"]).hubs.first)
  #expect(!fresh.isOnline)
  // Seen years ago and online, because the core said so.
  let old = try #require(devices(hubLastSeen: ["1577836800000"]).hubs.first)
  #expect(old.isOnline)
  // A status this build does not know never reads as Online.
  let later = try #require(devices(hubStatuses: ["later"]).hubs.first)
  #expect(!later.isOnline)
}

@Test("UX-DR30 a Hub that never sent a heartbeat is Offline and Not seen yet")
func devicesNeverSeen() throws {
  let hub = try #require(devices(hubStatuses: ["offline"], hubLastSeen: [""]).hubs.first)

  #expect(!hub.isOnline)
  #expect(hub.lastSeen == nil)
  #expect(hub.lastSeenCopy(now: now, timeZone: utc, locale: british) == Copy(.devicesNotSeen))
  #expect(try Catalogue.entries()["devices_not_seen"] == "Not seen yet")
}

@Test("UX-DR30 last seen follows the Voice rules in the phone's time zone")
func devicesLastSeenVoiceRules() throws {
  let hub = try #require(devices().hubs.first)
  #expect(
    hub.lastSeenCopy(now: now, timeZone: zurich, locale: british)
      == Copy(.devicesLastSeen, .text("09:02")))

  // Sunday 4 October, two days before: the weekday.
  let earlier = try #require(devices(hubLastSeen: ["1791136800000"]).hubs.first)
  #expect(
    earlier.lastSeenCopy(now: now, timeZone: utc, locale: british)
      == Copy(.devicesLastSeen, .text("Sun")))
}

@Test("UX-DR30 the Hubs keep the core's order by Device ID, each with its own status")
func devicesHubsInOrder() {
  let presentation = devices(
    hubIds: ["1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"], hubStatuses: ["offline", "online"],
    hubLastSeen: ["", "1791270120000"])

  #expect(presentation.hubs.map(\.id) == ["1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"])
  #expect(presentation.hubs.map(\.isOnline) == [false, true])
  #expect(!presentation.isEmpty)
}

@Test("UX-DR30 a Site without Devices is ready and empty: No Devices yet.")
func devicesNoDevices() throws {
  let presentation = devices(hubIds: [], hubStatuses: [], hubLastSeen: [])

  #expect(presentation.surface == .ready([]))
  #expect(presentation.isEmpty)
  #expect(presentation.failure == nil)
  #expect(try Catalogue.entries()["devices_empty"] == "No Devices yet.")
}

@Test("UX-DR84 Add a Hub shows for an Administrator or Owner and is hidden for a Member")
func devicesAddHubByRole() throws {
  #expect(devices(canAddHub: true).canAddHub)
  let member = devices(canAddHub: false)
  #expect(!member.canAddHub)
  // The Member still sees the list.
  #expect(member.hubs.count == 1)
  #expect(!DevicesPresentation.waiting.canAddHub)
  #expect(!devices(siteId: nil, canAddHub: true).canAddHub)
  #expect(try Catalogue.entries()["devices_add_hub"] == "Add a Hub")
}

@Test("UX-DR84 UX-DR67 Add a Node shows beside Add a Hub for an Administrator or Owner only")
func devicesAddNodeByRole() throws {
  let owner = devices()
  #expect(owner.canAddHub)
  #expect(owner.canAddNode)
  let member = devices(canAddHub: false, canAddNode: false)
  #expect(!member.canAddNode)
  // The Member still sees the list.
  #expect(member.hubs.count == 1)
  #expect(!DevicesPresentation.waiting.canAddNode)
  #expect(!devices(siteId: nil).canAddNode)
  // Each action follows its own flag from the core; neither is derived here.
  #expect(!devices(canAddHub: true, canAddNode: false).canAddNode)
  #expect(devices(canAddHub: false, canAddNode: true).canAddNode)
  #expect(!DevicesPresentation(surface: .waiting, canAddHub: true).canAddNode)
  #expect(try Catalogue.entries()["devices_add_node"] == "Add a Node")
  // No Nodes section: the list holds Hubs only.
  #expect(owner.hubs.map(\.id) == ["3f2a9c0d1e4b5a67"])
}

@Test("UX-DR65 a failed reload shows no rows, so nothing stays Online, with Try again")
func devicesFailedReload() throws {
  // Even if rows crossed the bridge with a failure, none is shown.
  let failed = devices(surface: "failed", notice: "unreachable")

  #expect(failed.surface == .failed(.unreachable))
  #expect(failed.hubs.isEmpty)
  #expect(!failed.isEmpty)
  #expect(failed.failure?.message == .devicesUnreachable)
  #expect(failed.failure?.offersTryAgain == true)
  #expect(try Catalogue.entries()["devices_unreachable"] == "Can't reach your Server.")
}

@Test("UX-DR65 a certificate failure has no Try again, and an unknown notice reads as unreachable")
func devicesOtherFailures() {
  let certificate = devices(surface: "failed", notice: "certificate")
  #expect(certificate.failure == .certificate)
  #expect(certificate.failure?.message == .noticeCertificate)
  #expect(certificate.failure?.offersTryAgain == false)

  #expect(devices(surface: "failed", notice: "later").failure == .unreachable)
  #expect(devices(surface: "failed", notice: nil).failure == .unreachable)
}

@Test("UX-DR65 nothing is listed while the Devices are read, signed out, or without a Site")
func devicesWaiting() {
  for surface in ["idle", "loading", "later"] {
    let presentation = devices(surface: surface)
    #expect(presentation.surface == .waiting)
    #expect(presentation.hubs.isEmpty)
    #expect(!presentation.isEmpty)
  }
  #expect(devices(siteId: nil).surface == .waiting)
}

@Test("lists of different lengths from the bridge show only whole rows")
func devicesRaggedLists() {
  let presentation = devices(
    hubIds: ["1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"], hubStatuses: ["online"],
    hubLastSeen: ["1791270120000", ""])

  #expect(presentation.hubs.map(\.id) == ["1b00aa11bb22cc33"])
}

@Test("UX-DR30 the Nodes section follows the Hubs: ID, Lot name, last seen, battery and charging")
func devicesNodesSection() throws {
  let presentation = devices(nodes: [tomatoes])
  let node = try #require(presentation.nodes.first)

  #expect(presentation.hubs.count == 1)
  #expect(node.id == "7c19aa01bb02cc03")
  #expect(node.lotName == "Tomatoes")
  #expect(node.battery == 62)
  #expect(node.batteryCopy == Copy(.lotDetailValuePercent, .text("62")))
  #expect(node.batteryIcon == nil)
  #expect(node.chargingLabel == .devicesCharging)
  #expect(
    node.lastSeenCopy(now: now, timeZone: utc, locale: british)
      == Copy(.devicesLastSeen, .text("07:02")))
  let entries = try Catalogue.entries()
  #expect(entries["devices_nodes"] == "Nodes")
  #expect(entries["devices_charging"] == "charging")
  #expect(entries["devices_not_charging"] == "not charging")
}

@Test(
  "UX-DR30 Nodes keep the Server's order, Lot name then unassigned last; the client never sorts")
func devicesNodesKeepServerOrder() throws {
  // Deliberately not sorted by Device ID or by name: the order is the core's.
  let presentation = devices(nodes: [tomatoes, beans, unassigned])

  #expect(presentation.nodes.map(\.id) == [tomatoes.id, beans.id, unassigned.id])
  #expect(presentation.nodes.map(\.lotName) == ["Tomatoes", "Beans", nil])
  #expect(try Catalogue.entries()["devices_no_lot"] == "Not in a Lot")
}

@Test("UX-DR30 a battery below 20 % shows battery--low; an unknown one shows nothing")
func devicesNodesBattery() throws {
  let presentation = devices(nodes: [beans, unassigned])
  let low = try #require(presentation.nodes.first)
  let bare = try #require(presentation.nodes.last)

  #expect(low.batteryIcon == .batteryLow)
  #expect(low.chargingLabel == .devicesNotCharging)
  #expect(bare.battery == nil)
  #expect(bare.batteryCopy == nil)
  #expect(bare.chargingLabel == nil)
  #expect(
    bare.lastSeenCopy(now: now, timeZone: utc, locale: british) == Copy(.devicesNotSeen))
}

@Test("UX-DR30 No Devices yet only when there is neither a Hub nor a Node; failure shows no Nodes")
func devicesNodesEmptyAndFailed() {
  #expect(!devices(hubIds: [], hubStatuses: [], hubLastSeen: [], nodes: [tomatoes]).isEmpty)
  #expect(devices(hubIds: [], hubStatuses: [], hubLastSeen: [], nodes: []).isEmpty)
  #expect(devices(surface: "failed", notice: "unreachable", nodes: [tomatoes]).nodes.isEmpty)
  #expect(devices(surface: "loading", nodes: [tomatoes]).nodes.isEmpty)
  #expect(devices(siteId: nil, nodes: [tomatoes]).nodes.isEmpty)
}

private let garden = [
  LotFixture(id: "lot-t", name: "Tomatoes", hasNode: true),
  LotFixture(id: "lot-b", name: "Beans", hasNode: true),
  LotFixture(id: "lot-basil", name: "Basil", hasNode: false),
]

@Test("UX-DR31 an Administrator or Owner can move and unassign; a Member sees neither")
func devicesManageNodesFollowsTheCoresRoleRule() {
  let admin = devices(nodes: [tomatoes, unassigned], canManageNodes: true, lots: garden)
  let member = devices(nodes: [tomatoes, unassigned], canManageNodes: false, lots: garden)

  #expect(admin.canManageNodes)
  #expect(admin.nodes.map(\.canUnassign) == [true, false])
  #expect(!member.canManageNodes)
  let failed = devices(surface: "failed", notice: "unreachable", canManageNodes: true)
  #expect(failed.canManageNodes == false)
  #expect(devices(siteId: nil, canManageNodes: true).canManageNodes == false)
}

@Test("UX-DR31 occupied Lots are not selectable and say Has a Node; the Node's own Lot says so too")
func devicesMovePickerDisablesOccupiedLots() throws {
  let presentation = devices(nodes: [tomatoes, unassigned], canManageNodes: true, lots: garden)
  let node = try #require(presentation.nodes.first)

  let choices = presentation.moveChoices(for: node, selected: "lot-basil")

  #expect(choices.map(\.id) == ["lot-t", "lot-b", "lot-basil"])
  #expect(choices.map(\.isSelectable) == [false, false, true])
  #expect(choices.map(\.isSelected) == [false, false, true])
  #expect(choices[0].reason == .devicesCurrentLot)
  #expect(choices[0].description == Copy(.devicesCurrentLotDescription, .text("Tomatoes")))
  #expect(choices[1].reason == .addNodeLotHasNode)
  #expect(choices[1].description == Copy(.addNodeLotDescriptionHasNode, .text("Beans")))
  #expect(choices[2].reason == nil)

  // A Lot with a Node can never be shown as picked, even when it was picked before it was taken.
  #expect(presentation.moveChoices(for: node, selected: "lot-b").allSatisfy { !$0.isSelected })

  // An unassigned Node has no current Lot: both occupied Lots have a Node.
  let spare = try #require(presentation.nodes.last)
  let spareChoices = presentation.moveChoices(for: spare, selected: nil)
  #expect(spareChoices.map(\.isSelectable) == [false, false, true])
}

@Test("UX-DR31 the Node being moved is working and a refused move says why under that Node only")
func devicesActionStateBelongsToOneNode() throws {
  let working = devices(nodes: [tomatoes, beans], canManageNodes: true, workingNodeId: tomatoes.id)
  #expect(working.nodes.map(\.isWorking) == [true, false])

  let refused = devices(
    nodes: [tomatoes, beans], canManageNodes: true, failedNodeId: beans.id,
    actionNotice: "lotTaken"
  )
  #expect(refused.nodes.map(\.actionNotice) == [nil, .lotTaken])
  #expect(NodeActionNoticeKind.lotTaken.message == .devicesActionLotTaken)
  #expect(NodeActionNoticeKind.forbidden.message == .devicesActionForbidden)
  #expect(NodeActionNoticeKind.notFound.message == .devicesActionNotFound)
  #expect(NodeActionNoticeKind.unexpected.message == .devicesActionUnexpected)
  #expect(NodeActionNoticeKind(rawValue: "") == nil)
}

@Test("UX-DR31 Unassign asks first, naming the Node; the copy is in the catalogue")
func devicesUnassignCopy() throws {
  let entries = try Catalogue.entries()

  #expect(entries["devices_unassign_question"] == "Unassign Node %@?")
  #expect(
    entries["devices_unassign_detail"]
      == "%@ will have no Node. Its Readings stay in Coldframe."
  )
  #expect(entries["devices_unassign"] == "Unassign")
  #expect(entries["devices_move"] == "Move")
  #expect(entries["devices_current_lot"] == "Current Lot")
  #expect(entries["add_node_lot_has_node"] == "Has a Node")
}

// MARK: - Silence and the Silence Window (Story 7.2)

private let context = Overview.context

@Test(
  "UX-DR30 a silent Hub and a silent Node show the help icon and Silent with the core's duration"
)
func devicesSilentRows() throws {
  let presentation = SilenceFixture.devices()
  let hub = try #require(presentation.hubs.first)
  let node = try #require(presentation.nodes.first)

  #expect(hub.silence.isSilent)
  #expect(hub.silence.icon == .help)
  #expect(hub.silence.silentText(context) == "Silent 12 min")
  #expect(hub.silence.silence == LotDurationPresentation(value: 12, unit: .minutes))
  #expect(
    hub.silence.silentSince
      == Date(timeIntervalSince1970: Double(Overview.nowMs - 12 * 60_000) / 1000))
  #expect(hub.silence.shortName == "3F2A")
  #expect(node.silence.isSilent)
  #expect(node.silence.icon == .help)
  #expect(node.silence.silentText(context) == "Silent 6 h")
  // The silence stands beside the last seen, which reads as before; the ID stays whole.
  #expect(hub.id == SilenceFixture.silentHubId)
  #expect(
    hub.lastSeenCopy(now: context.now, timeZone: context.timeZone, locale: context.locale)
      == Copy(.devicesLastSeen, .text("09:48")))
  #expect(
    node.lastSeenCopy(now: context.now, timeZone: context.timeZone, locale: context.locale)
      == Copy(.devicesLastSeen, .text("01:05")))
  // Sentence case in the catalogue; upper case is the style's.
  #expect(try Catalogue.entries()["devices_silent"] == "Silent %@")
  #expect(Typography.statusLabel.uppercase)
}

@Test("UX-DR30 a Device that is not silent has no mark, whatever its status or last seen")
func devicesHeardRows() throws {
  let presentation = SilenceFixture.devices()
  let hub = try #require(presentation.hubs.last)
  let node = try #require(presentation.nodes.last)

  for silence in [hub.silence, node.silence] {
    #expect(!silence.isSilent)
    #expect(silence.icon == nil)
    #expect(silence.silentText(context) == nil)
    #expect(silence.silentSince == nil)
  }
  // Offline is the Server's status word and silent the Server's Alert: neither makes the other.
  let offline = try #require(devices(hubStatuses: ["offline"]).hubs.first)
  #expect(!offline.silence.isSilent)
  // Days, when the core says days.
  var long = SilenceFixture.silentNode
  long.silenceValue = "2"
  long.silenceUnit = "days"
  let days = try #require(devices(nodes: [beans], nodeSilence: [long]).nodes.first)
  #expect(days.silence.silentText(context) == "Silent 2 d")
}

@Test("UX-DR30 every row names the Silence Window in force, for every Role")
func devicesWindowSummary() throws {
  for canSet in [true, false] {
    let presentation = SilenceFixture.devices(canSetSilenceWindow: canSet)
    #expect(
      presentation.hubs.map { $0.silence.windowText(context) } == [
        "Silence Window · 5 min", "Silence Window · 10 min",
      ])
    #expect(
      presentation.nodes.map { $0.silence.windowText(context) } == [
        "Silence Window · 6 h", "Silence Window · 8 h",
      ])
    #expect(presentation.hubs.map(\.silence.windowOverridden) == [false, true])
    #expect(presentation.nodes.map(\.silence.windowSeconds) == [21600, 28800])
  }
  var month = SilenceFixture.heardNode
  month.windowSeconds = "2592000"
  month.windowValue = "30"
  month.windowUnit = "days"
  let node = try #require(devices(nodes: [tomatoes], nodeSilence: [month]).nodes.first)
  #expect(node.silence.windowText(context) == "Silence Window · 30 d")
  #expect(try Catalogue.entries()["devices_silence_window_summary"] == "Silence Window · %@")
}

@Test(
  "UX-DR30 a row the core sent no silence for, or a value this client cannot read, is as before"
)
func devicesSilenceDegrades() throws {
  // No silence lists at all: an older core.
  let plain = devices(nodes: [tomatoes])
  #expect(plain.hubs.first?.silence == DeviceSilencePresentation.none)
  #expect(plain.nodes.first?.silence == DeviceSilencePresentation.none)
  #expect(plain.hubs.first?.silence.windowText(context) == nil)
  // Empty entries, a unit of later, and a value without its unit.
  var odd = SilenceFixture.silentHub
  odd.silenceUnit = "fortnights"
  odd.windowUnit = "fortnights"
  var half = SilenceFixture.silentHub
  half.silenceValue = ""
  half.windowValue = ""
  for device in [SilenceFixture.Device(), odd, half] {
    let hub = try #require(devices(hubSilence: [device]).hubs.first)
    #expect(!hub.silence.isSilent)
    #expect(hub.silence.silentText(context) == nil)
    #expect(hub.silence.windowText(context) == nil)
    #expect(hub.isOnline)
  }
  // Shorter silence lists than rows: the rows stay, the ones without an entry as before.
  let ragged = devices(
    hubIds: ["1b00aa11bb22cc33", "3f2a9c0d1e4b5a67"], hubStatuses: ["online", "offline"],
    hubLastSeen: ["1791270120000", ""], hubSilence: [SilenceFixture.heardHub])
  #expect(ragged.hubs.count == 2)
  #expect(ragged.hubs[0].silence.windowText(context) == "Silence Window · 10 min")
  #expect(ragged.hubs[1].silence == DeviceSilencePresentation.none)
}

@Test(
  "UX-DR31 UX-DR84 Silence Window is a row action for an Administrator or Owner; a Member has none"
)
func devicesSilenceWindowByRole() {
  let admin = SilenceFixture.devices()
  let member = SilenceFixture.devices(canSetSilenceWindow: false)

  #expect(admin.canSetSilenceWindow)
  #expect(admin.silenceWindow(for: SilenceFixture.silentHubId) != nil)
  #expect(admin.silenceWindow(for: SilenceFixture.heardNodeId) != nil)
  // For a Member the control does not exist: no action and no sheet, not a disabled one.
  #expect(!member.canSetSilenceWindow)
  for id in [
    SilenceFixture.silentHubId, SilenceFixture.heardHubId, SilenceFixture.silentNodeId,
    SilenceFixture.heardNodeId,
  ] {
    #expect(member.silenceWindow(for: id) == nil, "\(id)")
  }
  // The Member still sees the silence, the last seen and the window in force.
  #expect(member.hubs.first?.silence.silentText(context) == "Silent 12 min")
  #expect(member.nodes.first?.silence.windowText(context) == "Silence Window · 6 h")
  // The flag is the core's; it is its own, not Move's or Add a Hub's.
  #expect(!devices(canAddHub: true, canManageNodes: true).canSetSilenceWindow)
  #expect(devices(canAddHub: false, canSetSilenceWindow: true).canSetSilenceWindow)
  let failed = devices(surface: "failed", notice: "unreachable", canSetSilenceWindow: true)
  #expect(!failed.canSetSilenceWindow)
  #expect(!devices(siteId: nil, canSetSilenceWindow: true).canSetSilenceWindow)
  #expect(!DevicesPresentation.waiting.canSetSilenceWindow)
  // A Device that is not listed has no sheet.
  #expect(admin.silenceWindow(for: "0000000000000000") == nil)
}

@Test(
  "UX-DR31 the Silence Window sheet shows the window in force, a number with min, h or d, and Save"
)
func devicesSilenceWindowSheet() throws {
  let presentation = SilenceFixture.devices()
  let hub = try #require(presentation.silenceWindow(for: SilenceFixture.silentHubId))
  let node = try #require(presentation.silenceWindow(for: SilenceFixture.heardNodeId))

  #expect(hub.device == .hub)
  #expect(hub.title == Copy(.devicesSilenceWindowHub, .text(SilenceFixture.silentHubId)))
  #expect(context.resolve(hub.title) == "Silence Window for Hub 3f2a9c0d1e4b5a67")
  #expect(hub.currentText(context) == "Silence Window · 5 min")
  #expect(hub.initialText == "5")
  #expect(hub.initialUnit == .minutes)
  #expect(node.device == .node)
  #expect(context.resolve(node.title) == "Silence Window for Node 7c19aa01bb02cc03")
  #expect(node.currentText(context) == "Silence Window · 8 h")
  #expect(node.initialText == "8")
  #expect(node.initialUnit == .hours)

  let segments = node.unitSegments(selected: .hours)
  #expect(segments.map(\.value) == [.minutes, .hours, .days])
  #expect(segments.map { context.text($0.label) } == ["min", "h", "d"])
  #expect(segments.map(\.isSelected) == [false, true, false])
  // Selection is a trait and a checkmark, never colour alone.
  #expect(segments[1].traits == [.button, .selected])
  #expect(segments[1].showsCheckmark)

  let entries = try Catalogue.entries()
  #expect(entries[SilenceWindowPresentation.label.rawValue] == "Silence Window")
  #expect(entries[SilenceWindowPresentation.unitLabel.rawValue] == "Unit")
  #expect(
    entries[SilenceWindowPresentation.helper.rawValue]
      == "From 1 min to 30 d. Without its own value a Node uses 6 h and a Hub 5 min.")
  #expect(entries["devices_silence_window_save"] == "Save")
}

@Test("UX-DR31 Use default shows only when the Device has its own Silence Window")
func devicesSilenceWindowUseDefault() throws {
  let presentation = SilenceFixture.devices()

  #expect(presentation.silenceWindow(for: SilenceFixture.silentHubId)?.showsUseDefault == false)
  #expect(presentation.silenceWindow(for: SilenceFixture.heardHubId)?.showsUseDefault == true)
  #expect(presentation.silenceWindow(for: SilenceFixture.silentNodeId)?.showsUseDefault == false)
  #expect(presentation.silenceWindow(for: SilenceFixture.heardNodeId)?.showsUseDefault == true)
  // A Device whose window the Server did not send: an empty field in hours, and no default.
  let bare = devices(canSetSilenceWindow: true)
  let sheet = try #require(bare.silenceWindow(for: "3f2a9c0d1e4b5a67"))
  #expect(sheet.current == nil)
  #expect(sheet.currentText(context) == nil)
  #expect(sheet.initialText == "")
  #expect(sheet.initialUnit == .hours)
  #expect(!sheet.showsUseDefault)
  #expect(try Catalogue.entries()["devices_silence_window_use_default"] == "Use default")
}

@Test(
  "UX-DR31 a value outside 1 min to 30 days is refused before sending, with the range in the message"
)
func devicesSilenceWindowOutOfRange() throws {
  // The core refuses it, sends nothing and names the Device; the row and the sheet say the range.
  let refused = SilenceFixture.devices(
    failedId: SilenceFixture.heardNodeId, notice: "outOfRange")

  #expect(refused.nodes.map(\.silence.windowNotice) == [nil, .outOfRange])
  #expect(refused.hubs.allSatisfy { $0.silence.windowNotice == nil })
  #expect(SilenceWindowNoticeKind.outOfRange.message == .devicesSilenceWindowRange)
  #expect(SilenceWindowPresentation.rangeError == .devicesSilenceWindowRange)
  #expect(!SilenceWindowNoticeKind.outOfRange.takesSiteName)
  #expect(
    try Catalogue.entries()["devices_silence_window_range"]
      == "Enter a whole number from 1 min to 30 d.")
  // Nothing was saved: the window in force is the one from before.
  #expect(refused.nodes.last?.silence.windowText(context) == "Silence Window · 8 h")
  #expect(refused.nodes.allSatisfy { !$0.silence.windowWorking })
}

@Test("UX-DR84 UX-DR31 a 403 race says You can't change this on the Site, under that Device only")
func devicesSilenceWindowForbidden() throws {
  let refused = SilenceFixture.devices(
    failedId: SilenceFixture.silentHubId, notice: "forbidden")
  let notice = try #require(refused.hubs.first?.silence.windowNotice)

  #expect(notice == .forbidden)
  #expect(notice.message == .siteSettingsForbidden)
  #expect(notice.takesSiteName)
  #expect(refused.siteName == "Home garden")
  #expect(
    context.text(notice.message, .text(refused.siteName))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
  #expect(refused.hubs.last?.silence.windowNotice == nil)
  #expect(refused.nodes.allSatisfy { $0.silence.windowNotice == nil })
}

@Test("UX-DR31 the Device whose Silence Window is saved is working; every refusal has its words")
func devicesSilenceWindowState() throws {
  let working = SilenceFixture.devices(workingId: SilenceFixture.silentNodeId)
  #expect(working.nodes.map(\.silence.windowWorking) == [true, false])
  #expect(working.hubs.allSatisfy { !$0.silence.windowWorking })

  let entries = try Catalogue.entries()
  for kind in SilenceWindowNoticeKind.allCases {
    #expect(entries[kind.message.rawValue] != nil, "\(kind)")
    let refused = SilenceFixture.devices(
      failedId: SilenceFixture.heardHubId, notice: kind.rawValue)
    #expect(refused.hubs.last?.silence.windowNotice == kind)
    #expect(kind.takesSiteName == (kind == .forbidden))
  }
  #expect(SilenceWindowNoticeKind.notFound.message == .devicesSilenceWindowNotFound)
  #expect(entries["devices_silence_window_not_found"] == "That Device is gone.")
  #expect(SilenceWindowNoticeKind.unreachable.message == .devicesUnreachable)
  #expect(SilenceWindowNoticeKind.certificate.message == .noticeCertificate)
  #expect(SilenceWindowNoticeKind.unexpected.message == .devicesActionUnexpected)
  // A notice this client does not know still says that nothing changed; no notice says nothing.
  let later = SilenceFixture.devices(failedId: SilenceFixture.heardHubId, notice: "later")
  #expect(later.hubs.last?.silence.windowNotice == .unexpected)
  let none = SilenceFixture.devices(failedId: SilenceFixture.heardHubId, notice: "")
  #expect(none.hubs.last?.silence.windowNotice == nil)
  // A refused move is the Node's own notice; the two do not mix.
  let moved = devices(
    nodes: [tomatoes], canManageNodes: true, failedNodeId: tomatoes.id, actionNotice: "lotTaken")
  #expect(moved.nodes.first?.silence.windowNotice == nil)
}

// MARK: - Pause and Resume (Story 8.2)

/// Pause as the core flattens it into the `hub…` and `node…` pause lists of a `DevicesSnapshot`:
/// Hub 3f2a… paused by itself, Hub 1b00… paused by the Site alone, the Peppers Node 7c19… paused
/// by itself and by the Site, the Beans Node paused by the Site alone, and a Node on no Lot that
/// is not paused. What a row offers is the core's answer for the Role.
enum PausedDevicesFixture {
  /// One Device's entries of the pause lists.
  struct Device {
    var paused = false
    var bySite = false
    var canPause = false
    var canResume = false
    var resumeSiteHint = false
  }

  /// A Device as the core describes it to an Administrator or Owner (`admin`) or to a Member.
  static func device(byDevice: Bool, bySite: Bool, admin: Bool) -> Device {
    Device(
      paused: byDevice || bySite, bySite: bySite, canPause: admin && !byDevice,
      canResume: admin && byDevice, resumeSiteHint: admin && bySite)
  }

  static func lists(_ devices: [Device]) -> DevicesPresentation.PauseLists {
    DevicesPresentation.PauseLists(
      paused: devices.map(\.paused), pausedBySite: devices.map(\.bySite),
      canPause: devices.map(\.canPause), canResume: devices.map(\.canResume),
      resumeSiteHint: devices.map(\.resumeSiteHint))
  }

  static let ownHubId = PauseFixture.hubId
  static let siteHubId = "1b00aa11bb22cc33"
  static let bothNodeId = PauseFixture.nodeId
  static let siteNodeId = "1b2c3d4e5f607182"
  static let liveNodeId = "0a0b0c0d0e0f1011"

  /// The Devices of Site "Home garden" for an Administrator or Owner, or for a Member.
  static func devices(
    admin: Bool = true, pause: PauseFixture = .closed, surface: String = "ready"
  ) -> DevicesPresentation {
    DevicesPresentation(
      surface: surface, notice: nil, siteId: "a", canAddHub: admin, canAddNode: admin,
      hubIds: [ownHubId, siteHubId], hubStatuses: ["online", "offline"],
      hubLastSeen: [String(Overview.readingMs), String(Overview.earlierMs)],
      nodeIds: [bothNodeId, siteNodeId, liveNodeId], nodeLotNames: ["Peppers", "Beans", ""],
      nodeBatteries: ["62", "14", ""], nodeBatteryLow: [false, true, false],
      nodeCharging: ["charging", "notCharging", ""],
      nodeLastSeen: [String(Overview.readingMs), String(Overview.earlierMs), ""],
      canManageNodes: admin, nodeLotIds: ["lot-p", "lot-b", ""],
      lotIds: ["lot-b", "lot-p"], lotNames: ["Beans", "Peppers"], lotHasNode: [true, true],
      siteName: "Home garden", canSetSilenceWindow: admin,
      hubPause: lists([
        device(byDevice: true, bySite: false, admin: admin),
        device(byDevice: false, bySite: true, admin: admin),
      ]),
      nodePause: lists([
        device(byDevice: true, bySite: true, admin: admin),
        device(byDevice: false, bySite: true, admin: admin),
        device(byDevice: false, bySite: false, admin: admin),
      ]), pause: pause.build())
  }
}

@Test("UX-DR70 paused rows say Paused, or Paused by Site when the Site is a source, for anyone")
func devicesPausedRows() throws {
  for admin in [true, false] {
    let devices = PausedDevicesFixture.devices(admin: admin)
    let hubs = devices.hubs.map(\.pause)
    let nodes = devices.nodes.map(\.pause)

    #expect(hubs.map(\.paused) == [true, true])
    #expect(nodes.map(\.paused) == [true, true, false])
    #expect(
      (hubs + nodes).map { $0.label.map { context.text($0) } }
        == ["Paused", "Paused by Site", "Paused by Site", "Paused by Site", nil])
    // The pause icon and paused ink mean paused, and nothing else: a live row has neither.
    #expect((hubs + nodes).map(\.icon) == Array(repeating: .pauseOutline, count: 4) + [nil])
    #expect(DevicePausePresentation.ink == ColorTokens.statusPausedInk)
    // The Server's status and last seen of the row are as before.
    #expect(devices.hubs.map(\.status) == [.devicesOnline, .devicesOffline])
  }
  // A paused Device is never said to be fine.
  let entries = try Catalogue.entries()
  for key in ["lot_tile_label_paused", "lot_tile_label_paused_by_site"] {
    #expect(entries[key]?.contains("OK") == false)
  }
}

@Test("UX-DR70 an Administrator gets Pause or Resume on every row, as the Device's own Pause says")
func devicesPauseControls() throws {
  let devices = PausedDevicesFixture.devices()
  let ownHub = try #require(devices.hubs.first)
  let both = try #require(devices.nodes.first)
  let live = try #require(devices.nodes.last)

  // Its own Pause: Resume, and no second Pause.
  #expect(ownHub.pause.canResume)
  #expect(!ownHub.pause.canPause)
  // Paused by itself and by the Site: it can be resumed, and stays paused with the Site.
  #expect(both.pause.canResume)
  #expect(!both.pause.canPause)
  #expect(both.pause.resumeSiteHint)
  // Not paused: Pause.
  #expect(live.pause.canPause)
  #expect(!live.pause.canResume)
  #expect(devices.hubs.allSatisfy { $0.pause.hasControl })
  #expect(devices.nodes.allSatisfy { $0.pause.hasControl })
  // The spoken labels, also the names of the row's custom accessibility actions.
  #expect(context.resolve(ownHub.resumeLabel) == "Resume Hub \(ownHub.id)")
  #expect(context.resolve(ownHub.pauseLabel) == "Pause Hub \(ownHub.id)")
  #expect(context.resolve(live.pauseLabel) == "Pause Node \(live.id)")
  #expect(context.resolve(both.resumeLabel) == "Resume Node \(both.id)")
  #expect(context.text(.pauseAction) == "Pause")
  #expect(context.text(.pauseResume) == "Resume")
}

@Test(
  "UX-DR70 a Device paused by the Site says Resume the Site to resume this Node, and a Hub row this Hub"
)
func devicesPausedBySiteOnly() throws {
  let devices = PausedDevicesFixture.devices()
  let hub = try #require(devices.hubs.last)
  let node = try #require(devices.nodes.first { $0.id == PausedDevicesFixture.siteNodeId })

  // No Resume for the Device: its Site's Pause is lifted from the Site menu. Pause stays.
  for pause in [hub.pause, node.pause] {
    #expect(!pause.canResume)
    #expect(pause.canPause)
    #expect(pause.resumeSiteHint)
  }
  #expect(hub.resumeSiteHint.map { context.text($0) } == "Resume the Site to resume this Hub")
  #expect(node.resumeSiteHint.map { context.text($0) } == "Resume the Site to resume this Node")
  // A Device with only its own Pause, and one that is not paused, have no hint.
  #expect(devices.hubs.first?.resumeSiteHint == nil)
  #expect(devices.nodes.last?.resumeSiteHint == nil)
}

@Test("UX-DR84 a Member sees the paused rows and no Pause, Resume or hint, hidden and not disabled")
func devicesPauseForMember() {
  let devices = PausedDevicesFixture.devices(admin: false)
  let rows = devices.hubs.map(\.pause) + devices.nodes.map(\.pause)

  #expect(rows.filter(\.paused).count == 4)
  #expect(!rows.contains { $0.canPause || $0.canResume || $0.resumeSiteHint || $0.hasControl })
  #expect(devices.hubs.allSatisfy { $0.resumeSiteHint == nil })
  #expect(devices.nodes.allSatisfy { $0.resumeSiteHint == nil })
  #expect(devices.pause.sheet == nil)
}

@Test("UX-DR46 UX-DR70 Pause on a row opens the sheet of Devices with This Device selected")
func devicesPauseSheet() throws {
  let devices = PausedDevicesFixture.devices(pause: .forDevice)
  let sheet = try #require(devices.pause.sheet)

  #expect(sheet.scopes == [.device, .site])
  #expect(sheet.scope == .device)
  #expect(context.resolve(sheet.primaryLabel) == "Pause 7C19")
  // The sheet belongs to the shown list: no rows, no sheet.
  #expect(PausedDevicesFixture.devices(pause: .forDevice, surface: "failed").pause == .none)
  #expect(PausedDevicesFixture.devices(pause: .forDevice, surface: "loading").pause == .none)
}

@Test("UX-DR84 a 403 on a Resume says You can't change this on the Site, under that Device")
func devicesResumeForbidden() throws {
  let devices = PausedDevicesFixture.devices(
    pause: .resumeFailed("forbidden", deviceId: PausedDevicesFixture.ownHubId))
  let failure = try #require(devices.pauseFailure(for: PausedDevicesFixture.ownHubId))

  #expect(
    context.text(failure.message(unreachable: .devicesUnreachable), .text(failure.subject ?? ""))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
  #expect(!failure.tryAgain)
  #expect(devices.pauseFailure(for: PausedDevicesFixture.siteHubId) == nil)
  #expect(devices.pauseFailure(for: PausedDevicesFixture.bothNodeId) == nil)
}

@Test("UX-DR70 a Resume the Server did not answer says Can't reach your Server with Try again")
func devicesResumeUnreachable() throws {
  let devices = PausedDevicesFixture.devices(
    pause: .resumeFailed("unreachable", deviceId: PausedDevicesFixture.bothNodeId, tryAgain: true))
  let failure = try #require(devices.pauseFailure(for: PausedDevicesFixture.bothNodeId))

  #expect(
    context.text(failure.message(unreachable: .devicesUnreachable)) == "Can't reach your Server.")
  #expect(failure.tryAgain)
  // Nothing changed: the row is still paused and still offers Resume.
  #expect(devices.nodes.first?.pause.canResume == true)
}

@Test("UX-DR70 while a Pause or Resume runs no row starts another")
func devicesPauseOneAtATime() {
  let resuming = PausedDevicesFixture.devices(
    pause: PauseFixture(resumingScope: "device", resumingDeviceId: PausedDevicesFixture.ownHubId))
  var saving = PauseFixture.forDevice
  saving.working = true

  #expect(resuming.pause.working)
  #expect(PausedDevicesFixture.devices(pause: saving).pause.working)
  #expect(!PausedDevicesFixture.devices().pause.working)
}

@Test("UX-DR70 rows the core sent no pause for, or lists of different lengths, read as not paused")
func devicesPauseDegrades() {
  let bare = SilenceFixture.devices()
  let ragged = DevicesPresentation(
    surface: "ready", notice: nil, siteId: "a", canAddHub: true,
    hubIds: ["3f2a9c0d1e4b5a67", "1b00aa11bb22cc33"], hubStatuses: ["online", "online"],
    hubLastSeen: ["", ""],
    hubPause: DevicesPresentation.PauseLists(paused: [true], canResume: [true, true, true]))

  #expect(bare.hubs.allSatisfy { $0.pause == .none })
  #expect(bare.nodes.allSatisfy { $0.pause == .none })
  #expect(ragged.hubs.map(\.pause.paused) == [true, false])
  #expect(ragged.hubs.map(\.pause.canResume) == [true, true])
  #expect(ragged.hubs.map(\.pause.canPause) == [false, false])
}
