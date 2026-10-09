/**
 * The web app's call to the Server's Alerts list, and the Alerts page's data. Server-only, like
 * `sites.ts`: the browser never calls the Server (AD-14).
 */
import type { Cookies } from '@sveltejs/kit';
import type { Alert, AlertsNotice } from '$lib/alerts';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

/** The most Alerts asked for in one page: the contract's maximum. */
export const alertsPageLimit = 200;

/** The most pages one read follows, so a Server that never ends its cursors cannot hold the page. */
export const alertsPageCap = 10;

export interface AlertsDependencies extends SitesDependencies {
  /** The clock the times are told against; injectable for tests. */
  readonly now?: () => Date;
}

export interface AlertsRead {
  /** In the Server's order: open newest first, then closed in the last 7 days newest first. */
  readonly alerts: readonly Alert[];
  /** Every open Alert of the Site, as the last page counted them. */
  readonly openCount: number;
}

export interface AlertsData extends AlertsRead {
  readonly alertsNotice: AlertsNotice | null;
  /** When the list was loaded, ISO-8601: "today" for the times. */
  readonly loadedAt: string;
  /** The time zone the user chose on this browser, or null: the page then uses the browser's. */
  readonly timeZone: string | null;
}

/**
 * The Site's Alerts: every page, following `nextCursor` to the end or to the page cap. A page that
 * fails fails the read. A read that stops at the cap with a cursor still left returns the Alerts of
 * the pages it read, without a sign that more follow: `openCount` still counts every open Alert.
 */
export async function listAlerts(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<AlertsRead>> {
  const alerts: Alert[] = [];
  const seen = new Set<string>();
  let openCount = 0;
  let cursor: string | undefined;
  for (let page = 0; page < alertsPageCap; page += 1) {
    const query = cursor === undefined ? { limit: alertsPageLimit } : { cursor, limit: alertsPageLimit };
    const result = await call(locals, dependencies, (client) => client.GET('/sites/{siteId}/alerts', { params: { path: { siteId }, query } }));
    if (!('ok' in result)) {
      return result;
    }
    for (const alert of result.ok.alerts) {
      if (!seen.has(alert.id)) {
        seen.add(alert.id);
        alerts.push(alert);
      }
    }
    openCount = result.ok.openCount;
    cursor = result.ok.nextCursor;
    if (cursor === undefined) {
      break;
    }
  }
  return { ok: { alerts, openCount } };
}

/**
 * The Alerts of the current Site, loaded on every page load. A 401 signs out. Any other failure is
 * a notice and no rows: there is no stale mode for Alerts.
 */
export async function loadAlerts(locals: Locals, site: Site | null, cookies: Pick<Cookies, 'get'>, dependencies: AlertsDependencies = {}): Promise<AlertsData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  if (site === null) {
    return { alerts: [], openCount: 0, alertsNotice: null, ...base };
  }
  const result = await listAlerts(locals, site.id, dependencies);
  if ('ok' in result) {
    return { ...result.ok, alertsNotice: null, ...base };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { alerts: [], openCount: 0, alertsNotice: result.error === 'certificate' ? 'certificate' : 'unreachable', ...base };
}
