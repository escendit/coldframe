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

/// The unit a Silence Window is entered and shown in, as the core names it.
public enum SilenceWindowUnitKind: String, CaseIterable, Sendable {
  case minutes
  case hours
  case days

  /// "min", "h" or "d" on its segment.
  public var label: L10n {
    switch self {
    case .minutes: .devicesSilenceWindowUnitMinutes
    case .hours: .devicesSilenceWindowUnitHours
    case .days: .devicesSilenceWindowUnitDays
    }
  }
}

/// A Silence Window as the core wrote it, a number with a unit: "5 min", "8 h", "30 d".
public struct SilenceWindowLengthPresentation: Equatable, Sendable {
  public let value: Int
  public let unit: SilenceWindowUnitKind

  public init(value: Int, unit: SilenceWindowUnitKind) {
    self.value = value
    self.unit = unit
  }

  /// "8 h".
  public var short: Copy {
    switch unit {
    case .minutes: Copy(.durationMinutes, .number(value))
    case .hours: Copy(.durationHours, .number(value))
    case .days: Copy(.durationDays, .number(value))
    }
  }
}

/// Why a Device's Silence Window was not changed, named as the core names it. Each is inline
/// copy under the Device's row.
public enum SilenceWindowNoticeKind: String, CaseIterable, Sendable {
  /// 403: the Role is too low; it may have changed since the list was read.
  case forbidden
  case notFound
  /// Outside 1 min to 30 days: refused by the core before sending, or by the Server.
  case outOfRange
  case unreachable
  case certificate
  case unexpected

  public var message: L10n {
    switch self {
    case .forbidden: .siteSettingsForbidden
    case .notFound: .devicesSilenceWindowNotFound
    case .outOfRange: .devicesSilenceWindowRange
    case .unreachable: .devicesUnreachable
    case .certificate: .noticeCertificate
    case .unexpected: .devicesActionUnexpected
    }
  }

  /// "You can't change this on ‹Site›…" names the Site (UX-DR84).
  public var takesSiteName: Bool { self == .forbidden }
}

/// What a Device row says about silence (Story 7.2), as the core sent it for one `now`. Nothing
/// here measures a silence or decides that a Device is silent.
public struct DeviceSilencePresentation: Equatable, Sendable {
  /// The Device's short name, "3F2A" of "Hub 3F2A"; empty when the core sent none.
  public let shortName: String
  /// When the Device was last heard; nil unless the Server says it is silent.
  public let silentSince: Date?
  /// How long it has been silent, rounded down by the core; nil unless silent.
  public let silence: LotDurationPresentation?
  /// The Silence Window in force in seconds; nil when the Server sent none.
  public let windowSeconds: Int?
  /// The same as a number with a unit.
  public let window: SilenceWindowLengthPresentation?
  /// The Device has its own window, so "Use default" is offered.
  public let windowOverridden: Bool
  /// Its Silence Window is being saved.
  public let windowWorking: Bool
  /// The last change of its Silence Window that did not happen.
  public let windowNotice: SilenceWindowNoticeKind?

  public init(
    shortName: String = "", silentSince: Date? = nil, silence: LotDurationPresentation? = nil,
    windowSeconds: Int? = nil, window: SilenceWindowLengthPresentation? = nil,
    windowOverridden: Bool = false, windowWorking: Bool = false,
    windowNotice: SilenceWindowNoticeKind? = nil
  ) {
    self.shortName = shortName
    self.silentSince = silentSince
    self.silence = silence
    self.windowSeconds = windowSeconds
    self.window = window
    self.windowOverridden = windowOverridden
    self.windowWorking = windowWorking
    self.windowNotice = windowNotice
  }

  /// A Device that is not silent and whose window the Server did not send: the row as before.
  public static let none = DeviceSilencePresentation()

  public var isSilent: Bool { silence != nil }

  /// The shape beside "Silent 6 h"; the word is the meaning, never colour alone.
  public var icon: CarbonIcon? { isSilent ? .help : nil }

  /// "Silent 6 h", in sentence case (upper case comes from the style); nil unless silent.
  public func silentText(_ context: CopyContext) -> String? {
    silence.map { context.text(.devicesSilent, .text(context.resolve($0.short))) }
  }

  /// "Silence Window · 8 h": the window in force, shown to every Role as text, never a control.
  public func windowText(_ context: CopyContext) -> String? {
    window.map { context.text(.devicesSilenceWindowSummary, .text(context.resolve($0.short))) }
  }
}

/// The Silence Window sheet of one Hub or Node (UX-DR31, UX-DR84), for Administrators and Owners
/// only: the window in force, a number with a unit of min, h or d, Save, and "Use default" when
/// the Device has its own window. The core decides what is valid: a value that is no whole
/// number from 1 min to 30 days is refused before anything is sent.
public struct SilenceWindowPresentation: Equatable, Sendable, Identifiable {
  public enum Device: Equatable, Sendable {
    case hub
    case node
  }

  /// The Device ID.
  public let id: String
  public let device: Device
  /// The window in force; nil when the Server sent none.
  public let current: SilenceWindowLengthPresentation?
  public let overridden: Bool

  public init(
    id: String, device: Device, current: SilenceWindowLengthPresentation?, overridden: Bool
  ) {
    self.id = id
    self.device = device
    self.current = current
    self.overridden = overridden
  }

  /// "Silence Window for Hub 3f2a9c0d1e4b5a67": the sheet's title, and the spoken label of the
  /// row action that opens it.
  public var title: Copy {
    Copy(device == .hub ? .devicesSilenceWindowHub : .devicesSilenceWindowNode, .text(id))
  }

  /// "Silence Window · 8 h"; nil when the Server sent no window.
  public func currentText(_ context: CopyContext) -> String? {
    current.map { context.text(.devicesSilenceWindowSummary, .text(context.resolve($0.short))) }
  }

  /// What the number field holds when the sheet opens: the window in force.
  public var initialText: String { current.map { String($0.value) } ?? "" }

  /// The unit picked when the sheet opens: the one of the window in force, else hours.
  public var initialUnit: SilenceWindowUnitKind { current?.unit ?? .hours }

  /// Only a Device with its own window has a default to return to.
  public var showsUseDefault: Bool { overridden }

  /// min, h, d; selection is a trait and a checkmark, never colour alone.
  public func unitSegments(selected: SilenceWindowUnitKind)
    -> [SegmentPresentation<SilenceWindowUnitKind>]
  {
    SilenceWindowUnitKind.allCases.map {
      SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == selected)
    }
  }

  public static let label: L10n = .devicesSilenceWindow
  public static let unitLabel: L10n = .devicesSilenceWindowUnit
  public static let helper: L10n = .devicesSilenceWindowHelper
  /// The message of a refused value, with the range in it.
  public static let rangeError: L10n = .devicesSilenceWindowRange
}

/// One Hub row of the Devices list (UX-DR30): the full Device ID in `meta-mono`, the status as a
/// word with its own icon, and when the Hub was last seen. `isOnline` is the Server's answer;
/// nothing here computes it. A silent Hub adds the help icon and "Silent 12 min" (Story 7.2).
public struct HubRowPresentation: Equatable, Sendable, Identifiable {
  /// The Device ID, shown in full and never reformatted.
  public let id: String
  public let isOnline: Bool
  /// The last accepted heartbeat, or nil for a Hub that never sent one.
  public let lastSeen: Date?
  /// Its silence and its Silence Window, as the core sent them.
  public let silence: DeviceSilencePresentation

  public init(
    id: String, isOnline: Bool, lastSeen: Date?, silence: DeviceSilencePresentation = .none
  ) {
    self.id = id
    self.isOnline = isOnline
    self.lastSeen = lastSeen
    self.silence = silence
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
  /// Its silence and its Silence Window, as the core sent them.
  public let silence: DeviceSilencePresentation

  public init(
    id: String, lotName: String?, battery: Int?, batteryLow: Bool, charging: ChargeKind?,
    lastSeen: Date?, lotId: String? = nil, isWorking: Bool = false,
    actionNotice: NodeActionNoticeKind? = nil, silence: DeviceSilencePresentation = .none
  ) {
    self.silence = silence
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
  /// Silence Window is a row action for Administrators and Owners, as the core decided; for a
  /// Member the control does not exist, it is never merely disabled (UX-DR84).
  public let canSetSilenceWindow: Bool
  /// The current Site's name, for "You can't change this on ‹Site›…"; empty unless ready.
  public let siteName: String

  public init(
    surface: DevicesSurface, canAddHub: Bool, canAddNode: Bool = false,
    nodes: [NodeRowPresentation] = [], canManageNodes: Bool = false, lots: [MoveLotOption] = [],
    canSetSilenceWindow: Bool = false, siteName: String = ""
  ) {
    self.surface = surface
    self.canAddHub = canAddHub
    self.canAddNode = canAddNode
    if case .ready = surface {
      self.nodes = nodes
      self.canManageNodes = canManageNodes
      self.lots = lots
      self.canSetSilenceWindow = canSetSilenceWindow
      self.siteName = siteName
    } else {
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
      self.canSetSilenceWindow = false
      self.siteName = ""
    }
  }

  /// The Silence Window sheet of the Hub or Node `deviceId`; nil for a Role that may not change
  /// it and for a Device that is not listed, so the sheet cannot exist for a Member.
  public func silenceWindow(for deviceId: String) -> SilenceWindowPresentation? {
    guard canSetSilenceWindow else { return nil }
    if let hub = hubs.first(where: { $0.id == deviceId }) {
      return SilenceWindowPresentation(
        id: hub.id, device: .hub, current: hub.silence.window,
        overridden: hub.silence.windowOverridden)
    }
    if let node = nodes.first(where: { $0.id == deviceId }) {
      return SilenceWindowPresentation(
        id: node.id, device: .node, current: node.silence.window,
        overridden: node.silence.windowOverridden)
    }
    return nil
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

  /// The silence lists of one kind of Device as the core flattens them (Story 7.2), parallel to
  /// its ID list. `silentSince` holds Unix milliseconds in decimal and `windowSeconds` whole
  /// seconds in decimal; `silenceValues` with `silenceUnits` and `windowValues` with
  /// `windowUnits` are a number with `minutes`, `hours` or `days`. An entry that is empty,
  /// missing or not known to this client is left out, and the row then reads as before.
  public struct SilenceLists: Equatable, Sendable {
    public var shortNames: [String]
    public var silentSince: [String]
    public var silenceValues: [String]
    public var silenceUnits: [String]
    public var windowSeconds: [String]
    public var windowValues: [String]
    public var windowUnits: [String]
    public var windowOverridden: [Bool]

    public init(
      shortNames: [String] = [], silentSince: [String] = [], silenceValues: [String] = [],
      silenceUnits: [String] = [], windowSeconds: [String] = [], windowValues: [String] = [],
      windowUnits: [String] = [], windowOverridden: [Bool] = []
    ) {
      self.shortNames = shortNames
      self.silentSince = silentSince
      self.silenceValues = silenceValues
      self.silenceUnits = silenceUnits
      self.windowSeconds = windowSeconds
      self.windowValues = windowValues
      self.windowUnits = windowUnits
      self.windowOverridden = windowOverridden
    }

    private static func entry<Value>(_ list: [Value], _ index: Int) -> Value? {
      index < list.count ? list[index] : nil
    }

    /// The silence of the Device at `index`, whose Silence Window is being saved (`working`) or
    /// was not changed (`notice`).
    func presentation(at index: Int, working: Bool, notice: SilenceWindowNoticeKind?)
      -> DeviceSilencePresentation
    {
      let silence = Self.entry(silenceValues, index).flatMap { Int($0) }.flatMap { value in
        Self.entry(silenceUnits, index).flatMap(LotDurationPresentation.Unit.init(rawValue:))
          .map { LotDurationPresentation(value: value, unit: $0) }
      }
      let window = Self.entry(windowValues, index).flatMap { Int($0) }.flatMap { value in
        Self.entry(windowUnits, index).flatMap(SilenceWindowUnitKind.init(rawValue:))
          .map { SilenceWindowLengthPresentation(value: value, unit: $0) }
      }
      return DeviceSilencePresentation(
        shortName: Self.entry(shortNames, index) ?? "",
        silentSince: Self.entry(silentSince, index).flatMap { Int64($0) }.map {
          Date(timeIntervalSince1970: Double($0) / 1000)
        },
        silence: silence, windowSeconds: Self.entry(windowSeconds, index).flatMap { Int($0) },
        window: window, windowOverridden: Self.entry(windowOverridden, index) ?? false,
        windowWorking: working, windowNotice: notice)
    }
  }

  /// Mirrors the flat snapshot. `hubStatuses` holds `online` or `offline` as the Server said:
  /// anything but `online` reads as offline. `hubLastSeen` holds Unix milliseconds in decimal,
  /// or an empty string for a Hub that never sent a heartbeat. `hubSilence` and `nodeSilence`
  /// are the silence lists of the Hubs and the Nodes; `silenceWindowWorkingId` is the Device
  /// whose Silence Window is being saved, `silenceWindowFailedId` with `silenceWindowNotice` the
  /// last change that did not happen (all empty when none).
  public init(
    surface: String, notice: String?, siteId: String?, canAddHub: Bool, canAddNode: Bool = false,
    hubIds: [String], hubStatuses: [String], hubLastSeen: [String], nodeIds: [String] = [],
    nodeLotNames: [String] = [], nodeBatteries: [String] = [], nodeBatteryLow: [Bool] = [],
    nodeCharging: [String] = [], nodeLastSeen: [String] = [], canManageNodes: Bool = false,
    nodeLotIds: [String] = [], lotIds: [String] = [], lotNames: [String] = [],
    lotHasNode: [Bool] = [], workingNodeId: String = "", failedNodeId: String = "",
    actionNotice: String = "", siteName: String? = nil, hubSilence: SilenceLists = SilenceLists(),
    nodeSilence: SilenceLists = SilenceLists(), canSetSilenceWindow: Bool = false,
    silenceWindowWorkingId: String = "", silenceWindowFailedId: String = "",
    silenceWindowNotice: String = ""
  ) {
    self.canAddHub = siteId != nil && canAddHub
    self.canAddNode = siteId != nil && canAddNode
    switch surface {
    case "failed":
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
      self.canSetSilenceWindow = false
      self.siteName = ""
      self.surface = .failed(notice.flatMap(DevicesNoticeKind.init(rawValue:)) ?? .unreachable)
    case "ready":
      guard siteId != nil else {
        self.nodes = []
        self.canManageNodes = false
        self.lots = []
        self.canSetSilenceWindow = false
        self.siteName = ""
        self.surface = .waiting
        return
      }
      self.canManageNodes = canManageNodes
      self.canSetSilenceWindow = canSetSilenceWindow
      self.siteName = siteName ?? ""
      // A notice this client does not know still says that nothing changed.
      let windowNotice =
        silenceWindowNotice.isEmpty
        ? nil : SilenceWindowNoticeKind(rawValue: silenceWindowNotice) ?? .unexpected
      func silence(_ lists: SilenceLists, _ index: Int, _ id: String)
        -> DeviceSilencePresentation
      {
        lists.presentation(
          at: index, working: id == silenceWindowWorkingId,
          notice: id == silenceWindowFailedId ? windowNotice : nil)
      }
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
            ? NodeActionNoticeKind(rawValue: actionNotice) : nil,
          silence: silence(nodeSilence, $0, nodeIds[$0]))
      }
      let count = min(hubIds.count, hubStatuses.count, hubLastSeen.count)
      self.surface = .ready(
        (0..<count).map {
          HubRowPresentation(
            id: hubIds[$0], isOnline: hubStatuses[$0] == "online",
            lastSeen: Int64(hubLastSeen[$0]).map {
              Date(timeIntervalSince1970: Double($0) / 1000)
            }, silence: silence(hubSilence, $0, hubIds[$0]))
        })
    default:
      self.nodes = []
      self.canManageNodes = false
      self.lots = []
      self.canSetSilenceWindow = false
      self.siteName = ""
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
  /// The one-minute tick: reads the core's snapshot again, so the silence of silent Devices
  /// moves. Nothing is fetched.
  func tick()
  /// Sets the Silence Window of a Hub or Node from what the User typed: `text` is a whole number
  /// of `unit` (Administrator or Owner). The core refuses anything outside 1 min to 30 days with
  /// its `outOfRange` notice and sends nothing; false then, so the sheet stays open.
  func setSilenceWindow(_ deviceId: String, text: String, unit: SilenceWindowUnitKind) -> Bool
  /// "Use default": returns the Device to the Silence Window of its kind.
  func useDefaultSilenceWindow(_ deviceId: String)
}
