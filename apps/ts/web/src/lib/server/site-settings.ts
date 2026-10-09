import { fail, type ActionFailure } from '@sveltejs/kit';
import type { Lot } from '@coldframe/api-client';
import { checkLotName, type LotsNotice, type SiteSettingsAction, type SiteSettingsFailure, type SiteSettingsNotice, type SiteSettingsSuccess } from '$lib/lots';
import { checkSiteName, type Site } from '$lib/sites';
import { isReminderCadence, type ReminderCadence } from '$lib/notifications';
import { newIdempotencyKey } from './create-site';
import { getSiteReminderCadence, setSiteReminderCadence } from './notifications';
import { createLot, listLots, removeLot, renameLot } from './lots';
import { signedOutRedirect } from './shell';
import { renameSite, type SitesDependencies, type SitesError } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface SiteSettingsData {
  readonly lots: readonly Lot[];
  readonly lotsNotice: LotsNotice | null;
  /** The Idempotency-Key of the next Create Lot attempt; a new one on every load after success. */
  readonly createKey: string;
  /** The Site's Reminder cadence; null without a Site or when it could not be read. */
  readonly reminderCadence: ReminderCadence | null;
  readonly reminderCadenceNotice: LotsNotice | null;
}

/**
 * The Lots of the current Site, in the Server's order, for Site settings and Garden. A 401 signs
 * out; any other failure is a notice in place of the list.
 */
export async function loadLots(
  locals: Locals,
  site: Site | null,
  dependencies: SitesDependencies = {},
): Promise<{ lots: readonly Lot[]; lotsNotice: LotsNotice | null }> {
  if (site === null) {
    return { lots: [], lotsNotice: null };
  }
  const result = await listLots(locals, site.id, dependencies);
  if ('ok' in result) {
    return { lots: result.ok, lotsNotice: null };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { lots: [], lotsNotice: result.error === 'unreachable' || result.error === 'certificate' ? result.error : 'unavailable' };
}

/** The Site's Reminder cadence (a Member may read it). A 401 signs out; any other failure is a notice in its place. */
async function loadReminderCadence(locals: Locals, site: Site | null, dependencies: SitesDependencies): Promise<Pick<SiteSettingsData, 'reminderCadence' | 'reminderCadenceNotice'>> {
  if (site === null) {
    return { reminderCadence: null, reminderCadenceNotice: null };
  }
  const result = await getSiteReminderCadence(locals, site.id, dependencies);
  if ('ok' in result) {
    return isReminderCadence(result.ok.cadence) ? { reminderCadence: result.ok.cadence, reminderCadenceNotice: null } : { reminderCadence: null, reminderCadenceNotice: 'unavailable' };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { reminderCadence: null, reminderCadenceNotice: result.error === 'unreachable' || result.error === 'certificate' ? result.error : 'unavailable' };
}

/** Site settings: the Lots and the Site's Reminder cadence, read at the same time. */
export async function loadSiteSettings(locals: Locals, site: Site | null, dependencies: SitesDependencies = {}): Promise<SiteSettingsData> {
  const [lots, cadence] = await Promise.all([loadLots(locals, site, dependencies), loadReminderCadence(locals, site, dependencies)]);
  return { ...lots, ...cadence, createKey: newIdempotencyKey() };
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

const validKey = /^[\x20-\x7E]{1,200}$/u;

/** The copy of a Server refusal for one action. */
function noticeOf(action: SiteSettingsAction, error: Exclude<SitesError, 'unauthorized' | 'validation'>): SiteSettingsNotice {
  switch (error) {
    case 'forbidden':
    case 'lotClaimed':
    case 'unreachable':
    case 'certificate':
    case 'keyReused':
      return error;
    case 'notFound':
      return action === 'renameLot' || action === 'removeLot' ? 'lotNotFound' : 'siteNotFound';
    case 'unavailable':
      // Only a Site rename waits on Keycloak; for Lots a 503 is just an error from the Server. For the
      // Reminder cadence the contract's 503 is a save that did not reach every member: the Site holds the
      // cadence, and sending it again repairs it.
      if (action === 'setReminderCadence') {
        return 'cadenceNotDelivered';
      }
      return action === 'renameSite' ? 'unavailable' : 'unexpected';
    case 'unexpected':
      return 'unexpected';
  }
}

const statusOf: Readonly<Record<SiteSettingsNotice, number>> = {
  forbidden: 403,
  lotClaimed: 409,
  lotNotFound: 404,
  siteNotFound: 404,
  unavailable: 503,
  keyReused: 422,
  unexpected: 502,
  unreachable: 502,
  certificate: 502,
  cadenceNotDelivered: 503,
};

/**
 * One Site settings action. The Site ID and the names the notices mention come from the form;
 * the Server authorizes every change by the caller's Role (AD-4), so a stale page gets a 403.
 */
export async function siteSettingsAction(
  action: SiteSettingsAction,
  locals: Locals,
  request: Request,
  dependencies: SitesDependencies = {},
): Promise<SiteSettingsSuccess | ActionFailure<SiteSettingsFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const siteName = text(form, 'siteName');
  const lotId = action === 'renameLot' || action === 'removeLot' ? text(form, 'lotId') : null;
  const lotName = lotId === null ? null : text(form, 'lotName');
  const cadence = action === 'setReminderCadence' ? text(form, 'cadence') : null;
  const name = action === 'removeLot' || action === 'setReminderCadence' ? '' : text(form, 'name');
  const submittedKey = text(form, 'idempotencyKey');
  const idempotencyKey = action !== 'createLot' ? null : validKey.test(submittedKey) ? submittedKey : newIdempotencyKey();
  const failure = (status: number, fields: Partial<SiteSettingsFailure>): ActionFailure<SiteSettingsFailure> =>
    fail(status, { action, lotId, name, nameError: null, notice: null, siteName, lotName, idempotencyKey, cadence, ...fields });

  if (action === 'setReminderCadence') {
    // There is no "never" (UX-DR50): anything but the two cadences is not sent.
    if (!isReminderCadence(cadence)) {
      return failure(400, { notice: 'unexpected' });
    }
  } else if (action !== 'removeLot') {
    const nameError = action === 'renameSite' ? checkSiteName(name) : checkLotName(name);
    if (nameError !== null) {
      return failure(400, { nameError });
    }
  }

  const trimmed = name.trim();
  const result = await (() => {
    switch (action) {
      case 'renameSite':
        return renameSite(locals, siteId, trimmed, dependencies);
      case 'createLot':
        return createLot(locals, siteId, trimmed, idempotencyKey ?? newIdempotencyKey(), dependencies);
      case 'renameLot':
        return renameLot(locals, siteId, lotId ?? '', trimmed, dependencies);
      case 'removeLot':
        return removeLot(locals, siteId, lotId ?? '', dependencies);
      case 'setReminderCadence':
        return setSiteReminderCadence(locals, siteId, isReminderCadence(cadence) ? cadence : 'daily', dependencies);
    }
  })();

  if ('ok' in result) {
    return { action, done: true };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  if (result.error === 'validation') {
    return action === 'setReminderCadence' ? failure(400, { notice: 'unexpected' }) : failure(400, { nameError: 'invalid' });
  }
  const notice = noticeOf(action, result.error);
  // A reused key gets a new one; every other failure retries with the same key.
  return failure(statusOf[notice], { notice, ...(notice === 'keyReused' ? { idempotencyKey: newIdempotencyKey() } : {}) });
}
