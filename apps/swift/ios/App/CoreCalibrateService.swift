import ColdframeCore
import ColdframeIOS
import Foundation

/// Adapts `IosCalibrate` of the shared Kotlin core to `CalibrateService`. The snapshot is flat
/// strings, flags and lists; the Server calls, the steps and the rules stay in the core (AD-14).
@MainActor
final class CoreCalibrateService: CalibrateService {
  private let core: IosCalibrate
  private var watch: Watch?

  init(core: IosCalibrate) {
    self.core = core
  }

  func observe(_ onChange: @escaping @MainActor (CalibratePresentation) -> Void) {
    watch?.close()
    watch = core.watch { snapshot in
      MainActor.assumeIsolated {
        onChange(
          CalibratePresentation(
            surface: snapshot.surface, notice: snapshot.notice,
            noticeTryAgain: snapshot.noticeTryAgain, lotId: snapshot.lotId,
            lotName: snapshot.lotName, step: snapshot.step, working: snapshot.working,
            canRecord: snapshot.canRecord, hasFresh: snapshot.hasFresh,
            lastRawValue: snapshot.lastRawValue, lastReadingAt: snapshot.lastReadingAt,
            readingSeqs: snapshot.readingSeqs.map { $0.int64Value },
            readingRawValues: snapshot.readingRawValues.map { $0.int64Value },
            readingAts: snapshot.readingAts, pickedSeq: snapshot.pickedSeq,
            dryRaw: snapshot.dryRaw, wetRaw: snapshot.wetRaw, percent: snapshot.percent,
            announcementId: Int(snapshot.announcementId),
            announcementKind: snapshot.announcementKind,
            announcementStep: snapshot.announcementStep,
            announcementRaw: snapshot.announcementRaw, announcementAt: snapshot.announcementAt,
            announcementPercent: snapshot.announcementPercent,
            announcementLot: snapshot.announcementLot, pausedBySite: snapshot.pausedBySite,
            offersResume: snapshot.offersResume,
            offersThresholds: snapshot.offersThresholds))
      }
    }
  }

  func open(lotId: String, name: String) { core.open(lotId: lotId, name: name) }

  func close() { core.close() }

  func retry() { core.retry() }

  func pick(readingSeq: Int64) { core.pick(readingSeq: readingSeq) }

  func record() { core.record() }
}
