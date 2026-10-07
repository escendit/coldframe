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
  failedNodeId: String = "", actionNotice: String = ""
) -> DevicesPresentation {
  DevicesPresentation(
    surface: surface, notice: notice, siteId: siteId, canAddHub: canAddHub,
    canAddNode: canAddNode, hubIds: hubIds, hubStatuses: hubStatuses, hubLastSeen: hubLastSeen,
    nodeIds: nodes.map(\.id), nodeLotNames: nodes.map(\.lot), nodeBatteries: nodes.map(\.battery),
    nodeBatteryLow: nodes.map(\.low), nodeCharging: nodes.map(\.charging),
    nodeLastSeen: nodes.map(\.lastSeen), canManageNodes: canManageNodes,
    nodeLotIds: nodes.map(\.lotId), lotIds: lots.map(\.id), lotNames: lots.map(\.name),
    lotHasNode: lots.map(\.hasNode), workingNodeId: workingNodeId, failedNodeId: failedNodeId,
    actionNotice: actionNotice)
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
  id: "1b2c3d4e5f607182", lot: "Beans", lotId: "lot-b", battery: "14", low: true, charging: "notCharging",
  lastSeen: "1791248700000")
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
  #expect(devices(surface: "failed", notice: "unreachable", canManageNodes: true).canManageNodes == false)
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
  #expect(presentation.moveChoices(for: spare, selected: nil).map(\.isSelectable) == [false, false, true])
}

@Test("UX-DR31 the Node being moved is working and a refused move says why under that Node only")
func devicesActionStateBelongsToOneNode() throws {
  let working = devices(nodes: [tomatoes, beans], canManageNodes: true, workingNodeId: tomatoes.id)
  #expect(working.nodes.map(\.isWorking) == [true, false])

  let refused = devices(
    nodes: [tomatoes, beans], canManageNodes: true, failedNodeId: beans.id, actionNotice: "lotTaken")
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
  #expect(entries["devices_unassign_detail"] == "%@ will have no Node. Its Readings stay in Coldframe.")
  #expect(entries["devices_unassign"] == "Unassign")
  #expect(entries["devices_move"] == "Move")
  #expect(entries["devices_current_lot"] == "Current Lot")
  #expect(entries["add_node_lot_has_node"] == "Has a Node")
}
