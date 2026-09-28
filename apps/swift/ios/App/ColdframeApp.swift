import ColdframeIOS
import SwiftUI

/// The iOS app: the SwiftUI shell over the shared Kotlin core. The session resumes on start and
/// on every return to the foreground.
@main
struct ColdframeApp: App {
  @StateObject private var model = ShellModel(
    signIn: CoreSignInService(), appearance: CoreAppearanceService())
  @Environment(\.scenePhase) private var scenePhase

  var body: some Scene {
    WindowGroup {
      ColdframeRootView(
        presentation: model.presentation,
        theme: model.theme,
        onSignIn: { model.signIn.signIn() },
        onSignOut: { model.signIn.signOut() },
        onSelectTheme: { model.appearance.select($0) }
      )
      .onChange(of: scenePhase, initial: true) { _, phase in
        if phase == .active { model.signIn.resume() }
      }
    }
  }
}
