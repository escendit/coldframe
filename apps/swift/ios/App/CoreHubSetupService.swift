import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosHubSetup` of the shared Kotlin core to `HubSetupService`. The snapshot is flat
/// strings, flags and lists; BLE, the session keys and the Server URL stay in the core.
@MainActor
final class CoreHubSetupService: HubSetupService {
  private let core: IosHubSetup
  private var watch: Watch?

  init(core: IosHubSetup) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (HubSetupPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          HubSetupPresentation(
            open: snapshot.open, step: Int(snapshot.step), keepAwake: snapshot.keepAwake,
            showsCancel: snapshot.showsCancel, radio: snapshot.radio,
            candidateIds: snapshot.candidateIds, candidateNames: snapshot.candidateNames,
            candidateSignals: snapshot.candidateSignals, noHubYet: snapshot.noHubYet,
            selectedId: snapshot.selectedId, hub: snapshot.hub, codeText: snapshot.codeText,
            codeError: snapshot.codeError, codeWorking: snapshot.codeWorking,
            codeAccepted: snapshot.codeAccepted, deviceId: snapshot.deviceId,
            networksLoaded: snapshot.networksLoaded, networkSsids: snapshot.networkSsids,
            networkSecurities: snapshot.networkSecurities,
            networkSupported: snapshot.networkSupported.map { $0.boolValue },
            ssid: snapshot.ssid, otherNetwork: snapshot.otherNetwork, password: snapshot.password,
            wifiError: snapshot.wifiError, siteIds: snapshot.siteIds, siteNames: snapshot.siteNames,
            selectedSiteId: snapshot.selectedSiteId, fingerprint: snapshot.fingerprint,
            loadingKey: snapshot.loadingKey, keyNotice: snapshot.keyNotice,
            progressReached: Int(snapshot.progressReached),
            elapsedSeconds: Int(snapshot.elapsedSeconds), outcome: snapshot.outcome,
            outcomePrimary: snapshot.outcomePrimary, outcomeSecondary: snapshot.outcomeSecondary,
            helpShown: snapshot.helpShown, confirmingLeave: snapshot.confirmingLeave,
            announcementId: Int(snapshot.announcementId),
            announcementKind: snapshot.announcementKind,
            announcementAssertive: snapshot.announcementAssertive,
            announcementHub: snapshot.announcementHub, announcementSsid: snapshot.announcementSsid,
            announcementSignal: snapshot.announcementSignal,
            announcementOutcome: snapshot.announcementOutcome, serverHost: snapshot.serverHost))
      }
    }
  }

  func open() { core.open() }

  func close() { core.close() }

  func recheckRadio() { core.recheckRadio() }

  func announcing(_ active: Bool) { core.announcing(active: active) }

  func back() { core.back() }

  func leave() { core.leave() }

  func confirmLeave() { core.confirmLeave() }

  func stayInFlow() { core.stayInFlow() }

  func select(candidateId: String) { core.select(peripheralId: candidateId) }

  func continueFromScan() { core.continueFromScan() }

  func setCode(_ text: String) { core.setCode(text: text) }

  func submitCode() { core.submitCode() }

  func continueFromCode() { core.continueFromCode() }

  func chooseNetwork(_ ssid: String) { core.chooseNetwork(ssid: ssid) }

  func chooseOtherNetwork() { core.chooseOtherNetwork() }

  func setOtherSsid(_ ssid: String) { core.setOtherSsid(ssid: ssid) }

  func setPassword(_ password: String) { core.setPassword(password: password) }

  func continueFromWifi() { core.continueFromWifi() }

  func chooseSite(_ siteId: String) { core.chooseSite(siteId: siteId) }

  func retryKey() { core.retryKey() }

  func start() { core.start() }

  func outcomeAction(_ action: OutcomeActionKind) { core.outcomeAction(action: action.rawValue) }
}
