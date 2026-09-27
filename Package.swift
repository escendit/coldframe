// swift-tools-version: 6.0

import PackageDescription

// Swift code keeps the monorepo layout: sources under apps/swift, tests under tests/swift.
let package = Package(
  name: "Coldframe",
  products: [
    .library(name: "ColdframeIOS", targets: ["ColdframeIOS"])
  ],
  targets: [
    .target(
      name: "ColdframeIOS",
      path: "apps/swift/ios/Sources/ColdframeIOS"
    ),
    .testTarget(
      name: "ColdframeIOSTests",
      dependencies: ["ColdframeIOS"],
      path: "tests/swift/ios/ColdframeIOSTests"
    ),
  ]
)
