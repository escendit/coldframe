/**
 * The web app's call to the Server's Devices list, and the Devices page's data. Server-only, like
 * `sites.ts`: the browser never calls the Server (AD-14).
 */
import type { Cookies } from '@sveltejs/kit';
import type { DeviceListItem, DevicesNotice } from '$lib/devices';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface DevicesDependencies extends SitesDependencies {
  /** The clock the last-seen times are told against; injectable for tests. */
  readonly now?: () => Date;
}

export interface DevicesData {
  /** Every enrolled Device of the Site as the Server listed it; empty when the load failed. */
  readonly devices: readonly DeviceListItem[];
  readonly devicesNotice: DevicesNotice | null;
  /** When the list was loaded, ISO-8601: "today" for the last-seen times. */
  readonly loadedAt: string;
  /** The time zone the user chose on this browser, or null: the page then uses the browser's. */
  readonly timeZone: string | null;
}

/** Every enrolled Device of the Site, with `online` as the Server computed it for this answer. */
export async function listDevices(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<readonly DeviceListItem[]>> {
  const result = await call(locals, dependencies, (client) => client.GET('/sites/{siteId}/devices', { params: { path: { siteId } } }));
  return 'ok' in result ? { ok: result.ok.devices } : result;
}

/**
 * The Devices of the current Site, loaded on every page load. A 401 signs out. Any other failure
 * is a notice and no rows: `online` is never kept past a failed load.
 */
export async function loadDevices(
  locals: Locals,
  site: Site | null,
  cookies: Pick<Cookies, 'get'>,
  dependencies: DevicesDependencies = {},
): Promise<DevicesData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  if (site === null) {
    return { devices: [], devicesNotice: null, ...base };
  }
  const result = await listDevices(locals, site.id, dependencies);
  if ('ok' in result) {
    return { devices: result.ok, devicesNotice: null, ...base };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { devices: [], devicesNotice: result.error === 'certificate' ? 'certificate' : 'unreachable', ...base };
}
