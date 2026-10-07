import ColdframeDesignTokens
import Foundation

/// Why the Devices could not be read. No row is shown with it, so nothing stays "Online".
public enum DevicesNoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate

  public var message: L10n {
    switch self {
    case .unreachable: .devicesUnreachable
    case .certificate: .noticeCertificate
    }
  }

  /// The certificate notice has no action: it is never retried insecurely (AD-13).
  public var offersTryAgain: Bool { self == .unreachable }
}

/// Why a move or unassign did not happen (UX-DR31), named as the core names it. Each is inline copy
/// under the Node's row.
public enum NodeActionNoticeKind: String, CaseIterable, Sendable {
  case forbidden
  case lotTaken
  case notFound
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .forbidden: .devicesActionForbidden
    case .lotTaken: .devicesActionLotTaken
    case .notFound: .devicesActionNotFound
    case .unreachable: .devicesUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .devicesActionUnexpected
    }
  }
}

/// A Lot of the Site as the Move picker reads it: `hasNode` is the Server's `noNode` status turned
/// around, in the Server's order.
public struct MoveLotOption: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let hasNode: Bool

  public init(id: String, name: String, hasNode: Bool) {
    self.id = id
    self.name = name
    self.hasNode = hasNode
  }
}

/// One Lot of a Node's Move picker (UX-DR31). A Lot that has another Node, and the Node's own Lot,
/// cannot be picked and say why in words ("Has a Node", "Current Lot"), not by colour alone.
public struct MoveLotPresentation: Equatable, Sendable, Identifiable {
  public let id: String
  public let name: String
  public let hasNode: Bool
  public let isCurrent: Bool
  public let isSelected: Bool

  public init(id: String, name: String, hasNode: Bool, isCurrent: Bool, isSelected: Bool) {
    self.id = id
    self.name = name
    self.hasNode = hasNode
    self.isCurrent = isCurrent
    self.isSelected = isSelected
  }

  /// Only a free Lot, other than the Node's own, can be picked.
  public var isSelectable: Bool { !hasNode && !isCurrent }

  /// The reason under the name of a Lot that cannot be picked.
  public var reason: L10n? {
    if isCurrent { return .devicesCurrentLot }
    return hasNode ? .addNodeLotHasNode : nil
  }

  /// The spoken label when it is not the name alone: "Beans, has a Node".
  public var description: Copy? {
    if isCurrent { return Copy(.devicesCurrentLotDescription, .text(name)) }
    return hasNode ? Copy(.addNodeLotDescriptionHasNode, .text(name)) : nil
  }

  public var traits: Set<ControlTrait> { isSelected ? [.button, .selected] : [.button] }
}

/// One Hub row of the Devices list (UX-DR30): the full Device ID in `meta-mono`, the status as a
/// word with its own icon, and when the Hub was last seen. `isOnline` is the Server's answer;
/// nothing here computes it.
public struct HubRowPresentation: Equatable, Sendable, Identifiable {
  /// The Device ID, shown in full and never reformatted.
  public let id: String
  public let isOnline: Bool
  /// The last accepted heartbeat, or nil for a Hub that never sent one.
  public let lastSeen: Date?

  public init(id: String, isOnline: Bool, lastSeen: Date?) {
    self.id = id
    self.isOnline = isOnline
    self.lastSeen = lastSeen
  }

  /// The status word; never colour alone.
  public var status: L10n { isOnline ? .devicesOnline : .devicesOffline }

  /// The status shape: `checkmark--outline` online, `help` offline.
  public var icon: CarbonIcon { isOnline ? .checkmarkOutline : .help }

  /// "Last seen 07:02" by the Voice rules, or "Not seen yet".
  public func lastSeenCopy(now: Date, timeZone: TimeZone, locale: Locale) -> Copy {
    guard let lastSeen else { return Copy(.devicesNotSeen) }
    return Copy(
      .devicesLastSeen,
      .text(Formats.when(lastSeen, now: now, timeZone: timeZone, locale: locale)))
  }
}

/// One Node row of the Devices list (UX-DR30): the Device ID in `meta-mono`, the Lot name in
/// `body-lg` (or "Not in a Lot"), and its last seen, battery and charging in `helper`, as the
/// Server sent them. The Server's order (Lot name, unassigned last) is kept; nothing sorts.
public struct NodeRowPresentation: Equatable, Sendable, Identifiable {
  /// The Device ID, shown in full and never reformatted.
  public let id: String
  /// The Lot the Node is in; nil for an unassigned Node.
  public let lotName: String?
  /// The ID of that Lot; nil for an unassigned Node.
  public let lotId: String?
  public let battery: Int?
  /// Below 20 %, as the core decided: shows `battery--low`.
  public let batteryLow: Bool
  public let charging: ChargeKind?
  public let lastSeen: Date?
  /// A move or unassign of this Node is under way.
  public let isWorking: Bool
  /// The last move or unassign of this Node that did not happen.
  public let actionNotice: NodeActionNoticeKind?

  public init(
    id: String, lotName: String?, battery: Int?, batteryLow: Bool, charging: ChargeKind?,
    lastSeen: Date?, lotId: String? = nil, isWorking: Bool = false,
    actionNotice: NodeActionNoticeKind? = nil
  ) {
    self.id = id
    self.lotName = lotName
    self.lotId = lotId
    self.isWorking = isWorking
    self.actionNotice = actionNotice
    self.battery = battery
    self.batteryLow = batteryLow
    self.charging = charging
    self.lastSeen = lastSeen
  }

  public var batteryIcon: CarbonIcon? { batteryLow ? .batteryLow : nil }

  /// Unassign shows only for a Node that is in a Lot.
  public var canUnassign: Bool { lotId != nil }

  /// "Last seen 07:02" by the Voice rules, or "Not seen yet".
  public func lastSeenCopy(now: Date, timeZone: TimeZone, locale: Locale) -> Copy {
    guard let lastSeen else { return Copy(.devicesNotSeen) }
    return Copy(
      .devicesLastSeen,
      .text(Formats.when(lastSeen, now: now, timeZone: timeZone, locale: locale)))
  }

  /// "62 %", or nil when the Node's battery is not known.
  public var batteryCopy: Copy? {
    battery.map { Copy(.lotDetailValuePercent, .text(String($0))) }
  }

  public var chargingLabel: L10n? { charging?.label }
}

/// What the Devices tab shows.
public enum DevicesSurface: Equatable, Sendable {
  /// Signed out, no current Site, or the list is being read: no rows.
  case waiting
  /// The list could not be read: the notice, and no rows.
  case failed(DevicesNoticeKind)
  /// The Hubs of the current Site, by Device ID; empty is "No Devices yet."
  case ready([HubRowPresentation])
}

/// The Swift mirror of the core's `DevicesState`, built from the flattened `DevicesSnapshot`.
public struct DevicesPresentation: Equatable, Sendable {
  public let surface: DevicesSurface
  /// Add a Hub shows for Administrators and Owners; hidden for a Member, never disabled (UX-DR84).
  public let canAddHub: Bool
  /// Add a Node sits beside it, for the same Roles, as the core decided.
  public let canAddNode: Bool
  /// The Nodes of the current Site in the Server's order, shown under "Nodes" after the Hubs;
  /// empty unless ready.
  public let nodes: [NodeRowPresentation]
  /// Move and Unassign show on Node rows for Administrators and Owners; hidden for a Member, never
  /// disabled (UX-DR31), as the core decided.
  public let canManageNodes: Bool
  /// The Site's Lots in the Server's order, for the Move picker; empty unless the Role may move Nodes.
  public let lots: [MoveLotOption]

  public init(
    surface: DevicesSurface, canAddHub: Bool, canAddNode: Bool = false,
    nodes: [NodeRowPresentation] = [], canManageNodes: Bool = false, lots: [MoveLotOption] = []
  ) {
    self.surface = surface
    self.canAddHub = canAddHub
    self.canAddNode = canAddNode
    if case .ready = surface {
      self.nodes = nodes
      self.canManageNodes = canManageNodes
      self.lots = lots
    } else {
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
    }
  }

  /// The picker of `node`: every Lot in the Server's order, the ones that cannot be picked marked.
  /// `selected` is the Lot picked so far.
  public func moveChoices(for node: NodeRowPresentation, selected: String?)
    -> [MoveLotPresentation]
  {
    lots.map {
      let isCurrent = $0.id == node.lotId
      return MoveLotPresentation(
        id: $0.id, name: $0.name, hasNode: $0.hasNode && !isCurrent, isCurrent: isCurrent,
        isSelected: $0.id == selected && !$0.hasNode && !isCurrent)
    }
  }

  public static let waiting = DevicesPresentation(surface: .waiting, canAddHub: false)

  /// Mirrors the flat snapshot. `hubStatuses` holds `online` or `offline` as the Server said:
  /// anything but `online` reads as offline. `hubLastSeen` holds Unix milliseconds in decimal,
  /// or an empty string for a Hub that never sent a heartbeat.
  public init(
    surface: String, notice: String?, siteId: String?, canAddHub: Bool, canAddNode: Bool = false,
    hubIds: [String], hubStatuses: [String], hubLastSeen: [String], nodeIds: [String] = [],
    nodeLotNames: [String] = [], nodeBatteries: [String] = [], nodeBatteryLow: [Bool] = [],
    nodeCharging: [String] = [], nodeLastSeen: [String] = [], canManageNodes: Bool = false,
    nodeLotIds: [String] = [], lotIds: [String] = [], lotNames: [String] = [],
    lotHasNode: [Bool] = [], workingNodeId: String = "", failedNodeId: String = "",
    actionNotice: String = ""
  ) {
    self.canAddHub = siteId != nil && canAddHub
    self.canAddNode = siteId != nil && canAddNode
    switch surface {
    case "failed":
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
      self.surface = .failed(notice.flatMap(DevicesNoticeKind.init(rawValue:)) ?? .unreachable)
    case "ready":
      guard siteId != nil else {
        self.nodes = []
        self.canManageNodes = false
        self.lots = []
        self.surface = .waiting
        return
      }
      self.canManageNodes = canManageNodes
      let lotCount = min(lotIds.count, lotNames.count, lotHasNode.count)
      self.lots = (0..<lotCount).map {
        MoveLotOption(id: lotIds[$0], name: lotNames[$0], hasNode: lotHasNode[$0])
      }
      let nodeCount = min(
        nodeIds.count, nodeLotNames.count, nodeBatteries.count, nodeBatteryLow.count,
        nodeCharging.count, nodeLastSeen.count)
      self.nodes = (0..<nodeCount).map {
        NodeRowPresentation(
          id: nodeIds[$0], lotName: nodeLotNames[$0].isEmpty ? nil : nodeLotNames[$0],
          battery: Int(nodeBatteries[$0]), batteryLow: nodeBatteryLow[$0],
          charging: ChargeKind(rawValue: nodeCharging[$0]),
          lastSeen: Int64(nodeLastSeen[$0]).map { Date(timeIntervalSince1970: Double($0) / 1000) },
          lotId: $0 < nodeLotIds.count && !nodeLotIds[$0].isEmpty ? nodeLotIds[$0] : nil,
          isWorking: nodeIds[$0] == workingNodeId,
          actionNotice: nodeIds[$0] == failedNodeId
            ? NodeActionNoticeKind(rawValue: actionNotice) : nil)
      }
      let count = min(hubIds.count, hubStatuses.count, hubLastSeen.count)
      self.surface = .ready(
        (0..<count).map {
          HubRowPresentation(
            id: hubIds[$0], isOnline: hubStatuses[$0] == "online",
            lastSeen: Int64(hubLastSeen[$0]).map {
              Date(timeIntervalSince1970: Double($0) / 1000)
            })
        })
    default:
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
      self.surface = .waiting
    }
  }

  /// The Hub rows; empty unless ready.
  public var hubs: [HubRowPresentation] {
    if case .ready(let hubs) = surface { return hubs }
    return []
  }

  /// The list was read and the Site has no Hub and no Node to show: "No Devices yet."
  public var isEmpty: Bool {
    if case .ready(let hubs) = surface { return hubs.isEmpty && nodes.isEmpty }
    return false
  }

  /// The failure that replaces the rows.
  public var failure: DevicesNoticeKind? {
    if case .failed(let notice) = surface { return notice }
    return nil
  }
}

/// The Devices half of the shared Kotlin core, as the SwiftUI shell sees it. The app target
/// adapts `IosDevices` of `ColdframeCore`; no token or URL crosses this boundary. The core
/// follows the current Site of the Sites half.
@MainActor
public protocol DevicesService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (DevicesPresentation) -> Void)
  /// Reads the list again: on every entry of the Devices tab, and for Try again.
  func load()
  /// Moves a Node to another Lot (UX-DR31); needs no Bluetooth. The core refuses a Lot that cannot
  /// be picked and reads the list again after the Server accepted.
  func moveNode(_ nodeId: String, to lotId: String)
  /// Unassigns a Node from its Lot (UX-DR31), after the shell confirmed, naming the Node.
  func unassignNode(_ nodeId: String)
}
