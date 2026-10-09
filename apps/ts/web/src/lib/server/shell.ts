import { redirect, type Cookies } from '@sveltejs/kit';
import type { Site, SitesData } from '$lib/sites';
import type { DisplayUser } from '$lib/user';
import { browserZoneCookieName } from '$lib/notifications';
import { oidcRoutes, signinUrl, timeZoneCookieName, timeZoneHandOverCookieName } from './auth-handle';
import { guardShell } from './guard';
import { forget, isTransportFailure, readThrough, remember, sitesKey, userKeyOf, type LastGoodDependencies, type ReadOutcome } from './last-good';
import { call, listSites, type SitesDependencies, type SitesError } from './sites';
import { getConfig } from './runtime';

/** The Site this browser shows. First-party, httpOnly; holds a Site ID only. */
export const siteCookieName = 'cf_site';

export { timeZoneCookieName, timeZoneHandOverCookieName } from './auth-handle';

export const createSitePath = '/sites/new';

/** The Site overview: the only page of the shell with stale mode (UX-DR79). */
export const gardenPath = '/garden';

const oneYearSeconds = 60 * 60 * 24 * 365;

type Locals = Pick<App.Locals, 'session' | 'sessionEnded'>;

function secureCookies(): boolean {
  try {
    return getConfig().sessionCookieSecure;
  } catch {
    return true;
  }
}

/** Remembers a per-browser choice (current Site, time zone) for a year. */
export function rememberChoice(cookies: Cookies, name: string, value: string): void {
  cookies.set(name, value, { path: '/', httpOnly: true, sameSite: 'lax', secure: secureCookies(), maxAge: oneYearSeconds });
}

/**
 * A 401 from the Server means the session is no longer accepted: sign out locally and land on
 * Sign in with the signed-out notice, the same end as a session that expired (UX-DR93).
 */
export function signedOutRedirect(): never {
  redirect(303, `${oidcRoutes.signout}?redirect_uri=${encodeURIComponent(signinUrl('signed-out'))}`);
}

/** True for an IANA time-zone ID this runtime knows. */
export function isTimeZone(value: string | undefined | null): value is string {
  if (value === undefined || value === null || value === '' || value.length > 64) {
    return false;
  }
  try {
    new Intl.DateTimeFormat('en', { timeZone: value });
    return true;
  } catch {
    return false;
  }
}

/** A send the Server never judged: the device copy is kept and the hand-over runs again on the next load. */
function notAnswered(error: SitesError): boolean {
  return error === 'unreachable' || error === 'certificate' || error === 'unavailable' || error === 'unexpected';
}

/**
 * Hands this browser's time zone to the Server, once per browser session and User (DW-23). The
 * Server's settings are read; while the User has chosen no zone there, the zone confirmed on this
 * browser (`cf_time_zone`, from Create Site) is sent as their choice, else the browser's own zone as
 * the detected one. Once the Server answered, the hand-over is done for the session and
 * `cf_time_zone` follows the Server: the chosen zone, or no cookie while none is chosen. A send
 * without an answer (or a 5xx) keeps the copy for the next load. A 401 signs out.
 */
export async function handOverTimeZone(locals: Pick<App.Locals, 'session'>, cookies: Cookies, dependencies: SitesDependencies = {}): Promise<void> {
  const user = userKeyOf(locals) ?? '-';
  const marker = cookies.get(timeZoneHandOverCookieName);
  if (marker === user) {
    return;
  }
  const stored = cookies.get(timeZoneCookieName);
  // A marker of another User means the copy was theirs: it is never sent as this User's choice.
  const copy = marker === undefined && isTimeZone(stored) ? stored : null;
  const browser = cookies.get(browserZoneCookieName);
  const detected = isTimeZone(browser) ? browser : null;

  const read = await call(locals, dependencies, (client) => client.GET('/me/notification-settings'));
  if ('error' in read) {
    if (read.error === 'unauthorized') {
      signedOutRedirect();
    }
    return;
  }
  let settings = read.ok;
  if (typeof settings.timeZoneConfirmed !== 'boolean') {
    return;
  }
  if (!settings.timeZoneConfirmed) {
    if (copy === null && detected === null) {
      // Nothing to hand over yet: the page has not told us the browser's zone. The next load asks again.
      return;
    }
    const body = copy !== null ? { timeZone: copy } : detected !== null && detected !== settings.timeZone ? { detectedTimeZone: detected } : null;
    if (body !== null) {
      const sent = await call(locals, dependencies, (client) => client.PATCH('/me/notification-settings', { body }));
      if ('ok' in sent) {
        settings = sent.ok;
      } else if (sent.error === 'unauthorized') {
        signedOutRedirect();
      } else if (notAnswered(sent.error)) {
        return;
      }
    }
  }
  cookies.set(timeZoneHandOverCookieName, user, { path: '/', httpOnly: true, sameSite: 'lax', secure: secureCookies() });
  followServerTimeZone(cookies, settings);
}

/** Keeps `cf_time_zone` equal to the zone the User chose on the Server; no cookie while none is chosen. */
export function followServerTimeZone(cookies: Cookies, settings: { readonly timeZone?: string; readonly timeZoneConfirmed?: boolean }): void {
  const stored = cookies.get(timeZoneCookieName);
  if (settings.timeZoneConfirmed === true && typeof settings.timeZone === 'string' && settings.timeZone !== '') {
    if (stored !== settings.timeZone) {
      rememberChoice(cookies, timeZoneCookieName, settings.timeZone);
    }
  } else if (stored !== undefined) {
    cookies.delete(timeZoneCookieName, { path: '/' });
  }
}

/** The listed Site with this ID, else the first one: a stale or unknown choice falls back. */
export function pickCurrentSite(sites: readonly Site[], chosen: string | undefined): Site | null {
  return sites.find((site) => site.id === chosen) ?? sites[0] ?? null;
}

/**
 * The caller's Sites. On the Site overview a read that fails twice falls back to the last good
 * Sites; every other page reads once and keeps its notice, but a success is kept from any page.
 */
async function readSites(locals: Locals, overview: boolean, dependencies: LastGoodDependencies): Promise<ReadOutcome<readonly Site[]>> {
  const user = userKeyOf(locals);
  const key = user === null ? null : sitesKey(user);
  if (overview) {
    return readThrough(key, () => listSites(locals, dependencies), dependencies);
  }
  const result = await listSites(locals, dependencies);
  if ('ok' in result) {
    remember(key, result.ok, dependencies);
    return { ok: result.ok, fetchedAt: '', stale: false };
  }
  if (!isTransportFailure(result.error) && result.error !== 'certificate') {
    forget(key, dependencies);
  }
  return result;
}

/**
 * Loads the shell: the guard, then the caller's Sites from the Server. No Membership → Create
 * Site. `?site=` switches the current Site for this browser and is dropped from the URL. On the
 * Site overview and Lot detail, Sites the Server could not be asked for come from the last good answer, with
 * `sitesStale` saying when that was. A load that listed the Sites also hands over the time zone, once per
 * browser session.
 */
export async function loadShell(
  locals: Locals,
  url: URL,
  cookies: Cookies,
  dependencies: LastGoodDependencies = {},
): Promise<{ user: DisplayUser } & SitesData> {
  const { user } = guardShell(locals, url);
  const result = await readSites(locals, url.pathname === gardenPath || url.pathname.startsWith(`${gardenPath}/`), dependencies);
  if ('error' in result) {
    if (result.error === 'unauthorized') {
      signedOutRedirect();
    }
    const sitesNotice = result.error === 'unreachable' || result.error === 'certificate' ? result.error : 'unavailable';
    return { user, sites: [], currentSite: null, sitesNotice, sitesStale: null };
  }
  const sites = result.ok;
  const onCreateSite = url.pathname === createSitePath;
  if (sites.length === 0 && !onCreateSite) {
    redirect(303, createSitePath);
  }

  const requested = url.searchParams.get('site');
  if (requested !== null) {
    if (sites.some((site) => site.id === requested)) {
      rememberChoice(cookies, siteCookieName, requested);
    }
    const target = new URL(url);
    target.searchParams.delete('site');
    redirect(303, `${target.pathname}${target.search}`);
  }

  if (!result.stale) {
    // The Server just answered, so this is the moment to hand the browser's zone over (DW-23).
    await handOverTimeZone(locals, cookies, dependencies);
  }

  return { user, sites, currentSite: pickCurrentSite(sites, cookies.get(siteCookieName)), sitesNotice: null, sitesStale: result.stale ? result.fetchedAt : null };
}
