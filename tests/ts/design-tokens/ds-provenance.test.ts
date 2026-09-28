import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { loadTokens, type Theme, type TokensFile } from '../../../packages/design-tokens/scripts/lib/tokens.ts';
import {
  dsNameFor,
  findDsDrift,
  loadVendoredTheme,
  parseThemeCss,
  resolveDs,
} from '../../../packages/design-tokens/scripts/lib/theme-css.ts';
import { packageRoot, readRepoFile, repoRoot } from './helpers.ts';

const tokens = loadTokens();
const theme = loadVendoredTheme();
const themes: Theme[] = ['light', 'dark'];

/** UX-DR2: the neutral and brand semantic set, every one taken from the Escendit DS. */
const uxDr2 = [
  'background',
  'layer-01',
  'layer-02',
  'field-01',
  'text-primary',
  'text-secondary',
  'text-helper',
  'text-disabled',
  'text-on-color',
  'border-subtle',
  'border-strong',
  'overlay',
  'primary',
  'primary-hover',
  'primary-active',
  'secondary',
  'button-secondary',
  'header-bg',
  'header-border',
  'support-error',
  'support-success',
  'support-warning',
  'support-info',
  'setup-done',
];

/** Values the DS does not hold; README.md › Deviations explains each. */
const localValues = new Set(['primary-hover:dark', 'setup-done:light']);

describe('the vendored Escendit theme', () => {
  it('is a copy of theme.css with its source commit and licence', () => {
    const source = readFileSync(join(packageRoot, 'vendor/escendit-branding/SOURCE.md'), 'utf8');
    expect(source).toContain('67c33f39c49e118be6785d11ebdea1f3bc5191f8');
    expect(source).toContain('Apache-2.0');
    expect(existsSync(join(packageRoot, 'vendor/escendit-branding/LICENSE'))).toBe(true);
    expect(readFileSync(join(packageRoot, 'vendor/escendit-branding/theme.css'), 'utf8')).toContain(
      '--esc-background',
    );
  });

  it('resolves var() chains and rgb() alpha to hex', () => {
    const parsed = parseThemeCss(`
      @theme { --color-white: #fefefe; --color-a: var(--color-white); }
      :root, [data-theme="light"] { --esc-x: var(--color-a); --esc-o: rgb(22 22 22 / .5); }
      .dark, [data-theme="dark"] { --esc-x: #262626; --esc-o: rgb(22 22 22 / .7); }
    `);
    expect(resolveDs(parsed, '--esc-x', 'light')).toBe('#FEFEFE');
    expect(resolveDs(parsed, '--esc-x', 'dark')).toBe('#262626');
    expect(resolveDs(parsed, '--esc-o', 'light')).toBe('#16161680');
    expect(resolveDs(parsed, '--esc-o', 'dark')).toBe('#161616B3');
    expect(resolveDs(parsed, '--color-white', 'dark')).toBe('#FEFEFE');
    expect(resolveDs(parsed, '--missing', 'light')).toBeUndefined();
  });
});

describe('tokens with a DS mapping match the vendored theme', () => {
  const mapped = Object.entries(tokens.colors).flatMap(([name, token]) =>
    themes
      .map((t) => ({ name, theme: t, ds: dsNameFor(token, t), value: token[t] }))
      .filter((entry) => entry.ds !== undefined),
  );

  it.each(mapped)('$name ($theme) equals $ds', ({ name, theme: t, ds, value }) => {
    const resolved = resolveDs(theme, ds ?? '', t);
    expect(resolved, `token ${name} (${t}): ${String(ds)} does not resolve in theme.css`).toBeDefined();
    expect(resolved?.toUpperCase(), `token ${name} (${t}) differs from ${String(ds)}`).toBe(
      value.toUpperCase(),
    );
  });

  it('has no drift at all', () => {
    expect(findDsDrift(tokens, theme)).toEqual([]);
  });

  it('reports drift naming the token and theme', () => {
    const drifted: TokensFile = structuredClone(tokens);
    const background = drifted.colors.background;
    if (!background) throw new Error('background missing');
    background.dark = '#161616';
    expect(findDsDrift(drifted, theme)).toEqual([
      'background (dark): tokens.json #161616, --esc-background #262626',
    ]);
  });

  it.each(uxDr2.flatMap((name) => themes.map((t) => ({ name, theme: t }))))(
    'UX-DR2 token $name ($theme) comes from the DS',
    ({ name, theme: t }) => {
      const token = tokens.colors[name];
      expect(token, `UX-DR2 token ${name} is missing`).toBeDefined();
      if (!token || localValues.has(`${name}:${t}`)) return;
      expect(dsNameFor(token, t), `UX-DR2 token ${name} (${t}) has no DS mapping`).toBeDefined();
    },
  );

  it('keeps focus off orange and ink-on-bright off the DS white', () => {
    expect(tokens.colors.focus?.ds).toBeUndefined();
    for (const t of themes) {
      expect(tokens.colors.focus?.[t]).not.toBe(tokens.colors.primary?.[t]);
      expect(resolveDs(theme, '--color-focus', t)).toBe(tokens.colors.primary?.[t]);
    }
    expect(tokens.colors['ink-on-bright']?.light).not.toBe(tokens.colors['text-on-color']?.light);
    const readme = readRepoFile('packages/design-tokens/README.md');
    expect(readme).toContain('--color-focus');
    expect(readme).toContain('text-on-color');
    expect(readme).toContain('escendit/branding');
  });
});

describe('no dependency on @escendit/branding', () => {
  function packageJsons(dir: string): string[] {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
      if (entry.name === 'node_modules' || entry.name.startsWith('.')) return [];
      const path = join(dir, entry.name);
      if (entry.isDirectory()) return packageJsons(path);
      return entry.name === 'package.json' ? [path] : [];
    });
  }

  const manifests = ['package.json', 'apps', 'packages', 'tests'].flatMap((entry) =>
    entry === 'package.json' ? [join(repoRoot, entry)] : packageJsons(join(repoRoot, entry)),
  );

  it.each(manifests)('%s does not depend on it', (manifest) => {
    expect(readFileSync(manifest, 'utf8')).not.toContain('@escendit/branding');
  });
});
