import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosLots` of the shared Kotlin core to `LotsService`. The snapshot is flat strings,
/// flags and lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreLotsService: LotsService {
  private let core: IosLots
  private var watch: Watch?

  init(core: IosLots) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (LotsPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          LotsPresentation(
            surface: snapshot.surface, notice: snapshot.notice,
            siteId: snapshot.siteId, siteName: snapshot.siteName, role: snapshot.role,
            canRenameSite: snapshot.canRenameSite, canEditLots: snapshot.canEditLots,
            readOnlyNotice: snapshot.readOnlyNotice,
            siteNameDraft: snapshot.siteNameDraft, siteNameError: snapshot.siteNameError,
            siteRenameWorking: snapshot.siteRenameWorking,
            lotIds: snapshot.lotIds, lotNames: snapshot.lotNames,
            lotStatuses: snapshot.lotStatuses,
            newLotName: snapshot.newLotName, newLotNameError: snapshot.newLotNameError,
            createWorking: snapshot.createWorking,
            renamingLotId: snapshot.renamingLotId, renameDraft: snapshot.renameDraft,
            renameError: snapshot.renameError, renameWorking: snapshot.renameWorking,
            removingLotId: snapshot.removingLotId, removingLotName: snapshot.removingLotName,
            removeWorking: snapshot.removeWorking,
            actionNotice: snapshot.actionNotice,
            actionNoticeSubject: snapshot.actionNoticeSubject,
            canAddNode: snapshot.canAddNode))
      }
    }
  }

  func load() { core.load() }

  func setSiteName(_ name: String) { core.setSiteName(name: name) }

  func renameSite() { core.renameSite() }

  func setNewLotName(_ name: String) { core.setNewLotName(name: name) }

  func createLot() { core.createLot() }

  func startRename(lotId: String) { core.startRename(lotId: lotId) }

  func setRename(_ name: String) { core.setRename(name: name) }

  func rename() { core.rename() }

  func cancelRename() { core.cancelRename() }

  func askRemove(lotId: String) { core.askRemove(lotId: lotId) }

  func confirmRemove() { core.confirmRemove() }

  func cancelRemove() { core.cancelRemove() }
}
