import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import GardenPage from '../../../apps/ts/web/src/routes/(app)/garden/+page.svelte';
import AppShell from '$lib/components/AppShell.svelte';
import SiteSummaryHeader from '$lib/components/SiteSummaryHeader.svelte';
import StaleHeader from '$lib/components/StaleHeader.svelte';
import { createRawSnippet } from 'svelte';
import { lastGoodLoad, onFocusDecision, siteHeadline, staleTransition, webAppProbePath } from '$lib/garden';
import { formatDurationSpoken } from '$lib/i18n/format';
import type { Lot } from '$lib/lots';
import { createLastGoodStore, isTransportFailure, lotsKey, readThrough, sitesKey, userKeyOf } from '$lib/server/last-good';
import { loadGarden } from '$lib/server/lots';
import { loadShell } from '$lib/server/shell';
import { loadSiteSettings } from '$lib/server/site-settings';
import { loadDevices } from '$lib/server/devices';
import type { SitesResult } from '$lib/server/sites';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { filesUnder, read, rel, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: 'user-1', name: 'Simon Novak' } } } };
const otherLocals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-2', idToken: { sub: 'user-2', name: 'Ana' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home garden', role: 'Owner' };
const allotment: Site = { id: '0192a000-0000-7000-8000-00000000000b', name: 'Allotment', role: 'Member' };

function lot(name: string, status: string, fields: Partial<Lot> = {}): Lot {
  return { id: `id-${name}`, name, status: status as Lot['status'], statusSince: '2026-10-06T03:45:00.000Z', ...fields };
}

const tomatoes = lot('Tomatoes', 'needsWater', { lastReadingAt: '2026-10-06T07:02:00.000Z', moisturePercent: 21.7, lowThresholdPercent: 30 });
const cucumbers = lot('Cucumbers', 'needsWater', { moisturePercent: 15 });
const peppers = lot('Peppers', 'needsCalibration');
const beans = lot('Beans', 'unknown', { unknownCause: 'node' });
const peas = lot('Peas', 'unknown', { unknownCause: 'hub' });
const herbs = lot('Herbs', 'ok', { moisturePercent: 35 });
const basil = lot('Basil', 'ok');
const strawberries = lot('Strawberries', 'paused', { pausedBy: ['device'], pausedUntil: '2026-11-01T00:00:00.000Z' });
const potatoes = lot('Potatoes', 'noNode');

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

/** A clock that moves one minute per reading, from 07:00. */
function clock(): () => Date {
  let minute = 0;
  return () => new Date(Date.UTC(2026, 9, 6, 7, minute++, 0));
}

describe('Site headline', () => {
  const headline = (lots: readonly Lot[]) => siteHeadline(lots, 'en-GB', 'UTC');

  test('UX-DR129 no Lot has a Node → No Readings yet with its detail line', () => {
    const empty = { headline: 'No Readings yet', subline: "Nothing is measuring, so there's no status to show.", paused: false };
    expect(headline([])).toEqual(empty);
    expect(headline([potatoes, lot('Leeks', 'noNode')])).toEqual(empty);
  });

  test('UX-DR129 any needs water → the Lot by name, or the count', () => {
    expect(headline([tomatoes, peppers, beans, herbs, potatoes]).headline).toBe('Tomatoes needs water');
    expect(headline([tomatoes, cucumbers, beans]).headline).toBe('2 Lots need water');
  });

  test("UX-DR129 none needs water but some are unknown or need calibration → can't be read", () => {
    expect(headline([peppers, herbs]).headline).toBe("1 Lot can't be read");
    expect(headline([peppers, beans, peas, herbs]).headline).toBe("3 Lots can't be read");
    expect(headline([lot('Leeks', 'flooded'), herbs]).headline).toBe("1 Lot can't be read");
  });

  test('UX-DR129 every Lot with a Node paused by the Site → Paused, with the end when they share one, in paused ink', () => {
    const until = '2027-03-01T00:00:00.000Z';
    const bySite = (name: string, end?: string) => lot(name, 'paused', { pausedBy: ['site'], ...(end === undefined ? {} : { pausedUntil: end }) });
    expect(headline([bySite('A', until), bySite('B', until), potatoes])).toMatchObject({ headline: 'Paused until 1 Mar', paused: true });
    expect(headline([bySite('A', until), bySite('B', '2027-04-01T00:00:00.000Z')])).toMatchObject({ headline: 'Paused', paused: true });
    expect(headline([bySite('A'), bySite('B', until)])).toMatchObject({ headline: 'Paused', paused: true });
    expect(headline([bySite('A')])).toMatchObject({ headline: 'Paused', paused: true });
    // A Device paused on its own is not a paused Site.
    expect(headline([bySite('A', until), strawberries])).toMatchObject({ headline: 'Nothing needs water', paused: false });
  });

  test('UX-DR129 otherwise → Nothing needs water', () => {
    expect(headline([herbs, basil, potatoes])).toMatchObject({ headline: 'Nothing needs water', paused: false });
    expect(headline([herbs, strawberries]).headline).toBe('Nothing needs water');
  });

  test('UX-DR129 the counts subline: non-zero counts in the Server order, without needs water', () => {
    expect(headline([tomatoes, peppers, beans, peas, herbs, basil, strawberries, potatoes]).subline).toBe('1 needs Calibration · 2 unknown · 2 OK · 1 paused · 1 without Node');
    expect(headline([peppers, lot('Chili', 'needsCalibration')]).subline).toBe('2 need Calibration');
    expect(headline([herbs]).subline).toBe('1 OK');
    expect(headline([tomatoes, cucumbers]).subline).toBeNull();
  });

  test('UX-DR129 the headline is a heading; a paused Site uses the paused ink and no subline renders without counts', () => {
    const paused = render(SiteSummaryHeader, { props: { siteName: 'Home', headline: 'Paused until 1 Mar', subline: '2 paused', paused: true } }).body;
    expect(paused).toMatch(/<h2[^>]*class="cf-site-summary__headline[^"]*cf-site-summary__headline--paused[^"]*"[^>]*>Paused until 1 Mar<\/h2>/u);
    expect(read(`${webSrc}/lib/components/SiteSummaryHeader.svelte`)).toMatch(/\.cf-site-summary__headline--paused\s*\{[^}]*color:\s*var\(--cf-color-status-paused-ink\)/u);
    const bare = render(SiteSummaryHeader, { props: { siteName: 'Home', headline: '2 Lots need water', subline: null } }).body;
    expect(bare).not.toContain('cf-site-summary__subline');
    expect(bare).not.toContain('cf-site-summary__headline--paused');
  });

  test('spoken durations use CLDR plurals', () => {
    const minute = 60_000;
    expect([0, minute, 12 * minute, 60 * minute, 6 * 60 * minute, 24 * 60 * minute, 72 * 60 * minute].map(formatDurationSpoken)).toEqual(['0 minutes', '1 minute', '12 minutes', '1 hour', '6 hours', '1 day', '3 days']);
  });
});

describe('Last good data in the BFF', () => {
  test('UX-DR79 the store is keyed by user and Site, bounded, and forgets the least recently used', () => {
    const store = createLastGoodStore(3);
    store.set(sitesKey('user-1'), [home], '2026-10-06T07:00:00.000Z');
    store.set(lotsKey('user-1', home.id), [tomatoes], '2026-10-06T07:01:00.000Z');
    store.set(lotsKey('user-2', home.id), [herbs], '2026-10-06T07:02:00.000Z');
    expect(store.get(lotsKey('user-1', home.id))).toEqual({ value: [tomatoes], fetchedAt: '2026-10-06T07:01:00.000Z' });
    expect(store.get(lotsKey('user-2', home.id))?.value).toEqual([herbs]);
    expect(store.get(lotsKey('user-1', allotment.id))).toBeUndefined();
    // Reading the Sites makes them recent; the fourth entry pushes out the oldest unread one.
    expect(store.get(sitesKey('user-1'))?.value).toEqual([home]);
    store.set(lotsKey('user-1', allotment.id), [], '2026-10-06T07:03:00.000Z');
    expect(store.size).toBe(3);
    expect(store.get(lotsKey('user-1', home.id))).toBeUndefined();
    expect(store.get(sitesKey('user-1'))).toBeDefined();
    store.delete(sitesKey('user-1'));
    expect(store.get(sitesKey('user-1'))).toBeUndefined();
  });

  test('UX-DR79 the user key is the subject of the session; without one nothing is kept', () => {
    expect(userKeyOf(locals)).toBe('user-1');
    expect(userKeyOf({ session: { identity: { authenticated: true, idToken: { name: 'Simon' } } } })).toBeNull();
    expect(userKeyOf({ session: { identity: null } })).toBeNull();
    expect(userKeyOf({})).toBeNull();
  });

  test('UX-DR79 only a Server that did not answer usefully is a transport failure', () => {
    expect((['unreachable', 'unavailable', 'unexpected'] as const).map(isTransportFailure)).toEqual([true, true, true]);
    expect((['certificate', 'unauthorized', 'forbidden', 'notFound', 'validation', 'keyReused', 'lotClaimed'] as const).map(isTransportFailure)).toEqual([false, false, false, false, false, false, false]);
  });

  test('UX-DR79 a success is remembered with its time; one failure is retried and is not stale', async () => {
    const store = createLastGoodStore();
    const answers: SitesResult<string>[] = [{ ok: 'first' }, { error: 'unreachable' }, { ok: 'second' }];
    let calls = 0;
    const read = () => Promise.resolve(answers[calls++] ?? { error: 'unexpected' as const });
    const now = clock();
    expect(await readThrough('k', read, { lastGood: store, now })).toEqual({ ok: 'first', fetchedAt: '2026-10-06T07:00:00.000Z', stale: false });
    expect(await readThrough('k', read, { lastGood: store, now })).toEqual({ ok: 'second', fetchedAt: '2026-10-06T07:01:00.000Z', stale: false });
    expect(calls).toBe(3);
    expect(store.get('k')).toEqual({ value: 'second', fetchedAt: '2026-10-06T07:01:00.000Z' });
  });

  test('UX-DR79 a refresh and one retry failing serves the last good value as stale, with the time of the last success', async () => {
    const store = createLastGoodStore();
    const now = clock();
    let fail = false;
    let calls = 0;
    const read = (): Promise<SitesResult<string>> => {
      calls++;
      return Promise.resolve(fail ? { error: 'unreachable' } : { ok: 'good' });
    };
    await readThrough('k', read, { lastGood: store, now });
    fail = true;
    calls = 0;
    expect(await readThrough('k', read, { lastGood: store, now })).toEqual({ ok: 'good', fetchedAt: '2026-10-06T07:00:00.000Z', stale: true });
    expect(calls).toBe(2);
    // The first success leaves stale mode.
    fail = false;
    expect(await readThrough('k', read, { lastGood: store, now })).toMatchObject({ ok: 'good', stale: false });
  });

  test('UX-DR79 with nothing kept, or no user key, a failure stays a failure', async () => {
    const store = createLastGoodStore();
    const down = (): Promise<SitesResult<string>> => Promise.resolve({ error: 'unavailable' });
    expect(await readThrough('k', down, { lastGood: store })).toEqual({ error: 'unavailable' });
    expect(await readThrough(null, () => Promise.resolve({ ok: 'good' }), { lastGood: store })).toMatchObject({ ok: 'good', stale: false });
    expect(store.size).toBe(0);
    expect(await readThrough(null, down, { lastGood: store })).toEqual({ error: 'unavailable' });
  });

  test('UX-DR79 an answer that is not a transport failure is never retried, never served stale, and drops what was kept', async () => {
    for (const error of ['forbidden', 'notFound', 'unauthorized'] as const) {
      const store = createLastGoodStore();
      store.set('k', 'good', '2026-10-06T07:00:00.000Z');
      let calls = 0;
      const refused = (): Promise<SitesResult<string>> => {
        calls++;
        return Promise.resolve({ error });
      };
      expect(await readThrough('k', refused, { lastGood: store })).toEqual({ error });
      expect(calls).toBe(1);
      expect(store.get('k')).toBeUndefined();
    }
    // An untrusted certificate keeps its own notice (AD-13) and is not retried.
    const store = createLastGoodStore();
    store.set('k', 'good', '2026-10-06T07:00:00.000Z');
    let calls = 0;
    const untrusted = (): Promise<SitesResult<string>> => {
      calls++;
      return Promise.resolve({ error: 'certificate' });
    };
    expect(await readThrough('k', untrusted, { lastGood: store })).toEqual({ error: 'certificate' });
    expect(calls).toBe(1);
  });
});

describe('Stale mode of the Site overview', () => {
  /** A fake Server that answers until `down` is set, then refuses every connection. */
  function server() {
    const state = { down: false, sites: [home, allotment] as readonly Site[], lots: [tomatoes, herbs, potatoes] as readonly Lot[] };
    const fake = fakeServer((request) => {
      if (state.down) {
        return Promise.reject(fetchFailed('ECONNREFUSED'));
      }
      return new URL(request.url).pathname === '/sites' ? jsonResponse(200, { sites: state.sites }) : jsonResponse(200, { lots: state.lots });
    });
    return { state, fake };
  }

  test('UX-DR79 the shell serves the last good Sites on the overview after a refresh and one retry failed', async () => {
    const { state, fake } = server();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore(), now: clock() };
    const garden = new URL('http://x/garden');
    const liveData = await loadShell(locals, garden, new FakeCookies(), dependencies);
    expect(liveData).toMatchObject({ sites: [home, allotment], currentSite: home, sitesNotice: null, sitesStale: null });

    state.down = true;
    fake.seen.length = 0;
    const staleData = await loadShell(locals, garden, new FakeCookies(), dependencies);
    expect(staleData).toMatchObject({ sites: [home, allotment], currentSite: home, sitesNotice: null, sitesStale: '2026-10-06T07:00:00.000Z' });
    expect(fake.seen.map((seen) => seen.path)).toEqual(['/sites', '/sites']);

    state.down = false;
    expect(await loadShell(locals, garden, new FakeCookies(), dependencies)).toMatchObject({ sitesNotice: null, sitesStale: null });
  });

  test('UX-DR79 the last good Sites belong to one user and are lost on a restart', async () => {
    const { state, fake } = server();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore() };
    await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), dependencies);
    state.down = true;
    expect(await loadShell(otherLocals, new URL('http://x/garden'), new FakeCookies(), dependencies)).toMatchObject({ sites: [], sitesNotice: 'unreachable', sitesStale: null });
    // A new process starts with an empty store: the existing notice shows.
    expect(await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), { ...dependencies, lastGood: createLastGoodStore() })).toMatchObject({ sites: [], sitesNotice: 'unreachable', sitesStale: null });
  });

  test('UX-DR79 no stale mode outside the overview: Devices, Site settings and the other pages keep their notices', async () => {
    const { state, fake } = server();
    const store = createLastGoodStore();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: store };
    await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), dependencies);
    await loadGarden(locals, home, new FakeCookies(), null, dependencies);
    state.down = true;
    for (const path of ['/devices', '/settings/site', '/settings', '/alerts', '/members']) {
      fake.seen.length = 0;
      expect(await loadShell(locals, new URL(`http://x${path}`), new FakeCookies(), dependencies), path).toMatchObject({ sites: [], currentSite: null, sitesNotice: 'unreachable', sitesStale: null });
      expect(fake.seen, path).toHaveLength(1);
    }
    expect(await loadSiteSettings(locals, home, dependencies)).toMatchObject({ lots: [], lotsNotice: 'unreachable' });
    expect(await loadDevices(locals, home, new FakeCookies(), dependencies)).toMatchObject({ devices: [], devicesNotice: 'unreachable' });
  });

  test('UX-DR79 a Sites answer from any page is remembered for the overview', async () => {
    const { state, fake } = server();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore(), now: clock() };
    await loadShell(locals, new URL('http://x/devices'), new FakeCookies(), dependencies);
    state.down = true;
    expect(await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), dependencies)).toMatchObject({ sites: [home, allotment], sitesStale: '2026-10-06T07:00:00.000Z' });
  });

  test('UX-DR79 UX-DR20 Garden serves the last good Lots of that Site, in the Server order, with the time they were read', async () => {
    const { state, fake } = server();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore(), now: clock() };
    const cookies = new FakeCookies({ cf_time_zone: 'Europe/Zurich' });
    expect(await loadGarden(locals, home, cookies, null, dependencies)).toEqual({
      lots: [tomatoes, herbs, potatoes],
      lotsNotice: null,
      staleSince: null,
      loadedAt: '2026-10-06T07:00:00.000Z',
      timeZone: 'Europe/Zurich',
    });

    state.down = true;
    fake.seen.length = 0;
    expect(await loadGarden(locals, home, cookies, null, dependencies)).toEqual({
      lots: [tomatoes, herbs, potatoes],
      lotsNotice: null,
      staleSince: '2026-10-06T07:01:00.000Z',
      loadedAt: '2026-10-06T07:02:00.000Z',
      timeZone: 'Europe/Zurich',
    });
    expect(fake.seen.map((seen) => seen.path)).toEqual([`/sites/${siteId}/lots`, `/sites/${siteId}/lots`]);
    // Another Site of the same user has nothing kept: the existing notice.
    expect(await loadGarden(locals, allotment, cookies, null, dependencies)).toMatchObject({ lots: [], lotsNotice: 'unreachable', staleSince: null });

    state.down = false;
    state.lots = [herbs];
    expect(await loadGarden(locals, home, cookies, null, dependencies)).toMatchObject({ lots: [herbs], staleSince: null, lotsNotice: null });
  });

  test('UX-DR79 when the shell is already stale, Garden does not call the Server again', async () => {
    const { state, fake } = server();
    const dependencies = { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore(), now: clock() };
    await loadGarden(locals, home, new FakeCookies(), null, dependencies);
    state.down = true;
    fake.seen.length = 0;
    expect(await loadGarden(locals, home, new FakeCookies(), '2026-10-06T06:00:00.000Z', dependencies)).toMatchObject({ lots: [tomatoes, herbs, potatoes], staleSince: '2026-10-06T07:01:00.000Z', lotsNotice: null });
    // Nothing kept for this Site: the shell's time stands for the page, and the notice replaces the tiles.
    expect(await loadGarden(locals, allotment, new FakeCookies(), '2026-10-06T06:00:00.000Z', dependencies)).toMatchObject({ lots: [], staleSince: '2026-10-06T06:00:00.000Z', lotsNotice: 'unreachable' });
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR80 without last good data an unreachable Server shows the existing notice: no skeleton, no old tiles', async () => {
    const dependencies = { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')), lastGood: createLastGoodStore() };
    expect(await loadShell(locals, new URL('http://x/garden'), new FakeCookies(), dependencies)).toMatchObject({ sites: [], sitesNotice: 'unreachable', sitesStale: null });
    expect(await loadGarden(locals, home, new FakeCookies(), null, dependencies)).toMatchObject({ lots: [], lotsNotice: 'unreachable', staleSince: null });
    const body = gardenPage({ lots: [], lotsNotice: 'unreachable' });
    expect(text(body)).toContain("Can't reach your Coldframe Server.");
    expect(body).not.toMatch(/cf-lot-grid|skeleton|cf-stale-header/u);
  });

  test('a 401 signs out and drops what was kept; a refused Site keeps its notice', async () => {
    const store = createLastGoodStore();
    store.set(lotsKey('user-1', home.id), [tomatoes], '2026-10-06T07:00:00.000Z');
    const expired = { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch, lastGood: store };
    expect((await redirectOf(() => loadGarden(locals, home, new FakeCookies(), null, expired))).location).toContain('/.oidc/signout');
    expect(store.get(lotsKey('user-1', home.id))).toBeUndefined();
    const refused = { serverUrl, fetch: fakeServer(() => problemResponse(403, 'forbidden')).fetch, lastGood: store };
    expect(await loadGarden(locals, home, new FakeCookies(), null, refused)).toMatchObject({ lots: [], lotsNotice: 'unavailable', staleSince: null });
    expect(await loadGarden(locals, null, new FakeCookies(), null, refused)).toMatchObject({ lots: [], lotsNotice: null, staleSince: null });
  });
});

interface GardenFields {
  readonly lots?: readonly Lot[];
  readonly lotsNotice?: 'unreachable' | 'certificate' | 'unavailable' | null;
  readonly staleSince?: string | null;
  readonly site?: Site;
}

function gardenPage(fields: GardenFields = {}): string {
  const site = fields.site ?? home;
  const data = {
    user: { displayName: 'Simon', initials: 'S' },
    theme: 'system',
    sites: [site],
    currentSite: site,
    sitesNotice: null,
    sitesStale: null,
    lots: fields.lots ?? [],
    lotsNotice: fields.lotsNotice ?? null,
    staleSince: fields.staleSince ?? null,
    loadedAt: '2026-10-06T09:14:00.000Z',
    timeZone: 'UTC',
  };
  return render(GardenPage, { props: { data, params: {} } as never }).body;
}

describe('Garden with Lot statuses', () => {
  test('UX-DR129 UX-DR98 the header shows the headline as a heading and the counts', () => {
    const body = gardenPage({ lots: [tomatoes, peppers, beans, herbs, potatoes] });
    expect(body).toMatch(/<h2[^>]*>Tomatoes needs water<\/h2>/u);
    expect(text(body)).toContain('1 needs Calibration · 1 unknown · 1 OK · 1 without Node');
    expect(text(body)).not.toContain('No Readings yet');
  });

  test('UX-DR18 UX-DR20 the grid renders every Lot in the Server order', () => {
    const lots = [tomatoes, peppers, beans, herbs, strawberries, potatoes];
    const body = gardenPage({ lots });
    expect([...body.matchAll(/data-lot="([^"]+)"/gu)].map((match) => match[1])).toEqual(lots.map((target) => target.id));
    expect(body.slice(body.indexOf('cf-lot-grid'))).not.toMatch(/<button/u);
  });

  test('UX-DR24 UX-DR79 in stale mode the stale header replaces the summary header and every tile is stale', () => {
    const body = gardenPage({ lots: [tomatoes, herbs, potatoes], staleSince: '2026-10-06T07:02:00.000Z' });
    expect(body).toContain('cf-stale-header');
    expect(body).not.toContain('cf-site-summary');
    expect(text(body)).toContain("Home garden · can't reach your Server");
    expect(text(body)).toContain('2 h 12 min old');
    expect(text(body)).toContain('Last data 7:02 AM. You may be away from home, or the Server is down. Nothing below is live.');
    expect(body.match(/cf-lot-tile--stale/gu)).toHaveLength(3);
    expect(body).not.toMatch(/cf-lot-tile--(?:needs-water|ok|no-node)/u);
    expect(text(body)).not.toMatch(/Tomatoes needs water|Nothing needs water|~20/u);
  });

  test('UX-DR79 the unit of the page stays honest: live data shows no stale header', () => {
    const body = gardenPage({ lots: [tomatoes] });
    expect(body).not.toContain('cf-stale-header');
    expect(body).not.toContain('cf-lot-tile--stale');
  });

  test('UX-DR112 Garden refetches when the tab gets focus or becomes visible, with no polling', () => {
    const source = read(`${webSrc}/routes/(app)/garden/+page.svelte`);
    expect(source).toMatch(/addEventListener\('focus'/u);
    expect(source).toMatch(/addEventListener\('visibilitychange'/u);
    expect(source).toContain('invalidateAll()');
    expect(source).not.toMatch(/setInterval|setTimeout|EventSource|WebSocket|signalr/iu);
  });
});

describe('Stale header', () => {
  const props = { siteName: 'Home garden', staleSince: new Date('2026-10-06T07:02:00.000Z'), now: new Date('2026-10-06T09:14:00.000Z'), timeZone: 'UTC' };

  test('UX-DR24 cloud--offline, the Site and the reason, the age as the heading, and one explaining line', () => {
    const { body } = render(StaleHeader, { props });
    expect(body).toContain('data-icon="cloud--offline"');
    expect(text(body)).toBe("Home garden · can't reach your Server 2 h 12 min old Last data 7:02 AM. You may be away from home, or the Server is down. Nothing below is live.");
    expect(body).toMatch(/<h2[^>]*class="cf-stale-header__age[^"]*"[^>]*>2 h 12 min old<\/h2>/u);
    const source = read(`${webSrc}/lib/components/StaleHeader.svelte`);
    expect(source).toMatch(/\.cf-stale-header__age\s*\{[^}]*font-family:\s*var\(--cf-type-headline-font-family\)[^}]*color:\s*var\(--cf-color-stale-ink\)/u);
  });

  test('UX-DR24 the age uses min, h min and d', () => {
    const age = (now: string) => /<h2[^>]*>([^<]*)<\/h2>/u.exec(render(StaleHeader, { props: { ...props, now: new Date(now) } }).body)?.[1];
    expect(age('2026-10-06T07:02:30.000Z')).toBe('0 min old');
    expect(age('2026-10-06T07:40:00.000Z')).toBe('38 min old');
    expect(age('2026-10-06T09:02:00.000Z')).toBe('2 h old');
    expect(age('2026-10-08T09:14:00.000Z')).toBe('2 d old');
  });

  test('UX-DR24 UX-DR106 the age ticks once a minute in the browser and is never announced', () => {
    const { body } = render(StaleHeader, { props });
    expect(body).not.toMatch(/aria-live|role="(?:status|alert|timer)"/u);
    const source = read(`${webSrc}/lib/components/StaleHeader.svelte`);
    expect(source).toMatch(/setInterval\([\s\S]*?60_000\)/u);
    expect(source).toContain('clearInterval');
    expect(source).not.toMatch(/announce\(|announcer/u);
    // The stale-age tick is a repeating timer of the web app; the only polling is the Calibrate flow's wait (Story 5.2).
    const repeating = filesUnder(webSrc, ['.svelte', '.ts']).filter((file) => read(file).includes('setInterval')).map(rel);
    expect(repeating).toEqual(['apps/ts/web/src/lib/components/StaleHeader.svelte', 'apps/ts/web/src/routes/(app)/garden/[lotId]/calibrate/+page.svelte']);
  });
});

describe('Stale mode announcements and the Site menu', () => {
  test('UX-DR106 entering stale mode and leaving it are announced once; nothing else is', () => {
    expect(staleTransition(null, 'live')).toBeNull();
    expect(staleTransition(null, 'stale')).toBe('entered');
    expect(staleTransition('live', 'stale')).toBe('entered');
    expect(staleTransition('stale', 'live')).toBe('left');
    // A refetch without a change, and a tick of the age, announce nothing.
    expect(staleTransition('stale', 'stale')).toBeNull();
    expect(staleTransition('live', 'live')).toBeNull();
  });

  test('UX-DR112 UX-DR79 a look at the tab loads again when the web app answers, also after one network error', async () => {
    let asked = 0;
    const answers = (): Promise<unknown> => {
      asked++;
      return Promise.resolve(new Response(null, { status: 500 }));
    };
    // Any HTTP answer, a 500 too, means the web app can be reached.
    expect(await onFocusDecision(answers)).toBe('reload');
    expect(asked).toBe(1);

    asked = 0;
    const failsOnce = (): Promise<unknown> => (++asked === 1 ? Promise.reject(new TypeError('fetch failed')) : Promise.resolve(new Response(null)));
    expect(await onFocusDecision(failsOnce)).toBe('reload');
    expect(asked).toBe(2);
  });

  test('UX-DR79 two network errors mean the web app cannot be reached: one retry, and nothing loads again', async () => {
    let asked = 0;
    const offline = (): Promise<unknown> => {
      asked++;
      return Promise.reject(new TypeError('fetch failed'));
    };
    expect(await onFocusDecision(offline)).toBe('unreachable');
    expect(asked).toBe(2);
  });

  test('UX-DR24 UX-DR79 without the web app the data is as of the last successful load, or older when it was stale already', () => {
    expect(lastGoodLoad({ staleSince: null, loadedAt: '2026-10-06T07:05:00.000Z' })).toBe('2026-10-06T07:05:00.000Z');
    expect(lastGoodLoad({ staleSince: '2026-10-06T07:02:00.000Z', loadedAt: '2026-10-06T07:05:00.000Z' })).toBe('2026-10-06T07:02:00.000Z');
  });

  test('UX-DR112 UX-DR79 the overview asks the web app before it loads again, keeps its Lots when it cannot, and the shell disables the Site menu', () => {
    const source = read(`${webSrc}/routes/(app)/garden/+page.svelte`);
    // A static file of the web app: asking for it reads nothing from the Server.
    expect(webAppProbePath).toBe('/_app/version.json');
    expect(source).toMatch(/onFocusDecision\(\(\) => fetch\(webAppProbePath, \{ cache: 'no-store' \}\)\)/u);
    expect(source).toMatch(/decision === 'unreachable'[\s\S]*webApp\.unreachableSince \?\?= lastGoodLoad\(data\);[\s\S]*return;[\s\S]*await invalidateAll\(\)/u);
    expect(source).toMatch(/webApp\.unreachableSince \?\? data\.staleSince/u);
    const layout = read(`${webSrc}/routes/(app)/+layout.svelte`);
    expect(layout).toMatch(/webApp\.unreachableSince !== null/u);
  });

  test('UX-DR106 the announcements are polite and come from the catalogue', () => {
    const source = read(`${webSrc}/routes/(app)/garden/+page.svelte`);
    expect(source).toMatch(/announce\(t\('stale\.entered', \{ time: [^}]+\}\)\)/u);
    expect(source).toMatch(/announce\(t\('stale\.left'\)\)/u);
    expect(source).not.toContain("'assertive'");
  });

  test('UX-DR79 in stale mode the Site menu of the shell is built disabled', () => {
    const children = createRawSnippet(() => ({ render: () => '<p>content</p>' }));
    const user = { displayName: 'Simon Novak', initials: 'SN' };
    const source = read(`${webSrc}/lib/components/AppHeader.svelte`);
    expect(source).toMatch(/siteMenuItems\(currentSite\.role, \{ stale \}\)/u);
    // The menu is closed in a server render; the trigger stays, so the reason can be read.
    const { body } = render(AppShell, { props: { user, currentPath: '/garden', sites: [home], currentSite: home, stale: true, children } });
    expect(body).toContain('aria-label="Site menu for Home garden"');
    const layout = read(`${webSrc}/routes/(app)/+layout.svelte`);
    expect(layout).toMatch(/stale=\{stale\}/u);
  });
});
