/// Theme choice of Settings → Appearance (UX-DR15), stored per device by the core.
public enum ThemePreference: String, CaseIterable, Sendable {
  case system
  case light
  case dark

  /// Anything unknown, or nothing stored, is System.
  public init(stored: String?) {
    self = stored.flatMap(ThemePreference.init(rawValue:)) ?? .system
  }

  /// Whether the dark tokens apply, given the OS appearance.
  public func isDark(systemIsDark: Bool) -> Bool {
    switch self {
    case .system: systemIsDark
    case .light: false
    case .dark: true
    }
  }

  /// `nil` follows the OS; otherwise the forced scheme.
  public var forcedDark: Bool? {
    switch self {
    case .system: nil
    case .light: false
    case .dark: true
    }
  }

  public var label: L10n {
    switch self {
    case .system: .themeSystem
    case .light: .themeLight
    case .dark: .themeDark
    }
  }
}

/// The theme store as the SwiftUI shell sees it; the app target adapts `IosAppearance`.
@MainActor
public protocol AppearanceService: AnyObject {
  func observe(_ onChange: @escaping @MainActor (ThemePreference) -> Void)
  /// Applies at once; there is no Save.
  func select(_ preference: ThemePreference)
}
