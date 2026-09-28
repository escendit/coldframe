import Testing

@testable import ColdframeDesignTokens

@Test func holdsTheUxDr13Icons() {
  #expect(
    CarbonIcon.allCases.map(\.rawValue) == [
      "rain-drop", "checkmark--outline", "checkmark", "help", "tools", "pause--outline", "add",
      "cloud--offline", "overflow-menu--vertical", "chevron--down", "arrow--up", "arrow--down",
      "battery--low", "error--filled", "view", "in-progress", "time", "grid", "notification", "box",
      "settings",
    ])
}

@Test(arguments: CarbonIcon.allCases)
func parsesIntoDrawableCommandsInsideTheViewport(icon: CarbonIcon) throws {
  let commands = try IconPath.parse(icon.pathData)
  guard case .move = commands.first else {
    Issue.record("\(icon) does not start with a move")
    return
  }
  #expect(
    commands.contains {
      switch $0 {
      case .line, .cubic: true
      default: false
      }
    })
  let range = 0...IconPath.viewportSize
  for command in commands {
    switch command {
    case .move(let x, let y), .line(let x, let y):
      #expect(range.contains(x) && range.contains(y))
    case .cubic(let x1, let y1, let x2, let y2, let x, let y):
      #expect([x1, y1, x2, y2, x, y].allSatisfy(range.contains))
    case .close:
      break
    }
  }
  #expect(icon.commands == commands)
}

@Test func parsesEachCommand() throws {
  #expect(
    try IconPath.parse("M 1 2 L 3 4.5 C 1 2 3 4 5 6 Z") == [
      .move(x: 1, y: 2),
      .line(x: 3, y: 4.5),
      .cubic(x1: 1, y1: 2, x2: 3, y2: 4, x: 5, y: 6),
      .close,
    ])
}

@Test func rejectsDataItCannotDraw() {
  #expect(throws: IconPathError.unsupportedCommand("A")) {
    try IconPath.parse("M 1 2 A 1 1 0 0 1 2 2")
  }
  #expect(throws: IconPathError.truncated) { try IconPath.parse("M 1") }
  #expect(throws: IconPathError.invalidNumber("x")) { try IconPath.parse("L x 2") }
}
