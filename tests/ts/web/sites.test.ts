import { isActionFailure, isRedirect, type ActionFailure } from '@sveltejs/kit';
import { describe, expect, test } from 'vitest';
import { firstRunSteps } from '$lib/first-run';
import { createSiteAction, isTimeZone, loadCreateSite, type CreateSiteFailure } from '$lib/server/create-site';
import { loadShell, pickCurrentSite, siteCookieName, timeZoneCookieName } from '$lib/server/shell';
import { createSite, listSites } from '$lib/server/sites';
import { siteMenuItems } from '$lib/site-menu';
import { checkSiteName, sitesNoticeOf, type Site } from '$lib/sites';
import { fetchFailed, FakeCookies } from './fakes.ts';

const serverUrl = new URL('https://server.example');

const siteA: Site = { id: '0192a000-0000-7000-8000-00000000000a', name: 'Home', role: 'Owner' };
const siteB: Site = { id: '0192a000-0000-7000-8000-00000000000b', name: 'Allotment', role: 'Member' };

const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { name: 'Simon Novak' } } } };

interface Seen {
  readonly method: string;
  readonly path: string;
  readonly authorization: string | null;
  readonly key: string | null;
  readonly body: string;
}

/** A fake Server: records every request and answers from `answer`. */
function server(answer: (request: Request) => Response | Promise<Response>): { fetch: (request: Request) => Promise<Response>; seen: Seen[] } {
  const seen: Seen[] = [];
  return {
    seen,
    fetch: async (request) => {
      seen.push({
        method: request.method,
        path: new URL(request.url).pathname,
        authorization: request.headers.get('authorization'),
        key: request.headers.get('idempotency-key'),
        body: request.method === 'GET' ? '' : await request.clone().text(),
      });
      return answer(request);
    },
  };
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': status < 400 ? 'application/json' : 'application/problem+json' } });
}

function problem(status: number, slug: string): Response {
  return json(status, { type: `urn:coldframe:problem:${slug}`, title: slug, status });
}

function thrown(action: () => unknown): Promise<unknown> {
  return (async () => {
    try {
      await action();
    } catch (error) {
      return error;
    }
    throw new Error('Expected a throw.');
  })();
}

async function redirectOf(action: () => unknown): Promise<{ status: number; location: string }> {
  const error = await thrown(action);
  if (isRedirect(error)) {
    return { status: error.status, location: error.location };
  }
  throw error;
}

function formRequest(fields: Readonly<Record<string, string>>): Request {
  return new Request('http://localhost/sites/new', { method: 'POST', body: new URLSearchParams(fields) });
}

async function failureOf(result: Promise<ActionFailure<CreateSiteFailure>>): Promise<ActionFailure<CreateSiteFailure>> {
  const value = await result;
  expect(isActionFailure(value)).toBe(true);
  return value;
}

describe('Server calls (AD-14)', () => {
  test('listSites sends the session access token and keeps the Server order', async () => {
    const fake = server(() => json(200, { sites: [siteB, siteA] }));
    const result = await listSites(locals, { serverUrl, fetch: fake.fetch });
    expect(result).toEqual({ ok: [siteB, siteA] });
    expect(fake.seen).toEqual([{ method: 'GET', path: '/sites', authorization: 'Bearer access-token-1', key: null, body: '' }]);
  });

  test('createSite sends the Idempotency-Key and only {name}', async () => {
    const fake = server(() => json(201, siteA));
    const result = await createSite(locals, 'Home', 'key-1', { serverUrl, fetch: fake.fetch });
    expect(result).toEqual({ ok: siteA });
    expect(fake.seen[0]).toMatchObject({ method: 'POST', path: '/sites', authorization: 'Bearer access-token-1', key: 'key-1' });
    expect(JSON.parse(fake.seen[0]?.body ?? '')).toEqual({ name: 'Home' });
  });

  test.each([
    [400, 'validation', 'validation'],
    [401, 'unauthorized', 'unauthorized'],
    [422, 'idempotency-key-reused', 'keyReused'],
    [503, 'identity-provider-unavailable', 'unavailable'],
    [500, 'internal', 'unexpected'],
    [403, 'forbidden', 'forbidden'],
    [404, 'site-not-found', 'notFound'],
    [409, 'lot-claimed', 'lotClaimed'],
  ] as const)('a %i answer maps to %s → %s', async (status, slug, error) => {
    const fake = server(() => problem(status, slug));
    expect(await createSite(locals, 'Home', 'k', { serverUrl, fetch: fake.fetch })).toEqual({ error });
  });

  test('a refused connection is unreachable; an untrusted certificate is certificate, never retried', async () => {
    let calls = 0;
    const refused = await listSites(locals, {
      serverUrl,
      fetch: () => {
        calls++;
        return Promise.reject(fetchFailed('ECONNREFUSED'));
      },
    });
    expect(refused).toEqual({ error: 'unreachable' });
    const untrusted = await listSites(locals, {
      serverUrl,
      fetch: () => {
        calls++;
        return Promise.reject(fetchFailed('DEPTH_ZERO_SELF_SIGNED_CERT'));
      },
    });
    expect(untrusted).toEqual({ error: 'certificate' });
    expect(calls).toBe(2);
  });

  test('no access token in the session → unauthorized, and nothing is sent', async () => {
    const fake = server(() => json(200, { sites: [] }));
    expect(await listSites({ session: { identity: { authenticated: true } } }, { serverUrl, fetch: fake.fetch })).toEqual({ error: 'unauthorized' });
    expect(fake.seen).toEqual([]);
  });
});

describe('Shell load', () => {
  const list = (sites: readonly Site[]) => server(() => json(200, { sites }));

  test('UX-DR61 no Membership → every app route redirects (303) to Create Site', async () => {
    for (const path of ['/garden', '/alerts', '/settings/appearance']) {
      const fake = list([]);
      expect(await redirectOf(() => loadShell(locals, new URL(`http://x${path}`), new FakeCookies(), { serverUrl, fetch: fake.fetch }))).toEqual({
        status: 303,
        location: '/sites/new',
      });
    }
  });

  test('a 503 or 500 listing the Sites shows the unavailable notice with Try again', async () => {
    // A new Response per call: on the overview a failed read is retried once before the notice shows.
    for (const answer of [() => problem(503, 'identity-provider-unavailable'), () => json(500, {})]) {
      const fake = server(answer);
      const data = await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), { serverUrl, fetch: fake.fetch });
      expect(data.sitesNotice).toBe('unavailable');
      expect(data.sites).toEqual([]);
      expect(data.sitesStale).toBeNull();
    }
  });

  test('each shell notice has its copy; only the certificate notice has no Try again', () => {
    expect(sitesNoticeOf('unreachable')).toEqual({ message: 'sites.unreachable', tryAgain: true });
    expect(sitesNoticeOf('unavailable')).toEqual({ message: 'sites.unavailable', tryAgain: true });
    expect(sitesNoticeOf('certificate')).toEqual({ message: 'notice.certificate', tryAgain: false });
    expect(sitesNoticeOf(null)).toBeNull();
  });

  test('UX-DR61 Create Site itself loads with no Sites', async () => {
    const fake = list([]);
    const data = await loadShell(locals, new URL('http://x/sites/new'), new FakeCookies(), { serverUrl, fetch: fake.fetch });
    expect(data.sites).toEqual([]);
    expect(data.currentSite).toBeNull();
  });

  test('UX-DR23 Sites arrive in the Server order with the Role from the Server; the first is current by default', async () => {
    const fake = list([siteB, siteA]);
    const data = await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), { serverUrl, fetch: fake.fetch });
    expect(data.sites).toEqual([siteB, siteA]);
    expect(data.currentSite).toEqual(siteB);
    expect(data.sitesNotice).toBeNull();
    expect(JSON.stringify(data)).not.toContain('access-token-1');
  });

  test('UX-DR23 the cf_site cookie picks the current Site; a stale or unknown one falls back to the first', async () => {
    const fake = list([siteA, siteB]);
    const chosen = await loadShell(locals, new URL('http://x/garden'), new FakeCookies({ [siteCookieName]: siteB.id }), { serverUrl, fetch: fake.fetch });
    expect(chosen.currentSite).toEqual(siteB);
    const stale = await loadShell(locals, new URL('http://x/garden'), new FakeCookies({ [siteCookieName]: 'gone' }), { serverUrl, fetch: fake.fetch });
    expect(stale.currentSite).toEqual(siteA);
    expect(pickCurrentSite([], 'x')).toBeNull();
  });

  test('UX-DR23 ?site= switches the whole app, persists the choice per browser and drops the query', async () => {
    const fake = list([siteA, siteB]);
    const cookies = new FakeCookies();
    expect(await redirectOf(() => loadShell(locals, new URL(`http://x/alerts?site=${siteB.id}&x=1`), cookies, { serverUrl, fetch: fake.fetch }))).toEqual({
      status: 303,
      location: '/alerts?x=1',
    });
    expect(cookies.jar.get(siteCookieName)).toBe(siteB.id);
    expect(cookies.writes[0]?.options).toMatchObject({ path: '/', httpOnly: true, sameSite: 'lax' });
  });

  test('UX-DR23 ?site= naming a Site the user does not hold changes nothing', async () => {
    const fake = list([siteA]);
    const cookies = new FakeCookies({ [siteCookieName]: siteA.id });
    expect((await redirectOf(() => loadShell(locals, new URL('http://x/garden?site=other'), cookies, { serverUrl, fetch: fake.fetch }))).location).toBe('/garden');
    expect(cookies.jar.get(siteCookieName)).toBe(siteA.id);
  });

  test('a 401 from the Server signs out and lands on Sign in with the signed-out notice', async () => {
    const fake = server(() => problem(401, 'unauthorized'));
    const { location } = await redirectOf(() => loadShell(locals, new URL('http://x/garden'), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(location).toBe(`/.oidc/signout?redirect_uri=${encodeURIComponent('/signin?notice=signed-out')}`);
  });

  test('an unreachable Server or an untrusted certificate shows the existing notice in the shell', async () => {
    for (const [code, notice] of [
      ['ECONNREFUSED', 'unreachable'],
      ['CERT_HAS_EXPIRED', 'certificate'],
    ] as const) {
      const data = await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), { serverUrl, fetch: () => Promise.reject(fetchFailed(code)) });
      expect(data.sitesNotice).toBe(notice);
    }
  });

  test('signed out → Sign in, before the Server is called', async () => {
    const fake = list([siteA]);
    expect((await redirectOf(() => loadShell({ session: { identity: null } }, new URL('http://x/garden'), new FakeCookies(), { serverUrl, fetch: fake.fetch }))).location).toBe(
      '/signin',
    );
    expect(fake.seen).toEqual([]);
  });
});

describe('Create Site action', () => {
  test('UX-DR61 the form opens with a new Idempotency-Key and the time zone this browser chose', () => {
    const first = loadCreateSite(new FakeCookies());
    const second = loadCreateSite(new FakeCookies({ [timeZoneCookieName]: 'Europe/Zurich' }));
    expect(first.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/u);
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey);
    expect(first.chosenTimeZone).toBeNull();
    expect(second.chosenTimeZone).toBe('Europe/Zurich');
    expect(loadCreateSite(new FakeCookies({ [timeZoneCookieName]: 'Mars/Olympus' })).chosenTimeZone).toBeNull();
  });

  test('UX-DR61 Create Site sends POST /sites with the key and {name}, makes the Site current and opens its Garden', async () => {
    const fake = server(() => json(201, siteA));
    const cookies = new FakeCookies();
    const target = await redirectOf(() =>
      createSiteAction(locals, formRequest({ name: '  Home ', idempotencyKey: 'key-1', timeZone: 'Europe/Zurich' }), cookies, { serverUrl, fetch: fake.fetch }),
    );
    expect(target).toEqual({ status: 303, location: '/garden' });
    expect(fake.seen).toHaveLength(1);
    expect(fake.seen[0]?.key).toBe('key-1');
    // AD-11: the time zone is the User's and stays on this browser; the body is {name} only.
    expect(JSON.parse(fake.seen[0]?.body ?? '')).toEqual({ name: 'Home' });
    expect(cookies.jar.get(siteCookieName)).toBe(siteA.id);
    expect(cookies.jar.get(timeZoneCookieName)).toBe('Europe/Zurich');
  });

  test('UX-DR61 an unconfirmed or unknown time zone is not stored', async () => {
    const fake = server(() => json(201, siteA));
    const cookies = new FakeCookies();
    await redirectOf(() => createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'k', timeZone: 'Nowhere/Else' }), cookies, { serverUrl, fetch: fake.fetch }));
    expect(cookies.jar.has(timeZoneCookieName)).toBe(false);
    expect(isTimeZone('Europe/Zurich')).toBe(true);
    expect(isTimeZone('')).toBe(false);
  });

  test.each([
    ['', 'blank'],
    ['   ', 'blank'],
    ['x'.repeat(101), 'tooLong'],
  ] as const)('UX-DR61 an invalid name (%j) is a field error and no request is sent', async (name, nameError) => {
    const fake = server(() => json(201, siteA));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name, idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.status).toBe(400);
    expect(failure.data).toEqual({ name, nameError, notice: null, idempotencyKey: 'key-1' });
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR61 100 characters after trimming is accepted', () => {
    expect(checkSiteName(` ${'x'.repeat(100)} `)).toBeNull();
    expect(checkSiteName('x'.repeat(101))).toBe('tooLong');
  });

  test('UX-DR61 a 400 validation answer maps to the same field error', async () => {
    const fake = server(() => problem(400, 'validation'));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.data).toMatchObject({ nameError: 'invalid', notice: null, idempotencyKey: 'key-1' });
  });

  test('UX-DR61 identity provider down (503): notice, and the retry reuses the same key', async () => {
    const answers = [problem(503, 'identity-provider-unavailable'), json(201, siteA)];
    const fake = server(() => answers.shift() ?? json(500, {}));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.status).toBe(503);
    expect(failure.data).toMatchObject({ notice: 'unavailable', idempotencyKey: 'key-1', name: 'Home' });
    await redirectOf(() => createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: failure.data.idempotencyKey }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(fake.seen.map((seen) => seen.key)).toEqual(['key-1', 'key-1']);
  });

  test('UX-DR61 a reused key (422): notice, and a new key for the next attempt', async () => {
    const fake = server(() => problem(422, 'idempotency-key-reused'));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.status).toBe(422);
    expect(failure.data.notice).toBe('keyReused');
    expect(failure.data.idempotencyKey).not.toBe('key-1');
    expect(failure.data.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/u);
  });

  test('UX-DR61 unreachable and certificate failures keep the key and show the existing notices', async () => {
    for (const [code, notice] of [
      ['ECONNREFUSED', 'unreachable'],
      ['SELF_SIGNED_CERT_IN_CHAIN', 'certificate'],
    ] as const) {
      const failure = await failureOf(
        createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: () => Promise.reject(fetchFailed(code)) }),
      );
      expect(failure.data).toMatchObject({ notice, idempotencyKey: 'key-1' });
    }
  });

  test('UX-DR61 any other Server error says the Site was not created and keeps the key', async () => {
    const fake = server(() => json(500, {}));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'key-1' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.data).toMatchObject({ notice: 'unexpected', idempotencyKey: 'key-1' });
  });

  test('a 401 signs out to Sign in with the signed-out notice', async () => {
    const fake = server(() => problem(401, 'unauthorized'));
    const { location } = await redirectOf(() => createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: 'k' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(location).toContain('/.oidc/signout');
  });

  test('a missing or malformed key is replaced by a new one', async () => {
    const fake = server(() => problem(503, 'identity-provider-unavailable'));
    const failure = await failureOf(createSiteAction(locals, formRequest({ name: 'Home', idempotencyKey: '\u0000' }), new FakeCookies(), { serverUrl, fetch: fake.fetch }));
    expect(failure.data.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/u);
    expect(fake.seen[0]?.key).toBe(failure.data.idempotencyKey);
  });
});

describe('Garden and Site menu models', () => {
  test('UX-DR54 UX-DR82 four steps: Add a Hub next, the rest later; the web never makes them actionable', () => {
    for (const role of ['Owner', 'Administrator', 'Member'] as const) {
      const steps = firstRunSteps(role);
      expect(steps.tiles.map((tile) => [tile.step, tile.number, tile.state])).toEqual([
        ['addHub', 1, 'next'],
        ['addNode', 2, 'later'],
        ['calibrate', 3, 'later'],
        ['setThreshold', 4, 'later'],
      ]);
      expect(steps.actionable).toBe(false);
      expect(steps.memberNotice).toBe(role === 'Member');
    }
    expect(firstRunSteps('Administrator', true).actionable).toBe(true);
    expect(firstRunSteps('Member', true).actionable).toBe(false);
  });

  test('UX-DR22 Site settings always; Pause/Resume only once Pause exists, for Administrator and Owner', () => {
    expect(siteMenuItems('Owner').map((item) => item.action)).toEqual(['settings']);
    expect(siteMenuItems('Member').map((item) => item.action)).toEqual(['settings']);
    expect(siteMenuItems('Owner', { pauseAvailable: true }).map((item) => item.action)).toEqual(['pause', 'settings']);
    expect(siteMenuItems('Administrator', { pauseAvailable: true, paused: true }).map((item) => item.action)).toEqual(['resume', 'settings']);
    expect(siteMenuItems('Member', { pauseAvailable: true }).map((item) => item.action)).toEqual(['settings']);
    expect(siteMenuItems('Owner')[0]).toEqual({ action: 'settings', label: 'siteMenu.settings', disabled: false, href: '/settings/site' });
  });

  test('UX-DR22 in stale mode every item is disabled with Needs your Server', () => {
    const items = siteMenuItems('Owner', { pauseAvailable: true, stale: true });
    expect(items.map((item) => item.disabled)).toEqual([true, true]);
  });
});
