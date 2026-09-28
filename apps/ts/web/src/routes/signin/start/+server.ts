import { redirect } from '@sveltejs/kit';
import { oidcRoutes, signinUrl } from '$lib/server/auth-handle';
import { probeSignIn } from '$lib/server/probe';
import { defaultReturnPath, safeReturnPath } from '$lib/server/return-path';
import { getConfig } from '$lib/server/runtime';
import type { RequestHandler } from './$types';

/**
 * SIGN IN: checks that the Server answers and the issuer serves its discovery document, each
 * with a bounded timeout, then hands off to the package's `/.oidc/signin`. A failure goes back
 * to Sign in with the matching notice and never reaches Keycloak.
 */
export const GET: RequestHandler = async ({ url, locals }) => {
  const returnTo = safeReturnPath(url.searchParams.get('returnTo'));
  if ((locals.session?.identity ?? null) !== null) {
    redirect(303, returnTo);
  }
  const config = getConfig();
  const probe = await probeSignIn(config.serverUrl, config.issuer);
  if (!probe.ok) {
    redirect(303, signinUrl(probe.failure, returnTo === defaultReturnPath ? null : returnTo));
  }
  redirect(303, `${oidcRoutes.signin}?${new URLSearchParams({ redirect_uri: returnTo }).toString()}`);
};
