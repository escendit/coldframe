import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosNodeSetup` of the shared Kotlin core to `NodeSetupService`. The snapshot is flat
/// strings, flags and lists; BLE, the session keys, the Node's sealed key and the Server URL stay
/// in the core.
@MainActor
final class CoreNodeSetupService: NodeSetupService {
  private let core: IosNodeSetup
  private var watch: Watch?

  init(core: IosNodeSetup) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (NodeSetupPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          NodeSetupPresentation(
            open: snapshot.open, step: Int(snapshot.step), keepAwake: snapshot.keepAwake,
            showsCancel: snapshot.showsCancel, radio: snapshot.radio,
            candidateIds: snapshot.candidateIds, candidateNames: snapshot.candidateNames,
            candidateSignals: snapshot.candidateSignals,
            candidatePressed: snapshot.candidatePressed.map { $0.boolValue },
            noNodeYet: snapshot.noNodeYet, selectedId: snapshot.selectedId, node: snapshot.node,
            codeText: snapshot.codeText, codeError: snapshot.codeError,
            codeWorking: snapshot.codeWorking, codeAccepted: snapshot.codeAccepted,
            deviceId: snapshot.deviceId, siteName: snapshot.siteName,
            lotsLoaded: snapshot.lotsLoaded, lotIds: snapshot.lotIds, lotNames: snapshot.lotNames,
            lotSelectable: snapshot.lotSelectable.map { $0.boolValue },
            selectedLotId: snapshot.selectedLotId, lotName: snapshot.lotName,
            newLotOpen: snapshot.newLotOpen, newLotName: snapshot.newLotName,
            newLotError: snapshot.newLotError, creatingLot: snapshot.creatingLot,
            assigning: snapshot.assigning, lotNotice: snapshot.lotNotice,
            lotNoticeLot: snapshot.lotNoticeLot, lotsRetryable: snapshot.lotsRetryable,
            outcome: snapshot.outcome, outcomePrimary: snapshot.outcomePrimary,
            outcomeSecondary: snapshot.outcomeSecondary,
            stoppedStep: Int(snapshot.stoppedStep), confirmingLeave: snapshot.confirmingLeave,
            announcementId: Int(snapshot.announcementId),
            announcementKind: snapshot.announcementKind,
            announcementAssertive: snapshot.announcementAssertive,
            announcementNode: snapshot.announcementNode,
            announcementLot: snapshot.announcementLot,
            announcementSignal: snapshot.announcementSignal,
            announcementOutcome: snapshot.announcementOutcome))
      }
    }
  }

  func open(lotId: String?) { core.open(lotId: lotId) }

  func close() { core.close() }

  func recheckRadio() { core.recheckRadio() }

  func announcing(_ active: Bool) { core.announcing(active: active) }

  func back() { core.back() }

  func leave() { core.leave() }

  func confirmLeave() { core.confirmLeave() }

  func stayInFlow() { core.stayInFlow() }

  func continueFromPress() { core.continueFromPress() }

  func select(candidateId: String) { core.select(peripheralId: candidateId) }

  func continueFromScan() { core.continueFromScan() }

  func setCode(_ text: String) { core.setCode(text: text) }

  func submitCode() { core.submitCode() }

  func continueFromCode() { core.continueFromCode() }

  func retryLots() { core.retryLots() }

  func chooseLot(_ lotId: String) { core.chooseLot(lotId: lotId) }

  func openNewLot() { core.openNewLot() }

  func setNewLotName(_ name: String) { core.setNewLotName(name: name) }

  func createLot() { core.createLot() }

  func assign() { core.assign() }

  func outcomeAction(_ action: NodeOutcomeActionKind) {
    core.outcomeAction(action: action.rawValue)
  }
}
