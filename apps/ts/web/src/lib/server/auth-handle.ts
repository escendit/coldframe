import { isHttpError, isRedirect, redirect, type Handle, type RequestEvent } from '@sveltejs/kit';
import type { Notice } from '$lib/notices';
import { classifyFailure, type Failure } from './failures';
import { defaultReturnPath, safeReturnPath } from './return-path';

/** Routes of `@escendit/sveltekit-auth-keycloak` (package defaults). */
export const oidcRoutes = {
  signin: '/.oidc/signin',
  callback: '/.oidc/signin/callback',
  signout: '/.oidc/signout',
  signoutCallback: '/.oidc/signout/callback',
} as const;

/**
 * "Had a session" marker. Present while signed in; when a request carries it but no identity,
 * the session ended without the user signing out (UX-DR93). httpOnly, holds no data.
 */
export const sessionMarkerCookie = 'cf_session';

const markerMaxAgeSeconds = 60 * 60 * 24 * 30;

export const signinPath = '/signin';

/** `/signin`, with a notice when there is one. */
export function signinUrl(notice?: Notice | null, returnTo?: string | null): string {
  const query = new URLSearchParams();
  if (notice !== undefined && notice !== null) {
    query.set('notice', notice);
  }
  if (returnTo !== undefined && returnTo !== null && returnTo !== '') {
    query.set('returnTo', returnTo);
  }
  const search = query.toString();
  return search === '' ? signinPath : `${signinPath}?${search}`;
}

export interface AuthHandleOptions {
  /** Builds the package's middleware; called lazily and again after a failed discovery. */
  readonly createInner: () => Handle;
  /** Marks the marker cookie `Secure`. */
  readonly secureCookies: boolean;
  /**
   * Called when the code exchange failed without an `error` from Keycloak. The package swallows
   * the cause, so this re-checks the issuer to tell a certificate failure from any other.
   */
  readonly diagnoseCallbackFailure?: () => Promise<Failure>;
  readonly log?: (message: string, error?: unknown) => void;
}

interface Instance {
  readonly handle: Handle;
  discoveryFailed: boolean;
  watched: boolean;
}

interface Identity {
  readonly authenticated?: boolean;
}

function identityOf(event: RequestEvent): Identity | null {
  const session: unknown = (event.locals as { session?: unknown }).session;
  if (typeof session !== 'object' || session === null || !('identity' in session)) {
    return null;
  }
  const identity: unknown = session.identity;
  return typeof identity === 'object' && identity !== null ? identity : null;
}

function discoveryOf(event: RequestEvent): Promise<unknown> | null {
  const config: unknown = (event.locals as { config?: unknown }).config;
  if (typeof config === 'object' && config !== null && 'oidcConfiguration' in config) {
    const discovery: unknown = config.oidcConfiguration;
    if (discovery instanceof Promise) {
      return discovery;
    }
  }
  return null;
}

function isOidcPath(path: string): boolean {
  return path === oidcRoutes.signin || path === oidcRoutes.callback || path === oidcRoutes.signout || path === oidcRoutes.signoutCallback;
}

function isRedirectStatus(status: number): boolean {
  return status >= 300 && status < 400;
}

/**
 * Wraps the package's `OidcMiddleware` (which already includes the session middleware). It never
 * re-implements the code exchange, PKCE or refresh; it only turns failures into Sign-in notices,
 * retries a failed discovery on the next SIGN IN, and keeps the session marker.
 */
export function createAuthHandle(options: AuthHandleOptions): Handle {
  let instance: Instance | null = null;

  const current = (): Instance => {
    instance ??= { handle: options.createInner(), discoveryFailed: false, watched: false };
    return instance;
  };

  const drop = (failed: Instance): void => {
    if (instance === failed) {
      instance = null;
    }
  };

  const watch = (target: Instance, event: RequestEvent): void => {
    if (target.watched) {
      return;
    }
    const discovery = discoveryOf(event);
    if (discovery !== null) {
      target.watched = true;
      discovery.catch((error: unknown) => {
        target.discoveryFailed = true;
        options.log?.('OIDC discovery failed; the middleware is rebuilt on the next request.', error);
      });
    }
  };

  return async ({ event, resolve }) => {
    const path = event.url.pathname;

    // A discovery that failed is retried on the next request, never cached until restart.
    if (instance?.discoveryFailed === true) {
      instance = null;
    }
    const active = current();
    const hadSession = event.cookies.get(sessionMarkerCookie) !== undefined;

    if (path === oidcRoutes.signout) {
      // A deliberate sign-out clears the marker first, so it shows no notice.
      event.cookies.delete(sessionMarkerCookie, { path: '/' });
    }

    let response: Response;
    try {
      response = await active.handle({
        event,
        resolve: (resolved, resolveOptions) => {
          const identity = identityOf(resolved);
          if (identity !== null) {
            resolved.locals.sessionEnded = false;
            if (!hadSession) {
              resolved.cookies.set(sessionMarkerCookie, '1', {
                path: '/',
                httpOnly: true,
                sameSite: 'lax',
                secure: options.secureCookies,
                maxAge: markerMaxAgeSeconds,
              });
            }
          } else {
            resolved.locals.sessionEnded = hadSession && path !== oidcRoutes.signout;
            if (hadSession) {
              resolved.cookies.delete(sessionMarkerCookie, { path: '/' });
            }
          }
          return resolve(resolved, resolveOptions);
        },
      });
    } catch (error) {
      if (isRedirect(error) || isHttpError(error) || !isOidcPath(path)) {
        throw error;
      }
      drop(active);
      options.log?.(`Sign-in failed on ${path}.`, error);
      let returnTo: string | null = null;
      if (path === oidcRoutes.signin) {
        const requested = safeReturnPath(event.url.searchParams.get('redirect_uri'));
        returnTo = requested === defaultReturnPath ? null : requested;
      }
      redirect(303, signinUrl(classifyFailure(error, 'oidc'), returnTo));
    } finally {
      watch(active, event);
    }

    if (path === oidcRoutes.callback) {
      const keycloakError = event.url.searchParams.get('error');
      if (keycloakError === 'access_denied') {
        // Cancelled at Keycloak: back to Sign in, no notice.
        redirect(303, signinPath);
      }
      if (keycloakError !== null) {
        redirect(303, signinUrl('keycloak'));
      }
      if (response.status >= 400) {
        const failure = (await options.diagnoseCallbackFailure?.()) ?? 'keycloak';
        redirect(303, signinUrl(failure === 'certificate' ? 'certificate' : 'keycloak'));
      }
    }

    if (path === oidcRoutes.signout && isRedirectStatus(response.status)) {
      // Re-issue the package's redirect so SvelteKit attaches the cleared marker cookie.
      const location = response.headers.get('location');
      if (location !== null) {
        redirect(303, location);
      }
    }

    return response;
  };
}
