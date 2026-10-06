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
  public let battery: Int?
  /// Below 20 %, as the core decided: shows `battery--low`.
  public let batteryLow: Bool
  public let charging: ChargeKind?
  public let lastSeen: Date?

  public init(
    id: String, lotName: String?, battery: Int?, batteryLow: Bool, charging: ChargeKind?,
    lastSeen: Date?
  ) {
    self.id = id
    self.lotName = lotName
    self.battery = battery
    self.batteryLow = batteryLow
    self.charging = charging
    self.lastSeen = lastSeen
  }

  public var batteryIcon: CarbonIcon? { batteryLow ? .batteryLow : nil }

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

  public init(
    surface: DevicesSurface, canAddHub: Bool, canAddNode: Bool = false,
    nodes: [NodeRowPresentation] = []
  ) {
    self.surface = surface
    self.canAddHub = canAddHub
    self.canAddNode = canAddNode
    if case .ready = surface { self.nodes = nodes } else { self.nodes = [] }
  }

  public static let waiting = DevicesPresentation(surface: .waiting, canAddHub: false)

  /// Mirrors the flat snapshot. `hubStatuses` holds `online` or `offline` as the Server said:
  /// anything but `online` reads as offline. `hubLastSeen` holds Unix milliseconds in decimal,
  /// or an empty string for a Hub that never sent a heartbeat.
  public init(
    surface: String, notice: String?, siteId: String?, canAddHub: Bool, canAddNode: Bool = false,
    hubIds: [String], hubStatuses: [String], hubLastSeen: [String], nodeIds: [String] = [],
    nodeLotNames: [String] = [], nodeBatteries: [String] = [], nodeBatteryLow: [Bool] = [],
    nodeCharging: [String] = [], nodeLastSeen: [String] = []
  ) {
    self.canAddHub = siteId != nil && canAddHub
    self.canAddNode = siteId != nil && canAddNode
    switch surface {
    case "failed":
      self.nodes = []
      self.surface = .failed(notice.flatMap(DevicesNoticeKind.init(rawValue:)) ?? .unreachable)
    case "ready":
      guard siteId != nil else {
        self.nodes = []
        self.surface = .waiting
        return
      }
      let nodeCount = min(
        nodeIds.count, nodeLotNames.count, nodeBatteries.count, nodeBatteryLow.count,
        nodeCharging.count, nodeLastSeen.count)
      self.nodes = (0..<nodeCount).map {
        NodeRowPresentation(
          id: nodeIds[$0], lotName: nodeLotNames[$0].isEmpty ? nil : nodeLotNames[$0],
          battery: Int(nodeBatteries[$0]), batteryLow: nodeBatteryLow[$0],
          charging: ChargeKind(rawValue: nodeCharging[$0]),
          lastSeen: Int64(nodeLastSeen[$0]).map { Date(timeIntervalSince1970: Double($0) / 1000) })
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
}
