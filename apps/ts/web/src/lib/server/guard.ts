import { redirect } from '@sveltejs/kit';
import { parseNotice, type Notice } from '$lib/notices';
import type { DisplayUser } from '$lib/user';
import { signinUrl } from './auth-handle';
import { defaultReturnPath, safeReturnPath } from './return-path';
import { displayUserOf } from './user';

type Locals = Pick<App.Locals, 'session' | 'sessionEnded'>;

/**
 * Guard of the app shell, run on every navigation. Signed out → Sign in, with the signed-out
 * notice when the session ended on its own (UX-DR93). Returns display fields only.
 */
export function guardShell(locals: Locals, url: URL): { user: DisplayUser } {
  // Read the URL on every path, so SvelteKit tracks it and reruns this guard on each navigation.
  const requested = url.pathname === '/' ? defaultReturnPath : `${url.pathname}${url.search}`;
  const identity = locals.session?.identity ?? null;
  if (identity === null) {
    const returnTo = safeReturnPath(requested);
    redirect(303, signinUrl(locals.sessionEnded === true ? 'signed-out' : null, returnTo === defaultReturnPath ? null : returnTo));
  }
  return { user: displayUserOf(identity.idToken) };
}

/** State of the Sign-in surface. Already signed in → straight to the return path. */
export function signinState(locals: Locals, url: URL): { notice: Notice | null; returnTo: string | null } {
  const returnTo = safeReturnPath(url.searchParams.get('returnTo'));
  if ((locals.session?.identity ?? null) !== null) {
    redirect(303, returnTo);
  }
  const notice = locals.sessionEnded === true ? 'signed-out' : parseNotice(url.searchParams.get('notice'));
  return { notice, returnTo: returnTo === defaultReturnPath ? null : returnTo };
}
