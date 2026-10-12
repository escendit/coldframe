import type { DeviceListItem, Lot } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatNumber, formatSilence, formatWhen } from '$lib/i18n/format';
import { hasRole, type Role } from '$lib/roles';

export type { DeviceListItem } from '@coldframe/api-client';

/** Set when the Server could not list the Devices. No row is shown then, so nothing stays "Online". */
export type DevicesNotice = 'unreachable' | 'certificate';

export function devicesNoticeOf(notice: DevicesNotice | null): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'devices.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    default:
      return null;
  }
}

/**
 * The Hubs of the list, by Device ID. The Server lists every enrolled Device; Nodes have their own
 * section (`nodesOf`).
 */
export function hubsOf(devices: readonly DeviceListItem[]): readonly DeviceListItem[] {
  return devices.filter((device) => device.kind === 'hub').toSorted((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}

/**
 * The Nodes of the list, in the Server's order: by Lot name, unassigned last, then Device ID
 * (UX-DR30). Clients keep that order and never re-sort it.
 */
export function nodesOf(devices: readonly DeviceListItem[]): readonly DeviceListItem[] {
  return devices.filter((device) => device.kind === 'node');
}

/** "62 %" or the dash, from the Server's battery percentage. */
export function batteryText(device: Pick<DeviceListItem, 'batteryPercent'>, locale: string): string {
  return device.batteryPercent === undefined ? '—' : t('lotDetail.value.percent', { value: formatNumber(device.batteryPercent, locale) });
}

/** "charging", "not charging" or nothing, from the Server's charger state. */
export function chargingText(device: Pick<DeviceListItem, 'charging'>): string | null {
  return device.charging === undefined ? null : t(device.charging === 'charging' ? 'devices.charging.charging' : 'devices.charging.notCharging');
}

/** What a Role sees on the web Devices page besides the list (UX-DR84, UX-DR85). */
export interface DevicesAccess {
  /**
   * Adding Devices is for Owners and Administrators, and needs BLE: on the web they get the notice
   * in place of the Add actions. Members get neither.
   */
  readonly mobileAppNotice: boolean;
  /**
   * Moving a Node to another Lot and unassigning it are for Owners and Administrators and need no
   * BLE (UX-DR31). Members never see them: they are hidden, not disabled.
   */
  readonly canManageNodes: boolean;
  /**
   * A Device's Silence Window is set by Owners and Administrators only (UX-DR84). A Member sees the
   * silence and last seen, and no control: hidden, never disabled.
   */
  readonly canSetSilenceWindow: boolean;
}

export function devicesAccessOf(role: Role): DevicesAccess {
  const admin = hasRole(role, 'Administrator');
  return { mobileAppNotice: admin, canManageNodes: admin, canSetSilenceWindow: admin };
}

/** The id of a Device's row on the Devices page: what a Health Alert row links to (`/devices#device-…`). */
export function deviceRowId(deviceId: string): string {
  return `device-${deviceId}`;
}

/**
 * "Silent 6 h" for a Device with an open silent Alert, or null: `silentSince` is present exactly while
 * the Device is silent, and the duration is `now − silentSince`, rounded down. A silent-since the client
 * cannot read still says "Silent".
 */
export function silentText(device: Pick<DeviceListItem, 'silentSince'>, now: Date): string | null {
  if (device.silentSince === undefined) {
    return null;
  }
  const duration = formatSilence(device.silentSince, now);
  return duration === null ? t('devices.silentNoDuration') : t('devices.silent', { duration });
}

/** The units a Silence Window is entered in. */
export const silenceWindowUnits = ['min', 'h', 'd'] as const;

export type SilenceWindowUnit = (typeof silenceWindowUnits)[number];

const unitSeconds: Readonly<Record<SilenceWindowUnit, number>> = { min: 60, h: 3600, d: 86_400 };

const unitLabels: Readonly<Record<SilenceWindowUnit, MessageKey>> = {
  min: 'devices.silenceWindow.unit.min',
  h: 'devices.silenceWindow.unit.h',
  d: 'devices.silenceWindow.unit.d',
};

const unitDurations: Readonly<Record<SilenceWindowUnit, 'duration.minutes' | 'duration.hours' | 'duration.days'>> = {
  min: 'duration.minutes',
  h: 'duration.hours',
  d: 'duration.days',
};

export function silenceWindowUnitLabel(unit: SilenceWindowUnit): string {
  return t(unitLabels[unit]);
}

/** The Server's bounds of a Silence Window: 1 min to 30 days, in seconds. */
export const silenceWindowRange = { min: 60, max: 2_592_000 } as const;

/**
 * The seconds of what was entered, or null when the client refuses it before anything is sent: not a
 * whole number, an unknown unit, or outside 1 min to 30 days. The Server stays the validator.
 */
export function silenceWindowSeconds(amount: string, unit: string): number | null {
  const known = silenceWindowUnits.find((candidate) => candidate === unit);
  const digits = amount.trim();
  if (known === undefined || !/^\d{1,9}$/u.test(digits)) {
    return null;
  }
  const seconds = Number(digits) * unitSeconds[known];
  return seconds >= silenceWindowRange.min && seconds <= silenceWindowRange.max ? seconds : null;
}

/** A window as the number and unit the form shows: the largest unit that divides it, else minutes. */
export interface SilenceWindowParts {
  readonly amount: number;
  readonly unit: SilenceWindowUnit;
}

export function silenceWindowParts(seconds: number | undefined): SilenceWindowParts | null {
  if (seconds === undefined || !Number.isFinite(seconds) || seconds <= 0) {
    return null;
  }
  for (const unit of ['d', 'h'] as const) {
    if (seconds % unitSeconds[unit] === 0) {
      return { amount: seconds / unitSeconds[unit], unit };
    }
  }
  return { amount: Math.max(1, Math.round(seconds / unitSeconds.min)), unit: 'min' };
}

/** The window in force as text ("6 h", "5 min", "2 d"), or null when the Server sent none. */
export function silenceWindowText(device: Pick<DeviceListItem, 'silenceWindowSeconds'>): string | null {
  const parts = silenceWindowParts(device.silenceWindowSeconds);
  return parts === null ? null : t(unitDurations[parts.unit], { count: parts.amount });
}

/** A Lot in the move picker: a Lot that holds another Node cannot be chosen. */
export interface LotChoice {
  readonly id: string;
  readonly name: string;
  /** Another Node holds the Lot: shown disabled with "Has a Node". */
  readonly hasNode: boolean;
  /** The Node is on this Lot already: moving to it changes nothing. */
  readonly current: boolean;
}

/**
 * The Lots a Node can move to, in the Server's order. The Server's `noNode` status is the one fact
 * that a Lot is free; the Node's own Lot is marked current. The Server still decides: a Lot taken
 * meanwhile is refused with a 409.
 */
export function lotChoicesOf(lots: readonly Lot[], node: Pick<DeviceListItem, 'lotId'>): readonly LotChoice[] {
  return lots.map((lot) => {
    const current = lot.id === node.lotId;
    return { id: lot.id, name: lot.name, current, hasNode: !current && lot.status !== 'noNode' };
  });
}

export type DeviceAction = 'moveNode' | 'unassignNode' | 'setSilenceWindow';

/**
 * Why a move, an unassign or a Silence Window change did not happen; each has its copy on the page.
 * `range` is a Silence Window outside 1 min to 30 days, refused before it is sent or by the Server.
 */
export type DeviceActionNotice = 'forbidden' | 'lotClaimed' | 'notFound' | 'range' | 'unexpected' | 'unreachable' | 'certificate';

/** A change that did not happen. */
export interface DeviceActionFailure {
  readonly action: DeviceAction;
  readonly deviceId: string;
  readonly notice: DeviceActionNotice;
  /** Set Silence Window only: what was entered, so the form shows it again. */
  readonly amount?: string;
  readonly unit?: string;
}

/** A change that did. */
export interface DeviceActionSuccess {
  readonly action: DeviceAction;
  readonly deviceId: string;
  readonly done: true;
}

/** The status word and icon of a row. `online` is the Server's; nothing here computes it. */
export function statusOf(device: DeviceListItem): { readonly label: MessageKey; readonly icon: 'checkmark--outline' | 'help' } {
  return device.online ? { label: 'devices.online', icon: 'checkmark--outline' } : { label: 'devices.offline', icon: 'help' };
}

/** "Last seen 07:02", or "Not seen yet" for a Device that never sent a heartbeat. */
export function lastSeenText(device: DeviceListItem, now: Date, locale: string, timeZone: string): string {
  if (device.lastSeenAt === undefined) {
    return t('devices.notSeen');
  }
  return t('devices.lastSeen', { time: formatWhen(new Date(device.lastSeenAt), now, locale, timeZone) });
}
