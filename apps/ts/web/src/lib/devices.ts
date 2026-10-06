import type { DeviceListItem } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatWhen } from '$lib/i18n/format';
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
 * The Hubs of the list, by Device ID. The Server lists every enrolled Device; Nodes are not shown
 * until they have a section of their own.
 */
export function hubsOf(devices: readonly DeviceListItem[]): readonly DeviceListItem[] {
  return devices.filter((device) => device.kind === 'hub').toSorted((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}

/** What a Role sees on the web Devices page besides the list (UX-DR84, UX-DR85). */
export interface DevicesAccess {
  /**
   * Adding Devices is for Owners and Administrators, and needs BLE: on the web they get the notice
   * in place of the Add actions. Members get neither.
   */
  readonly mobileAppNotice: boolean;
}

export function devicesAccessOf(role: Role): DevicesAccess {
  return { mobileAppNotice: hasRole(role, 'Administrator') };
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
