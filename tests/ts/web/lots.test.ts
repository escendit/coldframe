import { isActionFailure, isRedirect, type ActionFailure } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import GardenPage from '../../../apps/ts/web/src/routes/(app)/garden/+page.svelte';
import SettingsPage from '../../../apps/ts/web/src/routes/(app)/settings/+page.svelte';
import SiteSettingsPage from '../../../apps/ts/web/src/routes/(app)/settings/site/+page.svelte';
import LotTiles from '$lib/components/LotTiles.svelte';
import { checkLotName, lotsNoticeOf, siteSettingsOf, type Lot, type SiteSettingsFailure, type SiteSettingsSuccess } from '$lib/lots';
import { createLot, listLots, removeLot, renameLot } from '$lib/server/lots';
import { loadLots, loadSiteSettings, siteSettingsAction } from '$lib/server/site-settings';
import { renameSite } from '$lib/server/sites';
import type { Site } from '$lib/sites';
import { fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { name: 'Simon Novak' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home', role: 'Owner' };
const statusSince = '2026-10-06T07:05:00.000Z';
const now = new Date('2026-10-06T07:17:00.000Z');
const tomatoes: Lot = { id: '0192a000-0000-7000-8000-000000000011', name: 'Tomatoes', status: 'noNode', statusSince };
const beans: Lot = { id: '0192a000-0000-7000-8000-000000000012', name: 'Beans', status: 'noNode', statusSince };
const peppers: Lot = { id: '0192a000-0000-7000-8000-000000000013', name: 'Peppers', status: 'unknown', statusSince, unknownCause: 'node' };

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

async function redirectOf(action: () => unknown): Promise<{ status: number; location: string }> {
  try {
    await action();
  } catch (error) {
    if (isRedirect(error)) {
      return { status: error.status, location: error.location };
    }
    throw error;
  }
  throw new Error('Expected a redirect.');
}

function formRequest(fields: Readonly<Record<string, string>>): Request {
  return new Request('http://localhost/settings/site', { method: 'POST', body: new URLSearchParams(fields) });
}

async function failureOf(result: Promise<SiteSettingsSuccess | ActionFailure<SiteSettingsFailure>>): Promise<SiteSettingsFailure> {
  const value = await result;
  if ('done' in value) {
    throw new Error('Expected a failure.');
  }
  expect(isActionFailure(value)).toBe(true);
  return value.data;
}

const siteFields = { siteId, siteName: 'Home' };

describe('Lot and Site Server calls (AD-14)', () => {
  test('UX-DR20 listLots sends the token and keeps the Server order', async () => {
    const fake = fakeServer(() => jsonResponse(200, { lots: [peppers, tomatoes, beans] }));
    expect(await listLots(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: [peppers, tomatoes, beans] });
    expect(fake.seen).toEqual([{ method: 'GET', path: `/sites/${siteId}/lots`, authorization: 'Bearer access-token-1', key: null, body: '' }]);
  });

  test('createLot sends the Idempotency-Key and {name}', async () => {
    const fake = fakeServer(() => jsonResponse(201, tomatoes));
    expect(await createLot(locals, siteId, 'Tomatoes', 'key-1', { serverUrl, fetch: fake.fetch })).toEqual({ ok: tomatoes });
    expect(fake.seen[0]).toMatchObject({ method: 'POST', path: `/sites/${siteId}/lots`, key: 'key-1' });
    expect(JSON.parse(fake.seen[0]?.body ?? '')).toEqual({ name: 'Tomatoes' });
  });

  test('renameLot and renameSite PATCH {name}; removeLot DELETEs and a 204 is ok', async () => {
    const answers = [jsonResponse(200, { ...tomatoes, name: 'Roma' }), jsonResponse(200, { ...home, name: 'Home garden' }), new Response(null, { status: 204 })];
    const fake = fakeServer(() => answers.shift() ?? jsonResponse(500, {}));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await renameLot(locals, siteId, tomatoes.id, 'Roma', dependencies)).toEqual({ ok: { ...tomatoes, name: 'Roma' } });
    expect(await renameSite(locals, siteId, 'Home garden', dependencies)).toEqual({ ok: { ...home, name: 'Home garden' } });
    expect(await removeLot(locals, siteId, tomatoes.id, dependencies)).toEqual({ ok: undefined });
    expect(fake.seen.map((seen) => `${seen.method} ${seen.path} ${seen.body}`)).toEqual([
      `PATCH /sites/${siteId}/lots/${tomatoes.id} {"name":"Roma"}`,
      `PATCH /sites/${siteId} {"name":"Home garden"}`,
      `DELETE /sites/${siteId}/lots/${tomatoes.id} `,
    ]);
  });

  test.each([
    [403, 'forbidden', 'forbidden'],
    [404, 'lot-not-found', 'notFound'],
    [409, 'lot-claimed', 'lotClaimed'],
    [401, 'unauthorized', 'unauthorized'],
  ] as const)('removeLot: a %i %s answer → %s', async (status, slug, error) => {
    const fake = fakeServer(() => problemResponse(status, slug));
    expect(await removeLot(locals, siteId, tomatoes.id, { serverUrl, fetch: fake.fetch })).toEqual({ error });
  });
});

describe('Loading Lots', () => {
  test('no current Site → no Lots and no call', async () => {
    const fake = fakeServer(() => jsonResponse(200, { lots: [] }));
    expect(await loadLots(locals, null, { serverUrl, fetch: fake.fetch })).toEqual({ lots: [], lotsNotice: null });
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR20 the Lots of the current Site in the Server order, with a fresh Create Lot key per load', async () => {
    const fake = fakeServer(() => jsonResponse(200, { lots: [beans, tomatoes] }));
    const first = await loadSiteSettings(locals, home, { serverUrl, fetch: fake.fetch });
    const second = await loadSiteSettings(locals, home, { serverUrl, fetch: fake.fetch });
    expect(first.lots).toEqual([beans, tomatoes]);
    expect(first.createKey).toMatch(/^[0-9a-f-]{36}$/u);
    expect(second.createKey).not.toBe(first.createKey);
  });

  test('a failure shows the existing notices in place of the Lots; a 401 signs out', async () => {
    const unreachable = await loadLots(locals, home, { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) });
    expect(unreachable).toEqual({ lots: [], lotsNotice: 'unreachable' });
    const certificate = await loadLots(locals, home, { serverUrl, fetch: () => Promise.reject(fetchFailed('DEPTH_ZERO_SELF_SIGNED_CERT')) });
    expect(certificate.lotsNotice).toBe('certificate');
    const down = await loadLots(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(500, 'internal')).fetch });
    expect(down.lotsNotice).toBe('unavailable');
    const expired = await redirectOf(() => loadLots(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
    expect(lotsNoticeOf('certificate')).toEqual({ message: 'notice.certificate', tryAgain: false });
    expect(lotsNoticeOf('unreachable')?.tryAgain).toBe(true);
  });
});

describe('Site settings actions', () => {
  test('UX-DR74 Rename Site sends the trimmed name and succeeds', async () => {
    const fake = fakeServer(() => jsonResponse(200, { ...home, name: 'Home garden' }));
    const result = await siteSettingsAction('renameSite', locals, formRequest({ ...siteFields, name: '  Home garden ' }), { serverUrl, fetch: fake.fetch });
    expect(result).toEqual({ action: 'renameSite', done: true });
    expect(JSON.parse(fake.seen[0]?.body ?? '')).toEqual({ name: 'Home garden' });
  });

  test('UX-DR74 a blank or too long name stays on the field and nothing reaches the Server', async () => {
    const fake = fakeServer(() => jsonResponse(200, home));
    const blank = await failureOf(siteSettingsAction('renameSite', locals, formRequest({ ...siteFields, name: '  ' }), { serverUrl, fetch: fake.fetch }));
    expect(blank).toMatchObject({ action: 'renameSite', nameError: 'blank', notice: null });
    const long = await failureOf(siteSettingsAction('createLot', locals, formRequest({ ...siteFields, name: 'x'.repeat(101), idempotencyKey: 'k' }), { serverUrl, fetch: fake.fetch }));
    expect(long).toMatchObject({ action: 'createLot', nameError: 'tooLong', idempotencyKey: 'k' });
    expect(fake.seen).toEqual([]);
    expect(checkLotName('x'.repeat(100))).toBeNull();
  });

  test('UX-DR74 a 400 validation answer maps to the field error', async () => {
    const fake = fakeServer(() => problemResponse(400, 'validation'));
    const failure = await failureOf(siteSettingsAction('renameLot', locals, formRequest({ ...siteFields, lotId: tomatoes.id, lotName: 'Tomatoes', name: 'Roma' }), { serverUrl, fetch: fake.fetch }));
    expect(failure).toMatchObject({ action: 'renameLot', lotId: tomatoes.id, nameError: 'invalid' });
  });

  test('UX-DR84 a 403 race names the Site and changes nothing', async () => {
    const fake = fakeServer(() => problemResponse(403, 'forbidden'));
    const failure = await failureOf(siteSettingsAction('renameSite', locals, formRequest({ ...siteFields, name: 'Home garden' }), { serverUrl, fetch: fake.fetch }));
    expect(failure).toMatchObject({ notice: 'forbidden', siteName: 'Home', name: 'Home garden' });
  });

  test('UX-DR74 Keycloak down (503) on Rename Site says the Site was not renamed', async () => {
    const fake = fakeServer(() => problemResponse(503, 'identity-provider-unavailable'));
    const failure = await failureOf(siteSettingsAction('renameSite', locals, formRequest({ ...siteFields, name: 'Home garden' }), { serverUrl, fetch: fake.fetch }));
    expect(failure.notice).toBe('unavailable');
  });

  test('UX-DR74 Create Lot: the same key after a failure, a new key after a reused key', async () => {
    const answers: (() => Response | Promise<Response>)[] = [
      () => problemResponse(503, 'unavailable'),
      () => Promise.reject(fetchFailed('ECONNREFUSED')),
      () => problemResponse(422, 'idempotency-key-reused'),
      () => jsonResponse(201, tomatoes),
    ];
    const fake = fakeServer(() => (answers.shift() ?? (() => jsonResponse(500, {})))());
    const dependencies = { serverUrl, fetch: fake.fetch };
    const request = (key: string) => formRequest({ ...siteFields, name: 'Tomatoes', idempotencyKey: key });

    const down = await failureOf(siteSettingsAction('createLot', locals, request('key-1'), dependencies));
    expect(down).toMatchObject({ notice: 'unexpected', idempotencyKey: 'key-1' });
    const unreachable = await failureOf(siteSettingsAction('createLot', locals, request('key-1'), dependencies));
    expect(unreachable).toMatchObject({ notice: 'unreachable', idempotencyKey: 'key-1' });
    const reused = await failureOf(siteSettingsAction('createLot', locals, request('key-1'), dependencies));
    expect(reused.notice).toBe('keyReused');
    expect(reused.idempotencyKey).not.toBe('key-1');
    expect(reused.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/u);
    expect(await siteSettingsAction('createLot', locals, request(reused.idempotencyKey ?? ''), dependencies)).toEqual({ action: 'createLot', done: true });
    expect(fake.seen.map((seen) => seen.key)).toEqual(['key-1', 'key-1', 'key-1', reused.idempotencyKey]);
  });

  test('UX-DR74 Remove Lot holding a Node (409) names the Lot; an unknown Lot (404) says so', async () => {
    const claimed = await failureOf(
      siteSettingsAction('removeLot', locals, formRequest({ ...siteFields, lotId: tomatoes.id, lotName: 'Tomatoes' }), { serverUrl, fetch: fakeServer(() => problemResponse(409, 'lot-claimed')).fetch }),
    );
    expect(claimed).toMatchObject({ action: 'removeLot', lotId: tomatoes.id, lotName: 'Tomatoes', notice: 'lotClaimed' });
    const gone = await failureOf(
      siteSettingsAction('removeLot', locals, formRequest({ ...siteFields, lotId: tomatoes.id, lotName: 'Tomatoes' }), { serverUrl, fetch: fakeServer(() => problemResponse(404, 'lot-not-found')).fetch }),
    );
    expect(gone.notice).toBe('lotNotFound');
  });

  test('UX-DR74 Remove Lot with no Node sends DELETE and succeeds', async () => {
    const fake = fakeServer(() => new Response(null, { status: 204 }));
    const result = await siteSettingsAction('removeLot', locals, formRequest({ ...siteFields, lotId: tomatoes.id, lotName: 'Tomatoes' }), { serverUrl, fetch: fake.fetch });
    expect(result).toEqual({ action: 'removeLot', done: true });
    expect(fake.seen.map((seen) => `${seen.method} ${seen.path}`)).toEqual([`DELETE /sites/${siteId}/lots/${tomatoes.id}`]);
  });

  test('a 401 signs out to Sign in with the signed-out notice', async () => {
    const fake = fakeServer(() => problemResponse(401, 'unauthorized'));
    const { location } = await redirectOf(() => siteSettingsAction('renameSite', locals, formRequest({ ...siteFields, name: 'Home garden' }), { serverUrl, fetch: fake.fetch }));
    expect(location).toContain('/.oidc/signout');
  });
});

function siteSettings(role: Site['role'], lots: readonly Lot[], form: SiteSettingsFailure | null = null): string {
  const site = { ...home, role };
  const data = {
    user: { displayName: 'Simon', initials: 'S' },
    theme: 'system',
    sites: [site],
    currentSite: site,
    sitesNotice: null,
    lots,
    lotsNotice: null,
    createKey: 'create-key-1',
  };
  return render(SiteSettingsPage, { props: { data, form, params: {} } as never }).body;
}

function failure(fields: Partial<SiteSettingsFailure>): SiteSettingsFailure {
  return { action: 'renameSite', lotId: null, name: '', nameError: null, notice: null, siteName: 'Home', lotName: null, idempotencyKey: null, ...fields };
}

describe('Site settings surface', () => {
  test('UX-DR74 UX-DR84 an Owner renames the Site and manages Lots', () => {
    const body = siteSettings('Owner', [tomatoes, beans]);
    expect(body).toMatch(/<h1[^>]*>Site settings<\/h1>/u);
    expect(body).toMatch(/<input[^>]*id="cf-site-name"[^>]*value="Home"/u);
    expect(text(body)).toContain('Rename Site');
    expect(text(body)).toContain('Create Lot');
    expect(body.match(/>Rename Lot</gu)).toHaveLength(2);
    expect(body.match(/>Remove Lot</gu)?.length).toBeGreaterThanOrEqual(2);
    expect(body).toContain('value="create-key-1"');
    expect(text(body)).not.toContain('Only Owners and Administrators can change Lots.');
  });

  test('UX-DR84 an Administrator manages Lots but sees the Site name as text, with no rename control', () => {
    const body = siteSettings('Administrator', [tomatoes]);
    expect(body).not.toContain('id="cf-site-name"');
    expect(text(body)).not.toContain('Rename Site');
    expect(body).toContain('data-site-name');
    expect(text(body)).toContain('Create Lot');
    expect(text(body)).toContain('Rename Lot');
  });

  test('UX-DR84 a Member sees the Site and Lots read-only with one notice, and no controls at all', () => {
    const body = siteSettings('Member', [tomatoes, beans]);
    expect(text(body)).toContain('Only Owners and Administrators can change Lots.');
    expect(body.match(/Only Owners and Administrators/gu)).toHaveLength(1);
    expect(body).not.toMatch(/<form|<button|<input/u);
    expect(text(body)).toMatch(/Tomatoes.*Beans/u);
  });

  test('UX-DR20 Lots are listed in the Server order, never re-sorted', () => {
    const body = siteSettings('Member', [peppers, tomatoes, beans]);
    expect([...body.matchAll(/data-lot="([^"]+)"/gu)].map((match) => match[1])).toEqual([peppers.id, tomatoes.id, beans.id]);
  });

  test('UX-DR74 with no Lots it says so', () => {
    expect(text(siteSettings('Owner', []))).toContain('No Lots yet.');
  });

  test('UX-DR74 the remove confirmation names the Lot and keeps history', () => {
    const body = siteSettings('Owner', [tomatoes]);
    expect(body).toMatch(/<dialog[^>]*class="cf-modal[ "]/u);
    expect(text(body)).toContain('Its history stays in Coldframe.');
  });

  test('UX-DR84 a 403 race shows the Owner or Administrator copy naming the Site', () => {
    const body = siteSettings('Owner', [], failure({ notice: 'forbidden', name: 'Home garden' }));
    expect(text(body)).toContain("You can't change this on Home. Ask an Owner or Administrator.");
    expect(body).toMatch(/<input[^>]*id="cf-site-name"[^>]*value="Home garden"/u);
  });

  test('UX-DR74 a Lot holding a Node shows Move or unassign the Node on that Lot first, in its row', () => {
    const body = siteSettings('Administrator', [tomatoes, beans], failure({ action: 'removeLot', lotId: tomatoes.id, lotName: 'Tomatoes', notice: 'lotClaimed' }));
    expect(text(body)).toContain('Move or unassign the Node on Tomatoes first.');
    const row = body.slice(body.indexOf(`data-lot="${tomatoes.id}"`), body.indexOf(`data-lot="${beans.id}"`));
    expect(row).toContain('Move or unassign the Node on Tomatoes first.');
  });

  test('UX-DR74 a field error sits under the field, and Create Lot keeps the key the retry must use', () => {
    const body = siteSettings('Owner', [], failure({ action: 'createLot', name: '', nameError: 'blank', idempotencyKey: 'retry-key' }));
    expect(body).toMatch(/id="cf-new-lot-name"[^>]*aria-invalid="true"|aria-invalid="true"[^>]*id="cf-new-lot-name"/u);
    expect(text(body)).toContain('Enter a name for the Lot.');
    expect(body).toContain('value="retry-key"');
    expect(body).not.toContain('value="create-key-1"');
  });

  test('UX-DR74 the Settings index lists Site settings first for the current Site', () => {
    const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null };
    const body = render(SettingsPage, { props: { data, params: {} } as never }).body;
    const siteRow = body.indexOf('href="/settings/site"');
    expect(siteRow).toBeGreaterThan(-1);
    expect(siteRow).toBeLessThan(body.indexOf('href="/settings/appearance"'));
    expect(text(body)).toContain('Name and Lots of Home');
  });

  test('access by Role', () => {
    expect(siteSettingsOf('Owner')).toEqual({ canRenameSite: true, canEditLots: true, readOnlyNotice: false });
    expect(siteSettingsOf('Administrator')).toEqual({ canRenameSite: false, canEditLots: true, readOnlyNotice: false });
    expect(siteSettingsOf('Member')).toEqual({ canRenameSite: false, canEditLots: false, readOnlyNotice: true });
  });
});

function garden(lots: readonly Lot[], lotsNotice: 'unreachable' | 'certificate' | 'unavailable' | null = null): string {
  const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null, sitesStale: null, lots, lotsNotice, staleSince: null, loadedAt: now.toISOString(), timeZone: 'UTC' };
  return render(GardenPage, { props: { data, params: {} } as never }).body;
}

describe('Lot tiles on Garden', () => {
  test('UX-DR18 a no-Node tile: name, add icon, a large +, foot add a Node, dotted and transparent, one accessible element', () => {
    const { body } = render(LotTiles, { props: { lots: [tomatoes], now, timeZone: 'UTC' } });
    expect(body).toMatch(/<a class="cf-lot-tile cf-lot-tile--no-node[^"]*" href="\/garden\/[^"]+" aria-label="Tomatoes, no Node, add a Node"/u);
    expect(body).toContain('data-icon="add"');
    expect(text(body)).toMatch(/Tomatoes No Node \+ add a Node/u);
    expect(body.match(/<a |<button/gu)).toHaveLength(1);
  });

  test('UX-DR18 a Lot whose Node has not reported is unknown, never fine: hatched, help, how long, no Readings yet', () => {
    const { body } = render(LotTiles, { props: { lots: [peppers], now, timeZone: 'UTC' } });
    expect(text(body)).toBe('Peppers Silent · unknown 12 min no Readings yet');
    expect(body).toContain('data-icon="help"');
    expect(body).toContain('aria-label="Peppers, unknown, Node silent for 12 minutes, no Readings yet"');
    expect(text(body)).not.toMatch(/\bOK\b/u);
  });

  test('UX-DR20 tiles keep the Server order and are each one link to Lot detail', () => {
    const body = garden([peppers, tomatoes, beans]);
    expect([...body.matchAll(/<li class="cf-lot-grid__cell[^"]*" data-lot="([^"]+)"/gu)].map((match) => match[1])).toEqual([peppers.id, tomatoes.id, beans.id]);
    const grid = body.slice(body.indexOf('cf-lot-grid'));
    expect(grid.match(/<a /gu)).toHaveLength(3);
    expect(grid).not.toMatch(/<button/u);
  });

  test('UX-DR18 the Lot grid sits below the empty-Site header, steps and notice', () => {
    const body = garden([tomatoes, beans]);
    const order = ['No Readings yet', 'cf-first-run', 'cf-garden-notice', 'cf-lot-grid'].map((marker) => body.indexOf(marker));
    expect(order.every((position) => position > -1)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
    expect(text(body)).toContain('Tomatoes No Node + add a Node Beans No Node + add a Node');
  });

  test('UX-DR18 no Lots → no grid; a load failure shows the existing notice in place of the grid', () => {
    expect(garden([])).not.toContain('cf-lot-grid');
    const failed = garden([], 'unreachable');
    expect(text(failed)).toContain("Can't reach your Coldframe Server.");
    expect(failed).not.toContain('cf-lot-grid');
    expect(text(garden([], 'certificate'))).not.toContain('Try again');
  });
});
