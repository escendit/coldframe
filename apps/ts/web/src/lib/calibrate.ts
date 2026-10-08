/**
 * The Calibrate flow's view models (Story 5.2). The Server decides everything that is a rule: who may
 * calibrate, which Sensor can be calibrated, where the Calibration stands and which stored Readings
 * exist (AD-14). Here a Reading is only told as fresh when it was taken after the step started, and
 * copy is formatted.
 */
import type { CalibrationReading, CalibrationState, SensorReading } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatTime } from '$lib/i18n/format';
import type { Lot } from '$lib/lots';
import { hasRole, type Role } from '$lib/roles';
import { soilApprox } from '$lib/lot-tiles';

export type { CalibrationReading, CalibrationState } from '@coldframe/api-client';

export type CalibrateStep = 'dry' | 'wet' | 'confirm' | 'paused';

/** Only Owners and Administrators calibrate; the control is hidden for a Member, never disabled. */
export function calibrateAccessOf(role: Role | null | undefined): boolean {
  return hasRole(role, 'Administrator');
}

/** The Sensor of the Lot whose Specification says `calibration: true`, or null. */
export function calibratableSensor(lot: Pick<Lot, 'sensors'>): (SensorReading & { readonly sensorId: string }) | null {
  const found = lot.sensors?.find((sensor) => sensor.calibratable === true && sensor.sensorId !== undefined);
  return found?.sensorId === undefined ? null : { ...found, sensorId: found.sensorId };
}

/** Both points the Server saved, and when, once the wet point completed the Calibration. */
export interface Recorded {
  readonly dryRaw: number;
  readonly wetRaw: number;
  /** ISO-8601: when the Calibration was saved, as this app learned it. */
  readonly savedAt: string;
}

export function calibrateStep(lot: Pick<Lot, 'status' | 'pausedBy'>, state: CalibrationState, recorded: Recorded | null): CalibrateStep {
  if (recorded !== null) {
    return 'confirm';
  }
  if (lot.status === 'paused' || (lot.pausedBy?.length ?? 0) > 0) {
    return 'paused';
  }
  return state.pendingDry === undefined ? 'dry' : 'wet';
}

/** The newest `readingSeq` among the Readings, or -1 when there are none. */
export function newestSeq(readings: readonly CalibrationReading[]): number {
  return readings.reduce((newest, reading) => Math.max(newest, reading.readingSeq), -1);
}

/**
 * The newest stored Reading stored after the step started, or null while the flow still waits. The step's
 * start is the newest `readingSeq` seen when it began: the order is the Server's, so no clock (the phone's
 * or the Node's) can make a Reading look older than the step.
 */
export function freshReading(readings: readonly CalibrationReading[], afterSeq: number): CalibrationReading | null {
  return readings.find((reading) => reading.readingSeq > afterSeq) ?? null;
}

function timeOf(reading: CalibrationReading, locale: string, timeZone: string): string {
  return formatTime(new Date(reading.measuredAt), locale, timeZone);
}

/** What a screen reader hears, politely, when a fresh Reading enables Record (UX-DR106). */
export function readingAnnouncement(step: 'dry' | 'wet', reading: CalibrationReading, locale: string, timeZone: string): string {
  return t(step === 'dry' ? 'calibrate.announce.dry' : 'calibrate.announce.wet', { time: timeOf(reading, locale, timeZone), raw: reading.rawValue });
}

/** The waiting panel's last Reading line, or null before any Reading. */
export function lastReadingText(readings: readonly CalibrationReading[], locale: string, timeZone: string): string | null {
  const [last] = readings;
  return last === undefined ? null : t('calibrate.last', { raw: last.rawValue, time: timeOf(last, locale, timeZone) });
}

export interface RecentReading {
  readonly readingSeq: number;
  readonly label: string;
}

/** The Server's recent stored Readings, newest first, each named by its `readingSeq`. */
export function recentReadings(readings: readonly CalibrationReading[], locale: string, timeZone: string): readonly RecentReading[] {
  return readings.map((reading) => ({ readingSeq: reading.readingSeq, label: t('calibrate.recentItem', { time: timeOf(reading, locale, timeZone), raw: reading.rawValue }) }));
}

export interface Confirmation {
  /** True once the Server stored a calibrated Reading after the save. */
  readonly ready: boolean;
  readonly text: string;
  /** The polite announcement for the first percentage; null before it. */
  readonly announcement: string | null;
}

/**
 * "% appears with the next Reading" until the Server stored a Reading under the new Calibration (a
 * soil Reading in `%` taken after the save), then "<Lot> reads ~NN %". No percentage before that.
 */
export function confirmation(lotName: string, lot: Pick<Lot, 'sensors'>, savedAt: string): Confirmation {
  const soil = lot.sensors?.find((sensor) => sensor.quantity === 'soil_moisture');
  if (soil?.unit === '%' && Date.parse(soil.measuredAt) > Date.parse(savedAt)) {
    const percent = soilApprox(soil.value);
    return {
      ready: true,
      text: t('calibrate.confirm.reads', { name: lotName, value: `~${String(percent)}` }),
      announcement: t('calibrate.confirm.announce', { name: lotName, percent }),
    };
  }
  return { ready: false, text: t('calibrate.confirm.pending'), announcement: null };
}

/** Why a Calibrate call did not happen; each has its copy. */
export type CalibrateNotice = 'indistinct' | 'forbidden' | 'notFound' | 'notDelivered' | 'unavailable' | 'unexpected' | 'unreachable' | 'certificate';

export const calibrateNoticeKeys: Readonly<Record<CalibrateNotice, MessageKey>> = {
  indistinct: 'calibrate.notice.indistinct',
  forbidden: 'calibrate.notice.forbidden',
  notFound: 'calibrate.notice.notFound',
  notDelivered: 'calibrate.notice.notDelivered',
  unavailable: 'calibrate.notice.unavailable',
  unexpected: 'calibrate.notice.unexpected',
  unreachable: 'notice.unreachable',
  certificate: 'notice.certificate',
};

export type CalibratePoint = 'dry' | 'wet';

/** A recorded point, as the action answers. */
export interface CalibrateSuccess {
  readonly done: true;
  readonly point: CalibratePoint;
  readonly calibrated: boolean;
  readonly dryRaw?: number;
  readonly wetRaw?: number;
  readonly savedAt?: string;
}

/** A point that was not recorded, or was saved but not confirmed (`notDelivered`, with both raw values). */
export interface CalibrateFailure {
  readonly notice: CalibrateNotice;
  readonly point: CalibratePoint;
  readonly dryRaw?: number;
  readonly wetRaw?: number;
  readonly savedAt?: string;
}
