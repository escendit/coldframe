import { createRawSnippet } from 'svelte';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import Hatch from '$lib/components/Hatch.svelte';
import Icon from '$lib/components/Icon.svelte';
import LotTiles from '$lib/components/LotTiles.svelte';
import { formatSoil, lotTile, soilApprox, statusOf, type TileContext } from '$lib/lot-tiles';
import type { Lot } from '$lib/lots';
import { read, webSrc } from './helpers.ts';

const now = new Date('2026-10-06T07:17:00.000Z');
const live: TileContext = { now, locale: 'en-GB', timeZone: 'UTC', staleSince: null };
const stale: TileContext = { ...live, staleSince: new Date('2026-10-06T07:02:00.000Z') };

function lot(name: string, status: string, fields: Partial<Lot> = {}): Lot {
  return { id: `id-${name}`, name, status: status as Lot['status'], statusSince: '2026-10-06T03:45:00.000Z', ...fields };
}

const tomatoes = lot('Tomatoes', 'needsWater', { lastReadingAt: '2026-10-06T07:02:00.000Z', moisturePercent: 21.7, lowThresholdPercent: 30 });
const peppers = lot('Peppers', 'needsCalibration', { lastReadingAt: '2026-10-06T07:02:00.000Z' });
const beans = lot('Beans', 'unknown', { unknownCause: 'node', statusSince: '2026-10-06T07:05:00.000Z', lastReadingAt: '2026-10-06T01:05:00.000Z', moisturePercent: 41 });
const peas = lot('Peas', 'unknown', { unknownCause: 'hub', statusSince: '2026-10-06T07:05:00.000Z', lastReadingAt: '2026-10-06T07:02:00.000Z', moisturePercent: 38 });
const herbs = lot('Herbs', 'ok', { lastReadingAt: '2026-10-06T07:03:00.000Z', moisturePercent: 35.2, lowThresholdPercent: 25 });
const strawberries = lot('Strawberries', 'paused', { pausedBy: ['device'], pausedUntil: '2026-11-01T00:00:00.000Z' });
const lettuce = lot('Lettuce', 'paused', { pausedBy: ['device', 'site'] });
const potatoes = lot('Potatoes', 'noNode');
const all = [tomatoes, peppers, beans, peas, herbs, strawberries, lettuce, potatoes];

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

/** The markup of one Lot's grid cell. */
function cell(body: string, target: Lot): string {
  const attribute = body.indexOf(`data-lot="${target.id}"`);
  expect(attribute).toBeGreaterThan(-1);
  const start = body.indexOf('>', attribute) + 1;
  return body.slice(start, body.indexOf('</li>', start));
}

function tiles(lots: readonly Lot[], staleSince: Date | null = null): string {
  return render(LotTiles, { props: { lots, now, timeZone: 'UTC', staleSince } }).body;
}

describe('Soil moisture formatting', () => {
  test('UX-DR128 soil moisture is ~ and the nearest 5, never a decimal', () => {
    expect([21.7, 22.5, 20, 0, 2.4, 97.6, 100].map(soilApprox)).toEqual([20, 25, 20, 0, 0, 100, 100]);
    expect(formatSoil(21.7, 'en')).toBe('~20');
    expect(formatSoil(41, 'en', true)).toBe('~40%');
    expect(formatSoil(-3, 'en')).toBe('~0');
    expect(formatSoil(140, 'en')).toBe('~100');
  });

  test('UX-DR128 an uncalibrated Lot shows raw and never a percentage', () => {
    const tile = lotTile({ ...peppers, moisturePercent: 55 }, live);
    expect(tile.value).toBe('raw');
    expect(`${tile.value ?? ''} ${tile.foot ?? ''}`).not.toMatch(/\d/u);
    expect(tile.spoken).not.toMatch(/\d/u);
    expect(tile.level).toBeNull();
  });
});

describe('Lot tile view models', () => {
  test('UX-DR18 UX-DR77 UX-DR98 needs water: rain-drop, ~20, Reading time and low Threshold, one spoken label', () => {
    expect(lotTile(tomatoes, live)).toEqual({
      id: tomatoes.id,
      name: 'Tomatoes',
      status: 'needsWater',
      variant: 'needsWater',
      icon: 'rain-drop',
      label: 'Needs water',
      value: '~20',
      foot: '07:02 · low 30%',
      spoken: 'Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02',
      level: 21.7,
      low: 30,
      hatched: false,
    });
  });

  test('UX-DR98 UX-DR128 a fractional low Threshold is spoken as the whole number the foot shows', () => {
    const tile = lotTile({ ...tomatoes, lowThresholdPercent: 29.6 }, live);
    expect(tile.foot).toBe('07:02 · low 30%');
    expect(tile.spoken).toBe('Tomatoes, needs water, about 20 percent, low 30 percent, Reading 07:02');
  });

  test('UX-DR18 UX-DR77 UX-DR98 OK: checkmark--outline, ~35, and only the parts the Server sent', () => {
    expect(lotTile(herbs, live)).toMatchObject({
      variant: 'ok',
      icon: 'checkmark--outline',
      label: 'OK',
      value: '~35',
      foot: '07:03 · low 25%',
      spoken: 'Herbs, OK, about 35 percent, low 25 percent',
      level: 35.2,
      low: 25,
    });
    expect(lotTile(lot('Basil', 'ok', { lastReadingAt: '2026-10-06T07:03:00.000Z' }), live)).toMatchObject({ value: null, foot: '07:03', spoken: 'Basil, OK', level: null, low: null });
    expect(lotTile(lot('Basil', 'ok'), live)).toMatchObject({ value: null, foot: null, spoken: 'Basil, OK' });
  });

  test('UX-DR18 UX-DR77 UX-DR98 unknown by the Node: silence since the last Reading, what it last read, hatched', () => {
    expect(lotTile(beans, live)).toMatchObject({
      variant: 'unknown',
      icon: 'help',
      label: 'Silent · unknown',
      value: '6 h',
      foot: 'was ~40% at 01:05',
      spoken: 'Beans, unknown, Node silent for 6 hours, last about 40 percent at 01:05',
      level: null,
      hatched: true,
    });
  });

  test('UX-DR18 UX-DR77 UX-DR98 unknown by the Hub: silence since the status changed', () => {
    expect(lotTile(peas, live)).toMatchObject({
      label: 'Hub silent · unknown',
      value: '12 min',
      foot: 'was ~40% at 07:02',
      spoken: 'Peas, unknown, Hub silent for 12 minutes, last about 40 percent at 07:02',
    });
  });

  test('UX-DR18 UX-DR77 unknown without a percentage or without a Reading says so', () => {
    expect(lotTile({ ...beans, moisturePercent: undefined }, live)).toMatchObject({ value: '6 h', foot: 'last Reading 01:05', spoken: 'Beans, unknown, Node silent for 6 hours, last Reading 01:05' });
    const fresh = lot('Chard', 'unknown', { unknownCause: 'node', statusSince: '2026-10-06T07:16:00.000Z' });
    expect(lotTile(fresh, live)).toMatchObject({ value: '1 min', foot: 'no Readings yet', spoken: 'Chard, unknown, Node silent for 1 minute, no Readings yet' });
    expect(lotTile(lot('Chard', 'unknown', { statusSince: '2026-10-04T07:16:00.000Z' }), live)).toMatchObject({ label: 'Silent · unknown', value: '2 d', spoken: 'Chard, unknown, Node silent for 2 days, no Readings yet' });
  });

  test('UX-DR18 UX-DR77 UX-DR98 needs calibration: tools, raw, no % until calibrated, hatched', () => {
    expect(lotTile(peppers, live)).toMatchObject({
      variant: 'needsCalibration',
      icon: 'tools',
      label: 'Needs Calibration',
      value: 'raw',
      foot: 'no % until calibrated',
      spoken: 'Peppers, needs Calibration, no percentage until calibrated',
      hatched: true,
    });
  });

  test('UX-DR18 UX-DR77 UX-DR98 paused: pause--outline, a dash, until the end or paused; by Site from pausedBy', () => {
    expect(lotTile(strawberries, live)).toMatchObject({ variant: 'paused', icon: 'pause--outline', label: 'Paused', value: '—', foot: 'until 1 Nov', spoken: 'Strawberries, paused until 1 November', hatched: false });
    expect(lotTile(lettuce, live)).toMatchObject({ label: 'Paused by Site', value: '—', foot: 'paused', spoken: 'Lettuce, paused with the Site' });
    expect(lotTile({ ...lettuce, pausedUntil: '2027-03-01T00:00:00.000Z' }, live)).toMatchObject({ label: 'Paused by Site', foot: 'until 1 Mar', spoken: 'Lettuce, paused until 1 March' });
    expect(lotTile(lot('Kale', 'paused', { pausedBy: ['device'] }), live)).toMatchObject({ label: 'Paused', foot: 'paused', spoken: 'Kale, paused' });
  });

  test('UX-DR18 UX-DR77 UX-DR98 no Node stays as it was', () => {
    expect(lotTile(potatoes, live)).toMatchObject({ variant: 'noNode', icon: 'add', label: 'No Node', value: '+', foot: 'add a Node', spoken: 'Potatoes, no Node, add a Node', hatched: false });
  });

  test('UX-DR77 a status this client does not know renders as unknown; nothing else is derived', () => {
    expect(statusOf(lot('Leeks', 'flooded'))).toBe('unknown');
    expect(lotTile(lot('Leeks', 'flooded', { statusSince: '2026-10-06T07:05:00.000Z' }), live)).toMatchObject({ status: 'unknown', variant: 'unknown', icon: 'help', label: 'Silent · unknown', value: '12 min' });
    // A dry Reading below the low Threshold does not make an OK Lot need water: the Server decides.
    expect(lotTile({ ...herbs, moisturePercent: 5 }, live)).toMatchObject({ status: 'ok', label: 'OK', value: '~5' });
  });

  test('UX-DR18 no secondary condition is appended to a label', () => {
    for (const target of all) {
      expect(lotTile(target, live).label).toMatch(/^(Needs water|OK|Silent · unknown|Hub silent · unknown|Needs Calibration|Paused|Paused by Site|No Node)$/u);
    }
  });

  test('UX-DR19 UX-DR98 stale: cloud--offline, Was ‹status›, no value, as of the last good refresh, nothing drawn as live', () => {
    const expected: [Lot, string, string][] = [
      [tomatoes, 'Was needs water', 'Tomatoes, was needs water, not live, as of 07:02'],
      [peppers, 'Was needs Calibration', 'Peppers, was needs Calibration, not live, as of 07:02'],
      [beans, 'Was silent · unknown', 'Beans, was unknown, not live, as of 07:02'],
      [peas, 'Was Hub silent · unknown', 'Peas, was unknown, not live, as of 07:02'],
      [herbs, 'Was OK', 'Herbs, was OK, not live, as of 07:02'],
      [strawberries, 'Was paused', 'Strawberries, was paused, not live, as of 07:02'],
      [lettuce, 'Was paused by Site', 'Lettuce, was paused, not live, as of 07:02'],
      [potatoes, 'Was no Node', 'Potatoes, was no Node, not live, as of 07:02'],
    ];
    for (const [target, label, spoken] of expected) {
      expect(lotTile(target, stale), target.name).toMatchObject({ variant: 'stale', icon: 'cloud--offline', label, value: null, foot: 'as of 07:02', spoken, level: null, low: null, hatched: false });
      expect(lotTile(target, stale).status).toBe(target.status);
    }
  });

  test('UX-DR19 a refresh older than today shows the weekday', () => {
    expect(lotTile(tomatoes, { ...live, staleSince: new Date('2026-10-04T18:00:00.000Z') }).foot).toBe('as of Sun');
  });
});

describe('Hatch', () => {
  test('UX-DR12 an inline SVG pattern coloured by the hatch tokens: 1.5 px lines every 8 px at 135°, no gradient', () => {
    const { body } = render(Hatch, { props: {} });
    expect(body).toMatch(/<svg[^>]*aria-hidden="true"/u);
    expect(body).toMatch(/<pattern[^>]*patternUnits="userSpaceOnUse"[^>]*width="8"[^>]*height="8"[^>]*patternTransform="rotate\(45\)"/u);
    const id = /<pattern[^>]*id="([^"]+)"/u.exec(body)?.[1] ?? '';
    expect(id).not.toBe('');
    expect(body).toContain(`fill="url(#${id})"`);
    const source = read(`${webSrc}/lib/components/Hatch.svelte`);
    expect(source).toMatch(/stroke:\s*var\(--cf-color-status-hatch-line\)/u);
    expect(source).toMatch(/stroke-width:\s*1\.5px/u);
    expect(source).toMatch(/fill:\s*var\(--cf-color-status-hatch-ground\)/u);
    expect(source).not.toMatch(/gradient\(|#[0-9a-f]{3,8}\b/iu);
  });

  test('UX-DR12 the plate is optional: with it the content sits on solid hatch ground', () => {
    const children = createRawSnippet(() => ({ render: () => '<p>content</p>' }));
    const plain = render(Hatch, { props: { children } }).body;
    const plated = render(Hatch, { props: { children, plate: true } }).body;
    expect(plain).toContain('<p>content</p>');
    expect(plain).not.toContain('cf-hatch--plate');
    expect(plated).toContain('cf-hatch--plate');
    expect(read(`${webSrc}/lib/components/Hatch.svelte`)).toMatch(/\.cf-hatch--plate[^{]*\{[^}]*background:\s*var\(--cf-color-status-hatch-ground\)/u);
  });

  test('UX-DR12 two hatches on one page do not share a pattern', () => {
    const body = tiles([peppers, beans]);
    const ids = [...body.matchAll(/<pattern[^>]*id="([^"]+)"/gu)].map((match) => match[1]);
    expect(ids).toHaveLength(2);
    expect(new Set(ids).size).toBe(2);
  });
});

describe('Lot tiles', () => {
  test('the Carbon icons of the tiles are registered', () => {
    for (const name of ['rain-drop', 'tools', 'pause--outline', 'cloud--offline', 'time'] as const) {
      expect(render(Icon, { props: { name } }).body).toContain('<svg');
    }
  });

  test('UX-DR17 each tile: name over icon and status label, then the value over the foot line', () => {
    const body = tiles(all);
    for (const target of all) {
      const html = cell(body, target);
      const order = ['cf-lot-tile__name', 'cf-icon', 'cf-lot-tile__label', 'cf-lot-tile__value', 'cf-lot-tile__foot'].map((marker) => html.indexOf(marker));
      expect(order.every((position) => position > -1), target.name).toBe(true);
      expect([...order].sort((a, b) => a - b), target.name).toEqual(order);
    }
    expect(text(cell(body, tomatoes))).toBe('Tomatoes Needs water ~20 7:02 AM · low 30%');
    expect(text(cell(body, beans))).toBe('Beans Silent · unknown 6 h was ~40% at 1:05 AM');
    expect(text(cell(body, peppers))).toBe('Peppers Needs Calibration raw no % until calibrated');
    expect(text(cell(body, strawberries))).toBe('Strawberries Paused — until Nov 1');
    expect(text(cell(body, potatoes))).toBe('Potatoes No Node + add a Node');
  });

  test('UX-DR17 the soil level and the low tick follow the Server values; hatched tiles put their text on the plate', () => {
    const body = tiles(all);
    expect(cell(body, tomatoes)).toMatch(/class="cf-lot-tile__level[^"]*" style="--cf-lot-level: 21\.7%;?"/u);
    expect(cell(body, tomatoes)).toMatch(/class="cf-lot-tile__low[^"]*" style="--cf-lot-low: 30%;?"/u);
    expect(cell(body, herbs)).toContain('--cf-lot-level: 35.2%');
    for (const target of [peppers, beans, peas]) {
      expect(cell(body, target), target.name).toContain('cf-hatch--plate');
      expect(cell(body, target), target.name).not.toContain('cf-lot-tile__level');
    }
    for (const target of [tomatoes, herbs, strawberries, lettuce, potatoes]) {
      expect(cell(body, target), target.name).not.toContain('cf-hatch');
    }
    const source = read(`${webSrc}/lib/components/LotTiles.svelte`);
    expect(source).toMatch(/aspect-ratio:\s*1 \/ 0\.82/u);
    expect(source).toMatch(/padding:\s*var\(--cf-spacing-tile-padding-web\)/u);
    expect(source).not.toMatch(/overflow\s*:\s*hidden|text-overflow|(?:^|[;{\s])(?:height|max-height|block-size|max-block-size)\s*:\s*\d/u);
  });

  test('UX-DR18 UX-DR99 each variant has its own class and icon, so shape and icon tell them apart without colour or text', () => {
    const body = tiles([tomatoes, peppers, beans, herbs, strawberries, potatoes]);
    const seen = [tomatoes, peppers, beans, herbs, strawberries, potatoes].map((target) => {
      const html = cell(body, target);
      return [/cf-lot-tile--([a-z-]+)/u.exec(html)?.[1], /data-icon="([^"]+)"/u.exec(html)?.[1]];
    });
    expect(seen).toEqual([
      ['needs-water', 'rain-drop'],
      ['needs-calibration', 'tools'],
      ['unknown', 'help'],
      ['ok', 'checkmark--outline'],
      ['paused', 'pause--outline'],
      ['no-node', 'add'],
    ]);
    const staleCell = cell(tiles([tomatoes], stale.staleSince), tomatoes);
    expect([/cf-lot-tile--([a-z-]+)/u.exec(staleCell)?.[1], /data-icon="([^"]+)"/u.exec(staleCell)?.[1]]).toEqual(['stale', 'cloud--offline']);
  });

  test('UX-DR18 the variants are styled from their tokens', () => {
    const source = read(`${webSrc}/lib/components/LotTiles.svelte`);
    const rule = (name: string): string => new RegExp(`\\.cf-lot-tile--${name}\\s*\\{([^}]*)\\}`, 'u').exec(source)?.[1] ?? '';
    expect(rule('needs-water')).toMatch(/background:\s*var\(--cf-color-status-water-fill\)[\s\S]*border:\s*0[\s\S]*color:\s*var\(--cf-color-status-water-ink\)/u);
    expect(rule('ok')).toMatch(/background:\s*var\(--cf-color-status-ok-fill\)[\s\S]*border:\s*1px solid var\(--cf-color-status-ok-border\)/u);
    expect(rule('unknown')).toMatch(/border:\s*1px dashed var\(--cf-color-status-unknown-border\)/u);
    expect(rule('needs-calibration')).toMatch(/border:\s*2px dashed var\(--cf-color-status-calibration-border\)/u);
    expect(rule('paused')).toMatch(/background:\s*var\(--cf-color-status-paused-fill\)[\s\S]*border:\s*2px solid var\(--cf-color-status-paused-border\)[\s\S]*color:\s*var\(--cf-color-status-paused-ink\)/u);
    expect(rule('no-node')).toMatch(/background:\s*transparent[\s\S]*border:\s*1px dotted var\(--cf-color-status-no-node-border\)/u);
    expect(rule('stale')).toMatch(/background:\s*transparent[\s\S]*border:\s*1px solid var\(--cf-color-stale-border\)[\s\S]*color:\s*var\(--cf-color-text-secondary\)/u);
    expect(source).toMatch(/\.cf-lot-tile--stale \.cf-lot-tile__foot\s*\{[^}]*color:\s*var\(--cf-color-stale-ink\)/u);
    expect(source).toMatch(/\.cf-lot-tile--needs-calibration \.cf-lot-tile__status\s*\{[^}]*color:\s*var\(--cf-color-status-calibration-ink\)/u);
  });

  test('UX-DR19 a stale tile has no fill, hatch, level or value, and says as of when', () => {
    const body = tiles(all, stale.staleSince);
    for (const target of all) {
      const html = cell(body, target);
      expect(html, target.name).toContain('cf-lot-tile--stale');
      expect(html, target.name).not.toMatch(/cf-hatch|cf-lot-tile__level|cf-lot-tile__low|cf-lot-tile__value/u);
      expect(html, target.name).toContain('data-icon="cloud--offline"');
    }
    expect(text(cell(body, tomatoes))).toBe('Tomatoes Was needs water as of 7:02 AM');
    expect(text(cell(body, herbs))).toBe('Herbs Was OK as of 7:02 AM');
    // The web has no skeleton: its first paint is server-rendered.
    expect(read(`${webSrc}/lib/components/LotTiles.svelte`)).not.toMatch(/skeleton/iu);
  });

  test('UX-DR20 tiles keep the Server order, carry the Server status and are not tappable', () => {
    const shuffled = [potatoes, herbs, tomatoes, beans];
    const body = tiles(shuffled);
    expect([...body.matchAll(/<li class="cf-lot-grid__cell[^"]*" data-lot="([^"]+)" data-status="([^"]+)"/gu)].map((match) => [match[1], match[2]])).toEqual(shuffled.map((target) => [target.id, target.status]));
    expect(body).not.toMatch(/<a |<button|tabindex/u);
  });

  test('UX-DR98 each tile is one accessibility element with the whole spoken label', () => {
    const body = tiles(all);
    const labels = [...body.matchAll(/<div class="cf-lot-tile [^"]*" role="img" aria-label="([^"]+)"/gu)].map((match) => match[1]);
    expect(labels).toEqual([
      'Tomatoes, needs water, about 20 percent, low 30 percent, Reading 7:02 AM',
      'Peppers, needs Calibration, no percentage until calibrated',
      'Beans, unknown, Node silent for 6 hours, last about 40 percent at 1:05 AM',
      'Peas, unknown, Hub silent for 12 minutes, last about 40 percent at 7:02 AM',
      'Herbs, OK, about 35 percent, low 25 percent',
      'Strawberries, paused until November 1',
      'Lettuce, paused with the Site',
      'Potatoes, no Node, add a Node',
    ]);
    const staleLabels = [...tiles([tomatoes], stale.staleSince).matchAll(/role="img" aria-label="([^"]+)"/gu)].map((match) => match[1]);
    expect(staleLabels).toEqual(['Tomatoes, was needs water, not live, as of 7:02 AM']);
  });

  test('UX-DR97 UX-DR108 one column below 400 px of grid width, then 2, 3 and 4; in one column the value follows the label', () => {
    const source = read(`${webSrc}/lib/components/LotTiles.svelte`);
    const css = /<style>([\s\S]*)<\/style>/u.exec(source)?.[1] ?? '';
    expect(css).toMatch(/\.cf-lot-grid-frame\s*\{[^}]*container-type:\s*inline-size/u);
    expect(css).toMatch(/\.cf-lot-grid\s*\{[^}]*grid-template-columns:\s*minmax\(0, 1fr\);/u);
    const steps = [...css.matchAll(/@container \(min-width: (\d+)px\)\s*\{\s*\.cf-lot-grid\s*\{\s*grid-template-columns:\s*repeat\((\d), minmax\(0, 1fr\)\)/gu)].map((match) => [match[1], match[2]]);
    expect(steps).toEqual([
      ['400', '2'],
      ['672', '3'],
      ['1056', '4'],
    ]);
    // One column: content starts at the top, so the value sits directly under the status label.
    expect(css).toMatch(/\.cf-lot-tile__body[^{]*\{[^}]*align-content:\s*start/u);
    expect(css).toMatch(/@container \(min-width: 400px\)\s*\{[\s\S]*?align-content:\s*space-between/u);
  });
});
