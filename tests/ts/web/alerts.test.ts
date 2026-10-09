import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import AlertsPage from '../../../apps/ts/web/src/routes/(app)/alerts/+page.svelte';
import { alertGroups, alertRow, alertsNoticeOf, type Alert, type AlertsContext, type AlertsNotice } from '$lib/alerts';
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

function page(alerts: readonly Alert[], alertsNotice: AlertsNotice | null = null, timeZone: string | null = 'UTC'): string {
  const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null, alerts, openCount: alerts.filter((item) => item.closedAt === undefined).length, alertsNotice, loadedAt: now.toISOString(), timeZone };
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
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { alerts: [tomatoes, closedWater], openCount: 1 } });
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
    expect(result).toEqual({ ok: { alerts: [tomatoes, herbs, peppers, closedWater, closedHigh], openCount: 3 } });
    expect(queries).toEqual([`limit=${String(alertsPageLimit)}`, `cursor=c-1&limit=${String(alertsPageLimit)}`, `cursor=c-2&limit=${String(alertsPageLimit)}`]);
  });

  test('an Alert the Server serves on two pages is listed once, where it first came', async () => {
    const pages: Record<string, unknown> = {
      '': { alerts: [tomatoes, herbs], openCount: 3, nextCursor: 'c-1' },
      'c-1': { alerts: [herbs, peppers], openCount: 3 },
    };
    const fake = fakeServer((request) => jsonResponse(200, pages[new URL(request.url).searchParams.get('cursor') ?? '']));
    expect(await listAlerts(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { alerts: [tomatoes, herbs, peppers], openCount: 3 } });
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
