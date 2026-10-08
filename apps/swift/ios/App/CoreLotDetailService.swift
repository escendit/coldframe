import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosLotDetail` of the shared Kotlin core to `LotDetailService`. The snapshot is flat
/// strings, flags and lists; no token or URL crosses this boundary (AD-14).
@MainActor
final class CoreLotDetailService: LotDetailService {
  private let core: IosLotDetail
  private var watch: Watch?
  private var eventsWatch: Watch?
  private var onChange: (@MainActor (LotDetailPresentation) -> Void)?

  init(core: IosLotDetail) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (LotDetailPresentation) -> Void) {
    self.onChange = onChange
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated { onChange(CoreLotDetailService.presentation(of: snapshot)) }
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

  func open(lotId: String, name: String) { core.open(lotId: lotId, name: name) }

  func close() { core.close() }

  func refresh() { core.refresh() }

  /// The core measures the stale age when a snapshot is taken; the tick takes a new one.
  func tick() { onChange?(CoreLotDetailService.presentation(of: core.current())) }

  func pick(_ quantity: SensorQuantityKind) { core.pick(quantity: quantity.rawValue) }

  private static func presentation(of snapshot: LotDetailSnapshot) -> LotDetailPresentation {
    LotDetailPresentation(
      surface: snapshot.surface, notice: snapshot.notice, lotId: snapshot.lotId,
      lotName: snapshot.lotName, stale: snapshot.stale, refreshing: snapshot.refreshing,
      fetchedAtEpochMs: snapshot.fetchedAtEpochMs, staleAgeDays: Int(snapshot.staleAgeDays),
      staleAgeHours: Int(snapshot.staleAgeHours), staleAgeMinutes: Int(snapshot.staleAgeMinutes),
      heroVariant: snapshot.heroVariant, heroLabel: snapshot.heroLabel,
      heroStatusSince: snapshot.heroStatusSince, heroValue: snapshot.heroValue,
      heroRawNumber: snapshot.heroRawNumber, heroSoilPercent: snapshot.heroSoilPercent,
      heroLowPercent: snapshot.heroLowPercent, heroReadingAt: snapshot.heroReadingAt,
      heroNote: snapshot.heroNote, heroPausedUntil: snapshot.heroPausedUntil,
      heroResumeSiteHint: snapshot.heroResumeSiteHint,
      heroNeedsWaterFill: snapshot.heroNeedsWaterFill,
      heroDurationValue: snapshot.heroDurationValue,
      heroDurationUnit: snapshot.heroDurationUnit, noNode: snapshot.noNode,
      canAddNode: snapshot.canAddNode, canCalibrate: snapshot.canCalibrate,
      hasSensors: snapshot.hasSensors,
      sensorQuantities: snapshot.sensorQuantities, sensorNumbers: snapshot.sensorNumbers,
      sensorUnits: snapshot.sensorUnits, sensorMeasuredAts: snapshot.sensorMeasuredAts,
      hasDevice: snapshot.hasDevice, deviceNodeId: snapshot.deviceNodeId,
      deviceBattery: snapshot.deviceBattery, deviceBatteryLow: snapshot.deviceBatteryLow,
      deviceCharging: snapshot.deviceCharging, deviceLastSeen: snapshot.deviceLastSeen,
      quantities: snapshot.quantities, picked: snapshot.picked,
      historyUnavailable: snapshot.historyUnavailable, hasChart: snapshot.hasChart,
      chartQuantity: snapshot.chartQuantity, chartUnit: snapshot.chartUnit,
      barDays: snapshot.barDays, barDayEpochMs: snapshot.barDayEpochMs.map { $0.int64Value },
      barPresent: snapshot.barPresent.map { $0.boolValue }, barLows: snapshot.barLows,
      barHighs: snapshot.barHighs, barCounts: snapshot.barCounts.map { Int($0.int32Value) },
      barFractions: snapshot.barFractions.map { $0.doubleValue },
      chartDaysWithReadings: Int(snapshot.chartDaysWithReadings),
      chartLowest: snapshot.chartLowest, chartLowestDay: snapshot.chartLowestDay,
      chartHighest: snapshot.chartHighest, chartHighestDay: snapshot.chartHighestDay)
  }
}
