import { fail, type ActionFailure } from '@sveltejs/kit';
import type { Lot } from '@coldframe/api-client';
import { checkLotName, type LotsNotice, type SiteSettingsAction, type SiteSettingsFailure, type SiteSettingsNotice, type SiteSettingsSuccess } from '$lib/lots';
import { checkSiteName, type Site } from '$lib/sites';
import { newIdempotencyKey } from './create-site';
import { createLot, listLots, removeLot, renameLot } from './lots';
import { signedOutRedirect } from './shell';
import { renameSite, type SitesDependencies, type SitesError } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface SiteSettingsData {
  readonly lots: readonly Lot[];
  readonly lotsNotice: LotsNotice | null;
  /** The Idempotency-Key of the next Create Lot attempt; a new one on every load after success. */
  readonly createKey: string;
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

export async function loadSiteSettings(locals: Locals, site: Site | null, dependencies: SitesDependencies = {}): Promise<SiteSettingsData> {
  return { ...(await loadLots(locals, site, dependencies)), createKey: newIdempotencyKey() };
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
      // Only a Site rename waits on Keycloak; for Lots a 503 is just an error from the Server.
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
  const name = action === 'removeLot' ? '' : text(form, 'name');
  const submittedKey = text(form, 'idempotencyKey');
  const idempotencyKey = action !== 'createLot' ? null : validKey.test(submittedKey) ? submittedKey : newIdempotencyKey();
  const failure = (status: number, fields: Partial<SiteSettingsFailure>): ActionFailure<SiteSettingsFailure> =>
    fail(status, { action, lotId, name, nameError: null, notice: null, siteName, lotName, idempotencyKey, ...fields });

  if (action !== 'removeLot') {
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
    }
  })();

  if ('ok' in result) {
    return { action, done: true };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  if (result.error === 'validation') {
    return failure(400, { nameError: 'invalid' });
  }
  const notice = noticeOf(action, result.error);
  // A reused key gets a new one; every other failure retries with the same key.
  return failure(statusOf[notice], { notice, ...(notice === 'keyReused' ? { idempotencyKey: newIdempotencyKey() } : {}) });
}
