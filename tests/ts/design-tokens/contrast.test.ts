import { readFileSync } from 'node:fs';

import { describe, expect, it } from 'vitest';

import {
  contrastRatio,
  evaluateContrast,
  MINIMUM_RATIO,
  PUBLISHED_TOLERANCE,
} from '../../../packages/design-tokens/scripts/lib/contrast.ts';
import { loadContrastPairs, loadTokens, type TokensFile } from '../../../packages/design-tokens/scripts/lib/tokens.ts';
import { designMdPath } from './helpers.ts';

const tokens = loadTokens();
const pairs = loadContrastPairs();

describe('contrast', () => {
  it('computes WCAG 2.2 ratios', () => {
    expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
    expect(contrastRatio('#FFFFFF', '#FFFFFF')).toBeCloseTo(1, 5);
    expect(contrastRatio('#0A0A0A', '#FF770F')).toBeCloseTo(7.4456, 3);
  });

  it('publishes the same ratios as the DESIGN.md contrast table', () => {
    const text = readFileSync(designMdPath, 'utf8');
    const start = text.indexOf('Load-bearing contrast');
    const end = text.indexOf('\n## ', start);
    expect(start).toBeGreaterThan(-1);
    const rows = text
      .slice(start, end)
      .split('\n')
      .filter((line) => line.startsWith('|'))
      .slice(2)
      .map((line) => line.split('|').map((cell) => cell.trim()));
    const distinct = (values: number[]) => [...new Set(values)].sort((a, b) => a - b);
    const numbers = (cell: string | undefined) =>
      (cell ?? '').split(/[/;]/).map((part) => Number(part.trim()));
    // A row is | pair | light | dark |, so the cells are ['', pair, light, dark, ''].
    const designLight = distinct(rows.flatMap((cells) => numbers(cells[2])));
    const designDark = distinct(rows.flatMap((cells) => numbers(cells[3])));
    expect(designLight.every(Number.isFinite) && designDark.every(Number.isFinite)).toBe(true);
    expect(distinct(pairs.map((pair) => pair.published.light))).toEqual(designLight);
    expect(distinct(pairs.map((pair) => pair.published.dark))).toEqual(designDark);
  });

  it('holds the load-bearing table of DESIGN.md › Colors', () => {
    expect(pairs).toHaveLength(19);
    expect(pairs.filter((pair) => pair.kind === 'text')).toHaveLength(12);
    for (const pair of pairs) {
      expect(tokens.colors[pair.foreground], pair.foreground).toBeDefined();
      expect(tokens.colors[pair.background], pair.background).toBeDefined();
    }
  });

  const results = evaluateContrast(tokens, pairs);

  it.each(results)('$label ($theme) meets its minimum', (result) => {
    expect(result.ratio, `${result.label} (${result.theme}) is ${result.ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(
      MINIMUM_RATIO[result.kind],
    );
    expect(result.passes).toBe(true);
  });

  it.each(results)('$label ($theme) matches the published value', (result) => {
    expect(
      Math.abs(result.ratio - result.published),
      `${result.label} (${result.theme}): computed ${result.ratio.toFixed(2)}, published ${String(result.published)}`,
    ).toBeLessThanOrEqual(PUBLISHED_TOLERANCE);
    expect(result.drifted).toBe(false);
  });

  it('fails a text pair below 4.5:1, naming pair, theme and ratio', () => {
    const lowered: TokensFile = structuredClone(tokens);
    const primaryText = lowered.colors['primary-text'];
    if (!primaryText) throw new Error('primary-text missing');
    primaryText.light = '#D06020';
    const failing = evaluateContrast(lowered, pairs).filter((result) => !result.passes);
    expect(failing.map((result) => `${result.label} (${result.theme})`)).toEqual([
      'primary-text on background (light)',
      'primary-text on layer-01 (light)',
    ]);
    expect(failing.every((result) => result.ratio < 4.5 && result.drifted)).toBe(true);
  });

  it('fails a non-text pair below 3:1', () => {
    const lowered: TokensFile = structuredClone(tokens);
    const line = lowered.colors['status-hatch-line'];
    if (!line) throw new Error('status-hatch-line missing');
    line.dark = '#5A5A5A';
    const failing = evaluateContrast(lowered, pairs).filter((result) => !result.passes);
    expect(failing.map((result) => `${result.label} (${result.theme})`)).toEqual([
      'Hatch line on hatch ground (dark)',
    ]);
  });
});
