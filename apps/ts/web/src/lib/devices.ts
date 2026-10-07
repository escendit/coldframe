import type { DeviceListItem, Lot } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatNumber, formatWhen } from '$lib/i18n/format';
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
}

export function devicesAccessOf(role: Role): DevicesAccess {
  const admin = hasRole(role, 'Administrator');
  return { mobileAppNotice: admin, canManageNodes: admin };
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

export type DeviceAction = 'moveNode' | 'unassignNode';

/** Why a move or unassign did not happen; each has its copy on the page. */
export type DeviceActionNotice = 'forbidden' | 'lotClaimed' | 'notFound' | 'unexpected' | 'unreachable' | 'certificate';

/** A move or unassign that did not happen. */
export interface DeviceActionFailure {
  readonly action: DeviceAction;
  readonly deviceId: string;
  readonly notice: DeviceActionNotice;
}

/** A move or unassign that did. */
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
