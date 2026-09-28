import ColdframeDesignTokens

/// Accessibility traits of a control, mapped to SwiftUI traits by the views (UX-DR98).
public enum ControlTrait: Hashable, Sendable {
  case button
  case selected
  case header
}

/// One segment of a Segmented choice (UX-DR36): selection is exposed as a trait and drawn with a
/// checkmark, never by colour alone.
public struct SegmentPresentation<Value: Hashable & Sendable>: Equatable, Sendable {
  public let value: Value
  public let label: L10n
  public let isSelected: Bool

  public init(value: Value, label: L10n, isSelected: Bool) {
    self.value = value
    self.label = label
    self.isSelected = isSelected
  }

  public var traits: Set<ControlTrait> {
    isSelected ? [.button, .selected] : [.button]
  }

  public var showsCheckmark: Bool { isSelected }
}

/// The Theme switcher's segments (UX-DR53), System first.
public func themeSegments(selected: ThemePreference) -> [SegmentPresentation<ThemePreference>] {
  ThemePreference.allCases.map {
    SegmentPresentation(value: $0, label: $0.label, isSelected: $0 == selected)
  }
}

/// The four mobile tabs (UX-DR57), in order.
public enum AppTab: String, CaseIterable, Hashable, Sendable {
  case garden
  case alerts
  case devices
  case settings

  public var label: L10n {
    switch self {
    case .garden: .navGarden
    case .alerts: .navAlerts
    case .devices: .navDevices
    case .settings: .navSettings
    }
  }

  public var title: L10n {
    switch self {
    case .garden: .gardenTitle
    case .alerts: .alertsTitle
    case .devices: .devicesTitle
    case .settings: .settingsTitle
    }
  }

  public var icon: CarbonIcon {
    switch self {
    case .garden: .grid
    case .alerts: .notification
    case .devices: .box
    case .settings: .settings
    }
  }

  /// The selected tab is exposed as selected (the native tab bar adds the button trait).
  public func traits(selected: AppTab) -> Set<ControlTrait> {
    self == selected ? [.button, .selected] : [.button]
  }
}

/// Rows of the Settings index (UX-DR71): Site settings (Story 1.9), Appearance, then Account.
/// My notifications and Members join with their stories.
public enum SettingsRow: CaseIterable, Sendable {
  case siteSettings
  case appearance
  case account

  public var title: L10n {
    switch self {
    case .siteSettings: .settingsSiteSettings
    case .appearance: .settingsAppearance
    case .account: .settingsAccount
    }
  }

  /// The line under the title; Site settings names the current Site (`%@`).
  public var helper: L10n? {
    switch self {
    case .siteSettings: .settingsSiteSettingsHelper
    case .appearance: .settingsAppearanceHelper
    case .account: nil
    }
  }
}

/// The sign-out confirmation (UX-DR113): a native dialog naming the result, with Cancel.
public struct ConfirmationPresentation: Equatable, Sendable {
  public let title: L10n
  public let message: L10n
  public let confirm: L10n
  public let cancel: L10n
  /// Only one modal level at a time (UX-DR76).
  public let isModal: Bool

  public static let signOut = ConfirmationPresentation(
    title: .settingsSignOutQuestion, message: .settingsSignOutDetail, confirm: .settingsSignOut,
    cancel: .modalCancel, isModal: true)
}

/// Minimum control size on iOS (UX-DR100); the token button height is larger still.
public enum TouchTarget {
  public static let minimum: Double = 44
  public static let control: Double = max(minimum, Spacing.buttonHeight)
}

/// Motion policy of the shell (UX-DR101): state changes swap without transitions.
public enum Motion {
  /// State changes swap instantly.
  public static let animatesStateChanges = false
}
