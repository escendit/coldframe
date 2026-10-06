import ColdframeIOS
import SwiftUI

/// The iOS app: the SwiftUI shell over the shared Kotlin core. The session resumes on start and
/// on every return to the foreground.
@main
struct ColdframeApp: App {
  @StateObject private var model: ShellModel = {
    let signIn = CoreSignInService()
    return ShellModel(
      signIn: signIn, appearance: CoreAppearanceService(),
      sites: CoreSitesService(core: signIn.sites), lots: CoreLotsService(core: signIn.lots),
      hubSetup: CoreHubSetupService(core: signIn.hubSetup),
      devices: CoreDevicesService(core: signIn.devices))
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
        devicesActions: model.devicesActions
      )
      .onChange(of: scenePhase, initial: true) { _, phase in
        if phase == .active { model.signIn.resume() }
      }
    }
  }
}
