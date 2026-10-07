import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosLots` of the shared Kotlin core to `LotsService`. The snapshot is flat strings,
/// flags and lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreLotsService: LotsService {
  private let core: IosLots
  private var watch: Watch?
  private var eventsWatch: Watch?
  private var onChange: (@MainActor (LotsPresentation) -> Void)?

  init(core: IosLots) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (LotsPresentation) -> Void) {
    self.onChange = onChange
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated { onChange(CoreLotsService.presentation(of: snapshot)) }
    }
  }

  func observeEvents(_ onEvent: @escaping @MainActor (LotsEventPresentation) -> Void) {
    eventsWatch?.close()
    eventsWatch = core.watchEvents { snapshot in
      MainActor.assumeIsolated {
        // An event this client does not know is not announced.
        if let event = LotsEventPresentation(
          kind: snapshot.kind, siteId: snapshot.siteId,
          fetchedAtEpochMs: snapshot.fetchedAtEpochMs)
        {
          onEvent(event)
        }
      }
    }
  }

  /// The core measures ages and durations when a snapshot is taken; the tick takes a new one.
  func tick() { onChange?(CoreLotsService.presentation(of: core.current())) }

  func refresh() { core.refresh() }

  private static func presentation(of snapshot: LotsSnapshot) -> LotsPresentation {
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
      stale: snapshot.stale, refreshing: snapshot.refreshing,
      fetchedAtEpochMs: snapshot.fetchedAtEpochMs,
      staleAgeDays: Int(snapshot.staleAgeDays), staleAgeHours: Int(snapshot.staleAgeHours),
      staleAgeMinutes: Int(snapshot.staleAgeMinutes),
      headline: snapshot.headline, headlineCount: Int(snapshot.headlineCount),
      headlineLotName: snapshot.headlineLotName,
      headlinePausedUntil: snapshot.headlinePausedUntil,
      headlinePausedInk: snapshot.headlinePausedInk,
      countStatuses: snapshot.countStatuses,
      countValues: snapshot.countValues.map { Int($0.int32Value) },
      lotVariants: snapshot.lotVariants, lotLabels: snapshot.lotLabels,
      lotValues: snapshot.lotValues, lotFoots: snapshot.lotFoots,
      lotSpokens: snapshot.lotSpokens, lotSoilPercents: snapshot.lotSoilPercents,
      lotLowPercents: snapshot.lotLowPercents, lotReadingAts: snapshot.lotReadingAts,
      lotDurationValues: snapshot.lotDurationValues,
      lotDurationUnits: snapshot.lotDurationUnits,
      lotPausedBySite: snapshot.lotPausedBySite.map { $0.boolValue },
      lotPausedUntils: snapshot.lotPausedUntils,
      lotOpensAddNode: snapshot.lotOpensAddNode.map { $0.boolValue },
      lotOpensCalibrate: snapshot.lotOpensCalibrate.map { $0.boolValue },
      menuItems: snapshot.menuItems, menuEnabled: snapshot.menuEnabled)
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
