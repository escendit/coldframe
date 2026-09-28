/// The iOS Dynamic Type text style a typography role scales with.
public enum IOSTextStyle: String, CaseIterable, Sendable {
  case largeTitle
  case title
  case title2
  case title3
  case headline
  case body
  case callout
  case subheadline
  case footnote
  case caption1
  case caption2

  /// Point size of the style at the default text size (Large).
  public var defaultPointSize: Double {
    switch self {
    case .largeTitle: 34
    case .title: 28
    case .title2: 22
    case .title3: 20
    case .headline: 17
    case .body: 17
    case .callout: 16
    case .subheadline: 15
    case .footnote: 13
    case .caption1: 12
    case .caption2: 11
    }
  }
}

/// A typography role of DESIGN.md.
///
/// `size` is the point size at the default text size. It scales with Dynamic Type relative to
/// `textStyle`; roles of 36 pt and more stop at `maximumPointSize`, twice their base size.
/// `uppercase` is applied by style; strings are never upper-cased.
public struct TypeRole: Equatable, Hashable, Sendable {
  public let fontFamily: String
  public let size: Double
  public let weight: Int
  /// Line height as a multiple of the font size.
  public let lineHeight: Double
  public let letterSpacingEm: Double
  public let uppercase: Bool
  public let textStyle: IOSTextStyle
  public let maximumPointSize: Double?

  public init(
    fontFamily: String,
    size: Double,
    weight: Int,
    lineHeight: Double,
    letterSpacingEm: Double,
    uppercase: Bool,
    textStyle: IOSTextStyle,
    maximumPointSize: Double?
  ) {
    self.fontFamily = fontFamily
    self.size = size
    self.weight = weight
    self.lineHeight = lineHeight
    self.letterSpacingEm = letterSpacingEm
    self.uppercase = uppercase
    self.textStyle = textStyle
    self.maximumPointSize = maximumPointSize
  }

  /// The point size when `textStyle` is scaled to `scaledStylePointSize`, after the cap.
  public func scaledSize(forStylePointSize scaledStylePointSize: Double) -> Double {
    let scaled = size * scaledStylePointSize / textStyle.defaultPointSize
    guard let maximumPointSize else { return scaled }
    return min(scaled, maximumPointSize)
  }
}
