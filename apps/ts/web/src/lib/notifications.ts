import type { NotificationWindow, ReminderCadence } from '@coldframe/api-client';
import type { MessageKey } from '$lib/i18n';

export type { NotificationSettings, NotificationWindow, ReminderCadence, SiteNotificationSettings } from '@coldframe/api-client';

/** The Reminder cadences in the order they are offered. There is no "never" (UX-DR50). */
export const reminderCadences: readonly ReminderCadence[] = ['daily', 'every2Days'];

export const cadenceLabel: Readonly<Record<ReminderCadence, MessageKey>> = {
  daily: 'reminders.daily',
  every2Days: 'reminders.every2Days',
};

export function isReminderCadence(value: unknown): value is ReminderCadence {
  return value === 'daily' || value === 'every2Days';
}

/** The form value of "Use Site setting": the request then carries no cadence of the User's own. */
export const useSiteSetting = 'site';

export type CadenceChoice = ReminderCadence | typeof useSiteSetting;

/** The window a User has before any change, and the end of a window given by its start alone. */
export const defaultWindow: NotificationWindow = { from: '07:00', to: '22:00' };

const time = /^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/u;

/** Minutes since midnight of an `HH:mm` (24 h) time, or null for anything else. */
export function minutesOf(value: string): number | null {
  if (!time.test(value)) {
    return null;
  }
  return Number(value.slice(0, 2)) * 60 + Number(value.slice(3, 5));
}

export type WindowError = 'fromInvalid' | 'toInvalid' | 'notBefore';

/**
 * The window a form describes: an empty end means 22:00 (UX-DR47). The Server stays the validator;
 * this only keeps a request it would refuse from being sent.
 */
export function windowOf(from: string, to: string): NotificationWindow | WindowError {
  const end = to === '' ? defaultWindow.to : to;
  const start = minutesOf(from);
  if (start === null) {
    return 'fromInvalid';
  }
  const stop = minutesOf(end);
  if (stop === null) {
    return 'toInvalid';
  }
  return start < stop ? { from, to: end } : 'notBefore';
}

export function checkWindow(from: string, to: string): WindowError | null {
  const result = windowOf(from, to);
  return typeof result === 'string' ? result : null;
}

const dayMinutes = 24 * 60;

/** Where the window sits on the 24 h bar, in percent of the day. */
export function windowBar(window: NotificationWindow): { readonly start: number; readonly length: number } {
  const start = minutesOf(window.from) ?? 0;
  const stop = Math.max(minutesOf(window.to) ?? dayMinutes, start);
  return { start: (start / dayMinutes) * 100, length: ((stop - start) / dayMinutes) * 100 };
}

/**
 * The browser's zone, written by the page so the web app's server can hand it to the Server as the
 * detected zone (DW-23). A session cookie; it is never read back by the page.
 */
export const browserZoneCookieName = 'cf_browser_zone';

/** A `document.cookie` assignment that tells the web app's server this browser's zone. */
export function serializeBrowserZoneCookie(zone: string, secure: boolean): string {
  return [`${browserZoneCookieName}=${encodeURIComponent(zone)}`, 'Path=/', 'SameSite=Lax', ...(secure ? ['Secure'] : [])].join('; ');
}

/** Set when the Server could not be asked for the settings. */
export type NotificationsPageNotice = 'unreachable' | 'certificate' | 'unavailable';

/** As `NotificationsPageNotice`, or the Site is no longer one of the caller's. */
export type SiteNotificationsNotice = NotificationsPageNotice | 'siteGone';

export function notificationsNoticeOf(notice: SiteNotificationsNotice | null, scope: 'mine' | 'site'): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'notice.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    case 'unavailable':
      return { message: scope === 'mine' ? 'notifications.unavailable' : 'notifications.siteUnavailable', tryAgain: true };
    case 'siteGone':
      return { message: 'notifications.notice.siteGone', tryAgain: false };
    default:
      return null;
  }
}

export type NotificationsAction = 'saveWindow' | 'chooseTimeZone' | 'setMute' | 'setCadence';

/** Why a change did not happen; each has its copy on the page. */
export type NotificationsNotice = 'notSaved' | 'certificate' | 'invalidWindow' | 'invalidTimeZone' | 'siteGone' | 'unexpected';

export const notificationsNoticeCopy: Readonly<Record<NotificationsNotice, MessageKey>> = {
  notSaved: 'notifications.notice.notSaved',
  certificate: 'notice.certificate',
  invalidWindow: 'notifications.notice.invalidWindow',
  invalidTimeZone: 'notifications.notice.invalidTimeZone',
  siteGone: 'notifications.notice.siteGone',
  unexpected: 'notifications.notice.unexpected',
};

/** Only a save the Server never judged can be repeated as it was. */
export function canTryAgain(notice: NotificationsNotice): boolean {
  return notice === 'notSaved' || notice === 'unexpected';
}

/** A change that did not happen. The controls show the Server's values again. */
export interface NotificationsFailure {
  readonly action: NotificationsAction;
  readonly notice: NotificationsNotice | null;
  /** Save window only: why the times were not sent. */
  readonly windowError: WindowError | null;
  /** The form fields of the attempt, so Try again sends the same change. */
  readonly fields: Readonly<Record<string, string>>;
  /** The Site name the notices mention. */
  readonly siteName: string;
}

export interface NotificationsSuccess {
  readonly action: NotificationsAction;
  readonly done: true;
}
