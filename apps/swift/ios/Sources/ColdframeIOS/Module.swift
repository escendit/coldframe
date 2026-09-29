/// The part of the iOS app that holds no Kotlin: presentation models, catalogue keys and, where
/// SwiftUI exists, the views. It builds and tests without Xcode; only the app target in
/// `apps/swift/ios/App` links the shared Kotlin core.
public enum Module {
  /// Name of this module.
  public static let name = "ColdframeIOS"
}
