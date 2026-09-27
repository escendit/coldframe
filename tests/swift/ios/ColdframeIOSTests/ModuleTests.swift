import Testing

@testable import ColdframeIOS

@Test func nameIsTheModuleName() {
  #expect(Module.name == "ColdframeIOS")
}
