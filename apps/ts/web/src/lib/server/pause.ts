/**
 * The web app's calls to the Server's four pause endpoints (Story 8.1), and the two form actions every
 * pause surface posts to (Story 8.2). Server-only, like `sites.ts`: the browser never calls the Server
 * (AD-14). The Server authorizes by the caller's Role (AD-4) and resolves the end date in the caller's
 * time zone; this sends the calendar date as it was picked.
 */
import { fail, type ActionFailure, type RequestEvent } from '@sveltejs/kit';
import type { DevicePause, SitePause } from '@coldframe/api-client';
import { isCalendarDate, type PauseFailure, type PauseNotice, type PauseScope, type PauseSuccess } from '$lib/pause';
import { signedOutRedirect } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

/** Pauses the whole Site until `until` (a calendar date, `YYYY-MM-DD`), or until someone resumes when it is null. */
export function pauseSite(locals: Locals, siteId: string, until: string | null, dependencies: SitesDependencies = {}): Promise<SitesResult<SitePause>> {
  return call(locals, dependencies, (client) => client.PUT('/sites/{siteId}/pause', { params: { path: { siteId } }, body: until === null ? {} : { until } }));
}

/** Resumes the Site. Its Devices follow; one that is also paused by itself stays paused. */
export function resumeSite(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<SitePause>> {
  return call(locals, dependencies, (client) => client.DELETE('/sites/{siteId}/pause', { params: { path: { siteId } } }));
}

/** Pauses one Device by itself. The Site's own Pause is another source and is left as it is. */
export function pauseDevice(locals: Locals, siteId: string, deviceId: string, until: string | null, dependencies: SitesDependencies = {}): Promise<SitesResult<DevicePause>> {
  return call(locals, dependencies, (client) =>
    client.PUT('/sites/{siteId}/devices/{deviceId}/pause', { params: { path: { siteId, deviceId } }, body: until === null ? {} : { until } }),
  );
}

/** Lifts the Device's own Pause. The answer still lists `site` while the Site is paused. */
export function resumeDevice(locals: Locals, siteId: string, deviceId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<DevicePause>> {
  return call(locals, dependencies, (client) => client.DELETE('/sites/{siteId}/devices/{deviceId}/pause', { params: { path: { siteId, deviceId } } }));
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

const statusOf: Readonly<Record<PauseNotice, number>> = {
  date: 400,
  forbidden: 403,
  notFound: 404,
  unexpected: 502,
  unreachable: 502,
  certificate: 502,
};

function noticeOf(error: Exclude<SitesError, 'unauthorized'>, dated: boolean): PauseNotice {
  switch (error) {
    case 'validation':
      // The only thing a pause request can get wrong is its date; a 400 without one is a form the page did not send.
      return dated ? 'date' : 'unexpected';
    case 'forbidden':
    case 'notFound':
    case 'unreachable':
    case 'certificate':
      return error;
    case 'lotClaimed':
    case 'keyReused':
    case 'unavailable':
    case 'unexpected':
      return 'unexpected';
  }
}

/**
 * Pause or Resume, as a form action: `siteId`, `scope` (`site` or `device`), `deviceId` for a Device,
 * and for a Pause the optional `until` date. Whole Site chosen from a Device pauses the Site and leaves
 * the Device's own source alone. Nothing is kept here: a success makes the page read again, and a
 * refusal says why with what was entered (a 400 is the date; a 403 a Role lost since the page loaded).
 */
export async function pauseAction(action: 'pause' | 'resume', locals: Locals, request: Request, dependencies: SitesDependencies = {}): Promise<PauseSuccess | ActionFailure<PauseFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const requested = text(form, 'scope');
  const scope: PauseScope = requested === 'device' ? 'device' : 'site';
  const deviceId = scope === 'device' ? text(form, 'deviceId') : '';
  const until = action === 'pause' ? text(form, 'until') : '';
  const target = scope === 'device' ? { scope, deviceId } : { scope };
  const entered = action === 'pause' && until !== '' ? { until } : {};
  const refused = (notice: PauseNotice): ActionFailure<PauseFailure> => fail(statusOf[notice], { action, ...target, notice, ...entered });

  if (siteId === '' || (requested !== 'site' && requested !== 'device') || (scope === 'device' && deviceId === '')) {
    return fail(400, { action, ...target, notice: 'unexpected', ...entered });
  }
  if (until !== '' && !isCalendarDate(until)) {
    return refused('date');
  }
  const end = until === '' ? null : until;
  let result: SitesResult<SitePause | DevicePause>;
  if (scope === 'site') {
    result = await (action === 'pause' ? pauseSite(locals, siteId, end, dependencies) : resumeSite(locals, siteId, dependencies));
  } else {
    result = await (action === 'pause' ? pauseDevice(locals, siteId, deviceId, end, dependencies) : resumeDevice(locals, siteId, deviceId, dependencies));
  }
  if ('ok' in result) {
    return { action, ...target, done: true };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  return refused(noticeOf(result.error, until !== ''));
}

/** The two actions of the overview, Lot detail and Devices: `?/pause` and `?/resume`. */
export const pauseActions = {
  pause: ({ locals, request }: Pick<RequestEvent, 'locals' | 'request'>) => pauseAction('pause', locals, request),
  resume: ({ locals, request }: Pick<RequestEvent, 'locals' | 'request'>) => pauseAction('resume', locals, request),
};
