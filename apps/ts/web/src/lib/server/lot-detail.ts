/**
 * The web app's calls for Lot detail, and the page's data. Server-only, like `lots.ts`: the
 * browser never calls the Server (AD-14). One read of the Lot (with its Node and latest Readings)
 * and one of its daily history per quantity; the last good answer is kept per user, Site and Lot
 * and served as stale when the Server cannot be reached (UX-DR79).
 */
import type { Cookies } from '@sveltejs/kit';
import type { Lot, LotHistory, SensorQuantity, SensorThresholds } from '@coldframe/api-client';
import { pickerQuantities } from '$lib/lot-detail';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { lotDetailKey, readThrough, recall, userKeyOf, type LastGoodDependencies } from './last-good';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';
import { listDevices } from './devices';
import { getSensorThresholds } from './thresholds';

type Locals = Pick<App.Locals, 'session'>;

/** The Lot as the detail reads it, with the daily history of each quantity its Node has. */
export interface LotDetail {
  readonly lot: Lot;
  readonly histories: Readonly<Partial<Record<SensorQuantity, LotHistory>>>;
  /** The Hub a Hub-silent Lot names, when the Server lists one; otherwise null. */
  readonly hubId: string | null;
  /** The Thresholds of the calibratable soil Sensor, for the chart band and the summary; null when it has none or they could not be read. */
  readonly thresholds: SensorThresholds | null;
}

/** Why the detail has no Lot: the Lot is gone, or the Server could not be read. */
export type LotDetailNotice = 'notFound' | 'unreachable' | 'certificate' | 'unavailable';

export interface LotDetailData {
  readonly detail: LotDetail | null;
  readonly notice: LotDetailNotice | null;
  /** Stale mode: when the data shown was last read from the Server (ISO-8601). Null while live. */
  readonly staleSince: string | null;
  readonly loadedAt: string;
  readonly timeZone: string | null;
}

/** One Lot with its Node and latest Readings. */
export function getLot(locals: Locals, siteId: string, lotId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<Lot>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/lots/{lotId}', { params: { path: { siteId, lotId } } }));
}

/** A page of the Lot's daily history of one quantity: the default window, the last 30 days. */
export function getLotHistory(locals: Locals, siteId: string, lotId: string, quantity: SensorQuantity, dependencies: SitesDependencies = {}): Promise<SitesResult<LotHistory>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/lots/{lotId}/history', { params: { path: { siteId, lotId }, query: { quantity } } }));
}

async function readDetail(locals: Locals, siteId: string, lotId: string, dependencies: SitesDependencies): Promise<SitesResult<LotDetail>> {
  const lot = await getLot(locals, siteId, lotId, dependencies);
  if ('error' in lot) {
    return lot;
  }
  const quantities = lot.ok.node === undefined ? [] : pickerQuantities(lot.ok.sensors);
  const answers = await Promise.all(quantities.map(async (quantity) => [quantity, await getLotHistory(locals, siteId, lotId, quantity, dependencies)] as const));
  const histories: Partial<Record<SensorQuantity, LotHistory>> = {};
  for (const [quantity, answer] of answers) {
    if ('error' in answer) {
      return answer;
    }
    histories[quantity] = answer.ok;
  }
  let hubId: string | null = null;
  if (lot.ok.status === 'unknown' && lot.ok.unknownCause === 'hub') {
    const devices = await listDevices(locals, siteId, dependencies);
    hubId = 'ok' in devices ? (devices.ok.find((device) => device.kind === 'hub')?.id ?? null) : null;
  }
  // The soil Sensor's Thresholds are an addition to the detail: a failed read leaves the detail without them.
  const soil = lot.ok.sensors?.find((sensor) => sensor.quantity === 'soil_moisture' && sensor.calibratable === true && sensor.sensorId !== undefined);
  const thresholds = soil?.sensorId === undefined ? null : await getSensorThresholds(locals, siteId, soil.sensorId, dependencies);
  return { ok: { lot: lot.ok, histories, hubId, thresholds: thresholds !== null && 'ok' in thresholds ? thresholds.ok : null } };
}

function noticeOf(error: SitesError): LotDetailNotice {
  return error === 'notFound' || error === 'certificate' || error === 'unreachable' ? error : 'unavailable';
}

/**
 * Lot detail. A transport failure is retried once; when that fails too the last good detail of
 * this Lot is served as stale. A 404 is a notice and drops what was kept; a 401 signs out.
 */
export async function loadLotDetail(
  locals: Locals,
  site: Site | null,
  lotId: string,
  cookies: Pick<Cookies, 'get'>,
  shellStale: string | null,
  dependencies: LastGoodDependencies = {},
): Promise<LotDetailData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  if (site === null) {
    return { detail: null, notice: null, staleSince: null, ...base };
  }
  const user = userKeyOf(locals);
  const key = user === null ? null : lotDetailKey(user, site.id, lotId);
  if (shellStale !== null) {
    const kept = recall<LotDetail>(key, dependencies);
    return kept === undefined ? { detail: null, notice: 'unreachable', staleSince: shellStale, ...base } : { detail: kept.value, notice: null, staleSince: kept.fetchedAt, ...base };
  }
  const result = await readThrough(key, () => readDetail(locals, site.id, lotId, dependencies), dependencies);
  if ('ok' in result) {
    return { detail: result.ok, notice: null, staleSince: result.stale ? result.fetchedAt : null, ...base };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { detail: null, notice: noticeOf(result.error), staleSince: null, ...base };
}
