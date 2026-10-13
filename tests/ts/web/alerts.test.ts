import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import AlertsPage from '../../../apps/ts/web/src/routes/(app)/alerts/+page.svelte';
import { alertGroups, alertRow, alertsNoticeOf, healthAlertRow, type Alert, type AlertsContext, type AlertsNotice, type HealthAlert } from '$lib/alerts';
import AlertRows from '$lib/components/AlertRows.svelte';
import Icon from '$lib/components/Icon.svelte';
import { messages } from '$lib/i18n';
import { alertsPageCap, alertsPageLimit, listAlerts, loadAlerts } from '$lib/server/alerts';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { read, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { name: 'Simon Novak' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home', role: 'Member' };
const now = new Date('2026-10-09T07:17:00.000Z');
const context: AlertsContext = { now, locale: 'en-GB', timeZone: 'UTC' };

function alert(id: string, lotName: string, fields: Partial<Alert> = {}): Alert {
  return { id, kind: 'threshold', side: 'low', quantity: 'soil_moisture', lotId: `lot-${lotName}`, lotName, deviceId: '3f2a9c0d1e4b5a67', openedAt: '2026-10-09T05:45:00.000Z', ...fields };
}

const tomatoes = alert('a-1', 'Tomatoes');
const herbs = alert('a-2', 'Herbs', { side: 'high', openedAt: '2026-10-09T05:10:00.000Z' });
const peppers = alert('a-3', 'Peppers', { quantity: 'air_temperature', side: 'low', openedAt: '2026-10-09T04:00:00.000Z' });
const beans = alert('a-4', 'Beans', { quantity: 'relative_humidity', side: 'high', openedAt: '2026-10-08T12:00:00.000Z' });
const silent = alert('a-5', 'Peas', { kind: 'silent', side: undefined, openedAt: '2026-10-09T06:00:00.000Z' });
const battery = alert('a-6', 'Lettuce', { kind: 'battery', side: undefined, openedAt: '2026-10-09T03:00:00.000Z' });
const uncalibrated = alert('a-7', 'Potatoes', { kind: 'uncalibrated', side: undefined, openedAt: '2026-10-09T02:00:00.000Z' });
const novel = alert('a-8', 'Basil', { kind: 'frost', side: undefined, openedAt: '2026-10-09T01:00:00.000Z' });
const closedWater = alert('a-9', 'Strawberries', { openedAt: '2026-10-09T05:45:00.000Z', closedAt: '2026-10-09T06:40:00.000Z', reason: 'recovered' });
const closedHigh = alert('a-10', 'Chard', { quantity: 'air_temperature', side: 'high', openedAt: '2026-10-07T10:00:00.000Z', closedAt: '2026-10-07T13:00:00.000Z', reason: 'recovered' });
const closedSilent = alert('a-11', 'Kale', { kind: 'silent', side: undefined, openedAt: '2026-09-20T10:00:00.000Z', closedAt: '2026-10-08T13:00:00.000Z', reason: 'removed' });

/** In the Server's order: open newest first, then closed newest first. */
const all = [silent, tomatoes, herbs, peppers, battery, uncalibrated, novel, beans, closedWater, closedSilent, closedHigh];

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

function page(alerts: readonly Alert[], alertsNotice: AlertsNotice | null = null, timeZone: string | null = 'UTC', healthAlerts?: readonly HealthAlert[]): string {
  const openCount = [...alerts, ...(healthAlerts ?? [])].filter((item) => item.closedAt === undefined).length;
  // Without `healthAlerts` the data is what a Server before Story 7.2 gave: the page renders as it did.
  const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null, alerts, ...(healthAlerts === undefined ? {} : { healthAlerts }), openCount, alertsNotice, loadedAt: now.toISOString(), timeZone };
  return render(AlertsPage, { props: { data, params: {} } as never }).body;
}

/** The markup of one Alert's list item. */
function item(body: string, id: string): string {
  const match = new RegExp(`<li[^>]*data-alert="${id}"[\\s\\S]*?</li>`, 'u').exec(body);
  if (match === null) {
    throw new Error(`No row for ${id}.`);
  }
  return match[0];
}

function ids(body: string): string[] {
  return [...body.matchAll(/data-alert="([^"]+)"/gu)].map((match) => match[1] ?? '');
}

describe('The Alert row model', () => {
  test('UX-DR14 UX-DR25 needs water only for an open, low-side, soil-moisture Threshold Alert: rain-drop, "Needs water" and the time', () => {
    expect(alertRow(tomatoes, context)).toEqual({
      id: 'a-1',
      variant: 'needsWater',
      icon: 'rain-drop',
      eyebrow: 'Needs water · 05:45',
      title: 'Tomatoes needs water',
      spoken: 'Tomatoes needs water, since 05:45',
      href: '/garden/lot-Tomatoes',
      hatched: false,
    });
    // Every other open Alert is something else, whatever its quantity, side or kind.
    for (const other of [herbs, peppers, beans, silent, battery, uncalibrated, novel]) {
      expect(alertRow(other, context).variant, other.lotName).not.toBe('needsWater');
    }
  });

  test('UX-DR14 UX-DR25 too wet is the neutral Threshold row with arrow--up, never needs water', () => {
    const row = alertRow(herbs, context);
    expect(row.variant).toBe('threshold');
    expect(row.icon).toBe('arrow--up');
    expect(row.eyebrow).toBe('Threshold Alert · above high · 05:10');
    expect(row.title).toBe('Herbs too wet');
    expect(row.spoken).toBe('Herbs too wet, since 05:10');
    expect(row.href).toBe('/garden/lot-Herbs');
  });

  test('UX-DR25 another quantity names itself and its side: arrow--down below low, arrow--up above high', () => {
    const low = alertRow(peppers, context);
    expect([low.variant, low.icon, low.eyebrow, low.title]).toEqual(['threshold', 'arrow--down', 'Threshold Alert · below low · 04:00', 'Peppers temperature too low']);
    // Earlier than today: the weekday, by the "when" rule.
    const high = alertRow(beans, context);
    expect([high.variant, high.icon, high.eyebrow, high.title]).toEqual(['threshold', 'arrow--up', 'Threshold Alert · above high · Thu', 'Beans humidity too high']);
    expect(alertRow({ ...peppers, quantity: 'gas_resistance' }, context).title).toBe('Peppers air (gas) too low');
  });

  test('UX-DR25 Health rows are hatched, with an icon by cause and the eyebrow "Health Alert"', () => {
    const rows = [silent, battery, uncalibrated].map((health) => alertRow(health, context));
    expect(rows.map((row) => row.variant)).toEqual(['health', 'health', 'health']);
    expect(rows.map((row) => row.hatched)).toEqual([true, true, true]);
    expect(rows.map((row) => row.icon)).toEqual(['help', 'battery--low', 'tools']);
    expect(rows.map((row) => row.eyebrow)).toEqual(['Health Alert', 'Health Alert', 'Health Alert']);
    expect(rows.map((row) => row.title)).toEqual(["Node on Lot 'Peas' silent", "Node battery low on Lot 'Lettuce'", "Soil Sensor on Lot 'Potatoes' needs Calibration"]);
    expect(rows[0]?.spoken).toBe("Node on Lot 'Peas' silent, since 06:00");
  });

  test('UX-DR25 a kind this client does not know is a Health row with the help icon', () => {
    const row = alertRow(novel, context);
    expect([row.variant, row.icon, row.eyebrow, row.hatched]).toEqual(['health', 'help', 'Health Alert', true]);
    expect(row.title).toBe("Health Alert on Lot 'Basil'");
    expect(row.href).toBe('/devices');
    // A Threshold Alert without a side this client knows claims no side either.
    const sideless = alertRow({ ...tomatoes, side: undefined }, context);
    expect([sideless.variant, sideless.icon]).toEqual(['health', 'help']);
  });

  test('UX-DR25 a Threshold Alert of a quantity this client does not know is the unknown Health row, never the raw name', () => {
    const row = alertRow({ ...peppers, quantity: 'wind_speed' as Alert['quantity'] }, context);
    expect([row.variant, row.icon, row.eyebrow, row.hatched, row.href]).toEqual(['health', 'help', 'Health Alert', true, '/devices']);
    expect(row.title).toBe("Health Alert on Lot 'Peppers'");
    expect(row.spoken).not.toContain('wind_speed');
    // Not a property of the quantity table either.
    expect(alertRow({ ...peppers, quantity: 'constructor' as Alert['quantity'] }, context).variant).toBe('health');
  });

  test('UX-DR14 UX-DR25 a closed Alert is the Closed row whatever it was: its eyebrow ends with "closed" and the time', () => {
    const water = alertRow(closedWater, context);
    expect(water).toEqual({
      id: 'a-9',
      variant: 'closed',
      icon: 'rain-drop',
      eyebrow: 'Needs water · closed 06:40',
      title: 'Strawberries needs water',
      spoken: 'Strawberries needs water, since 05:45, closed 06:40',
      href: '/garden/lot-Strawberries',
      hatched: false,
    });
    expect(alertRow(closedHigh, context).eyebrow).toBe('Threshold Alert · above high · closed Wed');
    const health = alertRow(closedSilent, context);
    expect([health.variant, health.hatched, health.eyebrow]).toEqual(['closed', false, 'Health Alert · closed Thu']);
    // Older than 7 days: the date.
    expect(health.spoken).toBe("Node on Lot 'Kale' silent, since 20 Sept, closed Thu");
  });

  test('UX-DR26 Threshold and uncalibrated rows open Lot detail; silent and battery rows open Devices', () => {
    expect([tomatoes, herbs, peppers, uncalibrated, closedHigh].map((item) => alertRow(item, context).href)).toEqual([
      '/garden/lot-Tomatoes',
      '/garden/lot-Herbs',
      '/garden/lot-Peppers',
      '/garden/lot-Potatoes',
      '/garden/lot-Chard',
    ]);
    expect([silent, battery, closedSilent].map((item) => alertRow(item, context).href)).toEqual(['/devices', '/devices', '/devices']);
  });

  test('UX-DR26 UX-DR64 open Alerts are grouped Threshold then Health, each in the order the Server sent, then Closed', () => {
    const groups = alertGroups(all, context);
    expect(groups.threshold.map((row) => row.id)).toEqual(['a-1', 'a-2', 'a-3', 'a-4']);
    expect(groups.health.map((row) => row.id)).toEqual(['a-5', 'a-6', 'a-7', 'a-8']);
    expect(groups.closed.map((row) => row.id)).toEqual(['a-9', 'a-11', 'a-10']);
    expect(alertGroups([], context)).toEqual({ threshold: [], health: [], closed: [] });
  });

  test('the row carries no value, Threshold or Reading time (DW-88): only the Lot, the condition and when', () => {
    for (const row of all.map((item) => alertRow(item, context))) {
      expect(`${row.eyebrow} ${row.title} ${row.spoken}`).not.toMatch(/%|~|°C|kΩ/u);
    }
  });
});

describe('The Alert rows', () => {
  function rows(alerts: readonly Alert[]): string {
    const groups = alertGroups(alerts, context);
    return render(AlertRows, { props: { rows: [...groups.threshold, ...groups.health, ...groups.closed], labelledBy: 'heading' } }).body;
  }

  test('UX-DR98 UX-DR26 a row is one link with one label stating the condition and when it started, and nothing else to act on', () => {
    const body = rows(all);
    const row = item(body, 'a-1');
    expect(row).toContain('href="/garden/lot-Tomatoes"');
    expect(row).toContain('aria-label="Tomatoes needs water, since 05:45"');
    expect(item(body, 'a-9')).toContain('aria-label="Strawberries needs water, since 05:45, closed 06:40"');
    expect(item(body, 'a-5')).toContain('href="/devices"');
    expect([...body.matchAll(/<a /gu)]).toHaveLength(all.length);
    // No actions on Alerts: no mark-watered, no dismiss, nothing but the link.
    expect(body).not.toMatch(/<button|<form|<input|<select/u);
  });

  test('UX-DR25 each variant has its own class, icon and eyebrow; uppercase comes from style', () => {
    const body = rows(all);
    expect(item(body, 'a-1')).toContain('cf-alert-row--needs-water');
    expect(item(body, 'a-1')).toContain('data-icon="rain-drop"');
    expect(item(body, 'a-2')).toContain('cf-alert-row--threshold');
    expect(item(body, 'a-2')).toContain('data-icon="arrow--up"');
    expect(item(body, 'a-3')).toContain('data-icon="arrow--down"');
    expect(item(body, 'a-5')).toContain('cf-alert-row--health');
    expect(item(body, 'a-5')).toContain('cf-hatch--plate');
    expect(item(body, 'a-6')).toContain('data-icon="battery--low"');
    expect(item(body, 'a-7')).toContain('data-icon="tools"');
    expect(item(body, 'a-9')).toContain('cf-alert-row--closed');
    expect(item(body, 'a-11')).not.toContain('cf-hatch');
    expect(text(item(body, 'a-2'))).toBe('Threshold Alert · above high · 05:10 Herbs too wet');
    expect(body).not.toMatch(/NEEDS WATER|THRESHOLD ALERT|HEALTH ALERT|CLOSED/u);

    const css = read(`${webSrc}/lib/components/AlertRows.svelte`);
    expect(css).toMatch(/\.cf-alert-row__eyebrow\s*\{[^}]*text-transform:\s*uppercase/u);
    expect(css).toMatch(/\.cf-alert-row__title\s*\{[^}]*--cf-type-section-font-size/u);
  });

  test('UX-DR14 orange fills the needs-water row and no other', () => {
    const css = (/<style>([\s\S]*)<\/style>/u.exec(read(`${webSrc}/lib/components/AlertRows.svelte`))?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//gu, '');
    const orange = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/gu)].filter((rule) => /status-water|--cf-color-primary(?:-hover|-active)?\)/u.test(rule[2] ?? '')).map((rule) => (rule[1] ?? '').trim());
    expect(orange).toEqual(['.cf-alert-row--needs-water']);
    expect(css).toMatch(/\.cf-alert-row--threshold\s*\{[^}]*--cf-color-layer-01[^}]*2px solid var\(--cf-color-border-strong\)/u);
    expect(css).toMatch(/\.cf-alert-row--health\s*\{[^}]*1px dashed var\(--cf-color-status-unknown-border\)/u);
    expect(css).toMatch(/\.cf-alert-row--closed\s*\{[^}]*transparent[^}]*1px solid var\(--cf-color-border-subtle\)[^}]*--cf-color-text-secondary/u);
  });

  test('the Threshold arrows are Carbon icons of the vendored set', () => {
    for (const name of ['arrow--up', 'arrow--down'] as const) {
      const { body } = render(Icon, { props: { name } });
      expect(body).toContain(`data-icon="${name}"`);
      expect(body).toContain('<svg');
    }
  });
});

describe('The Alerts page', () => {
  test('UX-DR64 UX-DR26 Threshold Alerts, then Health Alerts, then Closed, each newest first', () => {
    const body = page(all);
    expect(ids(body)).toEqual(['a-1', 'a-2', 'a-3', 'a-4', 'a-5', 'a-6', 'a-7', 'a-8', 'a-9', 'a-11', 'a-10']);
    const headings = [...body.matchAll(/<h2[^>]*>([^<]*)<\/h2>/gu)].map((match) => match[1]);
    expect(headings).toEqual(['Threshold Alerts', 'Health Alerts', 'Closed']);
    expect(body.indexOf('Threshold Alerts')).toBeLessThan(body.indexOf('data-alert="a-1"'));
    expect(body.indexOf('data-alert="a-4"')).toBeLessThan(body.indexOf('Health Alerts'));
    expect(body.indexOf('data-alert="a-8"')).toBeLessThan(body.indexOf('>Closed<'));
    expect(text(body)).not.toContain('No open Alerts.');
  });

  test('UX-DR82 no Alerts: "No open Alerts." and never "All good"', () => {
    const body = page([]);
    expect(text(body)).toBe('Alerts No open Alerts.');
    expect(ids(body)).toEqual([]);
    expect(body).not.toMatch(/<h2/u);
    for (const [key, value] of Object.entries(messages())) {
      expect(typeof value === 'string' ? value : Object.values(value).join(' '), key).not.toMatch(/all good/iu);
    }
  });

  test('UX-DR82 UX-DR64 only closed Alerts: "No open Alerts." and then Closed with its rows', () => {
    const body = page([closedWater, closedHigh]);
    expect(text(body)).toMatch(/^Alerts No open Alerts\. Closed /u);
    expect(ids(body)).toEqual(['a-9', 'a-10']);
    expect(body.indexOf('No open Alerts.')).toBeLessThan(body.indexOf('>Closed<'));
    expect([...body.matchAll(/<h2[^>]*>([^<]*)<\/h2>/gu)].map((match) => match[1])).toEqual(['Closed']);
  });

  test('only Health Alerts open: no Threshold section and no empty state', () => {
    const body = page([silent]);
    expect([...body.matchAll(/<h2[^>]*>([^<]*)<\/h2>/gu)].map((match) => match[1])).toEqual(['Health Alerts']);
    expect(text(body)).not.toContain('No open Alerts.');
  });

  test('a load that failed shows the notice with Try again and no rows; an untrusted certificate offers no retry', () => {
    const unreachable = page([], 'unreachable');
    expect(text(unreachable)).toBe("Alerts Can't reach your Server. Try again");
    expect(unreachable).toContain('href="/alerts"');
    expect(text(unreachable)).not.toContain('No open Alerts.');
    expect(alertsNoticeOf('unreachable')).toEqual({ message: 'alerts.unreachable', tryAgain: true });
    expect(alertsNoticeOf('certificate')).toEqual({ message: 'notice.certificate', tryAgain: false });
    expect(alertsNoticeOf(null)).toBeNull();
    expect(text(page([], 'certificate'))).not.toContain('Try again');
  });

  test('times are told in the chosen time zone', () => {
    expect(text(page([tomatoes], null, 'Europe/Zurich'))).toMatch(/Needs water · 7:45\sAM/u);
  });

  test('the page has no rail, no count in the side nav and no stale mode', () => {
    const source = read(`${webSrc}/routes/(app)/alerts/+page.svelte`);
    expect(source).not.toMatch(/stale|unreachableSince|rail/iu);
    expect(read(`${webSrc}/lib/components/SideNav.svelte`)).not.toMatch(/openCount/u);
  });
});

describe('The Alerts call (AD-14)', () => {
  test('UX-DR64 listAlerts sends the token and returns the Alerts in the Server\'s order with the open count', async () => {
    const fake = fakeServer(() => jsonResponse(200, { alerts: [tomatoes, closedWater], openCount: 1 }));
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { alerts: [tomatoes, closedWater], healthAlerts: [], openCount: 1 } });
    expect(fake.seen).toEqual([{ method: 'GET', path: `/sites/${siteId}/alerts`, authorization: 'Bearer access-token-1', key: null, body: '' }]);
  });

  test('listAlerts follows nextCursor to the end and gives no Alert twice', async () => {
    const queries: string[] = [];
    const pages: Record<string, unknown> = {
      '': { alerts: [tomatoes, herbs], openCount: 3, nextCursor: 'c-1' },
      'c-1': { alerts: [peppers, closedWater], openCount: 3, nextCursor: 'c-2' },
      'c-2': { alerts: [closedHigh], openCount: 3 },
    };
    const fake = fakeServer((request) => {
      const query = new URL(request.url).searchParams;
      queries.push(query.toString());
      return jsonResponse(200, pages[query.get('cursor') ?? '']);
    });
    const result = await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch });
    expect(result).toEqual({ ok: { alerts: [tomatoes, herbs, peppers, closedWater, closedHigh], healthAlerts: [], openCount: 3 } });
    expect(queries).toEqual([`limit=${String(alertsPageLimit)}`, `cursor=c-1&limit=${String(alertsPageLimit)}`, `cursor=c-2&limit=${String(alertsPageLimit)}`]);
  });

  test('an Alert the Server serves on two pages is listed once, where it first came', async () => {
    const pages: Record<string, unknown> = {
      '': { alerts: [tomatoes, herbs], openCount: 3, nextCursor: 'c-1' },
      'c-1': { alerts: [herbs, peppers], openCount: 3 },
    };
    const fake = fakeServer((request) => jsonResponse(200, pages[new URL(request.url).searchParams.get('cursor') ?? '']));
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { alerts: [tomatoes, herbs, peppers], healthAlerts: [], openCount: 3 } });
  });

  test('listAlerts stops at the page cap however long the Server keeps handing out cursors', async () => {
    let calls = 0;
    const fake = fakeServer(() => {
      calls += 1;
      return jsonResponse(200, { alerts: [alert(`a-${String(calls)}`, 'Tomatoes')], openCount: 99, nextCursor: `c-${String(calls)}` });
    });
    const result = await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch });
    expect(calls).toBe(alertsPageCap);
    expect('ok' in result && result.ok.alerts).toHaveLength(alertsPageCap);
  });

  test('a page that fails fails the whole read: no partial list', async () => {
    let calls = 0;
    const fake = fakeServer(() => {
      calls += 1;
      return calls === 1 ? jsonResponse(200, { alerts: [tomatoes], openCount: 2, nextCursor: 'c-1' }) : problemResponse(503, 'unavailable');
    });
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ error: 'unavailable' });
  });

  test('no current Site → no Alerts and no call', async () => {
    const fake = fakeServer(() => jsonResponse(200, { alerts: [], openCount: 0 }));
    expect(await loadAlerts(locals, null, new FakeCookies(), { serverUrl, fetch: fake.fetch, now: () => now })).toEqual({
      alerts: [],
      healthAlerts: [],
      openCount: 0,
      alertsNotice: null,
      loadedAt: '2026-10-09T07:17:00.000Z',
      timeZone: null,
    });
    expect(fake.seen).toEqual([]);
  });

  test('loadAlerts returns the Alerts, the count, the load time and the chosen time zone', async () => {
    const fake = fakeServer(() => jsonResponse(200, { alerts: [tomatoes], openCount: 1 }));
    expect(await loadAlerts(locals, home, new FakeCookies({ cf_time_zone: 'Europe/Zurich' }), { serverUrl, fetch: fake.fetch, now: () => now })).toEqual({
      alerts: [tomatoes],
      healthAlerts: [],
      openCount: 1,
      alertsNotice: null,
      loadedAt: '2026-10-09T07:17:00.000Z',
      timeZone: 'Europe/Zurich',
    });
  });

  test('a Site without Alerts loads as an empty list and a count of 0', async () => {
    const fake = fakeServer(() => jsonResponse(200, { alerts: [], openCount: 0 }));
    const data = await loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: fake.fetch, now: () => now });
    expect([data.alerts, data.openCount, data.alertsNotice]).toEqual([[], 0, null]);
  });

  test('a Server that cannot be reached is a notice and no rows; nothing is kept from before', async () => {
    const down = fakeServer(() => {
      throw fetchFailed('ECONNREFUSED');
    });
    const data = await loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: down.fetch, now: () => now });
    expect([data.alerts, data.openCount, data.alertsNotice]).toEqual([[], 0, 'unreachable']);

    const failing = fakeServer(() => problemResponse(503, 'unavailable'));
    expect((await loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: failing.fetch, now: () => now })).alertsNotice).toBe('unreachable');

    const untrusted = fakeServer(() => {
      throw fetchFailed('DEPTH_ZERO_SELF_SIGNED_CERT');
    });
    expect((await loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: untrusted.fetch, now: () => now })).alertsNotice).toBe('certificate');
  });

  test('a 401 signs out, as everywhere', async () => {
    const fake = fakeServer(() => problemResponse(401, 'unauthorized'));
    const redirect = await redirectOf(() => loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: fake.fetch, now: () => now }));
    expect(redirect.location).toContain('/.oidc/signout');
  });
});

/** Story 7.2: Health Alerts of `AlertList.healthAlerts`. The clock is 07:17 UTC. */
function health(id: string, fields: Partial<HealthAlert> = {}): HealthAlert {
  return { id, kind: 'silent', deviceId: '9c0d1e4b5a673f2a', deviceKind: 'node', openedAt: '2026-10-09T07:05:00.000Z', ...fields };
}

/** Story 7.3: low batteries. */
const lowLettuce = health('b-1', { kind: 'battery', lotId: 'lot-Lettuce', lotName: 'Lettuce', batteryPercent: 14 });
const lowSpare = health('b-2', { kind: 'battery', batteryPercent: 14 });

/** Story 7.4: a soil Sensor that needs Calibration on Lot Peppers, and one that was calibrated. */
const needsPeppers = health('u-1', { kind: 'uncalibrated', lotId: 'lot-Peppers', lotName: 'Peppers' });
const calibratedPeppers = health('u-2', { kind: 'uncalibrated', lotId: 'lot-Peppers', lotName: 'Peppers', openedAt: '2026-10-09T05:05:00.000Z', closedAt: '2026-10-09T05:20:00.000Z', reason: 'calibrated' });

const silentBeans = health('h-1', { lotId: 'lot-Beans', lotName: 'Beans', silentSince: '2026-10-09T01:05:00.000Z' });
const silentHub = health('h-2', { deviceId: '3f2a9c0d1e4b5a67', deviceKind: 'hub', silentSince: '2026-10-09T07:11:30.000Z', openedAt: '2026-10-09T07:16:30.000Z' });
const silentSpare = health('h-3', { silentSince: '2026-10-09T01:00:00.000Z', openedAt: '2026-10-09T07:00:00.000Z' });
const closedHub = health('h-4', { deviceId: '3f2a9c0d1e4b5a67', deviceKind: 'hub', silentSince: '2026-10-09T05:00:00.000Z', openedAt: '2026-10-09T05:05:00.000Z', closedAt: '2026-10-09T05:20:00.000Z', reason: 'recovered' });

describe('Health Alerts of the Health list (Story 7.2)', () => {
  test('UX-DR25 UX-DR117 a silent Node on a Lot: "Node on Lot \'Beans\' silent for 6 h", hatched, help icon', () => {
    expect(healthAlertRow(silentBeans, context)).toEqual({
      id: 'h-1',
      variant: 'health',
      icon: 'help',
      eyebrow: 'Health Alert',
      title: "Node on Lot 'Beans' silent for 6 h",
      spoken: "Node on Lot 'Beans' silent for 6 h, since 07:05",
      href: '/devices#device-9c0d1e4b5a673f2a',
      hatched: true,
    });
  });

  test('UX-DR25 UX-DR117 a silent Hub is named by the first four hex digits in upper case: "Hub 3F2A silent for 5 min"', () => {
    expect(healthAlertRow(silentHub, context)).toMatchObject({ variant: 'health', icon: 'help', title: 'Hub 3F2A silent for 5 min', href: '/devices#device-3f2a9c0d1e4b5a67' });
    // A Hub has no Lot, whatever else the Server sent with it.
    expect(healthAlertRow({ ...silentHub, lotName: 'Beans' }, context).title).toBe('Hub 3F2A silent for 5 min');
  });

  test('UX-DR25 UX-DR117 a silent Node on no Lot is named by its ID: "Node 9C0D silent for 6 h"', () => {
    expect(healthAlertRow(silentSpare, context).title).toBe('Node 9C0D silent for 6 h');
  });

  test('UX-DR117 the duration is now minus silent-since, rounded down: minutes, then hours, then days', () => {
    const at = (silentSince: string): string => healthAlertRow({ ...silentHub, silentSince }, context).title;
    expect(at('2026-10-09T07:16:01.000Z')).toBe('Hub 3F2A silent for 0 min');
    expect(at('2026-10-09T06:17:01.000Z')).toBe('Hub 3F2A silent for 59 min');
    expect(at('2026-10-09T06:17:00.000Z')).toBe('Hub 3F2A silent for 1 h');
    expect(at('2026-10-08T07:17:01.000Z')).toBe('Hub 3F2A silent for 23 h');
    expect(at('2026-10-06T07:00:00.000Z')).toBe('Hub 3F2A silent for 3 d');
    // A silent-since after the clock (skew) is no negative duration.
    expect(at('2026-10-09T08:00:00.000Z')).toBe('Hub 3F2A silent for 0 min');
  });

  test('UX-DR25 a closed row, and an open one without silent-since, drop the duration', () => {
    expect(healthAlertRow(closedHub, context)).toMatchObject({ variant: 'closed', title: 'Hub 3F2A silent', eyebrow: 'Health Alert · closed 05:20', spoken: 'Hub 3F2A silent, since 05:05, closed 05:20', hatched: false });
    expect(healthAlertRow({ ...silentBeans, closedAt: '2026-10-09T07:10:00.000Z' }, context).title).toBe("Node on Lot 'Beans' silent");
    expect(healthAlertRow({ ...silentSpare, closedAt: '2026-10-09T07:10:00.000Z' }, context).title).toBe('Node 9C0D silent');
    // Opened before the Server recorded silent-since, or a value it cannot read.
    expect(healthAlertRow({ ...silentHub, silentSince: undefined }, context).title).toBe('Hub 3F2A silent');
    expect(healthAlertRow({ ...silentBeans, silentSince: undefined }, context).title).toBe("Node on Lot 'Beans' silent");
    expect(healthAlertRow({ ...silentSpare, silentSince: 'yesterday' }, context).title).toBe('Node 9C0D silent');
  });

  test('UX-DR25 a Health kind this client does not know keeps the neutral Health row, with or without a Lot', () => {
    expect(healthAlertRow(health('h-9', { kind: 'frost', lotId: 'lot-Basil', lotName: 'Basil', silentSince: '2026-10-09T01:05:00.000Z' }), context)).toMatchObject({
      variant: 'health',
      icon: 'help',
      eyebrow: 'Health Alert',
      title: "Health Alert on Lot 'Basil'",
      href: '/devices#device-9c0d1e4b5a673f2a',
    });
    expect(healthAlertRow(health('h-9', { kind: 'frost' }), context).title).toBe('Health Alert');
  });

  // Story 7.4: the Server produces the uncalibrated kind, for a soil Sensor of a Node on a Lot.
  test('UX-DR25 UX-DR26 an uncalibrated Sensor reads "Soil Sensor on Lot \'Peppers\' needs Calibration", hatched, with the tools icon, and leads to Lot detail', () => {
    expect(healthAlertRow(needsPeppers, context)).toEqual({
      id: 'u-1',
      variant: 'health',
      icon: 'tools',
      eyebrow: 'Health Alert',
      title: "Soil Sensor on Lot 'Peppers' needs Calibration",
      spoken: "Soil Sensor on Lot 'Peppers' needs Calibration, since 07:05",
      href: '/garden/lot-Peppers',
      hatched: true,
    });
  });

  test('UX-DR25 a closed uncalibrated Alert is the Closed row with the same title and icon, whatever closed it', () => {
    expect(healthAlertRow(calibratedPeppers, context)).toEqual({
      id: 'u-2',
      variant: 'closed',
      icon: 'tools',
      eyebrow: 'Health Alert · closed 05:20',
      title: "Soil Sensor on Lot 'Peppers' needs Calibration",
      spoken: "Soil Sensor on Lot 'Peppers' needs Calibration, since 05:05, closed 05:20",
      href: '/garden/lot-Peppers',
      hatched: false,
    });
    for (const reason of ['unassigned', 'paused', 'removed'] as const) {
      expect(healthAlertRow({ ...calibratedPeppers, reason }, context)).toMatchObject({ variant: 'closed', title: "Soil Sensor on Lot 'Peppers' needs Calibration" });
    }
  });

  test('an uncalibrated Alert without a Lot, which the Server never sends, is the neutral row to its Node', () => {
    expect(healthAlertRow(health('h-9', { kind: 'uncalibrated' }), context)).toMatchObject({ icon: 'tools', title: 'Health Alert', href: '/devices#device-9c0d1e4b5a673f2a' });
  });

  test('UX-DR64 UX-DR26 the page lists an uncalibrated Sensor under Health Alerts and a calibrated one under Closed, both linked to Lot detail', () => {
    const body = page([tomatoes], null, 'UTC', [needsPeppers, calibratedPeppers]);
    expect(ids(body)).toEqual(['a-1', 'u-1', 'u-2']);
    expect(text(body)).toBe(
      "Alerts Threshold Alerts Needs water · 5:45 AM Tomatoes needs water Health Alerts Health Alert Soil Sensor on Lot 'Peppers' needs Calibration Closed Health Alert · closed 5:20 AM Soil Sensor on Lot 'Peppers' needs Calibration",
    );
    expect(item(body, 'u-1')).toContain('href="/garden/lot-Peppers"');
    expect(item(body, 'u-1')).toContain('data-variant="health"');
    expect(item(body, 'u-1')).toContain('data-icon="tools"');
    expect(item(body, 'u-1')).toContain('aria-label="Soil Sensor on Lot \'Peppers\' needs Calibration, since 7:05 AM"');
    expect(item(body, 'u-2')).toContain('href="/garden/lot-Peppers"');
    expect(item(body, 'u-2')).toContain('data-variant="closed"');
    // Among the groups: open under Health in the Server's order, closed after the closed Threshold Alerts.
    const groups = alertGroups([tomatoes, closedWater], context, [lowLettuce, needsPeppers, calibratedPeppers]);
    expect(groups.health.map((row) => row.id)).toEqual(['b-1', 'u-1']);
    expect(groups.closed.map((row) => row.id)).toEqual(['a-9', 'u-2']);
  });

  // Story 7.3: a low battery says the level it opened with.
  test('UX-DR25 a low battery on a Lot reads "Node battery 14 %, not charging — Lot \'Lettuce\'", hatched, with the low-battery icon', () => {
    expect(healthAlertRow(lowLettuce, context)).toEqual({
      id: 'b-1',
      variant: 'health',
      icon: 'battery--low',
      eyebrow: 'Health Alert',
      title: "Node battery 14 %, not charging — Lot 'Lettuce'",
      spoken: "Node battery 14 %, not charging — Lot 'Lettuce', since 07:05",
      href: '/devices#device-9c0d1e4b5a673f2a',
      hatched: true,
    });
  });

  test('UX-DR25 a low battery of a Node on no Lot is named by its ID: "Node 9C0D battery 14 %, not charging"', () => {
    expect(healthAlertRow(lowSpare, context)).toMatchObject({ icon: 'battery--low', title: 'Node 9C0D battery 14 %, not charging', href: '/devices#device-9c0d1e4b5a673f2a' });
    // An empty battery is a level.
    expect(healthAlertRow({ ...lowSpare, batteryPercent: 0 }, context).title).toBe('Node 9C0D battery 0 %, not charging');
  });

  test('UX-DR25 a closed low battery, and one without a level, read "battery low"', () => {
    const closedAt = '2026-10-09T07:10:00.000Z';
    expect(healthAlertRow({ ...lowLettuce, closedAt, reason: 'recovered' }, context)).toMatchObject({ variant: 'closed', icon: 'battery--low', title: "Node battery low on Lot 'Lettuce'", hatched: false });
    expect(healthAlertRow({ ...lowSpare, closedAt, reason: 'recovered' }, context).title).toBe('Node 9C0D battery low');
    // An Alert that states no level, or one the contract cannot describe.
    expect(healthAlertRow({ ...lowLettuce, batteryPercent: undefined }, context).title).toBe("Node battery low on Lot 'Lettuce'");
    expect(healthAlertRow({ ...lowSpare, batteryPercent: undefined }, context).title).toBe('Node 9C0D battery low');
    expect(healthAlertRow({ ...lowLettuce, batteryPercent: 140 }, context).title).toBe("Node battery low on Lot 'Lettuce'");
    expect(healthAlertRow({ ...lowLettuce, batteryPercent: 13.5 }, context).title).toBe("Node battery low on Lot 'Lettuce'");
    expect(healthAlertRow({ ...lowSpare, batteryPercent: -1 }, context).title).toBe('Node 9C0D battery low');
    // Never sent by the Server: a Hub has no battery to report, and is not named as a Node.
    expect(healthAlertRow({ ...lowSpare, deviceId: '3f2a9c0d1e4b5a67', deviceKind: 'hub' }, context)).toMatchObject({ icon: 'battery--low', title: 'Health Alert' });
  });

  test('UX-DR64 UX-DR26 the page lists a low battery under Health Alerts, linked to its Node on Devices', () => {
    const body = page([tomatoes], null, 'UTC', [lowLettuce, silentHub]);
    expect(ids(body)).toEqual(['a-1', 'b-1', 'h-2']);
    expect(item(body, 'b-1')).toContain('href="/devices#device-9c0d1e4b5a673f2a"');
    expect(item(body, 'b-1')).toContain('data-variant="health"');
    expect(item(body, 'b-1')).toContain("Node battery 14 %, not charging — Lot 'Lettuce'");
  });

  test('UX-DR26 a Health row links to the row of its Device on Devices', () => {
    expect([silentBeans, silentHub, silentSpare, closedHub].map((item) => healthAlertRow(item, context).href)).toEqual([
      '/devices#device-9c0d1e4b5a673f2a',
      '/devices#device-3f2a9c0d1e4b5a67',
      '/devices#device-9c0d1e4b5a673f2a',
      '/devices#device-3f2a9c0d1e4b5a67',
    ]);
  });

  test('UX-DR64 the groups: Threshold from alerts, Health from healthAlerts in the Server order, Closed holds both; nothing is re-sorted', () => {
    const groups = alertGroups([tomatoes, herbs, closedWater], context, [silentHub, silentBeans, silentSpare, closedHub]);
    expect(groups.threshold.map((row) => row.id)).toEqual(['a-1', 'a-2']);
    expect(groups.health.map((row) => row.id)).toEqual(['h-2', 'h-1', 'h-3']);
    expect(groups.closed.map((row) => row.id)).toEqual(['a-9', 'h-4']);
    // A missing Health list is no Health Alerts; an Alert listed twice shows once.
    expect(alertGroups([tomatoes], context, undefined).health).toEqual([]);
    expect(alertGroups([tomatoes], context, [silentBeans, silentBeans]).health.map((row) => row.id)).toEqual(['h-1']);
  });

  test('UX-DR64 UX-DR25 the page lists the Health rows under Health Alerts and the closed one under Closed', () => {
    const body = page([tomatoes], null, 'UTC', [silentHub, silentBeans, silentSpare, closedHub]);
    expect(ids(body)).toEqual(['a-1', 'h-2', 'h-1', 'h-3', 'h-4']);
    expect(text(body)).toBe(
      "Alerts Threshold Alerts Needs water · 5:45 AM Tomatoes needs water Health Alerts Health Alert Hub 3F2A silent for 5 min Health Alert Node on Lot 'Beans' silent for 6 h Health Alert Node 9C0D silent for 6 h Closed Health Alert · closed 5:20 AM Hub 3F2A silent",
    );
    expect(item(body, 'h-2')).toContain('href="/devices#device-3f2a9c0d1e4b5a67"');
    expect(item(body, 'h-2')).toContain('aria-label="Hub 3F2A silent for 5 min, since 7:16 AM"');
    expect(item(body, 'h-1')).toContain('data-variant="health"');
    expect(item(body, 'h-4')).toContain('data-variant="closed"');
    // Only Health Alerts open: no empty state.
    expect(text(page([], null, 'UTC', [silentHub]))).toBe('Alerts Health Alerts Health Alert Hub 3F2A silent for 5 min');
  });

  test('a Server that sends no Health list renders as before', () => {
    expect(text(page([]))).toBe('Alerts No open Alerts.');
    expect(ids(page([tomatoes]))).toEqual(['a-1']);
  });

  test('UX-DR64 listAlerts takes the Health Alerts of the first page only: later pages carry none', async () => {
    const pages: Record<string, unknown> = {
      '': { alerts: [tomatoes], healthAlerts: [silentBeans, silentHub], openCount: 3, nextCursor: 'c-1' },
      // A later page never has them; one that did would not replace the first page's.
      'c-1': { alerts: [herbs], healthAlerts: [closedHub], openCount: 3 },
    };
    const fake = fakeServer((request) => jsonResponse(200, pages[new URL(request.url).searchParams.get('cursor') ?? '']));
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { alerts: [tomatoes, herbs], healthAlerts: [silentBeans, silentHub], openCount: 3 } });
    const loaded = await loadAlerts(locals, home, new FakeCookies(), { serverUrl, fetch: fakeServer(() => jsonResponse(200, pages[''])).fetch, now: () => now });
    expect(loaded.healthAlerts).toEqual([silentBeans, silentHub]);
  });
});
