import Foundation
import Testing

@testable import ColdframeIOS

/// A snapshot as the core flattens it, with every field defaulted to an Owner of "Home" with the
/// Lots Tomatoes and Beans, both without a Node.
private func lots(
  surface: String = "ready", notice: String? = nil, siteId: String? = "a",
  siteName: String? = "Home", role: String? = "owner",
  canRenameSite: Bool = true, canEditLots: Bool = true, readOnlyNotice: Bool = false,
  siteNameDraft: String = "Home", siteNameError: String? = nil, siteRenameWorking: Bool = false,
  lotIds: [String] = ["t", "b"], lotNames: [String] = ["Tomatoes", "Beans"],
  lotStatuses: [String] = ["noNode", "noNode"],
  newLotName: String = "", newLotNameError: String? = nil, createWorking: Bool = false,
  renamingLotId: String? = nil, renameDraft: String = "", renameError: String? = nil,
  renameWorking: Bool = false,
  removingLotId: String? = nil, removingLotName: String? = nil, removeWorking: Bool = false,
  actionNotice: String? = nil, actionNoticeSubject: String? = nil
) -> LotsPresentation {
  LotsPresentation(
    surface: surface, notice: notice, siteId: siteId, siteName: siteName, role: role,
    canRenameSite: canRenameSite, canEditLots: canEditLots, readOnlyNotice: readOnlyNotice,
    siteNameDraft: siteNameDraft, siteNameError: siteNameError,
    siteRenameWorking: siteRenameWorking,
    lotIds: lotIds, lotNames: lotNames, lotStatuses: lotStatuses,
    newLotName: newLotName, newLotNameError: newLotNameError, createWorking: createWorking,
    renamingLotId: renamingLotId, renameDraft: renameDraft, renameError: renameError,
    renameWorking: renameWorking,
    removingLotId: removingLotId, removingLotName: removingLotName, removeWorking: removeWorking,
    actionNotice: actionNotice, actionNoticeSubject: actionNoticeSubject)
}

private func member() -> LotsPresentation {
  lots(role: "member", canRenameSite: false, canEditLots: false, readOnlyNotice: true)
}

@Test("UX-DR20 a failed Lot load replaces the grid with the Sites load notice")
func loadFailure() {
  let failed = lots(surface: "failed", notice: "unreachable")

  #expect(failed.failure == .unreachable)
  #expect(failed.tiles.isEmpty)
  #expect(failed.siteSettings == nil)
  #expect(lots(surface: "failed", notice: "certificate").failure?.action == nil)
  #expect(lots(surface: "idle").surface == .waiting)
  #expect(lots(siteName: nil).surface == .waiting)
  #expect(lots(surface: "loading", siteName: nil).surface == .waiting)
}

@Test("UX-DR74 an Owner renames the Site with Rename Site, which reads Renaming Site while working")
func ownerRenamesSite() throws {
  let settings = try #require(lots().siteSettings)
  let working = try #require(lots(siteRenameWorking: true).siteSettings)
  let blank = try #require(lots(siteNameDraft: "", siteNameError: "blank").siteSettings)

  #expect(settings.canRenameSite)
  #expect(settings.siteNameDraft == "Home")
  #expect(settings.renameSiteLabel == .siteSettingsRenameSite)
  #expect(settings.siteNameHelper == .siteSettingsSiteNameHelper)
  #expect(working.renameSiteLabel == .siteSettingsRenamingSite)
  #expect(blank.siteNameHelper == .createSiteNameBlank)
}

@Test("UX-DR74 Owners and Administrators create Lots with Create Lot")
func createLot() throws {
  let admin = try #require(
    lots(role: "administrator", canRenameSite: false, newLotName: "Peppers").siteSettings)
  let working = try #require(lots(createWorking: true).siteSettings)
  let blank = try #require(lots(newLotNameError: "blank").siteSettings)
  let long = try #require(lots(newLotNameError: "tooLong").siteSettings)

  #expect(admin.role == .administrator)
  #expect(!admin.canRenameSite)
  #expect(admin.canEditLots)
  #expect(admin.newLotName == "Peppers")
  #expect(admin.createLotLabel == .siteSettingsCreateLot)
  #expect(admin.lotNameHelper == .siteSettingsLotNameHelper)
  #expect(working.createLotLabel == .siteSettingsCreatingLot)
  #expect(blank.lotNameHelper == .siteSettingsLotNameBlank)
  #expect(long.lotNameHelper == .createSiteNameTooLong)
  #expect(
    lots(lotIds: [], lotNames: [], lotStatuses: []).siteSettings?.emptyLots == .siteSettingsNoLots)
}

@Test("UX-DR74 Rename Lot opens with the draft and reads Renaming Lot while working")
func renameLot() throws {
  let renaming = try #require(
    lots(renamingLotId: "t", renameDraft: "Cherry tomatoes").siteSettings?.renaming)
  let working = try #require(
    lots(renamingLotId: "t", renameDraft: "x", renameWorking: true).siteSettings?.renaming)
  let blank = try #require(
    lots(renamingLotId: "t", renameDraft: "", renameError: "blank").siteSettings?.renaming)

  #expect(renaming.lotId == "t")
  #expect(renaming.draft == "Cherry tomatoes")
  #expect(renaming.buttonLabel == .siteSettingsRenameLot)
  #expect(renaming.isButtonEnabled)
  #expect(working.buttonLabel == .siteSettingsRenamingLot)
  #expect(!working.isButtonEnabled)
  #expect(blank.error == .blank)
}

@Test("UX-DR74 Remove Lot confirms in a destructive dialog that names the Lot")
func removeLot() throws {
  let removing = try #require(
    lots(removingLotId: "t", removingLotName: "Tomatoes").siteSettings?.removing)
  let working = try #require(
    lots(removingLotId: "t", removingLotName: "Tomatoes", removeWorking: true)
      .siteSettings?.removing)

  #expect(removing.lotName == "Tomatoes")
  #expect(removing.title == .siteSettingsRemoveLotQuestion)
  #expect(removing.message == .siteSettingsRemoveLotDetail)
  #expect(removing.confirm == .siteSettingsRemoveLot)
  #expect(removing.cancel == .siteSettingsCancel)
  #expect(working.confirm == .siteSettingsRemovingLot)
  let entries = try Catalogue.entries()
  #expect(entries["site_settings_remove_lot_question"] == "Remove Lot %@?")
  #expect(entries["site_settings_remove_lot_detail"] == "Its history stays in Coldframe.")
}

@Test("UX-DR74 a claimed Lot shows Move or unassign the Node on the Lot first")
func lotClaimed() throws {
  let settings = try #require(
    lots(actionNotice: "lotClaimed", actionNoticeSubject: "Tomatoes").siteSettings)

  #expect(settings.notice == .lotClaimed)
  #expect(settings.notice?.message == .siteSettingsLotClaimed)
  #expect(settings.noticeSubject == "Tomatoes")
  #expect(
    try Catalogue.entries()["site_settings_lot_claimed"]
      == "Move or unassign the Node on %@ first.")
}

@Test("UX-DR74 action failures name what did not change")
func actionNotices() {
  let expected: [LotsActionNoticeKind: L10n] = [
    .lotNotFound: .siteSettingsLotNotFound,
    .renameSiteUnavailable: .siteSettingsRenameSiteUnavailable,
    .keyReused: .siteSettingsKeyReused,
    .unreachable: .noticeUnreachable,
    .certificate: .noticeCertificate,
    .unexpected: .siteSettingsUnexpected,
  ]
  for (kind, message) in expected {
    #expect(kind.message == message)
    #expect(!kind.takesSubject)
  }
  #expect(lots(actionNotice: "whatever").siteSettings?.notice == .unexpected)
  #expect(
    lots(actionNotice: "keyReused", actionNoticeSubject: "x").siteSettings?.noticeSubject == nil)
}

@Test("UX-DR84 a Member sees Site settings read-only, controls hidden, with one notice")
func memberReadOnly() throws {
  let settings = try #require(member().siteSettings)

  #expect(settings.role == .member)
  #expect(!settings.canRenameSite)
  #expect(!settings.canEditLots)
  #expect(settings.readOnlyNotice == .siteSettingsReadOnly)
  #expect(settings.lots.map(\.name) == ["Tomatoes", "Beans"])
  #expect(lots().siteSettings?.readOnlyNotice == nil)
  #expect(
    try Catalogue.entries()["site_settings_read_only"]
      == "Only Owners and Administrators can change Lots.")
}

@Test("UX-DR84 a Member never gets the rename or remove dialog, even if the core names a Lot")
func memberNoDialogs() {
  let settings = LotsPresentation(
    surface: "ready", notice: nil, siteId: "a", siteName: "Home", role: "member",
    canRenameSite: false, canEditLots: false, readOnlyNotice: true,
    siteNameDraft: "Home", siteNameError: nil, siteRenameWorking: false,
    lotIds: ["t"], lotNames: ["Tomatoes"], lotStatuses: ["noNode"],
    newLotName: "", newLotNameError: nil, createWorking: false,
    renamingLotId: "t", renameDraft: "x", renameError: nil, renameWorking: false,
    removingLotId: "t", removingLotName: "Tomatoes", removeWorking: false,
    actionNotice: nil, actionNoticeSubject: nil
  ).siteSettings

  #expect(settings?.renaming == nil)
  #expect(settings?.removing == nil)
}

@Test("UX-DR84 a 403 race names the Site and asks for an Owner or Administrator")
func forbiddenRace() throws {
  let settings = try #require(
    lots(
      role: "administrator", canRenameSite: false, actionNotice: "forbidden",
      actionNoticeSubject: "Home"
    ).siteSettings)

  #expect(settings.notice == .forbidden)
  #expect(settings.notice?.takesSubject == true)
  #expect(settings.noticeSubject == "Home")
  #expect(
    try Catalogue.entries()["site_settings_forbidden"]
      == "You can't change this on %@. Ask an Owner or Administrator.")
}
