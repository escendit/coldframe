import Testing

@testable import ColdframeIOS

@Test("UX-DR56 every notice shows its own catalogue copy")
func noticesMapToTheirCopy() {
  #expect(NoticeKind.unreachable.message == .noticeUnreachable)
  #expect(NoticeKind.certificate.message == .noticeCertificate)
  #expect(NoticeKind.keycloak.message == .noticeKeycloak)
  #expect(NoticeKind.signedOut.message == .noticeSignedOut)
}

@Test(
  "UX-DR56 the core snapshot becomes a notice with its one action or none",
  arguments: [
    ("unreachable", "tryAgain", NoticeKind.unreachable, NoticeActionKind.tryAgain),
    ("certificate", nil, NoticeKind.certificate, nil),
    ("keycloak", "tryAgain", NoticeKind.keycloak, NoticeActionKind.tryAgain),
    ("signedOut", "signIn", NoticeKind.signedOut, NoticeActionKind.signIn),
  ] as [(String, String?, NoticeKind, NoticeActionKind?)])
func snapshotsBecomeNotices(
  notice: String, action: String?, kind: NoticeKind, expected: NoticeActionKind?
) {
  let presentation = SignInPresentation(
    restoring: false, working: false, signedIn: false, notice: notice, action: action)

  #expect(presentation.notice == kind)
  #expect(presentation.noticeAction == expected)
  #expect(presentation.noticeAction?.label != .signinWorking)
}

@Test("UX-DR56 the actions read Try again and Sign in")
func actionsHaveTheirLabels() {
  #expect(NoticeActionKind.tryAgain.label == .noticeTryAgain)
  #expect(NoticeActionKind.signIn.label == .noticeSignIn)
}

@Test("UX-DR34 working replaces SIGN IN in place and ignores presses")
func workingLabelReplacesTheLabel() {
  let idle = SignInPresentation(
    restoring: false, working: false, signedIn: false, notice: nil, action: nil)
  let working = SignInPresentation(
    restoring: false, working: true, signedIn: false, notice: nil, action: nil)

  #expect(idle.buttonLabel == .signinAction)
  #expect(idle.isButtonEnabled)
  #expect(working.buttonLabel == .signinWorking)
  #expect(!working.isButtonEnabled)
  #expect(working.notice == nil)
}

@Test("UX-DR104 failures are announced assertively and the signed-out notice politely")
func announcementPriorities() {
  #expect(NoticeKind.unreachable.announcement == .assertive)
  #expect(NoticeKind.certificate.announcement == .assertive)
  #expect(NoticeKind.keycloak.announcement == .assertive)
  #expect(NoticeKind.signedOut.announcement == .polite)
}

@Test func signedInAndRestoringAreTheirOwnSurfaces() {
  let signedIn = SignInPresentation(
    restoring: false, working: false, signedIn: true, notice: nil, action: nil)
  let restoring = SignInPresentation(
    restoring: true, working: false, signedIn: false, notice: nil, action: nil)

  #expect(signedIn.surface == .signedIn)
  #expect(restoring.surface == .restoring)
  #expect(!restoring.isButtonEnabled)
}

@Test func anUnknownNoticeNameShowsNoNotice() {
  let presentation = SignInPresentation(
    restoring: false, working: false, signedIn: false, notice: "future", action: "later")

  #expect(presentation.surface == .signIn(notice: nil, action: nil, working: false))
}
