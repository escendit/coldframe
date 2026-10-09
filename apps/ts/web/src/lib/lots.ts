import type { MessageKey } from '$lib/i18n';
import { hasRole, type Role } from '$lib/roles';
import { checkSiteName, type NameError } from '$lib/sites';

export type { Lot, LotStatus } from '@coldframe/api-client';

/** What a Role may change in Site settings (UX-DR74, UX-DR84). Hidden, never disabled. */
export interface SiteSettingsAccess {
  /** Only an Owner renames the Site. */
  readonly canRenameSite: boolean;
  /** Owners and Administrators create, rename and remove Lots. */
  readonly canEditLots: boolean;
  /** Owners and Administrators set the Site's Reminder cadence; a Member reads it as text (UX-DR50). */
  readonly canSetReminderCadence: boolean;
  /** Members see one read-only notice instead of the controls. */
  readonly readOnlyNotice: boolean;
}

export function siteSettingsOf(role: Role): SiteSettingsAccess {
  return {
    canRenameSite: role === 'Owner',
    canEditLots: hasRole(role, 'Administrator'),
    canSetReminderCadence: hasRole(role, 'Administrator'),
    readOnlyNotice: !hasRole(role, 'Administrator'),
  };
}

/** A Lot name follows the Site name rule: 1 to 100 characters after trimming. Names need not be unique. */
export function checkLotName(name: string): NameError | null {
  return checkSiteName(name);
}

/** Set when the Server could not list the Lots; the copy is the shell's. */
export type LotsNotice = 'unreachable' | 'certificate' | 'unavailable';

export function lotsNoticeOf(notice: LotsNotice | null): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'notice.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    case 'unavailable':
      return { message: 'lots.unavailable', tryAgain: true };
    default:
      return null;
  }
}

export type SiteSettingsAction = 'renameSite' | 'createLot' | 'renameLot' | 'removeLot' | 'setReminderCadence';

/** Why a change did not happen; each has its copy on the page. */
/**
 * `cadenceNotDelivered` is the one failure that did save: the Site holds the picked Reminder cadence, but a
 * member was not handed it (503 `reminder-cadence-not-delivered`); sending it again repairs it.
 */
export type SiteSettingsNotice =
  | 'forbidden'
  | 'lotClaimed'
  | 'lotNotFound'
  | 'siteNotFound'
  | 'unavailable'
  | 'keyReused'
  | 'unexpected'
  | 'unreachable'
  | 'certificate'
  | 'cadenceNotDelivered';

/** A change that did not happen: the field value, its reason, or a notice. */
export interface SiteSettingsFailure {
  readonly action: SiteSettingsAction;
  readonly lotId: string | null;
  readonly name: string;
  readonly nameError: NameError | null;
  readonly notice: SiteSettingsNotice | null;
  /** The Site and Lot names the notices mention. */
  readonly siteName: string;
  readonly lotName: string | null;
  /** Create Lot only: the key the next attempt uses (the same one, except after a reused key). */
  readonly idempotencyKey: string | null;
  /** Set Reminder cadence only: the cadence of the attempt, so Try again sends it again. */
  readonly cadence: string | null;
}

export interface SiteSettingsSuccess {
  readonly action: SiteSettingsAction;
  readonly done: true;
}
