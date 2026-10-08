import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosSignIn` of the shared Kotlin core to `SignInService`. With `CoreSitesService` and
/// `CoreLotsService`, the only files of the app that import `ColdframeCore`; no token crosses
/// this boundary.
@MainActor
final class CoreSignInService: SignInService {
  private let core: IosSignIn
  private var watch: Watch?

  /// Server URL, issuer and client id come from Info.plist, set by `Config/Coldframe.xcconfig`
  /// (AD-23). Empty values fall back to the core's never-resolvable defaults.
  init(bundle: Bundle = .main) {
    func value(_ key: String) -> String? { bundle.object(forInfoDictionaryKey: key) as? String }
    core = IosSignIn.companion.create(
      serverUrl: value("ColdframeServerURL"),
      keycloakIssuer: value("ColdframeKeycloakIssuer"),
      clientId: value("ColdframeKeycloakClientId"))
  }

  func observe(_ onChange: @escaping @MainActor (SignInPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          SignInPresentation(
            restoring: snapshot.restoring, working: snapshot.working,
            signedIn: snapshot.signedIn, notice: snapshot.notice, action: snapshot.action))
      }
    }
  }

  /// The Sites half of the same core instance, for `CoreSitesService`.
  var sites: IosSites { core.sites }

  /// The Lots half of the same core instance, for `CoreLotsService`.
  var lots: IosLots { core.lots }

  /// Lot detail of the same core instance, for `CoreLotDetailService`.
  var lotDetail: IosLotDetail { core.lotDetail }

  /// Calibrate of the same core instance, for `CoreCalibrateService`.
  var calibrate: IosCalibrate { core.calibrate }

  /// Thresholds of the same core instance, for `CoreThresholdsService`.
  var thresholds: IosThresholds { core.thresholds }

  /// The Devices half of the same core instance, for `CoreDevicesService`.
  var devices: IosDevices { core.devices }

  /// Add a Hub of the same core instance, for `CoreHubSetupService`.
  var hubSetup: IosHubSetup { core.hubSetup }

  /// Add a Node of the same core instance, for `CoreNodeSetupService`.
  var nodeSetup: IosNodeSetup { core.nodeSetup }

  func signIn() { core.signIn() }

  func resume() { core.resume() }

  func signOut() { core.signOut() }
}

/// Adapts `IosAppearance` of the shared Kotlin core to `AppearanceService`. ColdframeCore exports
/// its own `ThemePreference`, so the Swift one is named with its module.
@MainActor
final class CoreAppearanceService: AppearanceService {
  private let core = IosAppearance()
  private var watch: Watch?

  func observe(_ onChange: @escaping @MainActor (ColdframeIOS.ThemePreference) -> Void) {
    watch?.close()
    watch = core.watch { stored in
      MainActor.assumeIsolated { onChange(ColdframeIOS.ThemePreference(stored: stored)) }
    }
  }

  func select(_ preference: ColdframeIOS.ThemePreference) {
    core.select(storedValue: preference.rawValue)
  }
}
