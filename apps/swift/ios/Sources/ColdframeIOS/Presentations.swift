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

  /// The label in the tab bar: "Alerts · 5" on the Alerts tab while Alerts are open (UX-DR64),
  /// the plain label otherwise.
  public func labelCopy(openAlerts: Int) -> Copy {
    self == .alerts && openAlerts > 0 ? Copy(.navAlertsCount, .number(openAlerts)) : Copy(label)
  }

  /// What VoiceOver says for the tab: "Alerts, 5 open" while Alerts are open.
  public func spokenCopy(openAlerts: Int) -> Copy {
    self == .alerts && openAlerts > 0 ? Copy(.countOpenAlerts, .number(openAlerts)) : Copy(label)
  }

  /// The selected tab is exposed as selected (the native tab bar adds the button trait).
  public func traits(selected: AppTab) -> Set<ControlTrait> {
    self == selected ? [.button, .selected] : [.button]
  }
}

/// Rows of the Settings index (UX-DR71): My notifications (Story 6.3, UX-DR72), Site settings
/// (Story 1.9), Appearance, then Account. Members joins with its story.
public enum SettingsRow: CaseIterable, Sendable {
  case notifications
  case siteSettings
  case appearance
  case account

  public var title: L10n {
    switch self {
    case .notifications: .settingsNotifications
    case .siteSettings: .settingsSiteSettings
    case .appearance: .settingsAppearance
    case .account: .settingsAccount
    }
  }

  /// The line under the title; Site settings names the current Site (`%@`).
  public var helper: L10n? {
    switch self {
    case .notifications: .settingsNotificationsHelper
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

/// A Lot opened from a tile, an Alert row or a tapped notification: what the destination needs
/// before the Server answers.
public struct OpenedLot: Hashable, Sendable {
  public let id: String
  public let name: String

  public init(id: String, name: String) {
    self.id = id
    self.name = name
  }
}

/// What the Garden tab's stack shows over the Site overview. The root hoists it, so a tapped
/// notification can open Lot detail, or return to the overview, from outside the tab (UX-DR120).
public struct GardenPath: Equatable, Sendable {
  /// The Lot whose detail is open.
  public var lot: OpenedLot?
  /// Site settings, opened from the Site menu.
  public var siteSettings: Bool

  public init(lot: OpenedLot? = nil, siteSettings: Bool = false) {
    self.lot = lot
    self.siteSettings = siteSettings
  }

  /// The overview alone.
  public static let root = GardenPath()
}
