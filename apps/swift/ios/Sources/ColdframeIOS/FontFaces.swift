import ColdframeDesignTokens

/// The bundled Ubuntu faces by PostScript name (registered through `UIAppFonts`).
public enum FontFaces {
  /// All faces the app bundles, as file names in `packages/design-tokens/fonts`.
  public static let files = [
    "Ubuntu-Regular.ttf", "Ubuntu-Light.ttf", "UbuntuCondensed-Regular.ttf",
    "UbuntuMono-Regular.ttf",
  ]

  /// The face of a role. Ubuntu Condensed and Mono ship in 400 only (DW-9).
  public static func postScriptName(for role: TypeRole) -> String {
    switch role.fontFamily {
    case "Ubuntu Condensed": "UbuntuCondensed-Regular"
    case "Ubuntu Mono": "UbuntuMono-Regular"
    default: role.weight <= 300 ? "Ubuntu-Light" : "Ubuntu-Regular"
    }
  }
}
