import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosDevices` of the shared Kotlin core to `DevicesService`. The snapshot is flat
/// strings, flags and string lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreDevicesService: DevicesService {
  private let core: IosDevices
  private var watch: Watch?
  private var onChange: (@MainActor (DevicesPresentation) -> Void)?
  /// Pause and Resume from a Device row; its state arrives with the snapshot.
  let pause: PauseService

  init(core: IosDevices) {
    self.core = core
    self.pause = CorePauseService(core: core.pause)
  }

  func observe(_ onChange: @escaping @MainActor (DevicesPresentation) -> Void) {
    self.onChange = onChange
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated { onChange(CoreDevicesService.presentation(of: snapshot)) }
    }
  }

  /// The core measures the silence of silent Devices when a snapshot is taken; the tick takes a
  /// new one.
  func tick() { onChange?(CoreDevicesService.presentation(of: core.current())) }

  private static func presentation(of snapshot: DevicesSnapshot) -> DevicesPresentation {
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
      actionNotice: snapshot.actionNotice, siteName: snapshot.siteName,
      hubSilence: DevicesPresentation.SilenceLists(
        shortNames: snapshot.hubShortNames, silentSince: snapshot.hubSilentSince,
        silenceValues: snapshot.hubSilenceValues, silenceUnits: snapshot.hubSilenceUnits,
        windowSeconds: snapshot.hubWindowSeconds, windowValues: snapshot.hubWindowValues,
        windowUnits: snapshot.hubWindowUnits,
        windowOverridden: snapshot.hubWindowOverridden.map { $0.boolValue }),
      nodeSilence: DevicesPresentation.SilenceLists(
        shortNames: snapshot.nodeShortNames, silentSince: snapshot.nodeSilentSince,
        silenceValues: snapshot.nodeSilenceValues, silenceUnits: snapshot.nodeSilenceUnits,
        windowSeconds: snapshot.nodeWindowSeconds, windowValues: snapshot.nodeWindowValues,
        windowUnits: snapshot.nodeWindowUnits,
        windowOverridden: snapshot.nodeWindowOverridden.map { $0.boolValue }),
      canSetSilenceWindow: snapshot.canSetSilenceWindow,
      silenceWindowWorkingId: snapshot.silenceWindowWorkingId,
      silenceWindowFailedId: snapshot.silenceWindowFailedId,
      silenceWindowNotice: snapshot.silenceWindowNotice,
      hubPause: DevicesPresentation.PauseLists(
        paused: snapshot.hubPaused.map { $0.boolValue },
        pausedBySite: snapshot.hubPausedBySite.map { $0.boolValue },
        canPause: snapshot.hubCanPause.map { $0.boolValue },
        canResume: snapshot.hubCanResume.map { $0.boolValue },
        resumeSiteHint: snapshot.hubResumeSiteHint.map { $0.boolValue }),
      nodePause: DevicesPresentation.PauseLists(
        paused: snapshot.nodePaused.map { $0.boolValue },
        pausedBySite: snapshot.nodePausedBySite.map { $0.boolValue },
        canPause: snapshot.nodeCanPause.map { $0.boolValue },
        canResume: snapshot.nodeCanResume.map { $0.boolValue },
        resumeSiteHint: snapshot.nodeResumeSiteHint.map { $0.boolValue }),
      pause: CorePauseService.presentation(of: snapshot.pause))
  }

  func load() { core.load() }

  func moveNode(_ nodeId: String, to lotId: String) { core.moveNode(nodeId: nodeId, lotId: lotId) }

  func unassignNode(_ nodeId: String) { core.unassignNode(nodeId: nodeId) }

  /// The core decides what is valid: false when it refused the value and sent nothing.
  func setSilenceWindow(_ deviceId: String, text: String, unit: SilenceWindowUnitKind) -> Bool {
    core.setSilenceWindowText(deviceId: deviceId, text: text, unit: unit.rawValue)
  }

  func useDefaultSilenceWindow(_ deviceId: String) {
    core.useDefaultSilenceWindow(deviceId: deviceId)
  }
}
