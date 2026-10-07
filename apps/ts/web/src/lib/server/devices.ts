/**
 * The web app's call to the Server's Devices list, and the Devices page's data. Server-only, like
 * `sites.ts`: the browser never calls the Server (AD-14).
 */
import { fail, type ActionFailure, type Cookies } from '@sveltejs/kit';
import type { Lot } from '@coldframe/api-client';
import { devicesAccessOf, type DeviceAction, type DeviceActionFailure, type DeviceActionNotice, type DeviceActionSuccess, type DeviceListItem, type DevicesNotice } from '$lib/devices';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { listLots } from './lots';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface DevicesDependencies extends SitesDependencies {
  /** The clock the last-seen times are told against; injectable for tests. */
  readonly now?: () => Date;
}

export interface DevicesData {
  /** Every enrolled Device of the Site as the Server listed it; empty when the load failed. */
  readonly devices: readonly DeviceListItem[];
  readonly devicesNotice: DevicesNotice | null;
  /**
   * The Site's Lots in the Server's order, for the Move picker; only loaded for a Role that may move
   * Nodes, and empty when that read fails (the picker then has nothing to offer).
   */
  readonly lots: readonly Lot[];
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
    return { devices: [], devicesNotice: null, lots: [], ...base };
  }
  const result = await listDevices(locals, site.id, dependencies);
  if ('ok' in result) {
    const lots = devicesAccessOf(site.role).canManageNodes && result.ok.some((device) => device.kind === 'node') ? await readLots(locals, site.id, dependencies) : [];
    return { devices: result.ok, devicesNotice: null, lots, ...base };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { devices: [], devicesNotice: result.error === 'certificate' ? 'certificate' : 'unreachable', lots: [], ...base };
}

async function readLots(locals: Locals, siteId: string, dependencies: SitesDependencies): Promise<readonly Lot[]> {
  const lots = await listLots(locals, siteId, dependencies);
  if ('ok' in lots) {
    return lots.ok;
  }
  if (lots.error === 'unauthorized') {
    signedOutRedirect();
  }
  return [];
}

/** Moves a Node to another Lot of the Site (Administrator and up). A Lot that holds a Node is `lotClaimed`. */
export function moveDevice(locals: Locals, siteId: string, deviceId: string, lotId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<unknown>> {
  return call(locals, dependencies, (client) =>
    client.POST('/sites/{siteId}/devices/{deviceId}/move', { params: { path: { siteId, deviceId } }, body: { lotId } }),
  );
}

/** Unassigns a Node from its Lot (Administrator and up). */
export function unassignDevice(locals: Locals, siteId: string, deviceId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<unknown>> {
  return call(locals, dependencies, (client) => client.POST('/sites/{siteId}/devices/{deviceId}/unassign', { params: { path: { siteId, deviceId } } }));
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

function noticeOf(error: Exclude<SitesError, 'unauthorized'>): DeviceActionNotice {
  switch (error) {
    case 'forbidden':
    case 'lotClaimed':
    case 'notFound':
    case 'unreachable':
    case 'certificate':
      return error;
    // A 400 is a form the page did not send, and a 422 or 503 cannot come from these calls.
    case 'validation':
    case 'keyReused':
    case 'unavailable':
    case 'unexpected':
      return 'unexpected';
  }
}

const statusOf: Readonly<Record<DeviceActionNotice, number>> = {
  forbidden: 403,
  lotClaimed: 409,
  notFound: 404,
  unexpected: 502,
  unreachable: 502,
  certificate: 502,
};

/**
 * One Node action of the Devices page. The Server authorizes it by the caller's Role (AD-4), so a
 * stale page of a demoted caller gets a 403 and nothing changes.
 */
export async function deviceAction(
  action: DeviceAction,
  locals: Locals,
  request: Request,
  dependencies: SitesDependencies = {},
): Promise<DeviceActionSuccess | ActionFailure<DeviceActionFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const deviceId = text(form, 'deviceId');
  const result = await (action === 'moveNode'
    ? moveDevice(locals, siteId, deviceId, text(form, 'lotId'), dependencies)
    : unassignDevice(locals, siteId, deviceId, dependencies));

  if ('ok' in result) {
    return { action, deviceId, done: true };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  const notice = noticeOf(result.error);
  return fail(statusOf[notice], { action, deviceId, notice });
}
