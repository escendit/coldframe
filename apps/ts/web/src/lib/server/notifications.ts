/**
 * The web app's calls for My notifications and the Site Reminder cadence (Story 6.3). Server-only:
 * the browser never calls the Server (AD-14). Settings are read on every load, with no last-good
 * copy, and the Server stays the only validator: the checks here only keep a request it would
 * refuse from being sent.
 */
import { fail, type ActionFailure, type Cookies } from '@sveltejs/kit';
import type { NotificationSettings, ReminderCadence, SetSiteNotificationSettingsRequest, SiteNotificationSettings, SiteReminderCadence, UpdateNotificationSettingsRequest } from '@coldframe/api-client';
import {
  isReminderCadence,
  useSiteSetting,
  windowOf,
  type NotificationsAction,
  type NotificationsFailure,
  type NotificationsNotice,
  type NotificationsPageNotice,
  type NotificationsSuccess,
  type SiteNotificationsNotice,
} from '$lib/notifications';
import type { Site } from '$lib/sites';
import { followServerTimeZone, signedOutRedirect } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

/** The caller's own Notification Window and time zone. */
export function getMyNotificationSettings(locals: Locals, dependencies: SitesDependencies = {}): Promise<SitesResult<NotificationSettings>> {
  return call(locals, dependencies, (client) => client.GET('/me/notification-settings'));
}

/** Changes the fields given; the answer is the settings in force. */
export function updateMyNotificationSettings(locals: Locals, body: UpdateNotificationSettingsRequest, dependencies: SitesDependencies = {}): Promise<SitesResult<NotificationSettings>> {
  return call(locals, dependencies, (client) => client.PATCH('/me/notification-settings', { body }));
}

/** The caller's own mute and Reminder cadence for one Site, with the Site's cadence (Member and up). */
export function getSiteNotificationSettings(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<SiteNotificationSettings>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/notification-settings', { params: { path: { siteId } } }));
}

/** Replaces both: `muted` is required and a missing `reminderCadence` means "use the Site setting". */
export function setSiteNotificationSettings(
  locals: Locals,
  siteId: string,
  body: SetSiteNotificationSettingsRequest,
  dependencies: SitesDependencies = {},
): Promise<SitesResult<SiteNotificationSettings>> {
  return call(locals, dependencies, (client) => client.PUT('/sites/{siteId}/notification-settings', { params: { path: { siteId } }, body }));
}

/** The Site's Reminder cadence (Member and up). */
export function getSiteReminderCadence(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<SiteReminderCadence>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/reminder-cadence', { params: { path: { siteId } } }));
}

/**
 * Sets the Site's Reminder cadence (Administrator and up). A 503 means the Site kept it but a member was
 * not reached: it counts as a save that failed, and sending it again repairs it.
 */
export function setSiteReminderCadence(locals: Locals, siteId: string, cadence: ReminderCadence, dependencies: SitesDependencies = {}): Promise<SitesResult<SiteReminderCadence>> {
  return call(locals, dependencies, (client) => client.PUT('/sites/{siteId}/reminder-cadence', { params: { path: { siteId } }, body: { cadence } }));
}

export interface NotificationsData {
  /** Null when they could not be read; `notice` then says why. */
  readonly settings: NotificationSettings | null;
  /** The caller's settings for the current Site; null without a Site or when they could not be read. */
  readonly siteSettings: SiteNotificationSettings | null;
  readonly notice: NotificationsPageNotice | null;
  readonly siteNotice: SiteNotificationsNotice | null;
}

function pageNoticeOf(error: SitesError): NotificationsPageNotice {
  return error === 'unreachable' || error === 'certificate' ? error : 'unavailable';
}

/** An answer outside the contract is not shown: the page's controls rely on these fields. */
function isSettings(value: NotificationSettings): boolean {
  const { window, timeZoneConfirmed } = value as { window?: { from?: unknown; to?: unknown }; timeZoneConfirmed?: unknown };
  return typeof timeZoneConfirmed === 'boolean' && typeof window?.from === 'string' && typeof window.to === 'string';
}

function isSiteSettings(value: SiteNotificationSettings): boolean {
  const { muted, siteReminderCadence } = value as { muted?: unknown; siteReminderCadence?: unknown };
  return typeof muted === 'boolean' && isReminderCadence(siteReminderCadence);
}

/**
 * My notifications: the caller's own settings and, with a current Site, their settings for it, read
 * at the same time. A 401 signs out. Without the caller's own settings the page shows a notice
 * only; a failed Site read leaves the window and the time zone usable. A zone the User chose is
 * also what the other pages format with from now on.
 */
export async function loadNotifications(locals: Locals, site: Site | null, cookies: Cookies, dependencies: SitesDependencies = {}): Promise<NotificationsData> {
  const [mine, forSite] = await Promise.all([getMyNotificationSettings(locals, dependencies), site === null ? null : getSiteNotificationSettings(locals, site.id, dependencies)]);
  if (('error' in mine && mine.error === 'unauthorized') || (forSite !== null && 'error' in forSite && forSite.error === 'unauthorized')) {
    signedOutRedirect();
  }
  if ('error' in mine) {
    return { settings: null, siteSettings: null, notice: pageNoticeOf(mine.error), siteNotice: null };
  }
  if (!isSettings(mine.ok)) {
    return { settings: null, siteSettings: null, notice: 'unavailable', siteNotice: null };
  }
  followServerTimeZone(cookies, mine.ok);
  if (forSite === null) {
    return { settings: mine.ok, siteSettings: null, notice: null, siteNotice: null };
  }
  if ('error' in forSite) {
    const siteNotice = forSite.error === 'notFound' || forSite.error === 'forbidden' ? 'siteGone' : pageNoticeOf(forSite.error);
    return { settings: mine.ok, siteSettings: null, notice: null, siteNotice };
  }
  if (!isSiteSettings(forSite.ok)) {
    return { settings: mine.ok, siteSettings: null, notice: null, siteNotice: 'unavailable' };
  }
  return { settings: mine.ok, siteSettings: forSite.ok, notice: null, siteNotice: null };
}

const statusOf: Readonly<Record<NotificationsNotice, number>> = {
  notSaved: 503,
  certificate: 502,
  invalidWindow: 400,
  invalidTimeZone: 400,
  siteGone: 404,
  unexpected: 500,
};

function noticeOf(action: NotificationsAction, error: Exclude<SitesError, 'unauthorized'>): NotificationsNotice {
  const perSite = action === 'setMute' || action === 'setCadence';
  switch (error) {
    case 'unreachable':
    case 'unavailable':
      return 'notSaved';
    case 'certificate':
      return 'certificate';
    case 'validation':
      return action === 'saveWindow' ? 'invalidWindow' : action === 'chooseTimeZone' ? 'invalidTimeZone' : 'unexpected';
    case 'forbidden':
    case 'notFound':
      return perSite ? 'siteGone' : 'unexpected';
    default:
      return 'unexpected';
  }
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

/** The text fields of the attempt, so Try again can send the same change. */
function fieldsOf(form: FormData): Record<string, string> {
  const fields: Record<string, string> = {};
  for (const [name, value] of form) {
    if (typeof value === 'string') {
      fields[name] = value;
    }
  }
  return fields;
}

/**
 * One My notifications action, from the page's forms:
 * - `saveWindow`: `from` and `to` (`HH:mm`; an empty `to` is left out, so the Server ends the window at 22:00).
 * - `chooseTimeZone`: `timeZone`, the User's own choice.
 * - `setMute` and `setCadence`: `siteId`, `muted` (present = muted) and `reminderCadence` (a cadence, or
 *   "site" for the Site setting). The Server replaces both values, so each form carries the other
 *   one as it is now.
 */
export async function notificationsAction(
  action: NotificationsAction,
  locals: Locals,
  request: Request,
  cookies: Cookies,
  dependencies: SitesDependencies = {},
): Promise<NotificationsSuccess | ActionFailure<NotificationsFailure>> {
  const form = await request.formData();
  const fields = fieldsOf(form);
  const siteName = text(form, 'siteName');
  const failure = (notice: NotificationsNotice): ActionFailure<NotificationsFailure> => fail(statusOf[notice], { action, notice, windowError: null, fields, siteName });

  let result: SitesResult<NotificationSettings | SiteNotificationSettings>;
  if (action === 'saveWindow') {
    const to = text(form, 'to');
    const window = windowOf(text(form, 'from'), to);
    if (typeof window === 'string') {
      return fail(400, { action, notice: null, windowError: window, fields, siteName });
    }
    result = await updateMyNotificationSettings(locals, { window: to === '' ? { from: window.from } : window }, dependencies);
  } else if (action === 'chooseTimeZone') {
    const timeZone = text(form, 'timeZone');
    if (timeZone === '' || timeZone.length > 64) {
      return failure('invalidTimeZone');
    }
    const sent = await updateMyNotificationSettings(locals, { timeZone }, dependencies);
    if ('ok' in sent) {
      followServerTimeZone(cookies, sent.ok);
    }
    result = sent;
  } else {
    const cadence = text(form, 'reminderCadence');
    if (cadence !== '' && cadence !== useSiteSetting && !isReminderCadence(cadence)) {
      // There is no "never" (UX-DR50): anything but the offered choices is not sent.
      return fail(400, { action, notice: 'unexpected', windowError: null, fields, siteName });
    }
    const body: SetSiteNotificationSettingsRequest = { muted: form.has('muted'), ...(isReminderCadence(cadence) ? { reminderCadence: cadence } : {}) };
    result = await setSiteNotificationSettings(locals, text(form, 'siteId'), body, dependencies);
  }

  if ('ok' in result) {
    return { action, done: true };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  return failure(noticeOf(action, result.error));
}
