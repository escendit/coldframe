import { describe, expect, test } from 'vitest';
import { diagnoseCallback, discoveryUrl, probeIssuer, probeServer, probeSignIn, serverHealthUrl, type Fetch } from '$lib/server/probe';
import { fetchFailed } from './fakes.ts';

const server = new URL('https://coldframe.example.org');
const issuer = new URL('https://id.example.org/realms/coldframe');

const discovery = { issuer: issuer.href, authorization_endpoint: `${issuer.href}/protocol/openid-connect/auth` };

function respond(status: number, body: unknown = ''): Fetch {
  const text = typeof body === 'string' ? body : JSON.stringify(body);
  return () => Promise.resolve(new Response(status === 204 ? null : text, { status }));
}

function reject(error: unknown): Fetch {
  return () => Promise.reject(error instanceof Error ? error : new Error(String(error)));
}

/** A fetch that never answers but honours the abort signal, like a silent host. */
const hang: Fetch = (_url, init) =>
  new Promise((_resolve, rejectPromise) => {
    init?.signal?.addEventListener('abort', () => {
      rejectPromise(init.signal?.reason instanceof Error ? init.signal.reason : new Error('aborted'));
    });
  });

describe('probes', () => {
  test('builds the health and discovery URLs', () => {
    expect(serverHealthUrl(server).href).toBe('https://coldframe.example.org/.well-known/healthz');
    expect(discoveryUrl(issuer).href).toBe('https://id.example.org/realms/coldframe/.well-known/openid-configuration');
    expect(discoveryUrl(new URL('https://id.example.org/realms/coldframe/')).href).toBe(
      'https://id.example.org/realms/coldframe/.well-known/openid-configuration',
    );
  });

  test.each([200, 204, 404, 500, 503])('UX-DR60 any HTTP response (%i) means the Server is reachable', async (status) => {
    expect(await probeServer(server, respond(status))).toEqual({ ok: true });
  });

  test.each(['ECONNREFUSED', 'ENOTFOUND'])('UX-DR92 Server probe %s → unreachable', async (code) => {
    expect(await probeServer(server, reject(fetchFailed(code)))).toEqual({ ok: false, failure: 'unreachable' });
  });

  test('UX-DR92 Server probe timeout → unreachable, bounded', async () => {
    const started = Date.now();
    expect(await probeServer(server, hang, 30)).toEqual({ ok: false, failure: 'unreachable' });
    expect(Date.now() - started).toBeLessThan(2_000);
  });

  test('UX-DR92 Server probe with an untrusted certificate → certificate', async () => {
    expect(await probeServer(server, reject(fetchFailed('DEPTH_ZERO_SELF_SIGNED_CERT')))).toEqual({ ok: false, failure: 'certificate' });
  });

  test('issuer probe accepts a discovery document', async () => {
    expect(await probeIssuer(issuer, respond(200, discovery))).toEqual({ ok: true });
  });

  test.each([
    ['a 500', respond(500)],
    ['a 404', respond(404)],
    ['a body that is not a discovery document', respond(200, { hello: 'world' })],
    ['a body that is not JSON', respond(200, '<html>')],
    ['a refused connection', reject(fetchFailed('ECONNREFUSED'))],
  ])('UX-DR92 issuer probe with %s → Keycloak error', async (_name, fetcher) => {
    expect(await probeIssuer(issuer, fetcher)).toEqual({ ok: false, failure: 'keycloak' });
  });

  test('UX-DR92 issuer probe timeout → Keycloak error', async () => {
    expect(await probeIssuer(issuer, hang, 30)).toEqual({ ok: false, failure: 'keycloak' });
  });

  test('UX-DR92 issuer probe with an untrusted certificate → certificate', async () => {
    expect(await probeIssuer(issuer, reject(fetchFailed('CERT_HAS_EXPIRED')))).toEqual({ ok: false, failure: 'certificate' });
  });

  test('UX-DR60 SIGN IN checks the Server first, then the issuer', async () => {
    const seen: string[] = [];
    const fetcher: Fetch = (url) => {
      seen.push(url.href);
      return url.pathname.endsWith('healthz') ? Promise.reject(fetchFailed('ECONNREFUSED')) : Promise.resolve(Response.json(discovery));
    };
    expect(await probeSignIn(server, issuer, fetcher)).toEqual({ ok: false, failure: 'unreachable' });
    expect(seen).toEqual(['https://coldframe.example.org/.well-known/healthz']);

    seen.length = 0;
    const healthy: Fetch = (url) => {
      seen.push(url.href);
      return Promise.resolve(url.pathname.endsWith('healthz') ? new Response('Healthy') : Response.json(discovery));
    };
    expect(await probeSignIn(server, issuer, healthy)).toEqual({ ok: true });
    expect(seen).toHaveLength(2);
  });

  test('UX-DR92 a failed code exchange is a certificate failure only when the issuer certificate is untrusted', async () => {
    expect(await diagnoseCallback(issuer, reject(fetchFailed('CERT_HAS_EXPIRED')))).toBe('certificate');
    expect(await diagnoseCallback(issuer, respond(200, discovery))).toBe('keycloak');
    expect(await diagnoseCallback(issuer, respond(500))).toBe('keycloak');
  });
});
