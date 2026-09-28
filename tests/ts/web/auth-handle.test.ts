import { isRedirect, redirect, type Handle, type RequestEvent } from '@sveltejs/kit';
import { describe, expect, test } from 'vitest';
import { createAuthHandle, oidcRoutes, sessionMarkerCookie, signinUrl } from '$lib/server/auth-handle';
import type { Failure } from '$lib/server/failures';
import { fakeEvent, fetchFailed, pageResolve } from './fakes.ts';

type Behaviour = (input: Parameters<Handle>[0]) => Promise<Response>;

interface Setup {
  readonly handle: Handle;
  readonly created: () => number;
}

/**
 * A stand-in for the package's OidcMiddleware: it sets `locals.session` and `locals.config`
 * like the real one, then does whatever the test says.
 */
function setup(
  behaviour: Behaviour,
  options: {
    identity?: object | null;
    discovery?: Promise<unknown>;
    diagnose?: () => Promise<Failure>;
    log?: (message: string, error?: unknown) => void;
  } = {},
): Setup {
  let created = 0;
  const handle = createAuthHandle({
    createInner: () => {
      created++;
      const discovery = options.discovery ?? Promise.resolve({});
      return async (input) => {
        const locals = input.event.locals as Record<string, unknown>;
        locals.session = { identity: options.identity ?? null };
        locals.config = { oidcConfiguration: discovery };
        return behaviour(input);
      };
    },
    secureCookies: true,
    ...(options.diagnose === undefined ? {} : { diagnoseCallbackFailure: options.diagnose }),
    ...(options.log === undefined ? {} : { log: options.log }),
  });
  return { handle, created: () => created };
}

const signedIn = { authenticated: true, idToken: { name: 'Simon Novak' } };

const passThrough: Behaviour = ({ event, resolve }) => Promise.resolve(resolve(event));

async function expectRedirect(action: () => unknown, location: string): Promise<void> {
  try {
    await action();
  } catch (error) {
    if (isRedirect(error)) {
      expect(error.location).toBe(location);
      return;
    }
    throw error;
  }
  throw new Error(`Expected a redirect to ${location}.`);
}

function markerWrites(event: RequestEvent & { cookies: { writes: { name: string; value: string }[] } }) {
  return event.cookies.writes.filter((write) => write.name === sessionMarkerCookie);
}

describe('auth handle', () => {
  test('UX-DR93 sets the httpOnly, Lax session marker while signed in', async () => {
    const { handle } = setup(passThrough, { identity: signedIn });
    const event = fakeEvent('/garden');
    const resolve = pageResolve();
    const response = await handle({ event, resolve });
    expect(response.status).toBe(200);
    expect(resolve.calls).toHaveLength(1);
    const [write] = event.cookies.writes;
    expect(write).toMatchObject({ name: sessionMarkerCookie, options: { httpOnly: true, sameSite: 'lax', secure: true, path: '/' } });
    expect(event.locals.sessionEnded).toBe(false);
  });

  test('UX-DR93 marker and no identity: the session ended, the marker is cleared', async () => {
    const { handle } = setup(passThrough, { identity: null });
    const event = fakeEvent('/alerts', { [sessionMarkerCookie]: '1' });
    await handle({ event, resolve: pageResolve() });
    expect(event.locals.sessionEnded).toBe(true);
    expect(markerWrites(event)).toEqual([expect.objectContaining({ value: '' })]);
  });

  test('UX-DR60 no marker and no identity: plain signed out, no notice', async () => {
    const { handle } = setup(passThrough, { identity: null });
    const event = fakeEvent('/garden');
    await handle({ event, resolve: pageResolve() });
    expect(event.locals.sessionEnded).toBe(false);
    expect(markerWrites(event)).toEqual([]);
  });

  test('UX-DR93 deliberate sign-out clears the marker first and shows no notice', async () => {
    const idp = 'http://idp.example/end-session?state=1';
    const { handle } = setup(() => Promise.resolve(new Response(null, { status: 307, headers: { Location: idp } })), { identity: signedIn });
    const event = fakeEvent(`${oidcRoutes.signout}?redirect_uri=/signin`, { [sessionMarkerCookie]: '1' });
    await expectRedirect(() => handle({ event, resolve: pageResolve() }), idp);
    expect(markerWrites(event)).toEqual([expect.objectContaining({ value: '' })]);
    expect(event.locals.sessionEnded).not.toBe(true);
  });

  test('UX-DR92 cancelled at Keycloak (access_denied) returns to Sign in without a notice', async () => {
    const { handle } = setup(() => Promise.resolve(new Response(null, { status: 400 })));
    const event = fakeEvent(`${oidcRoutes.callback}?error=access_denied&state=abc`);
    await expectRedirect(() => handle({ event, resolve: pageResolve() }), '/signin');
  });

  test.each(['server_error', 'temporarily_unavailable', 'invalid_request'])(
    'UX-DR92 Keycloak error %s on the callback shows the Keycloak notice',
    async (error) => {
      const { handle } = setup(() => Promise.resolve(new Response(null, { status: 400 })));
      const event = fakeEvent(`${oidcRoutes.callback}?error=${error}&state=abc`);
      await expectRedirect(() => handle({ event, resolve: pageResolve() }), '/signin?notice=keycloak');
    },
  );

  test('UX-DR92 failed code exchange (bare 400) shows the Keycloak notice', async () => {
    const { handle } = setup(() => Promise.resolve(new Response(null, { status: 400 })), { diagnose: () => Promise.resolve('keycloak') });
    const event = fakeEvent(`${oidcRoutes.callback}?code=c&state=abc`);
    await expectRedirect(() => handle({ event, resolve: pageResolve() }), '/signin?notice=keycloak');
  });

  test('UX-DR92 unknown state (JSON 400 invalid_challenge) shows the Keycloak notice', async () => {
    const { handle } = setup(() => Promise.resolve(Response.json({ error: 'invalid_challenge' }, { status: 400 })));
    const event = fakeEvent(`${oidcRoutes.callback}?code=c&state=unknown`);
    await expectRedirect(() => handle({ event, resolve: pageResolve() }), '/signin?notice=keycloak');
  });

  test('UX-DR92 exchange failing on an untrusted certificate shows the certificate notice', async () => {
    const { handle } = setup(() => Promise.resolve(new Response(null, { status: 400 })), { diagnose: () => Promise.resolve('certificate') });
    const event = fakeEvent(`${oidcRoutes.callback}?code=c&state=abc`);
    await expectRedirect(() => handle({ event, resolve: pageResolve() }), '/signin?notice=certificate');
  });

  test('UX-DR60 a successful callback passes the package redirect through', async () => {
    const { handle } = setup(() => Promise.resolve(new Response(null, { status: 307, headers: { Location: 'http://localhost:5173/garden' } })));
    const response = await handle({ event: fakeEvent(`${oidcRoutes.callback}?code=c&state=abc`), resolve: pageResolve() });
    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toBe('http://localhost:5173/garden');
  });

  test('UX-DR92 a thrown TLS error during sign-in shows the certificate notice and rebuilds the middleware', async () => {
    let fail = true;
    const { handle, created } = setup(() => {
      if (fail) {
        return Promise.reject(fetchFailed('SELF_SIGNED_CERT_IN_CHAIN'));
      }
      return Promise.resolve(new Response(null, { status: 307, headers: { Location: 'http://idp.example/auth' } }));
    });
    await expectRedirect(
      () => handle({ event: fakeEvent(`${oidcRoutes.signin}?redirect_uri=%2Fdevices`), resolve: pageResolve() }),
      '/signin?notice=certificate&returnTo=%2Fdevices',
    );
    expect(created()).toBe(1);
    fail = false;
    const response = await handle({ event: fakeEvent(oidcRoutes.signin), resolve: pageResolve() });
    expect(response.status).toBe(307);
    expect(created()).toBe(2);
  });

  test('UX-DR92 a thrown discovery error shows the Keycloak notice, keeping only a safe return path', async () => {
    const { handle } = setup(() => Promise.reject(new Error('discovery failed')));
    await expectRedirect(() => handle({ event: fakeEvent(oidcRoutes.signin), resolve: pageResolve() }), '/signin?notice=keycloak');
    await expectRedirect(
      () => handle({ event: fakeEvent(`${oidcRoutes.signin}?redirect_uri=%2Fgarden`), resolve: pageResolve() }),
      '/signin?notice=keycloak',
    );
    await expectRedirect(
      () => handle({ event: fakeEvent(`${oidcRoutes.signin}?redirect_uri=https%3A%2F%2Fevil.example`), resolve: pageResolve() }),
      '/signin?notice=keycloak',
    );
  });

  test('UX-DR92 a failed discovery is logged and retried on the next SIGN IN, not cached until restart', async () => {
    const failure = new Error('issuer down');
    const rejected = Promise.reject(failure);
    const logged: unknown[] = [];
    const { handle, created } = setup(passThrough, { discovery: rejected, log: (_message, error) => logged.push(error) });
    await handle({ event: fakeEvent('/signin'), resolve: pageResolve() });
    await rejected.catch(() => undefined);
    await Promise.resolve();
    expect(created()).toBe(1);
    expect(logged).toEqual([failure]);
    await handle({ event: fakeEvent(oidcRoutes.signin), resolve: pageResolve() });
    expect(created()).toBe(2);
  });

  test('UX-DR93 a failed discovery is replaced on any path, so a signed-in refresh recovers', async () => {
    const rejected = Promise.reject(new Error('issuer down'));
    const { handle, created } = setup(passThrough, { identity: signedIn, discovery: rejected });
    await handle({ event: fakeEvent('/garden'), resolve: pageResolve() });
    await rejected.catch(() => undefined);
    await Promise.resolve();
    expect(created()).toBe(1);
    await handle({ event: fakeEvent('/alerts'), resolve: pageResolve() });
    expect(created()).toBe(2);
  });

  test('passes the package session redirect through untouched', async () => {
    const { handle } = setup(() => {
      redirect(303, 'http://localhost:5173/garden');
    });
    await expectRedirect(() => handle({ event: fakeEvent('/garden'), resolve: pageResolve() }), 'http://localhost:5173/garden');
  });

  test('a transient refresh failure on a page is not turned into a sign-in notice', async () => {
    const { handle } = setup(() => Promise.reject(new Error('network blip')));
    await expect(handle({ event: fakeEvent('/garden'), resolve: pageResolve() })).rejects.toThrow('network blip');
  });

  test('builds Sign-in URLs', () => {
    expect(signinUrl()).toBe('/signin');
    expect(signinUrl('unreachable')).toBe('/signin?notice=unreachable');
    expect(signinUrl('signed-out', '/alerts')).toBe('/signin?notice=signed-out&returnTo=%2Falerts');
  });
});
