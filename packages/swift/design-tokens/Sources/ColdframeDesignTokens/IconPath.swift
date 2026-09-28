/// One absolute drawing command of an icon, in the 32×32 viewport.
public enum PathCommand: Equatable, Sendable {
  case move(x: Double, y: Double)
  case line(x: Double, y: Double)
  case cubic(x1: Double, y1: Double, x2: Double, y2: Double, x: Double, y: Double)
  case close
}

/// Why `IconPath.parse` rejected path data.
public enum IconPathError: Error, Equatable {
  case unsupportedCommand(String)
  case invalidNumber(String)
  case truncated
}

/// Parses `CarbonIcon.pathData`: space-separated `M x y`, `L x y`, `C x1 y1 x2 y2 x y` and `Z`.
public enum IconPath {
  /// Width and height of the viewport every path is drawn in.
  public static let viewportSize: Double = 32

  public static func parse(_ data: String) throws -> [PathCommand] {
    let tokens = data.split(separator: " ").map(String.init)
    var commands: [PathCommand] = []
    var index = 0

    func numbers(_ count: Int) throws -> [Double] {
      guard index + count <= tokens.count else { throw IconPathError.truncated }
      let values = try tokens[index..<index + count].map { token in
        guard let value = Double(token) else { throw IconPathError.invalidNumber(token) }
        return value
      }
      index += count
      return values
    }

    while index < tokens.count {
      let command = tokens[index]
      index += 1
      switch command {
      case "M":
        let v = try numbers(2)
        commands.append(.move(x: v[0], y: v[1]))
      case "L":
        let v = try numbers(2)
        commands.append(.line(x: v[0], y: v[1]))
      case "C":
        let v = try numbers(6)
        commands.append(.cubic(x1: v[0], y1: v[1], x2: v[2], y2: v[3], x: v[4], y: v[5]))
      case "Z":
        commands.append(.close)
      default:
        throw IconPathError.unsupportedCommand(command)
      }
    }
    return commands
  }
}

extension CarbonIcon {
  /// The parsed geometry of the icon.
  public var commands: [PathCommand] {
    // Generated path data is checked by the design-token tests, so it always parses.
    (try? IconPath.parse(pathData)) ?? []
  }
}
