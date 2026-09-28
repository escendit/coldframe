import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosSites` of the shared Kotlin core to `SitesService`. The snapshot is flat strings
/// and lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreSitesService: SitesService {
  private let core: IosSites
  private var watch: Watch?

  init(core: IosSites) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (SitesPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          SitesPresentation(
            surface: snapshot.surface, notice: snapshot.notice,
            siteIds: snapshot.siteIds, siteNames: snapshot.siteNames,
            siteRoles: snapshot.siteRoles,
            currentId: snapshot.currentId, currentName: snapshot.currentName,
            currentRole: snapshot.currentRole,
            formShown: snapshot.formShown, formName: snapshot.formName,
            formNameError: snapshot.formNameError, formWorking: snapshot.formWorking,
            formNotice: snapshot.formNotice, formCancellable: snapshot.formCancellable,
            timeZoneDetected: snapshot.timeZoneDetected, timeZoneChosen: snapshot.timeZoneChosen,
            timeZoneChanging: snapshot.timeZoneChanging,
            steps: snapshot.steps, stepStates: snapshot.stepStates,
            stepsActionable: snapshot.stepsActionable, memberNotice: snapshot.memberNotice,
            menuItems: snapshot.menuItems, menuEnabled: snapshot.menuEnabled))
      }
    }
  }

  func availableTimeZones() -> [String] { core.availableTimeZones() }

  func load() { core.load() }

  func select(siteId: String) { core.select(siteId: siteId) }

  func newSite() { core.doNewSite() }

  func cancelNewSite() { core.cancelNewSite() }

  func setName(_ name: String) { core.setName(name: name) }

  func confirmTimeZone() { core.confirmTimeZone() }

  func changeTimeZone() { core.changeTimeZone() }

  func pickTimeZone(_ zoneId: String) { core.pickTimeZone(zoneId: zoneId) }

  func submit() { core.submit() }
}
