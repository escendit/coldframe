import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosDevices` of the shared Kotlin core to `DevicesService`. The snapshot is flat
/// strings, flags and string lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreDevicesService: DevicesService {
  private let core: IosDevices
  private var watch: Watch?

  init(core: IosDevices) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (DevicesPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          DevicesPresentation(
            surface: snapshot.surface, notice: snapshot.notice, siteId: snapshot.siteId,
            canAddHub: snapshot.canAddHub, canAddNode: snapshot.canAddNode,
            hubIds: snapshot.hubIds, hubStatuses: snapshot.hubStatuses,
            hubLastSeen: snapshot.hubLastSeen, nodeIds: snapshot.nodeIds,
            nodeLotNames: snapshot.nodeLotNames, nodeBatteries: snapshot.nodeBatteries,
            nodeBatteryLow: snapshot.nodeBatteryLow.map { $0.boolValue },
            nodeCharging: snapshot.nodeCharging, nodeLastSeen: snapshot.nodeLastSeen,
            canManageNodes: snapshot.canManageNodes, nodeLotIds: snapshot.nodeLotIds,
            lotIds: snapshot.lotIds, lotNames: snapshot.lotNames,
            lotHasNode: snapshot.lotHasNode.map { $0.boolValue },
            workingNodeId: snapshot.workingNodeId, failedNodeId: snapshot.failedNodeId,
            actionNotice: snapshot.actionNotice))
      }
    }
  }

  func load() { core.load() }

  func moveNode(_ nodeId: String, to lotId: String) { core.moveNode(nodeId: nodeId, lotId: lotId) }

  func unassignNode(_ nodeId: String) { core.unassignNode(nodeId: nodeId) }
}
