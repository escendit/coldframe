import Foundation
import Testing

@testable import ColdframeIOS

private let context = Overview.context

/// A snapshot as the core flattens it: an Owner on Home garden with Simon (the User) and Ana.
private func members(
  surface: String = "ready", email: String = "", role: String = "member",
  emailError: String? = nil, working: Bool = false, notice: String? = nil,
  noticeTryAgain: Bool = false, sentTo: String = ""
) -> MembersPresentation {
  MembersPresentation(
    surface: surface, siteName: surface == "hidden" ? "" : "Home garden",
    memberIds: ["u-simon", "u-ana"], memberNames: ["Simon Novak", "ana@example.com"],
    memberEmails: ["simon@example.com", "ana@example.com"], memberRoles: ["owner", "member"],
    memberInitials: ["SN", "A"], memberIsYou: [true, false], email: email, role: role,
    roles: ["member", "administrator", "owner"], emailError: emailError, working: working,
    notice: notice, noticeTryAgain: noticeTryAgain, sentTo: sentTo,
    memberLastOwner: [false, false], editorUserId: nil, editorName: "", editorRole: "member",
    editorRoleError: nil, editorWorking: false, confirmingRemove: false, noticeName: "",
    noticeRole: "")
}

@Test(
  "UX-DR73 UX-DR71 Settings lists Members for an Owner, between My notifications and Site settings")
func membersRowForOwner() throws {
  #expect(
    SettingsRow.rows(role: .owner) == [
      .notifications, .members, .siteSettings, .appearance, .account,
    ])
  #expect(SettingsRow.members.title == .settingsMembers)
  #expect(SettingsRow.members.helper == .settingsMembersHelper)
  #expect(
    context.text(.settingsMembersHelper, .text("Home garden"))
      == "People on Home garden and their Roles")
  #expect(members().canManageMembers)
}

@Test("UX-DR76 UX-DR84 Members is hidden, never disabled, for an Administrator and a Member")
func membersRowHiddenBelowOwner() {
  for role in [SiteRoleKind.administrator, .member] {
    #expect(!SettingsRow.rows(role: role).contains(.members))
    #expect(SettingsRow.rows(role: role).contains(.siteSettings))
  }
  #expect(SettingsRow.rows(role: nil) == [.notifications, .appearance, .account])
  #expect(!members(surface: "hidden").canManageMembers)
  #expect(MembersPresentation.hidden.members.isEmpty)
}

@Test("UX-DR51 one tile per Membership in the Server's order, with initials, name and Role")
func memberTiles() {
  let presentation = members()

  #expect(presentation.members.map(\.id) == ["u-simon", "u-ana"])
  #expect(presentation.members.map(\.initials) == ["SN", "A"])
  #expect(presentation.members.map(\.name) == ["Simon Novak", "ana@example.com"])
  #expect(presentation.members.map(\.role) == [.owner, .member])
  // Sentence case in the catalogue; the status-label style makes it upper case.
  #expect(presentation.members.map { context.text($0.roleLabel) } == ["Owner", "Member"])
  // The User's own square is the primary one.
  #expect(presentation.members.map(\.isYou) == [true, false])
  #expect(
    presentation.members.map { $0.spoken(context) } == [
      "Simon Novak, Owner, you", "ana@example.com, Member",
    ])
}

@Test(
  "UX-DR52 the Role choice offers Member, Administrator, Owner with Member selected and its line")
func inviteRoleChoice() {
  let presentation = members()

  #expect(presentation.roleSegments.map(\.value) == [.member, .administrator, .owner])
  #expect(presentation.roleSegments.map(\.isSelected) == [true, false, false])
  #expect(presentation.roleSegments.map(\.showsCheckmark) == [true, false, false])
  #expect(
    context.text(presentation.roleDescription)
      == "Member sees everything and gets the same Alerts, but can't change settings.")
  #expect(
    context.text(members(role: "administrator").roleDescription)
      == "Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.")
  #expect(
    context.text(members(role: "owner").roleDescription)
      == "Owner can do everything, including inviting people and changing Roles.")
}

@Test("UX-DR52 Send invite names the result and reads Sending invite in place while it is sent")
func inviteButton() {
  #expect(context.text(members().buttonLabel) == "Send invite")
  #expect(members().isButtonEnabled)
  #expect(context.text(members(working: true).buttonLabel) == "Sending invite…")
  #expect(!members(working: true).isButtonEnabled)
}

@Test("UX-DR52 after sending the notice names the email and says to accept on the garden's Wi-Fi")
func inviteSentNotice() throws {
  let presentation = members(notice: "sent", sentTo: "ana.b@example.com")
  let copy = try #require(presentation.noticeCopy)

  #expect(
    context.resolve(copy)
      == "Invitation sent to ana.b@example.com. They accept from the email, on the garden's Wi-Fi.")
  #expect(presentation.notice?.announcement == .polite)
  #expect(presentation.email.isEmpty)
  // Nothing is added to the list until the invitation is accepted.
  #expect(presentation.members.count == 2)
}

@Test("UX-DR52 the email field takes one email address, its reason replacing the helper")
func inviteEmailField() {
  #expect(TextInputKind.email != TextInputKind.text)
  #expect(!TextInputKind.email.autocorrects)
  #expect(context.text(members().emailLine) == "They get an email with a link to accept.")
  #expect(
    context.text(members(emailError: "invalid").emailLine)
      == "Enter one email address, like ana@example.com.")
  #expect(context.text(members(emailError: "blank").emailLine) == "Enter an email address.")
}

@Test("UX-DR52 already invited says so without Try again; a send that failed offers it")
func inviteRefused() throws {
  let invited = members(email: "ana@example.com", notice: "alreadyInvited")
  #expect(
    context.resolve(try #require(invited.noticeCopy))
      == "That person is already invited or a member of this Site.")
  #expect(!invited.noticeTryAgain)
  #expect(invited.email == "ana@example.com")

  let notSent = members(notice: "notSent", noticeTryAgain: true)
  #expect(
    context.resolve(try #require(notSent.noticeCopy)) == "The invitation was not sent. Try again.")
  #expect(notSent.noticeTryAgain)
  #expect(notSent.notice?.announcement == .assertive)

  let forbidden = members(notice: "forbidden")
  #expect(
    context.resolve(try #require(forbidden.noticeCopy))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
  #expect(members(notice: "somethingNew").notice == .unexpected)
}

@Test("UX-DR90 without a Membership Create Site says an accepted invitation can take a minute")
func createSiteInvitationHint() {
  let first = CreateSitePresentation(
    name: "", nameError: nil, working: false, notice: nil, cancellable: false,
    timeZone: TimeZonePanelPresentation(detected: "Europe/Zurich", chosen: nil, changing: false))
  let fromNewSite = CreateSitePresentation(
    name: "", nameError: nil, working: false, notice: nil, cancellable: true,
    timeZone: TimeZonePanelPresentation(detected: "Europe/Zurich", chosen: nil, changing: false))

  #expect(
    first.invitationHint.map { context.text($0) }
      == "Accepted an invitation? It can take a minute to appear.")
  #expect(fromNewSite.invitationHint == nil)
}

/// Story 9.2: an Owner on Home garden with Simon (the User, the only Owner) and Ana (Member).
private func editing(
  lastOwner: [Bool] = [true, false], editorUserId: String? = nil, editorRole: String = "member",
  editorRoleError: String? = nil, editorWorking: Bool = false, confirmingRemove: Bool = false,
  notice: String? = nil, noticeTryAgain: Bool = false, noticeName: String = "",
  noticeRole: String = ""
) -> MembersPresentation {
  MembersPresentation(
    surface: "ready", siteName: "Home garden", memberIds: ["u-simon", "u-ana"],
    memberNames: ["Simon Novak", "Ana Costa"],
    memberEmails: ["simon@example.com", "ana@example.com"],
    memberRoles: ["owner", "member"], memberInitials: ["SN", "AC"], memberIsYou: [true, false],
    email: "", role: "member", roles: ["member", "administrator", "owner"], emailError: nil,
    working: false, notice: notice, noticeTryAgain: noticeTryAgain, sentTo: "",
    memberLastOwner: lastOwner, editorUserId: editorUserId,
    editorName: editorUserId == nil ? "" : "Ana Costa", editorRole: editorRole,
    editorRoleError: editorRoleError, editorWorking: editorWorking,
    confirmingRemove: confirmingRemove, noticeName: noticeName, noticeRole: noticeRole)
}

@Test("UX-DR51 the only Owner's tile reads Owner · last one, locked with why; others · change")
func lastOwnerTileLocked() {
  let tiles = editing().members

  #expect(tiles.map(\.isChangeable) == [false, true])
  // Sentence case in the catalogue; the status-label style makes it upper case.
  #expect(tiles.map { context.resolve($0.roleLine) } == ["Owner · last one", "Member · change"])
  #expect(
    tiles.map { $0.lockExplanation.map { context.text($0) } } == [
      "A Site always keeps at least one Owner.", nil,
    ])
  // With a second Owner, every tile can be changed.
  let twoOwners = editing(lastOwner: [false, false]).members
  #expect(twoOwners.map(\.isChangeable) == [true, true])
  #expect(context.resolve(twoOwners[0].roleLine) == "Owner · change")
}

@Test("UX-DR51 UX-DR73 a tapped tile opens its Role editor with Save Role and Remove from Site")
func roleEditor() throws {
  #expect(editing().editor == nil)
  let editor = try #require(editing(editorUserId: "u-ana", editorRole: "administrator").editor)

  #expect(editor.id == "u-ana")
  #expect(editor.name == "Ana Costa")
  #expect(editor.roleSegments.map(\.value) == [.member, .administrator, .owner])
  #expect(editor.roleSegments.map(\.isSelected) == [false, true, false])
  #expect(editor.roleLine == nil)
  #expect(context.text(editor.saveLabel) == "Save Role")
  #expect(context.text(editor.removeLabel) == "Remove from Site")
  #expect(editor.areButtonsEnabled)
  #expect(editor.removal == nil)

  let saving = try #require(editing(editorUserId: "u-ana", editorWorking: true).editor)
  #expect(context.text(saving.saveLabel) == "Saving Role…")
  #expect(!saving.areButtonsEnabled)
  // No editor on a surface that is not ready.
  #expect(MembersPresentation.hidden.editor == nil)
}

@Test("UX-DR51 a refused last-Owner change shows one line under the Role choice")
func lastOwnerRefused() throws {
  let editor = try #require(
    editing(editorUserId: "u-simon", editorRole: "member", editorRoleError: "lastOwner").editor)

  #expect(editor.roleError == .lastOwner)
  #expect(editor.roleLine.map { context.text($0) } == "A Site always keeps at least one Owner.")
}

@Test("UX-DR113 removing a Membership asks first in a dialog naming the person and the Site")
func removalConfirmation() throws {
  let editor = try #require(editing(editorUserId: "u-ana", confirmingRemove: true).editor)
  let removal = try #require(editor.removal)

  #expect(context.resolve(removal.title) == "Remove Ana Costa from Home garden?")
  #expect(
    context.resolve(removal.message)
      == "They lose access to Home garden and stop getting its Alerts.")
  #expect(context.text(removal.confirm) == "Remove")
  #expect(context.text(removal.cancel) == "Cancel")
  let removing = try #require(
    editing(editorUserId: "u-ana", editorWorking: true, confirmingRemove: true).editor?.removal)
  #expect(context.text(removing.confirm) == "Removing…")
  #expect(removing.working)
}

@Test("UX-DR73 a changed Role and a removal are said politely, naming the person")
func membershipChangedNotices() throws {
  let changed = editing(notice: "roleChanged", noticeName: "Ana Costa", noticeRole: "administrator")
  #expect(context.resolve(try #require(changed.noticeCopy)) == "Ana Costa is now Administrator.")
  #expect(changed.notice?.announcement == .polite)

  let removed = editing(notice: "removed", noticeName: "Ana Costa")
  #expect(
    context.resolve(try #require(removed.noticeCopy)) == "Ana Costa was removed from Home garden.")
  #expect(removed.notice?.announcement == .polite)
}

@Test("UX-DR73 UX-DR84 a gone member, a change not saved and a 403 race each say what happened")
func membershipChangeRefused() throws {
  let gone = editing(notice: "memberGone", noticeName: "Ana Costa")
  #expect(
    context.resolve(try #require(gone.noticeCopy))
      == "Ana Costa is no longer a member of Home garden.")
  #expect(gone.notice?.announcement == .assertive)

  let notChanged = editing(editorUserId: "u-ana", notice: "notChanged", noticeTryAgain: true)
  #expect(
    context.resolve(try #require(notChanged.noticeCopy)) == "The change was not saved. Try again.")
  #expect(notChanged.noticeTryAgain)

  let forbidden = editing(notice: "forbidden")
  #expect(
    context.resolve(try #require(forbidden.noticeCopy))
      == "You can't change this on Home garden. Ask an Owner or Administrator.")
}
