import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosAlerts` of the shared Kotlin core to `AlertsService`. The snapshot is flat
/// strings, flags and string lists; no token or URL crosses this boundary (AD-14). The core
/// reads the Alerts as soon as a Site is current, so the tab count needs no call from here.
@MainActor
final class CoreAlertsService: AlertsService {
  private let core: IosAlerts
  private var watch: Watch?

  init(core: IosAlerts) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (AlertsPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          AlertsPresentation(
            surface: snapshot.surface, notice: snapshot.notice, siteId: snapshot.siteId,
            siteName: snapshot.siteName, openCount: Int(snapshot.openCount),
            refreshing: snapshot.refreshing, ids: snapshot.alertIds,
            groups: snapshot.alertGroups, variants: snapshot.alertVariants,
            conditions: snapshot.alertConditions, eyebrows: snapshot.alertEyebrows,
            icons: snapshot.alertIcons, quantities: snapshot.alertQuantities,
            lotIds: snapshot.alertLotIds, lotNames: snapshot.alertLotNames,
            deviceIds: snapshot.alertDeviceIds, openedAts: snapshot.alertOpenedAt,
            closedAts: snapshot.alertClosedAt, targets: snapshot.alertTargets))
      }
    }
  }

  func load() { core.load() }

  func refresh() { core.refresh() }
}
