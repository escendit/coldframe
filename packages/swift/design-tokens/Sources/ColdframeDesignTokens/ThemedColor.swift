/// A colour token with one value per theme, each `0xRRGGBBAA`.
///
/// Tokens without a dark twin in DESIGN.md hold the same value twice.
public struct ThemedColor: Equatable, Hashable, Sendable {
  public let light: UInt32
  public let dark: UInt32

  public init(light: UInt32, dark: UInt32) {
    self.light = light
    self.dark = dark
  }

  /// The value for the current theme.
  public func resolve(isDark: Bool) -> UInt32 {
    isDark ? dark : light
  }

  /// Red, green, blue and alpha of a `0xRRGGBBAA` value, each 0...1.
  public static func components(of value: UInt32) -> (
    red: Double, green: Double, blue: Double, alpha: Double
  ) {
    (
      red: Double((value >> 24) & 0xFF) / 255,
      green: Double((value >> 16) & 0xFF) / 255,
      blue: Double((value >> 8) & 0xFF) / 255,
      alpha: Double(value & 0xFF) / 255
    )
  }
}
