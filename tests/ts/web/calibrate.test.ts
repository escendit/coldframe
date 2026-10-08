import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import CalibratePage from '../../../apps/ts/web/src/routes/(app)/garden/[lotId]/calibrate/+page.svelte';
import LotTiles from '$lib/components/LotTiles.svelte';
import { calibrateAccessOf, calibrateStep, calibratableSensor, confirmation, freshReading, newestSeq, readingAnnouncement, recentReadings, type CalibrateStep, type CalibrationReading, type CalibrationState } from '$lib/calibrate';
import type { Lot } from '$lib/lots';
import { calibrateSensor, calibrateAction, loadCalibrate } from '$lib/server/calibrate';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: 'user-1', name: 'Simon Novak' } } } };
const siteId = '0192a000-0000-7000-8000-00000000000a';
const lotId = '0192a000-0000-7000-8000-000000000011';
const sensorId = '0192a000-0000-7000-8000-0000000000aa';
const owner: Site = { id: siteId, name: 'Home garden', role: 'Owner' };
const member: Site = { id: siteId, name: 'Home garden', role: 'Member' };

const now = new Date('2026-10-06T05:17:00.000Z');
const reading = (readingSeq: number, rawValue: number, measuredAt: string): CalibrationReading => ({ readingSeq, rawValue, measuredAt });
const older = reading(40, 2900, '2026-10-06T05:02:00.000Z');
const newer = reading(41, 612, '2026-10-06T05:17:00.000Z');

const soil = { quantity: 'soil_moisture', value: 1840, unit: 'raw', measuredAt: '2026-10-06T05:02:00.000Z', sensorId, calibratable: true } as const;
const air = { quantity: 'air_temperature', value: 14.4, unit: '°C', measuredAt: '2026-10-06T05:02:00.000Z', sensorId: '0192a000-0000-7000-8000-0000000000ab', calibratable: false } as const;

function lot(status: string, fields: Partial<Lot> = {}): Lot {
  return { id: lotId, name: 'Tomatoes', status: status as Lot['status'], statusSince: '2026-10-06T03:45:00.000Z', sensors: [soil, air], ...fields };
}

const uncalibrated: CalibrationState = { calibrated: false, readings: [newer, older] };

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

describe('Calibrate rules the shell only formats (AD-14)', () => {
  test('Admin+ may calibrate; a Member may not: the control is hidden, not disabled', () => {
    expect(calibrateAccessOf('Owner')).toBe(true);
    expect(calibrateAccessOf('Administrator')).toBe(true);
    expect(calibrateAccessOf('Member')).toBe(false);
  });

  test('only a Sensor whose Specification says calibration shows Calibrate', () => {
    expect(calibratableSensor(lot('needsCalibration'))?.sensorId).toBe(sensorId);
    expect(calibratableSensor(lot('needsCalibration', { sensors: [air] }))).toBeNull();
    expect(calibratableSensor(lot('noNode', { sensors: undefined }))).toBeNull();
  });

  test('the flow starts at dry, resumes at wet when the Server kept the dry point, and ends in the confirmation', () => {
    const step = (state: CalibrationState, recorded: Parameters<typeof calibrateStep>[2] = null, status = 'needsCalibration'): CalibrateStep => calibrateStep(lot(status), state, recorded);
    expect(step(uncalibrated)).toBe('dry');
    expect(step({ ...uncalibrated, pendingDry: { rawValue: 2900 } })).toBe('wet');
    expect(step(uncalibrated, { dryRaw: 2900, wetRaw: 612, savedAt: now.toISOString() })).toBe('confirm');
  });

  test('a paused Device explains instead of waiting', () => {
    expect(calibrateStep(lot('paused', { pausedBy: ['device'] }), uncalibrated, null)).toBe('paused');
    expect(calibrateStep(lot('paused', { pausedBy: ['site'] }), uncalibrated, null)).toBe('paused');
  });

  test('a Reading is fresh only when stored after the step started, whatever the clocks say', () => {
    expect(freshReading([newer, older], 40)?.readingSeq).toBe(41);
    expect(freshReading([newer, older], 41)).toBeNull();
    expect(freshReading([], -1)).toBeNull();
    expect(newestSeq([newer, older])).toBe(41);
    expect(newestSeq([])).toBe(-1);
  });

  test('the polite announcement names the time, the raw value and the action that became available', () => {
    expect(readingAnnouncement('dry', newer, 'en', 'UTC')).toBe('New Reading 5:17 AM, raw 612. Record dry is available.');
    expect(readingAnnouncement('wet', newer, 'en', 'Europe/Zurich')).toBe('New Reading 7:17 AM, raw 612. Record wet is available.');
  });

  test('recent Readings are listed newest first with a time and the raw value', () => {
    expect(recentReadings([newer, older], 'en', 'UTC').map((item) => [item.readingSeq, item.label])).toEqual([
      [41, '5:17 AM, raw 612'],
      [40, '5:02 AM, raw 2,900'],
    ]);
  });

  test('the confirmation shows no percent before the Server stored a calibrated Reading, then updates in place', () => {
    const saved = '2026-10-06T05:20:00.000Z';
    const raw = confirmation('Tomatoes', lot('needsCalibration'), saved);
    expect(raw).toEqual({ ready: false, text: '% appears with the next Reading', announcement: null });
    // A percentage Reading from before the save is not the first calibrated one.
    const before = confirmation('Tomatoes', lot('ok', { sensors: [{ ...soil, unit: '%', value: 40, measuredAt: '2026-10-06T05:02:00.000Z' }] }), saved);
    expect(before.ready).toBe(false);
    const after = confirmation('Tomatoes', lot('ok', { sensors: [{ ...soil, unit: '%', value: 40, measuredAt: '2026-10-06T05:32:00.000Z' }] }), saved);
    expect(after).toEqual({ ready: true, text: 'Tomatoes reads ~40 %', announcement: 'Tomatoes reads about 40 percent.' });
  });
});

describe('The Calibrate calls (AD-14)', () => {
  test('loading reads the Lot and the Sensor Calibration state with the token, in that order', async () => {
    const server = fakeServer((request) => (request.url.endsWith('/calibration') ? jsonResponse(200, uncalibrated) : jsonResponse(200, lot('needsCalibration'))));
    const data = await loadCalibrate(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: server.fetch, now: () => now });
    expect(server.seen.map((request) => request.path)).toEqual([`/sites/${siteId}/lots/${lotId}`, `/sites/${siteId}/sensors/${sensorId}/calibration`]);
    expect(server.seen.every((request) => request.authorization === 'Bearer access-token-1')).toBe(true);
    expect(data).toMatchObject({ notice: null, sensorId, state: uncalibrated, lot: { name: 'Tomatoes', status: 'needsCalibration' } });
  });

  test('a Member is sent back to the Lot without any Server call', async () => {
    const server = fakeServer(() => jsonResponse(200, {}));
    try {
      await loadCalibrate(locals, member, lotId, new FakeCookies(), { serverUrl, fetch: server.fetch, now: () => now });
      throw new Error('Expected a redirect.');
    } catch (error) {
      expect(isRedirect(error) ? error.location : null).toBe(`/garden/${lotId}`);
    }
    expect(server.seen).toEqual([]);
  });

  test('a Lot without a calibratable Sensor says so and reads no Calibration', async () => {
    const server = fakeServer(() => jsonResponse(200, lot('noNode', { sensors: undefined })));
    const data = await loadCalibrate(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: server.fetch, now: () => now });
    expect(data).toMatchObject({ notice: 'noSensor', sensorId: null, state: null });
    expect(server.seen).toHaveLength(1);
  });

  test('a missing Lot is a notFound notice; an unreachable Server an unreachable one', async () => {
    const gone = fakeServer(() => problemResponse(404, 'lot-not-found'));
    expect((await loadCalibrate(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: gone.fetch })).notice).toBe('notFound');
    const down = fakeServer(() => {
      throw fetchFailed('ECONNREFUSED');
    });
    expect((await loadCalibrate(locals, owner, lotId, new FakeCookies(), { serverUrl, fetch: down.fetch })).notice).toBe('unreachable');
  });

  test('recording sends only the chosen point with its readingSeq', async () => {
    const server = fakeServer(() => jsonResponse(200, { calibrated: false, pendingDry: { rawValue: 2900 } }));
    const result = await calibrateSensor(locals, siteId, sensorId, 'dry', 40, { serverUrl, fetch: server.fetch });
    expect(result).toEqual({ result: { ok: { calibrated: false, pendingDry: { rawValue: 2900 } } }, notDelivered: false });
    expect(server.seen[0]).toMatchObject({ method: 'POST', path: `/sites/${siteId}/sensors/${sensorId}/calibration` });
    expect(JSON.parse(server.seen[0]?.body ?? '')).toEqual({ dry: { readingSeq: 40 } });
  });

  function form(fields: Record<string, string>): Request {
    return new Request('http://localhost/garden/x/calibrate?/record', { method: 'POST', body: new URLSearchParams(fields) });
  }
  const fields = { siteId, sensorId, point: 'wet', readingSeq: '41' };

  test('a dry point answers recorded dry; both points answer calibrated with both raw values', async () => {
    const dry = fakeServer(() => jsonResponse(200, { calibrated: false, pendingDry: { rawValue: 2900 } }));
    expect(await calibrateAction(locals, form({ ...fields, point: 'dry' }), { serverUrl, fetch: dry.fetch, now: () => now })).toEqual({ done: true, point: 'dry', calibrated: false });
    const wet = fakeServer(() => jsonResponse(200, { calibrated: true, calibrationId: 'c1', dry: { rawValue: 2900 }, wet: { rawValue: 612 } }));
    expect(await calibrateAction(locals, form(fields), { serverUrl, fetch: wet.fetch, now: () => now })).toEqual({
      done: true,
      point: 'wet',
      calibrated: true,
      dryRaw: 2900,
      wetRaw: 612,
      savedAt: now.toISOString(),
    });
  });

  test('indistinct points (400) say what happened, what did not change and what to do', async () => {
    const server = fakeServer(() => problemResponse(400, 'validation'));
    const failure = await calibrateAction(locals, form(fields), { serverUrl, fetch: server.fetch, now: () => now });
    expect(failure).toMatchObject({ status: 400, data: { notice: 'indistinct', point: 'wet' } });
  });

  test('a 403 race and a missing Sensor are notices; a 401 signs out', async () => {
    expect(await calibrateAction(locals, form(fields), { serverUrl, fetch: fakeServer(() => problemResponse(403, 'forbidden')).fetch })).toMatchObject({ status: 403, data: { notice: 'forbidden' } });
    expect(await calibrateAction(locals, form(fields), { serverUrl, fetch: fakeServer(() => problemResponse(404, 'sensor-not-found')).fetch })).toMatchObject({ data: { notice: 'notFound' } });
    await expect(calibrateAction(locals, form(fields), { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch })).rejects.toSatisfy(isRedirect);
  });

  test('not delivered (503): the saved point is kept and the save is said to be unconfirmed', async () => {
    const server = fakeServer((request) =>
      request.method === 'POST'
        ? problemResponse(503, 'calibration-not-delivered')
        : jsonResponse(200, { calibrated: true, dry: { rawValue: 2900 }, wet: { rawValue: 612 }, readings: [newer, older] }),
    );
    const failure = await calibrateAction(locals, form(fields), { serverUrl, fetch: server.fetch, now: () => now });
    expect(failure).toMatchObject({ status: 503, data: { notice: 'notDelivered', point: 'wet', dryRaw: 2900, wetRaw: 612, savedAt: now.toISOString() } });
  });

  test('a dry point that completes a pending wet point answers calibrated with both raw values', async () => {
    const server = fakeServer(() => jsonResponse(200, { calibrated: true, calibrationId: 'c1', dry: { rawValue: 2900 }, wet: { rawValue: 612 } }));
    expect(await calibrateAction(locals, form({ ...fields, point: 'dry' }), { serverUrl, fetch: server.fetch, now: () => now })).toEqual({
      done: true,
      point: 'dry',
      calibrated: true,
      dryRaw: 2900,
      wetRaw: 612,
      savedAt: now.toISOString(),
    });
  });

  test('not delivered whose read-back fails is still the not-delivered notice, without values', async () => {
    const server = fakeServer((request) => (request.method === 'POST' ? problemResponse(503, 'calibration-not-delivered') : problemResponse(500, 'internal')));
    expect(await calibrateAction(locals, form(fields), { serverUrl, fetch: server.fetch, now: () => now })).toMatchObject({ status: 503, data: { notice: 'notDelivered', point: 'wet' } });
  });

  test('a 503 that is not calibration-not-delivered is the generic unavailable notice', async () => {
    const server = fakeServer((request) => (request.method === 'POST' ? problemResponse(503, 'unavailable') : jsonResponse(200, uncalibrated)));
    expect(await calibrateAction(locals, form(fields), { serverUrl, fetch: server.fetch, now: () => now })).toMatchObject({ status: 503, data: { notice: 'unavailable' } });
  });
});

describe('The Calibrate page', () => {
  const base = { currentSite: owner, loadedAt: now.toISOString(), timeZone: 'UTC', notice: null, sensorId, state: uncalibrated, lot: { id: lotId, name: 'Tomatoes', status: 'needsCalibration', pausedBy: [], sensors: [soil, air] } };

  function page(data: Record<string, unknown> = {}, form: unknown = null): string {
    return render(CalibratePage, { props: { data: { ...base, ...data }, form, params: { lotId } } }).body;
  }

  test('step 1 waits for a Reading: last raw value and time, the setup-button hint, Record dry disabled until one arrives', () => {
    const body = page();
    expect(text(body)).toContain('Calibrate Tomatoes');
    expect(text(body)).toContain('Step 1 of 2: dry');
    expect(text(body)).toContain('Waiting for the next Reading');
    expect(text(body)).toContain('Last Reading: raw 612 at 5:17 AM');
    expect(text(body)).toContain("Short-press the Node's setup button");
    expect(body).toContain('data-step="dry"');
    expect(body).not.toMatch(/role="status"/u);
  });

  test('the recent Readings list offers each stored Reading by readingSeq', () => {
    const body = page();
    expect(text(body)).toContain('Recent Readings');
    expect(body).toMatch(/name="readingSeq" value="41"/u);
    expect(body).toMatch(/name="readingSeq" value="40"/u);
    expect(body).toContain('name="point" value="dry"');
  });

  test('step 2 resumes after the Server kept the dry point', () => {
    const body = page({ state: { ...uncalibrated, pendingDry: { rawValue: 2900 } } });
    expect(text(body)).toContain('Step 2 of 2: wet');
    expect(body).toContain('name="point" value="wet"');
    expect(body).not.toContain('name="point" value="dry"');
  });

  test('a paused Device gets the explanation and no waiting', () => {
    const body = page({ lot: { ...base.lot, status: 'paused', pausedBy: ['device'] } });
    expect(text(body)).toContain('Readings resume after the Pause ends');
    expect(text(body)).not.toContain('Waiting for the next Reading');
    expect(body).not.toContain('name="point"');
  });

  test('a failed record shows its notice inline and nothing advances', () => {
    const body = page({}, { notice: 'indistinct', point: 'dry' });
    expect(text(body)).toContain('too close together. Nothing was saved.');
    expect(body).toContain('data-step="dry"');
  });
});

describe('The Calibrate entry points', () => {
  const tile = (status: string): Lot => lot(status, { sensors: undefined });

  function tiles(canCalibrate: boolean, lots: Lot[]): string {
    return render(LotTiles, { props: { lots, now, timeZone: 'UTC', canCalibrate } }).body;
  }

  test('an Administrator sees Calibrate beside a needs-calibration tile, as a sibling of the tile link', () => {
    const body = tiles(true, [tile('needsCalibration')]);
    expect(body).toContain(`href="/garden/${lotId}/calibrate"`);
    expect(text(body)).toContain('Calibrate');
    expect(body).not.toMatch(/<a[^>]*href="\/garden\/[^"]*"[^>]*>(?:(?!<\/a>)[\s\S])*href="[^"]*calibrate"/u);
  });

  test('a Member sees no Calibrate, and no other status shows it', () => {
    expect(tiles(false, [tile('needsCalibration')])).not.toContain('/calibrate');
    expect(tiles(true, [tile('ok'), tile('needsWater'), tile('unknown'), tile('paused'), tile('noNode')])).not.toContain('/calibrate');
  });
});
