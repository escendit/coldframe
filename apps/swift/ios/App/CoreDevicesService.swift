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
            hubLastSeen: snapshot.hubLastSeen))
      }
    }
  }

  func load() { core.load() }
}
