/**
 * Pause and Resume in the web app (Story 8.2; UX-DR46, UX-DR70, UX-DR83, UX-DR84). Whether a Site or a
 * Device is paused is the Server's (`Site.paused`, `pausedBy`, `pausedUntil`; AD-8, AD-14): nothing here
 * computes or remembers it. This only says what a surface shows and offers for what the Server sent.
 */
import type { DeviceListItem } from '@coldframe/api-client';
import { t } from '$lib/i18n';
import { formatDay, shortDeviceId } from '$lib/i18n/format';
import { hasRole, type Role } from '$lib/roles';

/** What a Pause covers: one Device, or the whole Site. */
export type PauseScope = 'device' | 'site';

/** The Device a Pause modal was opened for; none from the Site menu. */
export interface PauseDevice {
  readonly id: string;
  readonly kind: DeviceListItem['kind'];
}

/** Pause and Resume are for Administrators and Owners; a Member has no such control (hidden, never disabled). */
export function pauseAccessOf(role: Role | null | undefined): boolean {
  return hasRole(role, 'Administrator');
}

/**
 * The scopes the modal offers. From the Site menu there is no Device, so Whole Site is the only
 * segment; from a Device, This Device comes first and is the one selected.
 */
export function pauseScopes(device: PauseDevice | null): readonly PauseScope[] {
  return device === null ? ['site'] : ['device', 'site'];
}

export function pauseScopeOptions(device: PauseDevice | null): readonly { readonly value: PauseScope; readonly label: string }[] {
  return pauseScopes(device).map((value) => ({ value, label: t(value === 'device' ? 'pause.scope.device' : 'pause.scope.site') }));
}

/** The primary button names the result: "Pause Home garden", or "Pause 7C19" for a Device. */
export function pausePrimaryLabel(scope: PauseScope, siteName: string, device: PauseDevice | null): string {
  return t('pause.action', { name: scope === 'device' && device !== null ? shortDeviceId(device.id) : siteName });
}

/**
 * True for a calendar date as the date field gives it, `YYYY-MM-DD`. Whether it lies ahead is the
 * Server's to say, in the User's time zone: the client has no zone logic of its own.
 */
export function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    return false;
  }
  const date = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(value);
}

/**
 * Why a Pause or a Resume did not happen. `date` is an end the Server refused (or that is no calendar
 * date), said under the date field; the others have their notice.
 */
export type PauseNotice = 'date' | 'forbidden' | 'notFound' | 'unreachable' | 'certificate' | 'unexpected';

/** A Pause or Resume that did not happen: nothing was changed. */
export interface PauseFailure {
  readonly action: 'pause' | 'resume';
  readonly scope: PauseScope;
  readonly deviceId?: string;
  readonly notice: PauseNotice;
  /** Pause only: the date that was picked, so the modal shows it again. */
  readonly until?: string;
}

/**
 * Whether the date field carries the mark "Choose a date after today.": only for the date the Server
 * refused. Editing the date takes the mark away at once, as the mobile core's `setUntil` does.
 */
export function pauseDateRefused(failure: Pick<PauseFailure, 'notice' | 'until'> | null, until: string): boolean {
  return failure?.notice === 'date' && (failure.until ?? '') === until;
}

/**
 * The saves of one Pause modal, so an answer can tell whether its modal is still the one that asked.
 * `begin()` starts a save and returns its check; `abandon()` (Cancel, Esc, a close) makes every save
 * begun before it stale. A refusal that arrives stale says nothing: the next opening starts empty.
 */
export function pauseAttempts(): { readonly begin: () => () => boolean; readonly abandon: () => void } {
  let current = 0;
  return {
    begin: () => {
      const mine = current;
      return () => mine === current;
    },
    abandon: () => {
      current += 1;
    },
  };
}

/** A Pause or Resume the Server accepted. The page reads its data again; the answer itself is not kept. */
export interface PauseSuccess {
  readonly action: 'pause' | 'resume';
  readonly scope: PauseScope;
  readonly deviceId?: string;
  readonly done: true;
}

/**
 * The notice of a refusal, and whether sending the same again can help. A 403 is the notice of every
 * change a caller may no longer make (UX-DR84); the others are the notices the Devices actions have.
 * A refused date has none: it is said under the field.
 */
export function pauseNoticeOf(failure: Pick<PauseFailure, 'notice' | 'scope'>, siteName: string): { readonly message: string; readonly tryAgain: boolean } | null {
  switch (failure.notice) {
    case 'date':
      return null;
    case 'forbidden':
      return { message: t('siteSettings.forbidden', { siteName }), tryAgain: false };
    case 'notFound':
      return { message: failure.scope === 'device' ? t('devices.silenceWindow.notFound') : t('notifications.notice.siteGone', { siteName }), tryAgain: false };
    case 'certificate':
      return { message: t('notice.certificate'), tryAgain: false };
    case 'unreachable':
      return { message: t('notice.unreachable'), tryAgain: true };
    case 'unexpected':
      return { message: t('devices.action.unexpected'), tryAgain: true };
  }
}

/** What a surface shows and offers for one Device. */
export interface DevicePauseView {
  /** The Server listed a Pause source. */
  readonly paused: boolean;
  /** "Paused", or "Paused by Site" when the Site is one of the sources; upper case is style only. */
  readonly label: string | null;
  /** "until 1 Mar", from the Server's `pausedUntil`. */
  readonly until: string | null;
  /** Pause for the Device: while it is not paused by itself, also when the Site pauses it. */
  readonly canPause: boolean;
  /** Resume in one tap: only the Device's own Pause can be lifted from the Device. */
  readonly canResume: boolean;
  /** Paused by the Site only: "Resume the Site to resume this Node" (or "… this Hub") in place of Resume. */
  readonly resumeSiteLine: string | null;
}

export interface DevicePauseContext {
  /** Administrator or Owner: the caller may pause and resume. */
  readonly admin: boolean;
  readonly locale: string;
  readonly timeZone: string;
}

/**
 * The paused state of a Device and its controls, from the Server's `pausedBy` and `pausedUntil` alone
 * (a Lot carries the same two members for its Node). A Member gets the state and no control.
 */
export function devicePauseOf(device: { readonly kind: DeviceListItem['kind']; readonly pausedBy?: readonly string[]; readonly pausedUntil?: string }, context: DevicePauseContext): DevicePauseView {
  const sources = device.pausedBy ?? [];
  const byDevice = sources.includes('device');
  const bySite = sources.includes('site');
  const paused = sources.length > 0;
  const end = paused && device.pausedUntil !== undefined ? new Date(device.pausedUntil) : null;
  return {
    paused,
    label: paused ? t(bySite ? 'lotTile.label.pausedBySite' : 'lotTile.label.paused') : null,
    until: end === null || Number.isNaN(end.getTime()) ? null : t('lotTile.foot.until', { date: formatDay(end, context.locale, context.timeZone) }),
    canPause: context.admin && !byDevice,
    canResume: context.admin && byDevice,
    resumeSiteLine: context.admin && bySite && !byDevice ? t(device.kind === 'hub' ? 'pause.resumeSiteHub' : 'lotDetail.resumeSite') : null,
  };
}
