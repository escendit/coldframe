import { env } from '$env/dynamic/private';
import { OidcMiddleware } from '@escendit/sveltekit-auth-keycloak';
import { InMemorySessionStore } from '@escendit/sveltekit-session';
import type { Handle, ServerInit } from '@sveltejs/kit';
import { sequence } from '@sveltejs/kit/hooks';
import { createAuthHandle } from '$lib/server/auth-handle';
import { loadConfig } from '$lib/server/config';
import { healthHandle } from '$lib/server/health';
import { diagnoseCallback } from '$lib/server/probe';
import { setConfig } from '$lib/server/runtime';
import { themeHandle } from '$lib/server/theme';

let authHandle: Handle | undefined;

/** Validates the environment at startup; a missing variable stops the app with its name. */
export const init: ServerInit = () => {
  const config = loadConfig(env);
  setConfig(config);

  // One store for the whole process, so rebuilding the middleware after a failed discovery
  // keeps every session.
  const sessionStore = new InMemorySessionStore();

  authHandle = createAuthHandle({
    createInner: () =>
      OidcMiddleware({
        issuer: config.issuer.href,
        clientId: config.clientId,
        clientSecret: config.clientSecret,
        allowInsecureRequests: config.allowInsecureHttp,
        cookie: { secure: config.sessionCookieSecure },
        sessionStore,
      }),
    secureCookies: config.sessionCookieSecure,
    diagnoseCallbackFailure: () => diagnoseCallback(config.issuer),
    log: (message, error) => {
      console.error(message, error);
    },
  });
};

const auth: Handle = (input) => {
  if (authHandle === undefined) {
    throw new Error('The web app has not been initialised.');
  }
  return authHandle(input);
};

// The health probes come first: they need no session and must not trigger OIDC discovery.
export const handle = sequence(healthHandle, auth, themeHandle);
