import { fail, redirect, type ActionFailure, type Cookies } from '@sveltejs/kit';
import { checkSiteName, type NameError } from '$lib/sites';
import { isTimeZone, rememberChoice, signedOutRedirect, siteCookieName, timeZoneCookieName, timeZoneHandOverCookieName } from './shell';
import { call, createSite, type SitesDependencies } from './sites';

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

export { isTimeZone };

/** Data of the Create Site page: a fresh key, and the time zone the User already chose. */
export function loadCreateSite(cookies: Cookies): { idempotencyKey: string; chosenTimeZone: string | null } {
  const chosen = cookies.get(timeZoneCookieName);
  return { idempotencyKey: newIdempotencyKey(), chosenTimeZone: isTimeZone(chosen) ? chosen : null };
}

/**
 * The zone confirmed on Create Site is the User's own choice, so it goes to the Server
 * (`PATCH /me/notification-settings`), never into `POST /sites` (AD-11). The Site exists by now, so
 * no outcome here stops the action. A zone the Server took, or could not be asked about, is kept on
 * this browser; in the second case the session's hand-over is opened again so the next load sends
 * it. A zone the Server refused is not kept.
 */
async function sendChosenTimeZone(locals: Locals, cookies: Cookies, timeZone: string, dependencies: SitesDependencies): Promise<void> {
  const sent = await call(locals, dependencies, (client) => client.PATCH('/me/notification-settings', { body: { timeZone } }));
  if ('ok' in sent) {
    rememberChoice(cookies, timeZoneCookieName, typeof sent.ok.timeZone === 'string' && sent.ok.timeZoneConfirmed ? sent.ok.timeZone : timeZone);
    return;
  }
  if (sent.error === 'validation') {
    return;
  }
  rememberChoice(cookies, timeZoneCookieName, timeZone);
  if (cookies.get(timeZoneHandOverCookieName) !== undefined) {
    cookies.delete(timeZoneHandOverCookieName, { path: '/' });
  }
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

/**
 * The Create Site action. Validates the name without calling the Server, then `POST /sites` with
 * the form's key and `{name}`. A 503 or a network failure keeps the key for the retry; a reused
 * key gets a new one. Success makes the new Site current, sends the confirmed time zone to the
 * Server as the User's choice and opens the Site's Garden.
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
      await sendChosenTimeZone(locals, cookies, timeZone, dependencies);
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
    case 'forbidden':
    case 'notFound':
    case 'lotClaimed':
      return fail(502, { name, nameError: null, notice: 'unexpected', idempotencyKey });
    case 'unreachable':
    case 'certificate':
      return fail(502, { name, nameError: null, notice: result.error, idempotencyKey });
  }
}
