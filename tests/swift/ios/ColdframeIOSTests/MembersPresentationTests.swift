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
    notice: notice, noticeTryAgain: noticeTryAgain, sentTo: sentTo)
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
