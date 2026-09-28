#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import SwiftUI
  #if canImport(UIKit)
    import UIKit
  #endif

  /// The colour tokens this story draws with, resolved for one theme.
  public struct ColdframePalette: Equatable, Sendable {
    public let isDark: Bool

    public init(isDark: Bool) {
      self.isDark = isDark
    }

    public func color(_ token: ThemedColor) -> Color {
      let parts = ThemedColor.components(of: token.resolve(isDark: isDark))
      return Color(
        .sRGB, red: parts.red, green: parts.green, blue: parts.blue, opacity: parts.alpha)
    }

    public var background: Color { color(ColorTokens.background) }
    public var layer01: Color { color(ColorTokens.layer01) }
    public var field01: Color { color(ColorTokens.field01) }
    public var textPrimary: Color { color(ColorTokens.textPrimary) }
    public var textSecondary: Color { color(ColorTokens.textSecondary) }
    public var textHelper: Color { color(ColorTokens.textHelper) }
    public var textOnColor: Color { color(ColorTokens.textOnColor) }
    public var borderSubtle: Color { color(ColorTokens.borderSubtle) }
    public var borderStrong: Color { color(ColorTokens.borderStrong) }
    public var primary: Color { color(ColorTokens.primary) }
    public var primaryActive: Color { color(ColorTokens.primaryActive) }
    public var primaryText: Color { color(ColorTokens.primaryText) }
    public var secondary: Color { color(ColorTokens.secondary) }
    public var buttonSecondary: Color { color(ColorTokens.buttonSecondary) }
    public var inkOnBright: Color { color(ColorTokens.inkOnBright) }
    public var supportError: Color { color(ColorTokens.supportError) }
    public var supportErrorText: Color { color(ColorTokens.supportErrorText) }
  }

  extension EnvironmentValues {
    @Entry public var palette = ColdframePalette(isDark: false)
  }

  extension IOSTextStyle {
    var swiftUI: Font.TextStyle {
      switch self {
      case .largeTitle: .largeTitle
      case .title: .title
      case .title2: .title2
      case .title3: .title3
      case .headline: .headline
      case .body: .body
      case .callout: .callout
      case .subheadline: .subheadline
      case .footnote: .footnote
      case .caption1: .caption
      case .caption2: .caption2
      }
    }

    #if canImport(UIKit)
      var uiKit: UIFont.TextStyle {
        switch self {
        case .largeTitle: .largeTitle
        case .title: .title1
        case .title2: .title2
        case .title3: .title3
        case .headline: .headline
        case .body: .body
        case .callout: .callout
        case .subheadline: .subheadline
        case .footnote: .footnote
        case .caption1: .caption1
        case .caption2: .caption2
        }
      }
    #endif
  }

  /// A generated typography role: scales with Dynamic Type relative to its text style, stops at
  /// `maximumPointSize`, and uppercases by style (`textCase`), never in the string.
  struct RoleFont: ViewModifier {
    let role: TypeRole
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    func body(content: Content) -> some View {
      content
        .font(font)
        .textCase(role.uppercase ? .uppercase : nil)
        .tracking(role.letterSpacingEm * role.size)
        .lineSpacing(max(0, (role.lineHeight - 1.2) * role.size))
    }

    private var font: Font {
      let name = FontFaces.postScriptName(for: role)
      #if canImport(UIKit)
        if let maximum = role.maximumPointSize {
          let traits = UITraitCollection(
            preferredContentSizeCategory: UIContentSizeCategory(dynamicTypeSize))
          let scaled = UIFontMetrics(forTextStyle: role.textStyle.uiKit)
            .scaledValue(for: role.size, compatibleWith: traits)
          return .custom(name, fixedSize: min(scaled, maximum))
        }
      #endif
      return .custom(name, size: role.size, relativeTo: role.textStyle.swiftUI)
    }
  }

  extension View {
    /// Applies a generated typography role.
    public func role(_ role: TypeRole) -> some View {
      modifier(RoleFont(role: role))
    }
  }

  #if canImport(UIKit)
    extension UIContentSizeCategory {
      init(_ size: DynamicTypeSize) {
        switch size {
        case .xSmall: self = .extraSmall
        case .small: self = .small
        case .medium: self = .medium
        case .large: self = .large
        case .xLarge: self = .extraLarge
        case .xxLarge: self = .extraExtraLarge
        case .xxxLarge: self = .extraExtraExtraLarge
        case .accessibility1: self = .accessibilityMedium
        case .accessibility2: self = .accessibilityLarge
        case .accessibility3: self = .accessibilityExtraLarge
        case .accessibility4: self = .accessibilityExtraExtraLarge
        case .accessibility5: self = .accessibilityExtraExtraExtraLarge
        @unknown default: self = .large
        }
      }
    }
  #endif

  /// A Carbon icon as a template image, for places that accept only images (tab items).
  @MainActor
  enum CarbonImages {
    private static var cache: [CarbonIcon: Image] = [:]

    static func image(_ icon: CarbonIcon) -> Image {
      if let cached = cache[icon] { return cached }
      let renderer = ImageRenderer(
        content: CarbonIconShape(icon).fill(Color.black).frame(width: 24, height: 24))
      renderer.scale = 3
      let image =
        renderer.cgImage.map { Image(decorative: $0, scale: 3).renderingMode(.template) }
        ?? Image(systemName: "square")
      cache[icon] = image
      return image
    }
  }
#endif
