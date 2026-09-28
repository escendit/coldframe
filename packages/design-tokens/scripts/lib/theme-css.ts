import { readFileSync } from 'node:fs';

import { formatHex } from './color.ts';
import { fromRoot, inputPaths, repoRoot } from './paths.ts';
import { THEMES, type ColorToken, type Theme, type TokensFile } from './tokens.ts';

/** Custom properties of the Escendit theme, by where they are declared. */
export interface DsTheme {
  /** `@theme` blocks: the palette. */
  palette: Map<string, string>;
  /** `:root, [data-theme="light"]`: semantic light values and the default. */
  light: Map<string, string>;
  /** `.dark, [data-theme="dark"]`: semantic dark overrides. */
  dark: Map<string, string>;
}

function stripComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Top-level `selector { body }` blocks; nested blocks stay inside their parent's body. */
function topLevelBlocks(css: string): { selector: string; body: string }[] {
  const blocks: { selector: string; body: string }[] = [];
  let depth = 0;
  let start = 0;
  let selector = '';
  for (let index = 0; index < css.length; index++) {
    const char = css[index];
    if (char === '{') {
      if (depth === 0) {
        selector = css.slice(start, index).trim();
        start = index + 1;
      }
      depth++;
    } else if (char === '}') {
      depth--;
      if (depth === 0) {
        blocks.push({ selector, body: css.slice(start, index) });
        start = index + 1;
      }
    } else if (char === ';' && depth === 0) {
      start = index + 1;
    }
  }
  return blocks;
}

function declarations(body: string): Map<string, string> {
  const result = new Map<string, string>();
  for (const match of body.matchAll(/(--[\w-]+)\s*:\s*([^;{}]+);/g)) {
    const [, name, value] = match;
    if (name && value) {
      result.set(name, value.trim());
    }
  }
  return result;
}

export function parseThemeCss(css: string): DsTheme {
  const theme: DsTheme = { palette: new Map(), light: new Map(), dark: new Map() };
  for (const { selector, body } of topLevelBlocks(stripComments(css))) {
    const normalised = selector.replace(/\s+/g, ' ');
    let target: Map<string, string> | undefined;
    if (normalised === '@theme' || normalised === '@theme inline') {
      target = theme.palette;
    } else if (normalised === ':root, [data-theme="light"]') {
      target = theme.light;
    } else if (normalised === '.dark, [data-theme="dark"]') {
      target = theme.dark;
    }
    if (target) {
      for (const [name, value] of declarations(body)) {
        target.set(name, value);
      }
    }
  }
  return theme;
}

export function loadVendoredTheme(root = repoRoot): DsTheme {
  return parseThemeCss(readFileSync(fromRoot(inputPaths.theme, root), 'utf8'));
}

function lookup(theme: DsTheme, name: string, mode: Theme): string | undefined {
  if (mode === 'dark' && theme.dark.has(name)) {
    return theme.dark.get(name);
  }
  return theme.light.get(name) ?? theme.palette.get(name);
}

function normaliseColor(value: string): string | undefined {
  const hex = /^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(value)?.[1];
  if (hex) {
    const full = hex.length === 3 ? hex.replace(/./g, (digit) => digit + digit) : hex;
    return `#${full.toUpperCase()}`;
  }
  const rgb = /^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)\s*(?:[/,]\s*([\d.]+)(%)?)?\s*\)$/i.exec(value);
  if (rgb) {
    const alpha = rgb[4] === undefined ? 1 : Number(rgb[4]) / (rgb[5] ? 100 : 1);
    return formatHex({
      r: Number(rgb[1]),
      g: Number(rgb[2]),
      b: Number(rgb[3]),
      a: Math.round(alpha * 255),
    });
  }
  return undefined;
}

/**
 * The colour a DS custom property holds in a theme, as upper-case `#RRGGBB` or `#RRGGBBAA`,
 * following `var()` references. Undefined when the property is missing or is not a colour.
 */
export function resolveDs(theme: DsTheme, name: string, mode: Theme): string | undefined {
  let value = lookup(theme, name, mode);
  for (let depth = 0; value !== undefined && depth < 16; depth++) {
    const reference = /^var\(\s*(--[\w-]+)\s*\)$/.exec(value)?.[1];
    if (!reference) {
      return normaliseColor(value);
    }
    value = lookup(theme, reference, mode);
  }
  return undefined;
}

/** The DS property a token is taken from in a theme, if any. */
export function dsNameFor(token: ColorToken, mode: Theme): string | undefined {
  if (token.ds === undefined) {
    return undefined;
  }
  return typeof token.ds === 'string' ? token.ds : token.ds[mode];
}

/** One line per token and theme whose value differs from its DS property, naming both. */
export function findDsDrift(tokens: TokensFile, theme: DsTheme): string[] {
  const drift: string[] = [];
  for (const [name, token] of Object.entries(tokens.colors)) {
    for (const mode of THEMES) {
      const ds = dsNameFor(token, mode);
      if (ds === undefined) continue;
      const resolved = resolveDs(theme, ds, mode);
      if (resolved?.toUpperCase() !== token[mode].toUpperCase()) {
        drift.push(`${name} (${mode}): tokens.json ${token[mode]}, ${ds} ${resolved ?? 'is missing'}`);
      }
    }
  }
  return drift;
}
