/**
 * The web app's calls for the Calibrate flow (Story 5.2). Server-only: the browser never calls the
 * Server (AD-14). The page's data is read from the Server on every load, with no last-good copy: a
 * Calibration is a live act and is never shown from a stale read.
 */
import { fail, redirect, type ActionFailure, type Cookies } from '@sveltejs/kit';
import type { CalibrationState, Lot } from '@coldframe/api-client';
import { calibratableSensor, calibrateAccessOf, type CalibrateFailure, type CalibrateNotice, type CalibratePoint, type CalibrateSuccess } from '$lib/calibrate';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { getLot } from './lot-detail';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface CalibrateDependencies extends SitesDependencies {
  /** The clock the save time is taken from; injectable for tests. */
  readonly now?: () => Date;
}

/** Why the page has no flow to show. */
export type CalibratePageNotice = 'notFound' | 'unreachable' | 'certificate' | 'unavailable' | 'noSensor';

export interface CalibrateData {
  readonly lot: Pick<Lot, 'id' | 'name' | 'status' | 'pausedBy' | 'sensors'> | null;
  readonly sensorId: string | null;
  readonly state: CalibrationState | null;
  readonly notice: CalibratePageNotice | null;
  readonly loadedAt: string;
  readonly timeZone: string | null;
}

/** Where a Sensor's Calibration stands, and the recent stored Readings to pick a point from. */
export function getSensorCalibration(locals: Locals, siteId: string, sensorId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<CalibrationState>> {
  return call(locals, dependencies, (client) => client.GET('/sites/{siteId}/sensors/{sensorId}/calibration', { params: { path: { siteId, sensorId } } }));
}

/** Saves one reference point, named by the `reading_seq` of a stored Reading. */
export function calibrateSensor(locals: Locals, siteId: string, sensorId: string, point: CalibratePoint, readingSeq: number, dependencies: SitesDependencies = {}) {
  const reference = { readingSeq };
  return call(locals, dependencies, (client) =>
    client.POST('/sites/{siteId}/sensors/{sensorId}/calibration', {
      params: { path: { siteId, sensorId } },
      body: point === 'dry' ? { dry: reference } : { wet: reference },
    }),
  );
}

function noticeOfPage(error: SitesError): CalibratePageNotice {
  return error === 'notFound' || error === 'unreachable' || error === 'certificate' ? error : 'unavailable';
}

/**
 * The Calibrate page. A Member is sent back to the Lot (the control is hidden for them, and the Server
 * answers 403 to a direct call). A 401 signs out.
 */
export async function loadCalibrate(
  locals: Locals,
  site: Site | null,
  lotId: string,
  cookies: Pick<Cookies, 'get'>,
  dependencies: CalibrateDependencies = {},
): Promise<CalibrateData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  const empty = { lot: null, sensorId: null, state: null, ...base };
  if (site === null) {
    return { ...empty, notice: null };
  }
  if (!calibrateAccessOf(site.role)) {
    redirect(303, `/garden/${lotId}`);
  }
  const lot = await getLot(locals, site.id, lotId, dependencies);
  if ('error' in lot) {
    if (lot.error === 'unauthorized') {
      signedOutRedirect();
    }
    return { ...empty, notice: noticeOfPage(lot.error) };
  }
  const summary = { id: lot.ok.id, name: lot.ok.name, status: lot.ok.status, pausedBy: lot.ok.pausedBy, sensors: lot.ok.sensors };
  const sensor = calibratableSensor(lot.ok);
  if (sensor === null) {
    return { ...empty, lot: summary, notice: 'noSensor' };
  }
  const state = await getSensorCalibration(locals, site.id, sensor.sensorId, dependencies);
  if ('error' in state) {
    if (state.error === 'unauthorized') {
      signedOutRedirect();
    }
    return { ...empty, lot: summary, sensorId: sensor.sensorId, notice: noticeOfPage(state.error) };
  }
  return { lot: summary, sensorId: sensor.sensorId, state: state.ok, notice: null, ...base };
}

const statusOfNotice: Readonly<Record<CalibrateNotice, number>> = {
  indistinct: 400,
  forbidden: 403,
  notFound: 404,
  notDelivered: 503,
  unavailable: 503,
  unexpected: 500,
  unreachable: 502,
  certificate: 502,
};

function noticeOf(error: SitesError): CalibrateNotice {
  switch (error) {
    case 'validation':
      return 'indistinct';
    case 'forbidden':
    case 'notFound':
    case 'unavailable':
    case 'unreachable':
    case 'certificate':
      return error;
    default:
      return 'unexpected';
  }
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

/**
 * Records one point from the page's form: `siteId`, `sensorId`, `point` (`dry` or `wet`) and the
 * `readingSeq` of the chosen stored Reading. When the Server saved a Calibration but did not confirm it
 * (503), the answer is `notDelivered` with both raw values, so the flow keeps the recorded point.
 */
export async function calibrateAction(
  locals: Locals,
  request: Request,
  dependencies: CalibrateDependencies = {},
): Promise<CalibrateSuccess | ActionFailure<CalibrateFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const sensorId = text(form, 'sensorId');
  const point: CalibratePoint = text(form, 'point') === 'dry' ? 'dry' : 'wet';
  const readingSeq = Number(text(form, 'readingSeq'));
  const savedAt = (dependencies.now?.() ?? new Date()).toISOString();

  const result = await calibrateSensor(locals, siteId, sensorId, point, readingSeq, dependencies);
  if ('ok' in result) {
    const saved = result.ok;
    return saved.calibrated && saved.dry !== undefined && saved.wet !== undefined && point === 'wet'
      ? { done: true, point, calibrated: true, dryRaw: saved.dry.rawValue, wetRaw: saved.wet.rawValue, savedAt }
      : { done: true, point, calibrated: saved.calibrated };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  if (result.error === 'unavailable') {
    // The Server answers 503 when it saved the Calibration but the Node has not confirmed it yet: read it back.
    const state = await getSensorCalibration(locals, siteId, sensorId, dependencies);
    if ('ok' in state && state.ok.calibrated && state.ok.dry !== undefined && state.ok.wet !== undefined) {
      const chosen = state.ok.readings.find((reading) => reading.readingSeq === readingSeq);
      if (chosen === undefined || chosen.rawValue === state.ok.wet.rawValue || chosen.rawValue === state.ok.dry.rawValue) {
        return fail(503, { notice: 'notDelivered', point, dryRaw: state.ok.dry.rawValue, wetRaw: state.ok.wet.rawValue, savedAt });
      }
    }
  }
  const notice = noticeOf(result.error);
  return fail(statusOfNotice[notice], { notice, point });
}
