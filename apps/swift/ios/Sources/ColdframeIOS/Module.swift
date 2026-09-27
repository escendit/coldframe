/// The part of the iOS app that holds no UI, so it builds and tests without Xcode.
///
/// The SwiftUI shell and the bridge to the shared Kotlin core arrive with the mobile sign-in
/// story. Until then the module holds only what proves that the workspace builds, tests and lints.
public enum Module {
  /// Name of this module.
  public static let name = "ColdframeIOS"
}
