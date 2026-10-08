import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosThresholds` of the shared Kotlin core to `ThresholdsService`. The snapshot is flat
/// strings, flags and lists; the Server calls, the validation gate and the rules stay in the core (AD-14).
@MainActor
final class CoreThresholdsService: ThresholdsService {
  private let core: IosThresholds
  private var watch: Watch?

  init(core: IosThresholds) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (ThresholdsPresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          ThresholdsPresentation(
            surface: snapshot.surface, notice: snapshot.notice,
            noticeTryAgain: snapshot.noticeTryAgain, lotId: snapshot.lotId,
            lotName: snapshot.lotName, canEdit: snapshot.canEdit, working: snapshot.working,
            dirty: snapshot.dirty, canSave: snapshot.canSave, saved: snapshot.saved,
            focusSensorId: snapshot.focusSensorId, sensorIds: snapshot.sensorIds,
            quantities: snapshot.quantities, units: snapshot.units, lows: snapshot.lows,
            highs: snapshot.highs, originalLows: snapshot.originalLows,
            originalHighs: snapshot.originalHighs, proposedLows: snapshot.proposedLows,
            currents: snapshot.currents, steps: snapshot.steps,
            trackMins: snapshot.trackMins.map { $0.doubleValue },
            trackMaxs: snapshot.trackMaxs.map { $0.doubleValue },
            draggables: snapshot.draggables.map { $0.boolValue },
            alerting: snapshot.alerting.map { $0.boolValue },
            lowMustStayBelowHigh: snapshot.lowMustStayBelowHigh.map { $0.boolValue },
            lowRequired: snapshot.lowRequired.map { $0.boolValue },
            offersProposal: snapshot.offersProposal.map { $0.boolValue }))
      }
    }
  }

  func open(lotId: String, name: String, sensorId: String) {
    core.open(lotId: lotId, name: name, sensorId: sensorId)
  }

  func close() { core.close() }

  func retry() { core.retry() }

  func setLowText(sensorId: String, text: String) {
    core.setLowText(sensorId: sensorId, text: text)
  }

  func setHighText(sensorId: String, text: String) {
    core.setHighText(sensorId: sensorId, text: text)
  }

  func setLow(sensorId: String, value: Double) { core.setLow(sensorId: sensorId, value: value) }

  func setHigh(sensorId: String, value: Double) { core.setHigh(sensorId: sensorId, value: value) }

  func addHigh(sensorId: String) { core.addHigh(sensorId: sensorId) }

  func clearHigh(sensorId: String) { core.clearHigh(sensorId: sensorId) }

  func turnOnAlerts(sensorId: String) { core.turnOnAlerts(sensorId: sensorId) }

  func turnOffAlerts(sensorId: String) { core.turnOffAlerts(sensorId: sensorId) }

  func save() { core.save() }
}
