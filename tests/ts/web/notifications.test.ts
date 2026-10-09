import { isActionFailure, isRedirect, type ActionFailure } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import NotificationsPage from '../../../apps/ts/web/src/routes/(app)/settings/notifications/+page.svelte';
import SettingsPage from '../../../apps/ts/web/src/routes/(app)/settings/+page.svelte';
import NotificationWindow from '$lib/components/NotificationWindow.svelte';
import TimeZonePanel from '$lib/components/TimeZonePanel.svelte';
import Toggle from '$lib/components/Toggle.svelte';
import { t } from '$lib/i18n';
import {
  browserZoneCookieName,
  canTryAgain,
  checkWindow,
  isReminderCadence,
  minutesOf,
  notificationsNoticeOf,
  reminderCadences,
  serializeBrowserZoneCookie,
  windowBar,
  windowOf,
  type NotificationSettings,
  type NotificationsFailure,
  type NotificationsSuccess,
  type SiteNotificationSettings,
} from '$lib/notifications';
import {
  getMyNotificationSettings,
  getSiteNotificationSettings,
  getSiteReminderCadence,
  loadNotifications,
  notificationsAction,
  setSiteNotificationSettings,
  setSiteReminderCadence,
  updateMyNotificationSettings,
} from '$lib/server/notifications';
import { handOverTimeZone, loadShell, timeZoneCookieName, timeZoneHandOverCookieName } from '$lib/server/shell';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: 'user-1', name: 'Simon Novak' } } } };
const otherLocals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-2', idToken: { sub: 'user-2', name: 'Ana Novak' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home garden', role: 'Owner' };

const fresh: NotificationSettings = { window: { from: '07:00', to: '22:00' }, timeZoneConfirmed: false };
const detected: NotificationSettings = { ...fresh, timeZone: 'Europe/Zurich' };
const confirmed: NotificationSettings = { window: { from: '06:30', to: '21:00' }, timeZone: 'Europe/Vienna', timeZoneConfirmed: true };
const forSite: SiteNotificationSettings = { muted: false, siteReminderCadence: 'daily' };

const mePath = '/me/notification-settings';
const sitePath = `/sites/${siteId}/notification-settings`;
const cadencePath = `/sites/${siteId}/reminder-cadence`;

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

async function redirectOf(action: () => unknown): Promise<{ status: number; location: string }> {
  try {
    await action();
  } catch (error) {
    if (isRedirect(error)) {
      return { status: error.status, location: error.location };
    }
    throw error;
  }
  throw new Error('Expected a redirect.');
}

function formRequest(fields: Readonly<Record<string, string>>): Request {
  return new Request('http://localhost/settings/notifications', { method: 'POST', body: new URLSearchParams(fields) });
}

async function failed(result: Promise<NotificationsSuccess | ActionFailure<NotificationsFailure>>): Promise<NotificationsFailure & { status: number }> {
  const value = await result;
  if ('done' in value) {
    throw new Error('Expected a failure.');
  }
  expect(isActionFailure(value)).toBe(true);
  return { ...value.data, status: value.status };
}

/** What the fake Server saw, one line per request. */
function lines(fake: { seen: readonly { method: string; path: string; body: string }[] }): string[] {
  return fake.seen.map((seen) => `${seen.method} ${seen.path} ${seen.body}`.trim());
}

describe('Notification settings Server calls (AD-14)', () => {
  test('the six calls send the token, the method, the path and the body of the contract', async () => {
    const fake = fakeServer((request) => {
      const path = new URL(request.url).pathname;
      return jsonResponse(200, path === mePath ? fresh : path === sitePath ? forSite : { cadence: 'daily' });
    });
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await getMyNotificationSettings(locals, dependencies)).toEqual({ ok: fresh });
    expect(await updateMyNotificationSettings(locals, { window: { from: '06:30' } }, dependencies)).toEqual({ ok: fresh });
    expect(await getSiteNotificationSettings(locals, siteId, dependencies)).toEqual({ ok: forSite });
    expect(await setSiteNotificationSettings(locals, siteId, { muted: true, reminderCadence: 'every2Days' }, dependencies)).toEqual({ ok: forSite });
    expect(await getSiteReminderCadence(locals, siteId, dependencies)).toEqual({ ok: { cadence: 'daily' } });
    expect(await setSiteReminderCadence(locals, siteId, 'every2Days', dependencies)).toEqual({ ok: { cadence: 'daily' } });
    expect(lines(fake)).toEqual([
      `GET ${mePath}`,
      `PATCH ${mePath} {"window":{"from":"06:30"}}`,
      `GET ${sitePath}`,
      `PUT ${sitePath} {"muted":true,"reminderCadence":"every2Days"}`,
      `GET ${cadencePath}`,
      `PUT ${cadencePath} {"cadence":"every2Days"}`,
    ]);
    expect(fake.seen.every((seen) => seen.authorization === 'Bearer access-token-1')).toBe(true);
  });

  test('a 503 reminder-cadence-not-delivered is unavailable: a save that failed and can be repeated', async () => {
    const fake = fakeServer(() => problemResponse(503, 'reminder-cadence-not-delivered'));
    expect(await setSiteReminderCadence(locals, siteId, 'every2Days', { serverUrl, fetch: fake.fetch })).toEqual({ error: 'unavailable' });
  });
});

describe('Notification Window rules the shell only checks before sending', () => {
  test('UX-DR47 times are HH:mm on a 24 h clock', () => {
    expect(minutesOf('07:00')).toBe(420);
    expect(minutesOf('23:59')).toBe(1439);
    for (const bad of ['7:00', '24:00', '07:60', '', '07:00:00']) {
      expect(minutesOf(bad), bad).toBeNull();
    }
  });

  test('UX-DR47 a start alone is a window that ends at 22:00; the start must be before the end', () => {
    expect(windowOf('06:30', '')).toEqual({ from: '06:30', to: '22:00' });
    expect(windowOf('06:30', '21:00')).toEqual({ from: '06:30', to: '21:00' });
    expect(checkWindow('07:00', '22:00')).toBeNull();
    expect(checkWindow('', '22:00')).toBe('fromInvalid');
    expect(checkWindow('07:00', '25:00')).toBe('toInvalid');
    expect(checkWindow('09:00', '09:00')).toBe('notBefore');
    expect(checkWindow('22:30', '')).toBe('notBefore');
  });

  test('UX-DR47 the 24 h bar places the window in percent of the day', () => {
    expect(windowBar({ from: '06:00', to: '18:00' })).toEqual({ start: 25, length: 50 });
    expect(windowBar({ from: '00:00', to: '23:59' }).start).toBe(0);
  });

  test('UX-DR50 the cadences are daily and every 2 days; there is no never', () => {
    expect(reminderCadences).toEqual(['daily', 'every2Days']);
    expect(isReminderCadence('daily')).toBe(true);
    expect(isReminderCadence('never')).toBe(false);
    expect(isReminderCadence('site')).toBe(false);
  });
});

describe('Hand-over of the time zone through the web app (DW-23)', () => {
  function serverWith(settings: NotificationSettings, patch: (body: Record<string, unknown>) => Response | Promise<Response> = (body) => jsonResponse(200, patched(settings, body))) {
    return fakeServer(async (request) => (request.method === 'GET' ? jsonResponse(200, settings) : patch((await request.json()) as Record<string, unknown>)));
  }

  function patched(settings: NotificationSettings, body: Record<string, unknown>): NotificationSettings {
    if (typeof body.timeZone === 'string') {
      return { ...settings, timeZone: body.timeZone, timeZoneConfirmed: true };
    }
    return typeof body.detectedTimeZone === 'string' && !settings.timeZoneConfirmed ? { ...settings, timeZone: body.detectedTimeZone } : settings;
  }

  test('UX-DR48 a zone confirmed on this browser goes to an unconfirmed Server as the choice, once', async () => {
    const fake = serverWith(fresh);
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich', [browserZoneCookieName]: 'America/New_York' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(lines(fake)).toEqual([`GET ${mePath}`, `PATCH ${mePath} {"timeZone":"Europe/Zurich"}`]);
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Zurich');
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(true);
    const marker = cookies.writes.find((write) => write.name === timeZoneHandOverCookieName);
    // A session cookie: it ends with the browser session.
    expect(marker?.options).toMatchObject({ path: '/', httpOnly: true, sameSite: 'lax' });
    expect(marker?.options.maxAge).toBeUndefined();

    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(fake.seen).toHaveLength(2);
  });

  test('UX-DR48 a zone the User chose on the Server always wins: the browser copy follows it and nothing is sent', async () => {
    const fake = serverWith(confirmed);
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich', [browserZoneCookieName]: 'America/New_York' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(lines(fake)).toEqual([`GET ${mePath}`]);
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Vienna');
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(true);
  });

  test('UX-DR48 with no stored choice the browser zone goes as the detected zone, and stays unconfirmed', async () => {
    const fake = serverWith(fresh);
    const cookies = new FakeCookies({ [browserZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(lines(fake)).toEqual([`GET ${mePath}`, `PATCH ${mePath} {"detectedTimeZone":"Europe/Zurich"}`]);
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(true);
  });

  test('the detected zone the Server already holds is not sent again', async () => {
    const fake = serverWith(detected);
    const cookies = new FakeCookies({ [browserZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(lines(fake)).toEqual([`GET ${mePath}`]);
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(true);
  });

  test('with nothing to hand over yet the hand-over stays open for the next load', async () => {
    const fake = serverWith(fresh);
    const cookies = new FakeCookies();
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(lines(fake)).toEqual([`GET ${mePath}`]);
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(false);
  });

  test('a send that fails keeps the copy for the next start: no answer, a 503 or a 500', async () => {
    for (const patch of [() => Promise.reject(fetchFailed('ECONNREFUSED')), () => problemResponse(503, 'unavailable'), () => problemResponse(500, 'internal')]) {
      const fake = serverWith(fresh, patch);
      const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
      await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
      expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Zurich');
      expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(false);
    }
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) });
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Zurich');
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(false);
  });

  test('a zone the Server refuses (400) ends the hand-over and drops the copy', async () => {
    const fake = serverWith(fresh, () => problemResponse(400, 'validation'));
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(true);
  });

  test('a copy left by another User of this browser is never sent as this User’s choice', async () => {
    const fake = serverWith(fresh);
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich', [browserZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: fake.fetch });
    const other = serverWith(fresh);
    await handOverTimeZone(otherLocals, cookies, { serverUrl, fetch: other.fetch });
    expect(lines(other)).toEqual([`GET ${mePath}`, `PATCH ${mePath} {"detectedTimeZone":"Europe/Zurich"}`]);
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
  });

  test('a 401 signs out; an answer outside the contract changes nothing', async () => {
    const expired = fakeServer(() => problemResponse(401, 'unauthorized'));
    expect((await redirectOf(() => handOverTimeZone(locals, new FakeCookies(), { serverUrl, fetch: expired.fetch }))).location).toContain('/.oidc/signout');
    const odd = fakeServer(() => jsonResponse(200, { sites: [] }));
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
    await handOverTimeZone(locals, cookies, { serverUrl, fetch: odd.fetch });
    expect(lines(odd)).toEqual([`GET ${mePath}`]);
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Zurich');
    expect(cookies.jar.has(timeZoneHandOverCookieName)).toBe(false);
  });

  test('the shell hands over on a load that listed the Sites, and never when the Sites could not be read', async () => {
    const fake = fakeServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === '/sites') {
        return jsonResponse(200, { sites: [home] });
      }
      return request.method === 'GET' ? jsonResponse(200, fresh) : jsonResponse(200, patched(fresh, (await request.json()) as Record<string, unknown>));
    });
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
    const data = await loadShell(locals, new URL('http://x/alerts'), cookies, { serverUrl, fetch: fake.fetch });
    expect(data.currentSite).toEqual(home);
    expect(lines(fake)).toEqual(['GET /sites', `GET ${mePath}`, `PATCH ${mePath} {"timeZone":"Europe/Zurich"}`]);
    expect(JSON.stringify(data)).not.toContain('Europe/Zurich');

    let calls = 0;
    const down = await loadShell(locals, new URL('http://x/alerts'), new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' }), {
      serverUrl,
      fetch: () => {
        calls++;
        return Promise.reject(fetchFailed('ECONNREFUSED'));
      },
    });
    expect(down.sitesNotice).toBe('unreachable');
    expect(calls).toBe(1);
  });

  test('the page tells the web app the browser zone in a session cookie', () => {
    expect(serializeBrowserZoneCookie('Europe/Zurich', true)).toBe(`${browserZoneCookieName}=Europe%2FZurich; Path=/; SameSite=Lax; Secure`);
    expect(serializeBrowserZoneCookie('UTC', false)).toBe(`${browserZoneCookieName}=UTC; Path=/; SameSite=Lax`);
  });
});

describe('Loading My notifications', () => {
  function answers(mine: Response | (() => Response), site: Response | (() => Response) = jsonResponse(200, forSite)) {
    return fakeServer((request) => {
      const answer = new URL(request.url).pathname === mePath ? mine : site;
      return typeof answer === 'function' ? answer() : answer;
    });
  }

  test('UX-DR72 with a current Site: my settings and my settings for that Site, read together', async () => {
    const fake = answers(jsonResponse(200, confirmed), jsonResponse(200, { ...forSite, muted: true, reminderCadence: 'every2Days' }));
    const cookies = new FakeCookies();
    const data = await loadNotifications(locals, home, cookies, { serverUrl, fetch: fake.fetch });
    expect(data).toEqual({ settings: confirmed, siteSettings: { muted: true, reminderCadence: 'every2Days', siteReminderCadence: 'daily' }, notice: null, siteNotice: null });
    expect(lines(fake).sort()).toEqual([`GET ${mePath}`, `GET ${sitePath}`]);
    // The Server's zone is the one the other pages format with.
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Vienna');
  });

  test('UX-DR72 without a Site only my own settings are read', async () => {
    const fake = answers(jsonResponse(200, fresh));
    const cookies = new FakeCookies();
    const data = await loadNotifications(locals, null, cookies, { serverUrl, fetch: fake.fetch });
    expect(data).toEqual({ settings: fresh, siteSettings: null, notice: null, siteNotice: null });
    expect(lines(fake)).toEqual([`GET ${mePath}`]);
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
  });

  test('a failed read is a notice in place of the controls; a 401 signs out', async () => {
    const refused = { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) };
    expect(await loadNotifications(locals, home, new FakeCookies(), refused)).toEqual({ settings: null, siteSettings: null, notice: 'unreachable', siteNotice: null });
    const untrusted = { serverUrl, fetch: () => Promise.reject(fetchFailed('CERT_HAS_EXPIRED')) };
    expect((await loadNotifications(locals, home, new FakeCookies(), untrusted)).notice).toBe('certificate');
    const down = answers(() => problemResponse(500, 'internal'));
    expect((await loadNotifications(locals, home, new FakeCookies(), { serverUrl, fetch: down.fetch })).notice).toBe('unavailable');
    const odd = answers(() => jsonResponse(200, { sites: [] }));
    expect((await loadNotifications(locals, home, new FakeCookies(), { serverUrl, fetch: odd.fetch })).notice).toBe('unavailable');

    const siteDown = answers(jsonResponse(200, fresh), () => problemResponse(503, 'unavailable'));
    expect(await loadNotifications(locals, home, new FakeCookies(), { serverUrl, fetch: siteDown.fetch })).toEqual({ settings: fresh, siteSettings: null, notice: null, siteNotice: 'unavailable' });
    const siteGone = answers(() => jsonResponse(200, fresh), () => problemResponse(404, 'site-not-found'));
    expect((await loadNotifications(locals, home, new FakeCookies(), { serverUrl, fetch: siteGone.fetch })).siteNotice).toBe('siteGone');

    const expired = answers(() => problemResponse(401, 'unauthorized'), () => problemResponse(401, 'unauthorized'));
    expect((await redirectOf(() => loadNotifications(locals, home, new FakeCookies(), { serverUrl, fetch: expired.fetch }))).location).toContain('/.oidc/signout');
  });

  test('each notice has its copy; only a failed read offers Try again', () => {
    expect(notificationsNoticeOf('unreachable', 'mine')).toEqual({ message: 'notice.unreachable', tryAgain: true });
    expect(notificationsNoticeOf('unavailable', 'mine')).toEqual({ message: 'notifications.unavailable', tryAgain: true });
    expect(notificationsNoticeOf('unavailable', 'site')).toEqual({ message: 'notifications.siteUnavailable', tryAgain: true });
    expect(notificationsNoticeOf('certificate', 'site')).toEqual({ message: 'notice.certificate', tryAgain: false });
    expect(notificationsNoticeOf('siteGone', 'site')).toEqual({ message: 'notifications.notice.siteGone', tryAgain: false });
    expect(notificationsNoticeOf(null, 'mine')).toBeNull();
  });
});

describe('My notifications actions', () => {
  const siteFields = { siteId, siteName: 'Home garden' };

  test('UX-DR47 Save sends the window; a start alone leaves the end to the Server (22:00)', async () => {
    const fake = fakeServer(() => jsonResponse(200, fresh));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await notificationsAction('saveWindow', locals, formRequest({ from: '06:30', to: '21:00' }), new FakeCookies(), dependencies)).toEqual({ action: 'saveWindow', done: true });
    expect(await notificationsAction('saveWindow', locals, formRequest({ from: '06:30', to: '' }), new FakeCookies(), dependencies)).toEqual({ action: 'saveWindow', done: true });
    expect(lines(fake)).toEqual([`PATCH ${mePath} {"window":{"from":"06:30","to":"21:00"}}`, `PATCH ${mePath} {"window":{"from":"06:30"}}`]);
  });

  test('UX-DR47 a window the Server would refuse stays on the fields and nothing is sent', async () => {
    const fake = fakeServer(() => jsonResponse(200, fresh));
    for (const [from, to, windowError] of [
      ['', '22:00', 'fromInvalid'],
      ['09:00', '09:00', 'notBefore'],
      ['22:30', '', 'notBefore'],
    ] as const) {
      const failure = await failed(notificationsAction('saveWindow', locals, formRequest({ from, to }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
      expect(failure).toMatchObject({ status: 400, action: 'saveWindow', windowError, notice: null, fields: { from, to } });
    }
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR48 Confirm or a pick sends the zone as my choice, and the other pages format with it', async () => {
    const fake = fakeServer(() => jsonResponse(200, { ...fresh, timeZone: 'Europe/Vienna', timeZoneConfirmed: true }));
    const cookies = new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' });
    expect(await notificationsAction('chooseTimeZone', locals, formRequest({ timeZone: 'Europe/Vienna' }), cookies, { serverUrl, fetch: fake.fetch })).toEqual({
      action: 'chooseTimeZone',
      done: true,
    });
    expect(lines(fake)).toEqual([`PATCH ${mePath} {"timeZone":"Europe/Vienna"}`]);
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Vienna');
  });

  test('UX-DR48 no zone is not a choice; a zone the Server does not know says so', async () => {
    const fake = fakeServer(() => problemResponse(400, 'validation'));
    const none = await failed(notificationsAction('chooseTimeZone', locals, formRequest({ timeZone: '' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(none).toMatchObject({ status: 400, notice: 'invalidTimeZone' });
    expect(fake.seen).toEqual([]);
    const cookies = new FakeCookies();
    const unknown = await failed(notificationsAction('chooseTimeZone', locals, formRequest({ timeZone: 'Mars/Olympus' }), cookies, { serverUrl, fetch: fake.fetch }));
    expect(unknown).toMatchObject({ status: 400, notice: 'invalidTimeZone', fields: { timeZone: 'Mars/Olympus' } });
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
  });

  test('UX-DR49 the mute switch sends the new mute with my current cadence', async () => {
    const fake = fakeServer(() => jsonResponse(200, forSite));
    const dependencies = { serverUrl, fetch: fake.fetch };
    await notificationsAction('setMute', locals, formRequest({ ...siteFields, muted: 'on', reminderCadence: 'every2Days' }), new FakeCookies(), dependencies);
    await notificationsAction('setMute', locals, formRequest({ ...siteFields, reminderCadence: 'site' }), new FakeCookies(), dependencies);
    expect(lines(fake)).toEqual([`PUT ${sitePath} {"muted":true,"reminderCadence":"every2Days"}`, `PUT ${sitePath} {"muted":false}`]);
  });

  test('UX-DR50 my cadence sends the choice with my current mute; Use Site setting sends no cadence', async () => {
    const fake = fakeServer(() => jsonResponse(200, forSite));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await notificationsAction('setCadence', locals, formRequest({ ...siteFields, muted: 'on', reminderCadence: 'daily' }), new FakeCookies(), dependencies)).toEqual({
      action: 'setCadence',
      done: true,
    });
    await notificationsAction('setCadence', locals, formRequest({ ...siteFields, reminderCadence: 'site' }), new FakeCookies(), dependencies);
    expect(lines(fake)).toEqual([`PUT ${sitePath} {"muted":true,"reminderCadence":"daily"}`, `PUT ${sitePath} {"muted":false}`]);
    const never = await failed(notificationsAction('setCadence', locals, formRequest({ ...siteFields, reminderCadence: 'never' }), new FakeCookies(), dependencies));
    expect(never).toMatchObject({ status: 400, notice: 'unexpected' });
    expect(fake.seen).toHaveLength(2);
  });

  test.each([
    ['saveWindow', { from: '06:30', to: '21:00' }],
    ['chooseTimeZone', { timeZone: 'Europe/Vienna' }],
    ['setMute', { ...siteFields, muted: 'on', reminderCadence: 'site' }],
    ['setCadence', { ...siteFields, reminderCadence: 'every2Days' }],
  ] as const)('%s: an unreachable Server or a 503 is not saved and keeps the attempt for Try again', async (action, fields) => {
    const refused = await failed(notificationsAction(action, locals, formRequest(fields), new FakeCookies(), { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) }));
    expect(refused).toMatchObject({ status: 503, action, notice: 'notSaved', windowError: null, fields });
    const down = fakeServer(() => problemResponse(503, 'unavailable'));
    expect((await failed(notificationsAction(action, locals, formRequest(fields), new FakeCookies(), { serverUrl, fetch: down.fetch }))).notice).toBe('notSaved');
    const broken = fakeServer(() => problemResponse(500, 'internal'));
    expect((await failed(notificationsAction(action, locals, formRequest(fields), new FakeCookies(), { serverUrl, fetch: broken.fetch }))).notice).toBe('unexpected');
    const untrusted = { serverUrl, fetch: () => Promise.reject(fetchFailed('CERT_HAS_EXPIRED')) };
    expect((await failed(notificationsAction(action, locals, formRequest(fields), new FakeCookies(), untrusted))).notice).toBe('certificate');
    const expired = fakeServer(() => problemResponse(401, 'unauthorized'));
    expect((await redirectOf(() => notificationsAction(action, locals, formRequest(fields), new FakeCookies(), { serverUrl, fetch: expired.fetch }))).location).toContain('/.oidc/signout');
  });

  test('a window the Server refuses, and a Site that is no longer mine, each say so and name the Site', async () => {
    const invalid = fakeServer(() => problemResponse(400, 'validation'));
    expect((await failed(notificationsAction('saveWindow', locals, formRequest({ from: '06:30', to: '21:00' }), new FakeCookies(), { serverUrl, fetch: invalid.fetch }))).notice).toBe('invalidWindow');
    for (const [status, slug] of [
      [403, 'forbidden'],
      [404, 'site-not-found'],
    ] as const) {
      const fake = fakeServer(() => problemResponse(status, slug));
      const failure = await failed(notificationsAction('setMute', locals, formRequest({ ...siteFields, muted: 'on' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
      expect(failure).toMatchObject({ status: 404, notice: 'siteGone', siteName: 'Home garden' });
    }
  });

  test('only a save the Server never judged offers Try again', () => {
    expect(canTryAgain('notSaved')).toBe(true);
    expect(canTryAgain('unexpected')).toBe(true);
    for (const notice of ['certificate', 'invalidWindow', 'invalidTimeZone', 'siteGone'] as const) {
      expect(canTryAgain(notice)).toBe(false);
    }
  });
});

function page(site: Site | null, settings: NotificationSettings | null, siteSettings: SiteNotificationSettings | null, form: NotificationsFailure | null = null, extra: Record<string, unknown> = {}): string {
  const data = {
    user: { displayName: 'Simon', initials: 'S' },
    theme: 'system',
    sites: site === null ? [] : [site],
    currentSite: site,
    sitesNotice: null,
    sitesStale: null,
    settings,
    siteSettings,
    notice: null,
    siteNotice: null,
    ...extra,
  };
  return render(NotificationsPage, { props: { data, form, params: {} } as never }).body;
}

function failure(fields: Partial<NotificationsFailure>): NotificationsFailure {
  return { action: 'saveWindow', notice: null, windowError: null, fields: {}, siteName: 'Home garden', ...fields };
}

describe('My notifications surface', () => {
  test.each(['Owner', 'Administrator', 'Member'] as const)('UX-DR72 a %s has the window, the time zone, the mute switch and their own cadence, and nothing else', (role) => {
    const body = page({ ...home, role }, fresh, forSite);
    expect(body).toMatch(/<h1[^>]*>My notifications<\/h1>/u);
    expect(body).toContain('href="/settings"');
    expect(body).toMatch(/<input[^>]*id="cf-window-from"[^>]*type="time"[^>]*value="07:00"/u);
    expect(body).toMatch(/<input[^>]*id="cf-window-to"[^>]*type="time"[^>]*value="22:00"/u);
    expect(text(body)).toContain('07:00 to 22:00');
    expect(text(body)).toContain('Time zone');
    expect(text(body)).toContain('Mute Home garden');
    expect(text(body)).toContain('My Reminder cadence');
    for (const action of ['saveWindow', 'chooseTimeZone', 'setMute', 'setCadence']) {
      expect(body, action).toContain(`action="?/${action}"`);
    }
    // Stories 6.5 and 6.6 own these.
    expect(text(body)).not.toMatch(/Browser notifications|Notifications are off|Never/u);
  });

  test('UX-DR72 without a Site only the window and the time zone show', () => {
    const body = page(null, fresh, null);
    expect(body).toContain('id="cf-window-from"');
    expect(text(body)).toContain('Time zone');
    expect(body).not.toContain('role="switch"');
    expect(text(body)).not.toMatch(/Mute|Reminder cadence/u);
    expect(body).not.toContain('action="?/setMute"');
    expect(body).not.toContain('action="?/setCadence"');
  });

  test('UX-DR47 the window control: two time fields, the range in large type, Save, and a helper naming its own start', () => {
    const body = page(home, confirmed, forSite);
    expect(body).toMatch(/<label[^>]*for="cf-window-from"[^>]*>From<\/label>/u);
    expect(body).toMatch(/<label[^>]*for="cf-window-to"[^>]*>To<\/label>/u);
    expect(text(body)).toContain('06:30 to 21:00');
    expect(text(body)).toContain('Outside this window, anything waits for one summary at 06:30.');
    expect(text(body)).not.toContain('summary at 07:00');
    expect(body).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Save<\/span>/u);
  });

  test('UX-DR47 the 24 h bar is decorative: hidden from screen readers, with no control in it', () => {
    const { body } = render(NotificationWindow, { props: { window: { from: '06:00', to: '18:00' } } });
    const bar = /<div class="cf-window__bar[^"]*"[^>]*aria-hidden="true"[^>]*>([\s\S]*?)<\/div>(?:\s|<!--[^>]*-->)*<p class="cf-window__range/u.exec(body);
    expect(bar).not.toBeNull();
    expect(bar?.[1]).not.toMatch(/<(?:input|button|a)\b/u);
    expect(bar?.[1]).toContain('margin-inline-start: 25%');
    expect(bar?.[1]).toContain('inline-size: 50%');
  });

  test('UX-DR47 a window that cannot be sent names the reason under its field and keeps what was typed', () => {
    const body = page(home, fresh, forSite, failure({ windowError: 'notBefore', fields: { from: '09:00', to: '08:00' } }));
    expect(body).toMatch(/<input[^>]*id="cf-window-from"[^>]*value="09:00"/u);
    expect(body).toMatch(/<input[^>]*id="cf-window-to"[^>]*aria-invalid="true"/u);
    expect(text(body)).toContain('The start must be before the end.');
    // The preview stays on the window in force.
    expect(text(body)).toContain('07:00 to 22:00');
  });

  test('UX-DR48 an unconfirmed zone is proposed for Confirm or Change, in the dashed panel', () => {
    const body = page(home, detected, forSite);
    expect(text(body)).toContain('Is your time zone Europe/Zurich?');
    expect(text(body)).toMatch(/Confirm\s+Change/u);
    expect(text(body)).toContain('Used for your Notification Window.');
    expect(text(body)).not.toContain('on this browser');
    expect(body).toMatch(/<fieldset class="cf-time-zone/u);
  });

  test('UX-DR48 a zone I chose is stated, with Change and no Confirm; it is never replaced by a proposal', () => {
    const body = page(home, confirmed, forSite);
    expect(text(body)).toContain('Your time zone is Europe/Vienna.');
    expect(text(body)).not.toContain('Is your time zone');
    expect(body).not.toMatch(/>Confirm</u);
    expect(text(body)).toContain('Change');
    const { body: panel } = render(TimeZonePanel, { props: { detected: 'America/New_York', chosen: 'Europe/Vienna', zones: [] } });
    expect(text(panel)).toContain('Your time zone is Europe/Vienna.');
  });

  test('UX-DR49 the mute switch is a native checkbox with switch semantics, labelled Mute <Site>, and says it affects only me', () => {
    const off = page(home, fresh, forSite);
    const input = /<input[^>]*role="switch"[^>]*>/u.exec(off)?.[0] ?? '';
    expect(input).toContain('type="checkbox"');
    expect(input).toContain('name="muted"');
    expect(input).not.toContain('checked');
    expect(off).toMatch(/<label class="cf-toggle[^"]*"[^>]*>[\s\S]*Mute Home garden[\s\S]*<\/label>/u);
    expect(text(off)).toContain('Only you stop getting notifications from Home garden.');
    const on = page(home, fresh, { ...forSite, muted: true });
    expect(/<input[^>]*role="switch"[^>]*>/u.exec(on)?.[0]).toContain('checked');
  });

  test('UX-DR49 the Toggle is one 44 px target and shows its state in words, not by colour alone', () => {
    const { body } = render(Toggle, { props: { id: 'cf-x', name: 'x', label: 'Mute Home garden', checked: true } });
    expect(body).toMatch(/<input[^>]*id="cf-x"[^>]*>/u);
    expect(text(body)).toContain(t('toggle.on'));
    expect(text(render(Toggle, { props: { id: 'cf-x', name: 'x', label: 'Mute Home garden', checked: false } }).body)).toContain(t('toggle.off'));
  });

  test('UX-DR50 my cadence offers Use Site setting, Daily and Every 2 days, names the Site setting, and has no Never', () => {
    const body = page(home, fresh, { muted: true, siteReminderCadence: 'every2Days' });
    const group = /<form[^>]*action="\?\/setCadence"[\s\S]*?<\/form>/u.exec(body)?.[0] ?? '';
    const labels = [...group.matchAll(/<button[^>]*name="reminderCadence"[^>]*value="([^"]*)"[^>]*aria-pressed="([^"]*)"/gu)].map((match) => `${match[1] ?? ''}:${match[2] ?? ''}`);
    expect(labels).toEqual(['site:true', 'daily:false', 'every2Days:false']);
    expect(text(group)).toMatch(/Use Site setting\s+Daily\s+Every 2 days/u);
    expect(text(group)).toContain('Site setting: Every 2 days');
    expect(text(group)).not.toContain('Never');
    // Each control carries the other current value, as the Server replaces both.
    expect(group).toMatch(/<input[^>]*type="hidden"[^>]*name="muted"[^>]*value="on"/u);
    const own = page(home, fresh, { muted: false, reminderCadence: 'daily', siteReminderCadence: 'every2Days' });
    expect(own).toMatch(/<button[^>]*value="daily"[^>]*aria-pressed="true"/u);
    expect(/<form[^>]*action="\?\/setMute"[\s\S]*?<\/form>/u.exec(own)?.[0]).toMatch(/<input[^>]*type="hidden"[^>]*name="reminderCadence"[^>]*value="daily"/u);
  });

  test('UX-DR72 a save that failed shows the Server’s values again and a notice with Try again that repeats the change', () => {
    const body = page(home, fresh, forSite, failure({ action: 'setMute', notice: 'notSaved', fields: { siteId, siteName: 'Home garden', muted: 'on', reminderCadence: 'site' } }));
    expect(/<input[^>]*role="switch"[^>]*>/u.exec(body)?.[0]).not.toContain('checked');
    const retry = /<form[^>]*id="cf-notifications-retry"[\s\S]*?<\/form>/u.exec(body)?.[0] ?? '';
    expect(retry).toContain('action="?/setMute"');
    expect(retry).toMatch(/<input[^>]*type="hidden"[^>]*name="muted"[^>]*value="on"/u);
    expect(text(retry)).toContain('Not saved: your Server could not be reached. Nothing was changed.');
    expect(retry).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Try again<\/span>/u);

    const window = page(home, fresh, forSite, failure({ action: 'saveWindow', notice: 'notSaved', fields: { from: '06:30', to: '21:00' } }));
    expect(window).toMatch(/<input[^>]*id="cf-window-from"[^>]*value="07:00"/u);

    const gone = page(home, fresh, forSite, failure({ action: 'setCadence', notice: 'siteGone' }));
    expect(text(gone)).toContain('Home garden is no longer one of your Sites. Nothing was changed.');
    expect(text(gone)).not.toContain('Try again');
  });

  test('UX-DR72 settings that could not be read are a notice with Try again, and no control', () => {
    const body = page(home, null, null, null, { notice: 'unavailable' });
    expect(text(body)).toContain("Your Server couldn't read your notification settings. Nothing was changed.");
    expect(text(body)).toContain('Try again');
    expect(body).not.toMatch(/<input|role="switch"/u);
    const siteOnly = page(home, fresh, null, null, { siteNotice: 'unavailable' });
    expect(siteOnly).toContain('id="cf-window-from"');
    expect(text(siteOnly)).toContain("Your Server couldn't read your settings for Home garden. Nothing was changed.");
    expect(siteOnly).not.toContain('role="switch"');
  });

  test('UX-DR72 the Settings index lists My notifications above Site settings', () => {
    const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null };
    const body = render(SettingsPage, { props: { data, params: {} } as never }).body;
    const mine = body.indexOf('href="/settings/notifications"');
    expect(mine).toBeGreaterThan(-1);
    expect(mine).toBeLessThan(body.indexOf('href="/settings/site"'));
    expect(text(body)).toContain('My notifications');
  });
});
