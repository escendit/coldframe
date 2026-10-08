import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import LotDetailPage from '../../../apps/ts/web/src/routes/(app)/garden/[lotId]/+page.svelte';
import HistoryChartView from '$lib/components/HistoryChart.svelte';
import { deviceCells, formatValue, heroSpoken, historyChart, lotHero, pickerQuantities, sensorCells, type HeroContext, type LotHistory, type SensorReading } from '$lib/lot-detail';
import type { Lot } from '$lib/lots';
import { createLastGoodStore } from '$lib/server/last-good';
import { getLotHistory, loadLotDetail } from '$lib/server/lot-detail';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { read, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: 'user-1', name: 'Simon Novak' } } } };
const siteId = '0192a000-0000-7000-8000-00000000000a';
const lotId = '0192a000-0000-7000-8000-000000000011';
const home: Site = { id: siteId, name: 'Home garden', role: 'Owner' };

const now = new Date('2026-10-06T07:17:00.000Z');
const live: HeroContext = { now, locale: 'en', timeZone: 'UTC', staleSince: null, admin: true, hubId: null };
const stale: HeroContext = { ...live, staleSince: new Date('2026-10-06T07:02:00.000Z') };

function lot(status: string, fields: Partial<Lot> = {}): Lot {
  return { id: lotId, name: 'Tomatoes', status: status as Lot['status'], statusSince: '2026-10-06T05:45:00.000Z', ...fields };
}

const node = { deviceId: '7c19000000000001', batteryPercent: 62, charging: 'charging', lastSeenAt: '2026-10-06T07:02:00.000Z' } as const;
const sensors: SensorReading[] = [
  { quantity: 'soil_moisture', value: 1840, unit: 'raw', measuredAt: '2026-10-06T07:02:00.000Z' },
  { quantity: 'air_temperature', value: 14.4, unit: '°C', measuredAt: '2026-10-06T07:02:00.000Z' },
  { quantity: 'relative_humidity', value: 78.2, unit: '%', measuredAt: '2026-10-06T07:02:00.000Z' },
  { quantity: 'gas_resistance', value: 142.37, unit: 'kΩ', measuredAt: '2026-10-06T07:02:00.000Z' },
];
const needsCalibration = lot('needsCalibration', { node, sensors, lastReadingAt: '2026-10-06T07:02:00.000Z' });
const ok = lot('ok', { node, sensors, moisturePercent: 35.2, lowThresholdPercent: 25, lastReadingAt: '2026-10-06T07:02:00.000Z' });

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

const history: LotHistory = {
  quantity: 'soil_moisture',
  unit: 'raw',
  days: [
    { day: '2026-10-04', low: 1700, high: 2100, readingCount: 96 },
    { day: '2026-10-05', low: 1840, high: 2210, readingCount: 96 },
    { day: '2026-10-06', low: 1790, high: 2050, readingCount: 30 },
  ],
};

interface PageData {
  detail: unknown;
  notice: string | null;
  staleSince: string | null;
}

function page(data: Partial<PageData> & { lot?: Lot | null; role?: Site['role'] }, context: { histories?: Record<string, LotHistory>; hubId?: string | null; thresholds?: unknown } = {}): string {
  const site = { ...home, role: data.role ?? 'Owner' };
  const detail = data.lot === null ? null : { lot: data.lot ?? ok, histories: context.histories ?? { soil_moisture: history }, hubId: context.hubId ?? null, thresholds: context.thresholds ?? null };
  const props = {
    data: {
      user: { displayName: 'Simon', initials: 'S' },
      theme: 'system',
      sites: [site],
      currentSite: site,
      sitesNotice: null,
      sitesStale: null,
      detail,
      notice: data.notice ?? null,
      staleSince: data.staleSince ?? null,
      loadedAt: now.toISOString(),
      timeZone: 'UTC',
    },
    params: { lotId },
  };
  return render(LotDetailPage, { props: props as never }).body;
}

describe('Lot detail hero', () => {
  test('UX-DR27 the hero names the Lot, the status icon, label and since, and shows the soil Reading as raw while it needs calibration', () => {
    const hero = lotHero(needsCalibration, live);
    expect(hero).toMatchObject({ name: 'Tomatoes', icon: 'tools', label: 'Needs Calibration', since: 'since 5:45 AM', value: 'raw 1,840', unit: null, low: null, reading: null, orange: false });
    // The value is the Server's raw count, whatever percentage a Server might send.
    expect(lotHero({ ...needsCalibration, moisturePercent: 55 }, live).value).toBe('raw 1,840');
    expect(lotHero(lot('needsCalibration', { node, sensors: [] }), live).value).toBe('raw');
  });

  test('UX-DR27 the hero shows the Server percentage as ~N with low Threshold and the ±5 % reading time, orange only when water is needed', () => {
    const hero = lotHero(ok, live);
    expect(hero).toMatchObject({ value: '~35', unit: '%', low: 'low 25%', reading: '±5 % · 7:02 AM', orange: false, icon: 'checkmark--outline' });
    expect(lotHero({ ...ok, status: 'needsWater' }, live)).toMatchObject({ orange: true, icon: 'rain-drop' });
    // Without a percentage from the Server there is no value, low or reading.
    expect(lotHero({ ...ok, moisturePercent: undefined }, live)).toMatchObject({ value: null, low: null, reading: null });
  });

  test('UX-DR27 paused by Site says Paused with the Site and, for Admin+, how to resume; a Member is not told to resume', () => {
    const paused = lot('paused', { pausedBy: ['device', 'site'], node, sensors });
    expect(lotHero(paused, live).notes).toEqual(['Paused with the Site', 'Resume the Site to resume this Node']);
    expect(lotHero(paused, { ...live, admin: false }).notes).toEqual(['Paused with the Site']);
    expect(lotHero(paused, live).value).toBe('—');
  });

  test('UX-DR27 stale: the hero says what the status was and as of when, with no value, since or percentage', () => {
    const hero = lotHero(ok, stale);
    expect(hero).toMatchObject({ variant: 'stale', icon: 'cloud--offline', label: 'Was OK', since: null, value: null, low: null, reading: null, asOf: 'as of 7:02 AM', notes: [] });
  });

  test('UX-DR78 needsCalibration says no % until calibrated; unknown says Check power or range, or names the Hub', () => {
    expect(lotHero(needsCalibration, live).notes).toEqual(['no % until calibrated']);
    const node_ = lot('unknown', { unknownCause: 'node', statusSince: '2026-10-06T07:05:00.000Z', lastReadingAt: '2026-10-06T01:05:00.000Z' });
    expect(lotHero(node_, live)).toMatchObject({ label: 'Silent · unknown', notes: ['Check power or range.'], value: '6 h' });
    const hub = lot('unknown', { unknownCause: 'hub', statusSince: '2026-10-06T07:05:00.000Z' });
    expect(lotHero(hub, { ...live, hubId: '3F2A' }).notes).toEqual(["Hub 3F2A is silent; Lots behind it can't be read."]);
    expect(lotHero(hub, live).label).toBe('Hub silent · unknown');
    expect(lotHero(hub, live).notes).toEqual(["The Hub is silent; Lots behind it can't be read."]);
  });

  test('UX-DR78 paused says paused until the date, or just Paused', () => {
    expect(lotHero(lot('paused', { pausedBy: ['device'], pausedUntil: '2026-11-01T00:00:00.000Z' }), live)).toMatchObject({ label: 'Paused', notes: ['Paused until Nov 1'], value: '—' });
    expect(lotHero(lot('paused', { pausedBy: ['device'] }), live).notes).toEqual(['Paused']);
  });

  test('UX-DR63 UX-DR78 a noNode Lot shows an empty detail: no value, no Sensors, no chart, and the web says to add a Node from the mobile app', () => {
    const body = page({ lot: lot('noNode') });
    expect(lotHero(lot('noNode'), live)).toMatchObject({ label: 'No Node', value: null, notes: ['This Lot has no Node.'] });
    expect(text(body)).toContain('Add a Node from the mobile app.');
    expect(body).not.toContain('cf-cell');
    expect(body).not.toContain('cf-chart');
    expect(text(body)).not.toContain('Sensors');
  });
});

describe('Sensor cells', () => {
  test('UX-DR28 a cell is a label over a value in the Server unit: raw count, whole °C and %, kΩ to 3 significant digits, with the Reading time', () => {
    expect(sensorCells(sensors, live).map((cell) => [cell.label, cell.value, cell.time])).toEqual([
      ['Soil moisture', 'raw 1,840', '7:02 AM'],
      ['Temperature', '14 °C', '7:02 AM'],
      ['Humidity', '78 %', '7:02 AM'],
      ['Air (gas)', '142 kΩ', '7:02 AM'],
    ]);
    expect([0.4237, 1.4237, 14.237, 1423.7, 14237].map((value) => formatValue('gas_resistance', value, 'en'))).toEqual(['0.424 kΩ', '1.42 kΩ', '14.2 kΩ', '1,420 kΩ', '14,200 kΩ']);
  });

  test('UX-DR28 stale cells keep their label and show no value or time', () => {
    expect(sensorCells(sensors, stale).map((cell) => [cell.value, cell.time])).toEqual([['—', null], ['—', null], ['—', null], ['—', null]]);
  });

  test('UX-DR28 UX-DR63 the page draws a 3-up row of Sensor cells with the Server values and no Threshold control', () => {
    const body = page({ lot: ok });
    expect(body.match(/class="cf-cell svelte-[^"]*" data-quantity=/gu)).toHaveLength(4);
    expect(text(body)).toContain('Soil moisture raw 1,840 7:02 AM Temperature 14 °C');
    expect(body).toContain('cf-cells--sensors');
    expect(body).not.toMatch(/<button[^>]*>[^<]*Threshold/u);
  });

  test('UX-DR28 a Node without a Reading has an empty Sensors section, and the picker offers soil moisture alone', () => {
    const body = page({ lot: lot('ok', { node, sensors: [] }) }, { histories: {} });
    expect(text(body)).toContain('No Readings yet.');
    expect(pickerQuantities([])).toEqual(['soil_moisture']);
    expect(pickerQuantities(sensors)).toEqual(['soil_moisture', 'air_temperature', 'relative_humidity', 'gas_resistance']);
  });
});

describe('Device cells', () => {
  test('UX-DR29 the Node cell shows battery and charging, the other last seen and every 15 min; below 20 % the icon is battery--low', () => {
    expect(deviceCells(node, live)).toEqual([
      { id: 'battery', label: 'Node 7c19000000000001', value: '62 %', meta: 'charging', icon: null },
      { id: 'lastSeen', label: 'Last seen', value: '7:02 AM', meta: 'every 15 min', icon: null },
    ]);
    expect(deviceCells({ ...node, batteryPercent: 19, charging: 'notCharging' }, live)[0]).toMatchObject({ value: '19 %', meta: 'not charging', icon: 'battery--low' });
    expect(deviceCells({ ...node, batteryPercent: 20 }, live)[0]?.icon).toBeNull();
  });

  test('UX-DR29 a missing battery, charger state or last seen reads as a dash; stale shows nothing live', () => {
    expect(deviceCells({ deviceId: node.deviceId }, live)).toEqual([
      { id: 'battery', label: 'Node 7c19000000000001', value: '—', meta: '', icon: null },
      { id: 'lastSeen', label: 'Last seen', value: '—', meta: 'every 15 min', icon: null },
    ]);
    expect(deviceCells(node, stale).map((cell) => cell.value)).toEqual(['—', '—']);
  });

  test('UX-DR29 each Device cell links to the Devices list; a low battery shows battery--low on the page', () => {
    const body = page({ lot: lot('ok', { node: { ...node, batteryPercent: 14 }, sensors, moisturePercent: 40 }) });
    expect(body.match(/<a class="cf-cell__link[^"]*" href="\/devices"/gu)).toHaveLength(2);
    expect(body).toContain('data-icon="battery--low"');
    expect(text(body)).toContain('Node 7c19000000000001 14 % charging');
  });
});

describe('History chart', () => {
  test('UX-DR32 one bar per day with Readings, gaps for the others, over 30 days ending today; no zero bars', () => {
    const chart = historyChart(history, 'soil_moisture', now, 'en');
    expect(chart.bars.map((bar) => [bar.slot, bar.day, bar.low])).toEqual([[27, '2026-10-04', 1700], [28, '2026-10-05', 1840], [29, '2026-10-06', 1790]]);
    expect(chart.axis).toEqual({ start: 'Sep 7', end: 'Oct 6' });
    expect(chart.bars.every((bar) => bar.height > 0 && bar.height <= 1)).toBe(true);
    // Days outside the 30 shown are dropped; an empty history has no bars.
    expect(historyChart({ ...history, days: [{ day: '2026-08-01', low: 1, high: 2, readingCount: 1 }] }, 'soil_moisture', now, 'en').bars).toEqual([]);
  });

  test('UX-DR32 without a Threshold every bar is normal-style: an outline, drawn from tokens only', () => {
    const body = render(HistoryChartView, { props: { chart: historyChart(history, 'soil_moisture', now, 'en') } }).body;
    expect(body.match(/<rect class="cf-chart__bar/gu)).toHaveLength(3);
    const source = read(`${webSrc}/lib/components/HistoryChart.svelte`);
    expect(source).toContain('var(--cf-color-chart-bar)');
    expect(source).not.toMatch(/animation|transition/u);
    expect(source).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(/iu);
    // No Threshold and a raw History: no band, no low line, no below-low bar, no legend.
    expect(body).not.toMatch(/cf-chart__band|cf-chart__low-line|cf-chart__bar--below-low|solid bar/u);
  });

  const percentHistory: LotHistory = {
    quantity: 'soil_moisture',
    unit: '%',
    days: [
      { day: '2026-10-04', low: 20, high: 60, readingCount: 50 },
      { day: '2026-10-05', low: 45, high: 65, readingCount: 96 },
      { day: '2026-10-06', low: 30, high: 55, readingCount: 30 },
    ],
  };
  const band = { low: 30, high: 70 };

  test('UX-DR5 UX-DR32 a percent History with a Threshold draws the band between low and high and marks the days whose low is below low', () => {
    const chart = historyChart(percentHistory, 'soil_moisture', now, 'en', band);
    expect(chart.band).toEqual({ low: 0.3, high: 0.7 });
    expect(chart.bars.map((bar) => [bar.day, bar.height, bar.belowLow])).toEqual([['2026-10-04', 0.2, true], ['2026-10-05', 0.45, false], ['2026-10-06', 0.3, false]]);
    expect(chart.legend).toBe('solid bar = below 30 %');
    expect(chart.lowPercent).toBe(30);
  });

  test('UX-DR5 UX-DR33 the readout and the accessible summary speak percentages and name the below-low days', () => {
    const chart = historyChart(percentHistory, 'soil_moisture', now, 'en', band);
    expect(chart.bars[0]?.readout).toBe('Oct 4: lowest 20 %, below 30 %');
    expect(chart.latest?.readout).toBe('Oct 6: lowest 30 %');
    expect(chart.summary).toBe('Soil moisture, 30 days, lowest 20 % on Oct 4, 3 days with Readings. Below 30 % on Oct 4.');
    const none = historyChart({ ...percentHistory, days: percentHistory.days.slice(1) }, 'soil_moisture', now, 'en', band);
    expect(none.summary).toContain('Never below 30 %.');
  });

  test('UX-DR5 a high is optional: without one the band runs to the top and no high line is drawn', () => {
    const chart = historyChart(percentHistory, 'soil_moisture', now, 'en', { low: 30, high: null });
    expect(chart.band).toEqual({ low: 0.3, high: null });
    const body = render(HistoryChartView, { props: { chart } }).body;
    expect(body).toContain('cf-chart__band');
    expect(body).not.toContain('cf-chart__high-line');
  });

  test('UX-DR5 the band is drawn only when the History unit is percent for soil moisture', () => {
    expect(historyChart(history, 'soil_moisture', now, 'en', band).band).toBeNull();
    expect(historyChart(history, 'soil_moisture', now, 'en', band).legend).toBeNull();
    expect(historyChart(history, 'soil_moisture', now, 'en', band).bars.every((bar) => !bar.belowLow)).toBe(true);
    const temperature: LotHistory = { quantity: 'air_temperature', unit: '°C', days: [{ day: '2026-10-06', low: 11.6, high: 18.2, readingCount: 40 }] };
    expect(historyChart(temperature, 'air_temperature', now, 'en', band).band).toBeNull();
    expect(historyChart(percentHistory, 'soil_moisture', now, 'en', null).band).toBeNull();
  });

  test('UX-DR5 UX-DR32 the chart draws the band, a 2 px low line, a dashed 1 px high line and solid below-low bars, from tokens, with the legend', () => {
    const body = render(HistoryChartView, { props: { chart: historyChart(percentHistory, 'soil_moisture', now, 'en', band) } }).body;
    expect(body).toContain('cf-chart__band');
    expect(body).toContain('cf-chart__low-line');
    expect(body).toContain('cf-chart__high-line');
    expect(body.match(/cf-chart__bar--below-low/gu)).toHaveLength(1);
    expect(text(body)).toContain('solid bar = below 30 %');
    const source = read(`${webSrc}/lib/components/HistoryChart.svelte`);
    for (const token of ['--cf-color-chart-band', '--cf-color-chart-high-line', '--cf-color-chart-bar-below-low']) {
      expect(source).toContain(`var(${token})`);
    }
    expect(source).toMatch(/stroke-dasharray/u);
    expect(source).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(/iu);
  });

  test('UX-DR33 the readout names a day: soil its low, the other quantities their min and max', () => {
    const soil = historyChart(history, 'soil_moisture', now, 'en');
    expect(soil.latest?.readout).toBe('Oct 6: lowest raw 1,790');
    const temperature = historyChart({ quantity: 'air_temperature', unit: '°C', days: [{ day: '2026-10-06', low: 11.6, high: 18.2, readingCount: 40 }] }, 'air_temperature', now, 'en');
    expect(temperature.latest?.readout).toBe('Oct 6: 12 °C to 18 °C');
  });

  test('UX-DR33 a Sensor picker switches the quantity and the chart, and the page offers it only with more than one Sensor', () => {
    const temperatureHistory: LotHistory = { quantity: 'air_temperature', unit: '°C', days: [{ day: '2026-10-06', low: 11.6, high: 18.2, readingCount: 40 }] };
    const body = page({ lot: ok }, { histories: { soil_moisture: history, air_temperature: temperatureHistory } });
    expect(body).toContain('cf-segmented');
    expect(text(body)).toMatch(/Sensor Soil moisture Temperature Humidity Air \(gas\)/u);
    expect(body).toMatch(/aria-pressed="true"[^>]*>[\s\S]*?Soil moisture/u);
    expect(page({ lot: lot('ok', { node, sensors: sensors.slice(0, 1), moisturePercent: 40 }) })).not.toContain('cf-segmented');
  });

  test('UX-DR33 UX-DR98 nothing animates and the chart is read through a text summary, not its bars', () => {
    const source = read(`${webSrc}/lib/components/HistoryChart.svelte`);
    expect(source).not.toMatch(/transition|animation|@keyframes/u);
    const body = render(HistoryChartView, { props: { chart: historyChart(history, 'soil_moisture', now, 'en') } }).body;
    expect(body).toContain('role="img"');
    expect(body).toMatch(/<svg[^>]*aria-hidden="true"/u);
  });

  test('UX-DR98 the text summary is the chart\'s accessibility label: quantity, 30 days, lowest value and day, days with Readings', () => {
    expect(historyChart(history, 'soil_moisture', now, 'en').summary).toBe('Soil moisture, 30 days, lowest raw 1,700 on Oct 4, 3 days with Readings.');
    expect(historyChart({ ...history, days: history.days.slice(0, 1) }, 'soil_moisture', now, 'en').summary).toBe('Soil moisture, 30 days, lowest raw 1,700 on Oct 4, 1 day with Readings.');
    expect(historyChart({ ...history, days: [] }, 'soil_moisture', now, 'en').summary).toBe('Soil moisture, 30 days, no Readings.');
    const body = page({ lot: ok });
    expect(body).toContain('aria-label="Soil moisture, 30 days, lowest raw 1,700 on Oct 4, 3 days with Readings."');
  });

  test('UX-DR98 the hero is spoken as the tile is: one label with the status, value and since the Server gave', () => {
    expect(heroSpoken(ok, live)).toBe('Tomatoes, OK, about 35 percent, low 25 percent');
    expect(heroSpoken(ok, stale)).toBe('Tomatoes, was OK, not live, as of 7:02 AM');
    expect(page({ lot: ok })).toContain('aria-label="Tomatoes, OK, about 35 percent, low 25 percent"');
  });
});

describe('Lot detail page', () => {
  test('UX-DR63 the page is hero, Sensor cells, History chart, then Device cells, with a way back to Garden', () => {
    const body = page({ lot: ok });
    const order = ['Back to Garden', 'cf-hero', 'cf-detail-sensors', 'cf-detail-history', 'cf-cells--device'].map((marker) => body.indexOf(marker));
    expect(order.every((position) => position > -1)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
    expect(body).toContain('href="/garden"');
    expect(body).not.toMatch(/<button[^>]*>[^<]*(?:Calibrate|Pause|Resume|Thresholds)/u);
  });

  test('UX-DR63 UX-DR19 stale: the stale header is shown and no live value is: not the hero value, the Sensor values, battery or last seen', () => {
    const body = page({ lot: ok, staleSince: '2026-10-06T07:02:00.000Z' });
    expect(text(body)).toContain("can't reach your Server");
    expect(text(body)).toContain('Was OK');
    expect(text(body)).not.toMatch(/raw 1,840|14 °C|78 %|142 kΩ|62 %|~35/u);
    expect(text(body)).toContain('Soil moisture —');
  });

  test('UX-DR63 a Lot that is gone, or a Server that cannot be reached, is a notice and no hero', () => {
    const missing = page({ lot: null, notice: 'notFound' });
    expect(text(missing)).toContain('Home garden has no such Lot.');
    expect(missing).not.toContain('cf-hero');
    expect(text(page({ lot: null, notice: 'unreachable' }))).toContain("Can't reach your Coldframe Server.");
  });

  test('UX-DR63 the one-column fallback: cells go 1-up below 400 px of their own width, and nothing is clipped', () => {
    const source = read(`${webSrc}/routes/(app)/garden/[lotId]/+page.svelte`);
    expect(source).toMatch(/@container \(min-width: 400px\)/u);
    expect(source).not.toMatch(/overflow\s*:\s*hidden|text-overflow|white-space\s*:\s*nowrap/u);
  });
});

describe('Lot detail load', () => {
  const now_ = () => new Date('2026-10-06T07:17:00.000Z');

  function server(overrides: { lot?: Lot; fail?: boolean } = {}) {
    return fakeServer((request) => {
      if (overrides.fail === true) {
        return Promise.reject(fetchFailed('ECONNREFUSED'));
      }
      const url = new URL(request.url);
      if (url.pathname.endsWith('/history')) {
        return jsonResponse(200, { ...history, quantity: url.searchParams.get('quantity') });
      }
      if (url.pathname.endsWith('/devices')) {
        return jsonResponse(200, { devices: [{ id: '3f2a9c0d1e4b5a67', kind: 'hub', online: true }] });
      }
      return jsonResponse(200, overrides.lot ?? ok);
    });
  }

  test('UX-DR63 the Lot and one history per Sensor quantity are read with the token, the default window and no client conversion', async () => {
    const fake = server();
    const data = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: fake.fetch, now: now_, lastGood: createLastGoodStore() });
    expect(data).toMatchObject({ notice: null, staleSince: null, detail: { lot: ok, hubId: null } });
    expect(fake.seen.map((request) => `${request.method} ${request.path}`).sort()).toEqual([
      `GET /sites/${siteId}/lots/${lotId}`,
      `GET /sites/${siteId}/lots/${lotId}/history`,
      `GET /sites/${siteId}/lots/${lotId}/history`,
      `GET /sites/${siteId}/lots/${lotId}/history`,
      `GET /sites/${siteId}/lots/${lotId}/history`,
    ]);
    expect(fake.seen.every((request) => request.authorization === 'Bearer access-token-1')).toBe(true);
    expect(Object.keys(data.detail?.histories ?? {})).toEqual(['soil_moisture', 'air_temperature', 'relative_humidity', 'gas_resistance']);
  });

  test('UX-DR63 a Lot without a Node reads no history', async () => {
    const fake = server({ lot: lot('noNode') });
    const data = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore() });
    expect(data.detail?.histories).toEqual({});
    expect(fake.seen).toHaveLength(1);
  });

  test('UX-DR78 a Hub-silent Lot names the Hub the Server lists', async () => {
    const fake = server({ lot: lot('unknown', { unknownCause: 'hub', node, sensors: [] }) });
    const data = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: fake.fetch, lastGood: createLastGoodStore() });
    expect(data.detail?.hubId).toBe('3f2a9c0d1e4b5a67');
  });

  test('UX-DR63 the history call sends the quantity as the query', async () => {
    const fake = server();
    await getLotHistory(locals, siteId, lotId, 'gas_resistance', { serverUrl, fetch: fake.fetch });
    expect(fake.seen).toHaveLength(1);
    expect(fake.seen[0]?.path).toBe(`/sites/${siteId}/lots/${lotId}/history`);
  });

  test('UX-DR79 after a refresh and one retry fail the last good detail is served as stale; a 404 is a notice; a 401 signs out', async () => {
    const store = createLastGoodStore();
    const good = server();
    await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: good.fetch, now: now_, lastGood: store });
    const down = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: server({ fail: true }).fetch, now: () => new Date('2026-10-06T07:30:00.000Z'), lastGood: store });
    expect(down).toMatchObject({ notice: null, staleSince: '2026-10-06T07:17:00.000Z', detail: { lot: ok } });
    const missing = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: fakeServer(() => problemResponse(404, 'lot-not-found')).fetch, lastGood: store });
    expect(missing).toMatchObject({ detail: null, notice: 'notFound' });
    try {
      await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch, lastGood: store });
      throw new Error('Expected a redirect.');
    } catch (error) {
      expect(isRedirect(error)).toBe(true);
    }
  });

  test('UX-DR79 with no kept detail an unreachable Server is the unreachable notice, and no Site is no call', async () => {
    const empty = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: server({ fail: true }).fetch, lastGood: createLastGoodStore() });
    expect(empty).toMatchObject({ detail: null, notice: 'unreachable' });
    const fake = server();
    expect(await loadLotDetail(locals, null, lotId, new FakeCookies(), null, { serverUrl, fetch: fake.fetch })).toMatchObject({ detail: null, notice: null });
    expect(fake.seen).toEqual([]);
  });
});

describe('Lot detail Calibrate entry (Story 5.2)', () => {
  const soilSensor = { quantity: 'soil_moisture', value: 1840, unit: 'raw', measuredAt: '2026-10-06T07:02:00.000Z', sensorId: '0192a000-0000-7000-8000-0000000000aa', calibratable: true } as const;
  const calibratable = lot('needsCalibration', { node, sensors: [soilSensor, ...sensors.slice(1)] });

  test('an Owner or Administrator sees Calibrate for a calibratable Sensor', () => {
    for (const role of ['Owner', 'Administrator'] as const) {
      const body = page({ lot: calibratable, role });
      expect(body).toContain(`href="/garden/${lotId}/calibrate"`);
      expect(text(body)).toContain('Calibrate');
    }
  });

  test('a Member sees no Calibrate, and neither does a Lot without a calibratable Sensor', () => {
    expect(page({ lot: calibratable, role: 'Member' })).not.toContain('/calibrate');
    expect(page({ lot: needsCalibration, role: 'Owner' })).not.toContain('/calibrate');
  });
});

describe('Thresholds on Lot detail', () => {
  const soilWithId: SensorReading[] = [{ quantity: 'soil_moisture', value: 40, unit: '%', measuredAt: '2026-10-06T07:02:00.000Z', sensorId: '0192a000-0000-7000-8000-0000000000aa', calibratable: true }];
  const calibrated = lot('ok', { node, sensors: soilWithId, moisturePercent: 40, lowThresholdPercent: 30 });
  const thresholds = { unit: '%', low: { kind: 'default', value: 30 }, high: { kind: 'override', value: 70 } };

  test('UX-DR45 an Owner reaches Thresholds from Lot detail and from the Sensor cell', () => {
    const body = page({ lot: calibrated }, { thresholds, histories: { soil_moisture: { quantity: 'soil_moisture', unit: '%', days: [] } } });
    expect(text(body)).toContain('Set Thresholds');
    expect(body).toContain(`href="/garden/${lotId}/thresholds"`);
    expect(body).toMatch(/<a[^>]*class="cf-cell__link[^"]*"[^>]*href="\/garden\/[^"]*\/thresholds#sensor-0192a000-0000-7000-8000-0000000000aa"/u);
  });

  test('UX-DR84 a Member sees the Thresholds read-only: the values and a view link, no edit wording', () => {
    const body = page({ lot: calibrated, role: 'Member' }, { thresholds });
    expect(text(body)).toContain('Soil moisture: low 30 %, high 70 %');
    expect(text(body)).toContain('View Thresholds');
    expect(text(body)).not.toContain('Set Thresholds');
  });

  test('UX-DR5 Lot detail passes the Threshold to the chart only for a percent History', () => {
    const percent: LotHistory = { quantity: 'soil_moisture', unit: '%', days: [{ day: '2026-10-06', low: 20, high: 50, readingCount: 9 }] };
    expect(page({ lot: calibrated }, { thresholds, histories: { soil_moisture: percent } })).toContain('cf-chart__band');
    expect(page({ lot: calibrated }, { thresholds, histories: { soil_moisture: history } })).not.toContain('cf-chart__band');
  });

  test('the Lot detail read also reads the soil Sensor Thresholds, and a failure of that read does not fail the detail', async () => {
    const server = fakeServer((request) => {
      const path = new URL(request.url).pathname;
      if (path.endsWith('/thresholds')) {
        return problemResponse(503, 'unavailable');
      }
      return path.endsWith('/history') ? jsonResponse(200, history) : jsonResponse(200, calibrated);
    });
    const data = await loadLotDetail(locals, home, lotId, new FakeCookies(), null, { serverUrl, fetch: server.fetch, lastGood: createLastGoodStore() });
    expect(server.seen.some((request) => request.path.endsWith('/thresholds'))).toBe(true);
    expect(data.notice).toBeNull();
    expect(data.detail?.thresholds).toBeNull();
  });
});
