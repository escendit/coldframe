import { fail, redirect, type ActionFailure, type Cookies } from '@sveltejs/kit';
import { checkSiteName, type NameError } from '$lib/sites';
import { rememberChoice, signedOutRedirect, siteCookieName, timeZoneCookieName } from './shell';
import { createSite, type SitesDependencies } from './sites';

/** What the Create Site form shows after a failed submission. */
export interface CreateSiteFailure {
  readonly name: string;
  readonly nameError: NameError | null;
  readonly notice: 'unavailable' | 'unexpected' | 'keyReused' | 'unreachable' | 'certificate' | null;
  /** The key the next attempt uses: the same one, except after a reused key. */
  readonly idempotencyKey: string;
}

type Locals = Pick<App.Locals, 'session'>;

/** A new Idempotency-Key: one per Create Site attempt, made when the form opens. */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
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

/** Data of the Create Site page: a fresh key, and the time zone this browser already chose. */
export function loadCreateSite(cookies: Cookies): { idempotencyKey: string; chosenTimeZone: string | null } {
  const chosen = cookies.get(timeZoneCookieName);
  return { idempotencyKey: newIdempotencyKey(), chosenTimeZone: isTimeZone(chosen) ? chosen : null };
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

/**
 * The Create Site action. Validates the name without calling the Server, then `POST /sites` with
 * the form's key and `{name}`. A 503 or a network failure keeps the key for the retry; a reused
 * key gets a new one. Success makes the new Site current and opens its Garden.
 */
export async function createSiteAction(
  locals: Locals,
  request: Request,
  cookies: Cookies,
  dependencies: SitesDependencies = {},
): Promise<ActionFailure<CreateSiteFailure>> {
  const form = await request.formData();
  const name = text(form, 'name');
  const submittedKey = text(form, 'idempotencyKey');
  const idempotencyKey = submittedKey !== '' && /^[\x20-\x7E]{1,200}$/u.test(submittedKey) ? submittedKey : newIdempotencyKey();
  const timeZone = text(form, 'timeZone');

  const nameError = checkSiteName(name);
  if (nameError !== null) {
    return fail(400, { name, nameError, notice: null, idempotencyKey });
  }

  const result = await createSite(locals, name.trim(), idempotencyKey, dependencies);
  if ('ok' in result) {
    rememberChoice(cookies, siteCookieName, result.ok.id);
    if (isTimeZone(timeZone)) {
      rememberChoice(cookies, timeZoneCookieName, timeZone);
    }
    redirect(303, '/garden');
  }
  switch (result.error) {
    case 'unauthorized':
      return signedOutRedirect();
    case 'validation':
      return fail(400, { name, nameError: 'invalid', notice: null, idempotencyKey });
    case 'keyReused':
      return fail(422, { name, nameError: null, notice: 'keyReused', idempotencyKey: newIdempotencyKey() });
    case 'unavailable':
      return fail(503, { name, nameError: null, notice: 'unavailable', idempotencyKey });
    case 'unexpected':
      return fail(502, { name, nameError: null, notice: 'unexpected', idempotencyKey });
    case 'unreachable':
    case 'certificate':
      return fail(502, { name, nameError: null, notice: result.error, idempotencyKey });
  }
}
