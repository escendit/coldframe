import ColdframeIOS
import SwiftUI

/// The iOS app: the SwiftUI shell over the shared Kotlin core. The session resumes on start and
/// on every return to the foreground, and the Site overview reads its Lots again.
@main
struct ColdframeApp: App {
  @StateObject private var model: ShellModel = {
    let signIn = CoreSignInService()
    return ShellModel(
      signIn: signIn, appearance: CoreAppearanceService(),
      sites: CoreSitesService(core: signIn.sites), lots: CoreLotsService(core: signIn.lots),
      hubSetup: CoreHubSetupService(core: signIn.hubSetup),
      devices: CoreDevicesService(core: signIn.devices),
      nodeSetup: CoreNodeSetupService(core: signIn.nodeSetup),
      lotDetail: CoreLotDetailService(core: signIn.lotDetail))
  }()
  @Environment(\.scenePhase) private var scenePhase

  var body: some Scene {
    WindowGroup {
      ColdframeRootView(
        presentation: model.presentation,
        sites: model.sites,
        sitesActions: model.sitesActions,
        lots: model.lots,
        lotsActions: model.lotsActions,
        hubSetup: model.hubSetup,
        hubSetupActions: model.hubSetupActions,
        theme: model.theme,
        onSignIn: { model.signIn.signIn() },
        onSignOut: { model.signIn.signOut() },
        onSelectTheme: { model.appearance.select($0) },
        devices: model.devices,
        devicesActions: model.devicesActions,
        nodeSetup: model.nodeSetup,
        nodeSetupActions: model.nodeSetupActions,
        lotDetail: model.lotDetail,
        lotDetailActions: model.lotDetailActions
      )
      .onChange(of: scenePhase, initial: true) { _, phase in
        if phase == .active {
          model.signIn.resume()
          // The overview reads its Lots again every time the app comes to the front.
          model.lotsService?.refresh()
          // An open Lot detail reads its Lot again too; with none open this does nothing.
          model.lotDetailService?.refresh()
        }
      }
    }
  }
}
