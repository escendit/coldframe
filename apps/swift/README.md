# apps/swift

Swift runtimes.

| Folder | What | Arrives in |
| --- | --- | --- |
| `ios/` | The iOS app, a SwiftUI shell over the shared core | Epic 1, with sign-in on iOS |

`ios/Sources/ColdframeIOS` is the part of the app without UI. It is a SwiftPM target of the
`Package.swift` in the repository root, so it builds and tests without Xcode.
Tests live in [`tests/swift`](../../tests/swift).
