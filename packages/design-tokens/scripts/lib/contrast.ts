import { parseHex } from './color.ts';
import { THEMES, type ContrastKind, type ContrastPair, type Theme, type TokensFile } from './tokens.ts';

/** WCAG 2.2 AA: 1.4.3 text, 1.4.11 non-text. */
export const MINIMUM_RATIO: Record<ContrastKind, number> = { text: 4.5, 'non-text': 3 };

/** Published ratios have two decimals. */
export const PUBLISHED_TOLERANCE = 0.01 + 1e-9;

function channel(value: number): number {
  const srgb = value / 255;
  return srgb <= 0.04045 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
}

export function relativeLuminance(hex: string): number {
  const { r, g, b } = parseHex(hex);
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** Contrast ratio of two opaque colours. */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a);
  const lb = relativeLuminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

export interface ContrastResult {
  label: string;
  theme: Theme;
  kind: ContrastKind;
  foreground: string;
  background: string;
  ratio: number;
  minimum: number;
  published: number;
  /** At or above the minimum. */
  passes: boolean;
  /** Differs from the published ratio by more than 0.01. */
  drifted: boolean;
}

export function evaluateContrast(tokens: TokensFile, pairs: readonly ContrastPair[]): ContrastResult[] {
  return THEMES.flatMap((theme) =>
    pairs.map((pair) => {
      const foreground = tokens.colors[pair.foreground]?.[theme];
      const background = tokens.colors[pair.background]?.[theme];
      if (!foreground || !background) {
        throw new Error(`contrast pair "${pair.label}" names a colour that tokens.json does not hold`);
      }
      const ratio = contrastRatio(foreground, background);
      const minimum = MINIMUM_RATIO[pair.kind];
      const published = pair.published[theme];
      return {
        label: pair.label,
        theme,
        kind: pair.kind,
        foreground: pair.foreground,
        background: pair.background,
        ratio,
        minimum,
        published,
        passes: ratio >= minimum,
        drifted: Math.abs(ratio - published) > PUBLISHED_TOLERANCE,
      };
    }),
  );
}

/** A plain-text table of the recomputed ratios, one row per pair and theme. */
export function formatContrastTable(results: readonly ContrastResult[]): string {
  const rows = results.map((result) => [
    result.theme,
    result.label,
    result.kind,
    `${result.ratio.toFixed(2)}:1`,
    `${result.minimum.toFixed(1)}:1`,
    result.published.toFixed(2),
    result.passes ? (result.drifted ? 'pass, differs from published' : 'pass') : 'FAIL',
  ]);
  const header = ['Theme', 'Pair', 'Kind', 'Ratio', 'Minimum', 'Published', 'Result'];
  const widths = header.map((title, column) =>
    Math.max(title.length, ...rows.map((row) => (row[column] ?? '').length)),
  );
  const line = (cells: string[]) =>
    cells
      .map((cell, column) => cell.padEnd(widths[column] ?? 0))
      .join('  ')
      .trimEnd();
  return [line(header), line(widths.map((width) => '-'.repeat(width))), ...rows.map(line)].join('\n');
}
