import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import ThresholdsPage from '../../../apps/ts/web/src/routes/(app)/garden/[lotId]/thresholds/+page.svelte';
import CalibratePage from '../../../apps/ts/web/src/routes/(app)/garden/[lotId]/calibrate/+page.svelte';
import { editOf, requestOf, stepOf, thresholdsAccessOf, turnOnAlerts, validityMessage, validityOf, type SensorThresholds } from '$lib/thresholds';
import { loadThresholds, saveThresholdsAction } from '$lib/server/thresholds';
import type { Lot } from '$lib/lots';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { read, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: 'user-1', name: 'Simon Novak' } } } };
const siteId = '0192a000-0000-7000-8000-00000000000a';
const lotId = '0192a000-0000-7000-8000-000000000011';
const soilId = '0192a000-0000-7000-8000-0000000000aa';
const airId = '0192a000-0000-7000-8000-0000000000ab';
const owner: Site = { id: siteId, name: 'Home garden', role: 'Owner' };
const member: Site = { id: siteId, name: 'Home garden', role: 'Member' };
const now = new Date('2026-10-06T07:17:00.000Z');

const soil = { quantity: 'soil_moisture', value: 40, unit: '%', measuredAt: '2026-10-06T07:02:00.000Z', sensorId: soilId, calibratable: true } as const;
const air = { quantity: 'air_temperature', value: 14.4, unit: '°C', measuredAt: '2026-10-06T07:02:00.000Z', sensorId: airId, calibratable: false } as const;
const lot: Lot = { id: lotId, name: 'Tomatoes', status: 'ok', statusSince: '2026-10-06T05:45:00.000Z', moisturePercent: 40, lowThresholdPercent: 30, sensors: [soil, air] };

const soilThresholds: SensorThresholds = { unit: '%', low: { kind: 'default', value: 30 }, high: { kind: 'cleared' } };
const airThresholds: SensorThresholds = { unit: '°C', low: { kind: 'cleared' }, high: { kind: 'cleared' }, proposedLow: 7.5 };

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

function answer(request: Request): Response {
  const path = new URL(request.url).pathname;
  if (path.endsWith(`/lots/${lotId}`)) {
    return jsonResponse(200, lot);
  }
  return jsonResponse(200, path.includes(soilId) ? soilThresholds : airThresholds);
}

const columns = [
  { sensorId: soilId, quantity: 'soil_moisture', reading: 40, readingUnit: '%', thresholds: soilThresholds },
  { sensorId: airId, quantity: 'air_temperature', reading: 14.4, readingUnit: '°C', thresholds: airThresholds },
] as const;

function page(role: Site['role'], overrides: Record<string, unknown> = {}, form: unknown = null): string {
  const site = { ...owner, role };
  const data = {
    user: { displayName: 'Simon', initials: 'S' },
    theme: 'system',
    sites: [site],
    currentSite: site,
    sitesNotice: null,
    sitesStale: null,
    lot: { id: lotId, name: 'Tomatoes' },
    canEdit: role !== 'Member',
    columns,
    notice: null,
    loadedAt: now.toISOString(),
    timeZone: 'UTC',
    ...overrides,
  };
  return render(ThresholdsPage, { props: { data, form, params: { lotId } } as never }).body;
}

describe('Threshold rules the shell only formats (AD-14)', () => {
  test('UX-DR84 only an Owner or Administrator edits; a Member sees Thresholds read-only', () => {
    expect(thresholdsAccessOf('Owner')).toBe(true);
    expect(thresholdsAccessOf('Administrator')).toBe(true);
    expect(thresholdsAccessOf('Member')).toBe(false);
  });

  test('UX-DR45 a calibrated soil Sensor moves in 5 % steps, any other Sensor in whole units', () => {
    expect(stepOf('soil_moisture', '%')).toBe(5);
    expect(stepOf('relative_humidity', '%')).toBe(1);
    expect(stepOf('air_temperature', '°C')).toBe(1);
  });

  test('UX-DR69 low must stay below high gates Save, and the Server stays the validator', () => {
    expect(validityOf({ low: 70, high: 60 })).toBe('lowNotBelowHigh');
    expect(validityOf({ low: 60, high: 60 })).toBe('lowNotBelowHigh');
    expect(validityOf({ low: null, high: 60 })).toBe('lowRequired');
    expect(validityOf({ low: 30, high: null })).toBe('ok');
    expect(validityOf({ low: null, high: null })).toBe('ok');
    expect(validityMessage('lowNotBelowHigh')).toBe('Low must stay below high.');
    expect(validityMessage('ok')).toBeNull();
  });

  test('a Sensor without a default offers the Server proposedLow when alerts are turned on, never a high', () => {
    expect(editOf(airThresholds)).toEqual({ low: null, high: null });
    expect(turnOnAlerts(editOf(airThresholds), airThresholds)).toEqual({ low: 7.5, high: null });
    expect(turnOnAlerts({ low: null, high: null }, soilThresholds)).toEqual({ low: null, high: null });
  });

  test('only a changed side is sent: an override, or cleared for an emptied high; nothing when nothing changed', () => {
    expect(requestOf(soilThresholds, { low: 30, high: null })).toBeNull();
    expect(requestOf(soilThresholds, { low: 25, high: null })).toEqual({ low: { kind: 'override', value: 25 } });
    expect(requestOf(soilThresholds, { low: 25, high: 70 })).toEqual({ low: { kind: 'override', value: 25 }, high: { kind: 'override', value: 70 } });
    const withHigh: SensorThresholds = { ...soilThresholds, high: { kind: 'override', value: 70 } };
    expect(requestOf(withHigh, { low: 30, high: null })).toEqual({ high: { kind: 'cleared' } });
    expect(requestOf(airThresholds, { low: 7.5, high: null })).toEqual({ low: { kind: 'override', value: 7.5 } });
  });
});

describe('The Thresholds page', () => {
  test('loading reads the Lot, then Thresholds of each Sensor with the token; a Member may load', async () => {
    const server = fakeServer(answer);
    const data = await loadThresholds(locals, member, lotId, new FakeCookies(), { serverUrl, fetch: server.fetch, now: () => now });
    expect(server.seen.map((request) => request.path)).toEqual([`/sites/${siteId}/lots/${lotId}`, `/sites/${siteId}/sensors/${soilId}/thresholds`, `/sites/${siteId}/sensors/${airId}/thresholds`]);
    expect(server.seen.every((request) => request.method === 'GET' && request.authorization === 'Bearer access-token-1')).toBe(true);
    expect(data).toMatchObject({ notice: null, canEdit: false, lot: { name: 'Tomatoes' } });
    expect(data.columns.map((column) => [column.sensorId, column.reading])).toEqual([[soilId, 40], [airId, 14.4]]);
  });

  test('a missing Lot is a notFound notice; an unreachable Server an unreachable one', async () => {
    const gone = fakeServer(() => problemResponse(404, 'lot-not-found'));
    expect((await loadThresholds(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: gone.fetch })).notice).toBe('notFound');
    const down = fakeServer(() => {
      throw fetchFailed('ECONNREFUSED');
    });
    expect((await loadThresholds(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: down.fetch })).notice).toBe('unreachable');
  });

  test('UX-DR45 the Threshold column draws a track, the low line, the current Reading marker, values, and a dashed no-high marker', () => {
    const body = page('Owner');
    expect(body).toContain('cf-threshold__track');
    expect(body).toContain('cf-threshold__low-line');
    expect(body).toContain('cf-threshold__reading');
    expect(body).toContain('cf-threshold__no-high');
    expect(body).toContain('role="slider"');
    expect(text(body)).toContain('Soil moisture');
    expect(text(body)).toContain('no high');
    // A typed value as well as a dragged one, in 5 % steps for the calibrated soil.
    expect(body).toMatch(/<input[^>]*type="number"[^>]*step="5"/u);
    const source = read(`${webSrc}/lib/components/ThresholdColumn.svelte`);
    expect(source).toContain('var(--cf-color-layer-01)');
    expect(source).toContain('var(--cf-color-primary-text)');
    expect(source).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(/iu);
  });

  test('UX-DR45 a Sensor with no Threshold says alerts are off and offers to turn them on', () => {
    const body = page('Owner');
    expect(text(body)).toContain('Alerts are off');
    expect(text(body)).toContain('Turn on Alerts');
  });

  test('UX-DR69 the modal has Cancel and Save, Save is an enabled submit while valid', () => {
    const body = page('Owner');
    expect(text(body)).toContain('Cancel');
    expect(body).toMatch(/<button[^>]*type="submit"[^>]*>\s*<span[^>]*>Save/u);
    expect(body).not.toMatch(/<button[^>]*disabled[^>]*>\s*<span[^>]*>Save/u);
    expect(body).toContain(`href="/garden/${lotId}"`);
  });

  test('UX-DR69 Low must stay below high shows inline under the field and Save is disabled while invalid', () => {
    const invalid = [{ ...columns[0], thresholds: { ...soilThresholds, low: { kind: 'override', value: 70 }, high: { kind: 'override', value: 60 } } }, columns[1]];
    const body = page('Owner', { columns: invalid });
    expect(text(body)).toContain('Low must stay below high.');
    expect(body).toMatch(/<button[^>]*disabled[^>]*>\s*<span[^>]*>Save/u);
  });

  test('UX-DR84 a Member sees Thresholds read-only: no inputs, no Save, no turn-on, hidden not disabled', () => {
    const body = page('Member');
    expect(text(body)).toContain('Soil moisture');
    expect(text(body)).toContain('30 %');
    expect(body).not.toMatch(/<input[^>]*type="number"/u);
    expect(body).not.toContain('role="slider"');
    expect(body).not.toMatch(/Save|Turn on Alerts|Add high|disabled/u);
    expect(text(body)).toContain('Only an Owner or Administrator can change Thresholds.');
  });

  test('UX-DR91 each failure says what happened, what did not change and what to do next, and the edits stay', () => {
    expect(text(page('Owner', {}, { notice: 'invalid' }))).toContain('The Server did not accept these Thresholds. Nothing was changed. Check that Low is below High, then save again.');
    expect(text(page('Owner', {}, { notice: 'forbidden' }))).toContain("You can't change this on Home garden. Ask an Owner or Administrator.");
    expect(text(page('Owner', {}, { notice: 'notSaved' }))).toContain('Not saved. The Server could not be reached, so your edits are kept here. Try again.');
  });
});

describe('Saving Thresholds', () => {
  function form(changes: unknown): Request {
    return new Request('http://localhost/garden/x/thresholds?/save', { method: 'POST', body: new URLSearchParams({ siteId, lotId, changes: JSON.stringify(changes) }) });
  }

  async function redirectOf(run: () => Promise<unknown>): Promise<string | null> {
    try {
      await run();
      return null;
    } catch (error) {
      return isRedirect(error) ? error.location : null;
    }
  }

  test('saving puts each changed Sensor and returns to the Lot', async () => {
    const server = fakeServer(() => jsonResponse(200, soilThresholds));
    const location = await redirectOf(() => saveThresholdsAction(locals, form([{ sensorId: soilId, body: { low: { kind: 'override', value: 25 } } }]), { serverUrl, fetch: server.fetch }));
    expect(location).toBe(`/garden/${lotId}`);
    expect(server.seen).toHaveLength(1);
    expect(server.seen[0]).toMatchObject({ method: 'PUT', path: `/sites/${siteId}/sensors/${soilId}/thresholds` });
    expect(JSON.parse(server.seen[0]?.body ?? '')).toEqual({ low: { kind: 'override', value: 25 } });
  });

  test('UX-DR91 a 400 is invalid, a 403 forbidden, a 503 or an unreachable Server not saved; nothing is retried by the app', async () => {
    const change = form([{ sensorId: soilId, body: { low: { kind: 'override', value: 25 } } }]);
    const run = async (respond: () => Response): Promise<unknown> => {
      const server = fakeServer(respond);
      return saveThresholdsAction(locals, change.clone(), { serverUrl, fetch: server.fetch });
    };
    expect(await run(() => problemResponse(400, 'validation'))).toMatchObject({ status: 400, data: { notice: 'invalid' } });
    expect(await run(() => problemResponse(403, 'forbidden'))).toMatchObject({ status: 403, data: { notice: 'forbidden' } });
    expect(await run(() => problemResponse(503, 'unavailable'))).toMatchObject({ status: 503, data: { notice: 'notSaved' } });
    expect(
      await run(() => {
        throw fetchFailed('ECONNREFUSED');
      }),
    ).toMatchObject({ data: { notice: 'notSaved' } });
  });
});

describe('Entry points', () => {
  test('UX-DR45 the Calibration confirmation leads on to Thresholds for the same Lot', () => {
    const source = read(`${webSrc}/routes/(app)/garden/[lotId]/calibrate/+page.svelte`);
    expect(source).toContain('/thresholds');
    expect(CalibratePage).toBeDefined();
  });
});
