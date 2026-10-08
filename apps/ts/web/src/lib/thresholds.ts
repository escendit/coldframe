/**
 * The Thresholds page's view models (Story 5.4). The Server decides every rule: who may change them, the
 * range, whether a Sensor alerts and the proposed low (AD-14, AD-19). Here a value is edited, formatted and
 * turned into the request; the client check of low and high only gates Save, the Server still validates.
 */
import type { SensorQuantity, SensorThresholds, SetSensorThresholdsRequest } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatNumber } from '$lib/i18n/format';
import { hasRole, type Role } from '$lib/roles';

export type { SensorThresholds, SetSensorThresholdsRequest } from '@coldframe/api-client';

/** Only Owners and Administrators change Thresholds; the control is hidden for a Member, never disabled. */
export function thresholdsAccessOf(role: Role | null | undefined): boolean {
  return hasRole(role, 'Administrator');
}

/** The step of the calibrated soil percentage (5 %). */
export const soilStep = 5;

/** The step a Threshold moves in: 5 % for the calibrated soil Sensor, whole units otherwise. */
export function stepOf(quantity: SensorQuantity, unit: string): number {
  return quantity === 'soil_moisture' && unit === '%' ? soilStep : 1;
}

/** The two values being edited; `null` is no Threshold on that side. */
export interface Edit {
  readonly low: number | null;
  readonly high: number | null;
}

/** The edit that starts from the Thresholds in force. */
export function editOf(thresholds: SensorThresholds): Edit {
  return { low: thresholds.low.value ?? null, high: thresholds.high.value ?? null };
}

export type Validity = 'ok' | 'lowNotBelowHigh' | 'lowRequired';

/** Gates Save: a high needs a low, and low must stay strictly below high. */
export function validityOf(edit: Edit): Validity {
  if (edit.high !== null && edit.low === null) {
    return 'lowRequired';
  }
  return edit.low !== null && edit.high !== null && edit.low >= edit.high ? 'lowNotBelowHigh' : 'ok';
}

export function validityMessage(validity: Validity): string | null {
  switch (validity) {
    case 'lowNotBelowHigh':
      return t('thresholds.lowNotBelowHigh');
    case 'lowRequired':
      return t('thresholds.lowRequired');
    default:
      return null;
  }
}

/** Turning alerts on offers the Server's proposed low and never a proposed high. */
export function turnOnAlerts(edit: Edit, thresholds: SensorThresholds): Edit {
  return edit.low === null && thresholds.proposedLow !== undefined ? { low: thresholds.proposedLow, high: edit.high } : edit;
}

/** Turning alerts off clears both sides: the Sensor is watched only. */
export function turnOffAlerts(): Edit {
  return { low: null, high: null };
}

type Side = NonNullable<SetSensorThresholdsRequest['low']>;

function sideOf(initial: SensorThresholds['low'], value: number | null): Side | undefined {
  if (value === (initial.value ?? null)) {
    return undefined;
  }
  return value === null ? { kind: 'cleared' } : { kind: 'override', value };
}

/** The request for what changed, or null when nothing did; an unchanged side is left out. */
export function requestOf(initial: SensorThresholds, edit: Edit): SetSensorThresholdsRequest | null {
  const low = sideOf(initial.low, edit.low);
  const high = sideOf(initial.high, edit.high);
  return low === undefined && high === undefined ? null : { ...(low === undefined ? {} : { low }), ...(high === undefined ? {} : { high }) };
}

const unitKeys: Readonly<Record<string, MessageKey>> = {
  '%': 'thresholds.unit.percent',
  '°C': 'thresholds.unit.celsius',
  'kΩ': 'thresholds.unit.kiloohm',
  raw: 'thresholds.unit.raw',
};

/** A value in the Sensor's display unit: `30 %`, `7.5 °C`. */
export function formatThreshold(value: number, unit: string, locale: string): string {
  return t(unitKeys[unit] ?? 'thresholds.unit.raw', { value: formatNumber(value, locale, 1) });
}

/** "low 30 %, high none" for one Sensor's Thresholds in force. */
export function thresholdSummary(sensorName: string, thresholds: SensorThresholds, locale: string): string {
  const side = (value: number | undefined): string => (value === undefined ? t('thresholds.none') : formatThreshold(value, thresholds.unit, locale));
  return t('thresholds.summary', { name: sensorName, low: side(thresholds.low.value), high: side(thresholds.high.value) });
}

/** Where a value sits on the track as a fraction 0 (bottom) to 1 (top), clamped. */
export function fractionOf(value: number, min: number, max: number): number {
  return max === min ? 0 : Math.min(1, Math.max(0, (value - min) / (max - min)));
}

/** The value under a pointer at `fraction` of the track, snapped to the step and kept within the range. */
export function valueAt(fraction: number, min: number, max: number, step: number): number {
  const raw = min + Math.min(1, Math.max(0, fraction)) * (max - min);
  return Math.min(max, Math.max(min, Math.round(raw / step) * step));
}
