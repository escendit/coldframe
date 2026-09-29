/// A UX-DR92/93 notice, named as the core names it.
public enum NoticeKind: String, CaseIterable, Sendable {
  case unreachable
  case certificate
  case keycloak
  case signedOut

  /// The catalogue copy of the notice.
  public var message: L10n {
    switch self {
    case .unreachable: .noticeUnreachable
    case .certificate: .noticeCertificate
    case .keycloak: .noticeKeycloak
    case .signedOut: .noticeSignedOut
    }
  }

  /// Failures interrupt VoiceOver; the signed-out notice waits its turn (UX-DR104).
  public var announcement: Announcement {
    self == .signedOut ? .polite : .assertive
  }
}

/// The one action a notice may carry.
public enum NoticeActionKind: String, CaseIterable, Sendable {
  case tryAgain
  case signIn

  public var label: L10n {
    switch self {
    case .tryAgain: .noticeTryAgain
    case .signIn: .noticeSignIn
    }
  }
}

/// How urgently an appearing message is announced.
public enum Announcement: Sendable {
  case polite
  case assertive
}

/// Which surface shows.
public enum SignInSurface: Equatable, Sendable {
  /// Stored tokens are being read; only the background shows.
  case restoring
  /// The Sign-in surface, with a notice (and its action) or none.
  case signIn(notice: NoticeKind?, action: NoticeActionKind?, working: Bool)
  /// The tab shell.
  case signedIn
}

/// The Swift mirror of the core's `SignInState`: the SwiftUI views render only this, so they
/// never see errors, URLs or tokens.
public struct SignInPresentation: Equatable, Sendable {
  public let surface: SignInSurface

  public init(surface: SignInSurface) {
    self.surface = surface
  }

  /// Built from the flattened core snapshot (`SignInSnapshot`). Unknown names show no notice.
  public init(
    restoring: Bool, working: Bool, signedIn: Bool, notice: String?, action: String?
  ) {
    if signedIn {
      surface = .signedIn
    } else if restoring {
      surface = .restoring
    } else {
      surface = .signIn(
        notice: notice.flatMap(NoticeKind.init(rawValue:)),
        action: action.flatMap(NoticeActionKind.init(rawValue:)),
        working: working)
    }
  }

  public static let restoring = SignInPresentation(surface: .restoring)

  /// The SIGN IN label: the working label replaces it in place while signing in.
  public var buttonLabel: L10n {
    if case .signIn(_, _, true) = surface { return .signinWorking }
    return .signinAction
  }

  /// Presses are ignored while signing in.
  public var isButtonEnabled: Bool {
    if case .signIn(_, _, let working) = surface { return !working }
    return false
  }

  public var notice: NoticeKind? {
    if case .signIn(let notice, _, _) = surface { return notice }
    return nil
  }

  public var noticeAction: NoticeActionKind? {
    if case .signIn(_, let action, _) = surface { return action }
    return nil
  }
}

/// The SwiftUI shell's only view of the shared Kotlin core. The app target adapts `IosSignIn`
/// of `ColdframeCore` to it, so this package builds and tests without the Kotlin framework.
@MainActor
public protocol SignInService: AnyObject {
  /// Calls `onChange` with the current presentation and every change.
  func observe(_ onChange: @escaping @MainActor (SignInPresentation) -> Void)
  func signIn()
  /// On start and on every return to the foreground.
  func resume()
  /// After the user confirmed in the native dialog.
  func signOut()
}
