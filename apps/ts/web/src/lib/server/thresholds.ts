/**
 * The web app's calls for the Thresholds page (Story 5.4). Server-only: the browser never calls the
 * Server (AD-14). The page's data is read on every load, with no last-good copy: setting a Threshold is
 * a live act and is never shown from a stale read. The Sensor grain stays the only validator.
 */
import { fail, redirect, type ActionFailure, type Cookies } from '@sveltejs/kit';
import type { Lot, SensorQuantity, SensorThresholds, SetSensorThresholdsRequest } from '@coldframe/api-client';
import type { Site } from '$lib/sites';
import { thresholdsAccessOf } from '$lib/thresholds';
import { isTimeZone } from './create-site';
import { getLot } from './lot-detail';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface ThresholdsDependencies extends SitesDependencies {
  readonly now?: () => Date;
}

/** Why the page has no columns to show. */
export type ThresholdsPageNotice = 'notFound' | 'unreachable' | 'certificate' | 'unavailable';

/** One Sensor of the Lot with its current Reading and the Thresholds in force. */
export interface ThresholdsColumn {
  readonly sensorId: string;
  readonly quantity: SensorQuantity;
  /** The latest converted Reading, or null before one. */
  readonly reading: number | null;
  readonly readingUnit: string | null;
  readonly thresholds: SensorThresholds;
}

export interface ThresholdsData {
  readonly lot: Pick<Lot, 'id' | 'name'> | null;
  readonly columns: readonly ThresholdsColumn[];
  /** Owner or Administrator: the Member's page is read-only. */
  readonly canEdit: boolean;
  readonly notice: ThresholdsPageNotice | null;
  readonly loadedAt: string;
  readonly timeZone: string | null;
}

/** A Sensor's Thresholds in force and the proposed low (Member and up). */
export function getSensorThresholds(locals: Locals, siteId: string, sensorId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<SensorThresholds>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/sensors/{sensorId}/thresholds', { params: { path: { siteId, sensorId } } }));
}

/** Sets the sides given (Administrator and up). */
export function setSensorThresholds(
  locals: Locals,
  siteId: string,
  sensorId: string,
  body: SetSensorThresholdsRequest,
  dependencies: SitesDependencies = {},
): Promise<SitesResult<SensorThresholds>> {
  return call(locals, dependencies, (client) => client.PUT('/sites/{siteId}/sensors/{sensorId}/thresholds', { params: { path: { siteId, sensorId } }, body }));
}

function noticeOfPage(error: SitesError): ThresholdsPageNotice {
  return error === 'notFound' || error === 'unreachable' || error === 'certificate' ? error : 'unavailable';
}

/** The Thresholds page: the Lot, then the Thresholds of each of its Sensors, one read each. A Member may read. */
export async function loadThresholds(locals: Locals, site: Site | null, lotId: string, cookies: Pick<Cookies, 'get'>, dependencies: ThresholdsDependencies = {}): Promise<ThresholdsData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  const empty = { lot: null, columns: [], canEdit: false, ...base };
  if (site === null) {
    return { ...empty, notice: null };
  }
  const canEdit = thresholdsAccessOf(site.role);
  const lot = await getLot(locals, site.id, lotId, dependencies);
  if ('error' in lot) {
    if (lot.error === 'unauthorized') {
      signedOutRedirect();
    }
    return { ...empty, canEdit, notice: noticeOfPage(lot.error) };
  }
  const sensors = (lot.ok.sensors ?? []).filter((sensor) => sensor.sensorId !== undefined);
  const columns: ThresholdsColumn[] = [];
  for (const sensor of sensors) {
    const sensorId = sensor.sensorId ?? '';
    const thresholds = await getSensorThresholds(locals, site.id, sensorId, dependencies);
    if ('error' in thresholds) {
      if (thresholds.error === 'unauthorized') {
        signedOutRedirect();
      }
      return { ...empty, lot: { id: lot.ok.id, name: lot.ok.name }, canEdit, notice: noticeOfPage(thresholds.error) };
    }
    columns.push({ sensorId, quantity: sensor.quantity, reading: sensor.value, readingUnit: sensor.unit, thresholds: thresholds.ok });
  }
  return { lot: { id: lot.ok.id, name: lot.ok.name }, columns, canEdit, notice: null, ...base };
}

/** Why a save did not happen; each has its copy (UX-DR91). */
export type ThresholdsFailure = 'invalid' | 'forbidden' | 'notFound' | 'notSaved' | 'certificate' | 'unexpected';

const statusOfNotice: Readonly<Record<ThresholdsFailure, number>> = {
  invalid: 400,
  forbidden: 403,
  notFound: 404,
  notSaved: 503,
  certificate: 502,
  unexpected: 500,
};

function noticeOf(error: SitesError): ThresholdsFailure {
  switch (error) {
    case 'validation':
      return 'invalid';
    case 'forbidden':
    case 'notFound':
    case 'certificate':
      return error;
    case 'unavailable':
    case 'unreachable':
      return 'notSaved';
    default:
      return 'unexpected';
  }
}

interface Change {
  readonly sensorId: string;
  readonly body: SetSensorThresholdsRequest;
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

function isChange(entry: unknown): entry is Change {
  if (typeof entry !== 'object' || entry === null) {
    return false;
  }
  const { sensorId, body } = entry as { sensorId?: unknown; body?: unknown };
  return typeof sensorId === 'string' && typeof body === 'object' && body !== null;
}

function changesOf(raw: string): Change[] {
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter(isChange) : [];
  } catch {
    return [];
  }
}

/**
 * Saves what changed, one Sensor after the other, from the page's form: `siteId`, `lotId` and `changes`
 * (a JSON list of `{sensorId, body}`). The first refusal stops the save and answers its notice; what was
 * already saved stays saved. A save that went through returns to the Lot.
 */
export async function saveThresholdsAction(locals: Locals, request: Request, dependencies: ThresholdsDependencies = {}): Promise<ActionFailure<{ notice: ThresholdsFailure }>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const lotId = text(form, 'lotId');
  for (const change of changesOf(text(form, 'changes'))) {
    const result = await setSensorThresholds(locals, siteId, change.sensorId, change.body, dependencies);
    if ('error' in result) {
      if (result.error === 'unauthorized') {
        return signedOutRedirect();
      }
      const notice = noticeOf(result.error);
      return fail(statusOfNotice[notice], { notice });
    }
  }
  return redirect(303, `/garden/${lotId}`);
}
