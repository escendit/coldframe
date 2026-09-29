import { describe, expect, test } from 'vitest';
import { filesUnder, read, rel, webSrc } from './helpers.ts';

interface Sheet {
  readonly file: string;
  readonly css: string;
}

/** The CSS of every `<style>` block and stylesheet of the web app, comments removed. */
function sheets(): Sheet[] {
  return filesUnder(webSrc, ['.svelte', '.css']).map((file) => {
    const source = read(file);
    const css = file.endsWith('.css') ? source : [...source.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/gu)].map((match) => match[1] ?? '').join('\n');
    return { file: rel(file), css: css.replace(/\/\*[\s\S]*?\*\//gu, '') };
  });
}

/** `selector { declarations }` pairs; nested at-rules are flattened well enough for these checks. */
function rules(css: string): { selector: string; body: string }[] {
  return [...css.matchAll(/([^{}]+)\{([^{}]*)\}/gu)].map((match) => ({ selector: (match[1] ?? '').trim(), body: match[2] ?? '' }));
}

const all = sheets();

describe('visual rules', () => {
  test('UX-DR101 UX-DR114 no transitions, animations or keyframes anywhere', () => {
    for (const { file, css } of all) {
      expect(css, file).not.toMatch(/@keyframes/u);
      for (const match of css.matchAll(/(?:^|[;{\s])(transition|animation)(?:-[a-z-]+)?\s*:\s*([^;}]+)/gu)) {
        expect(match[2]?.trim(), `${file}: ${match[0]}`).toBe('none');
      }
    }
  });

  test('UX-DR114 no toasts, spinners, carousels, snooze or streaks in the web app', () => {
    for (const file of filesUnder(webSrc, ['.svelte', '.ts', '.css', '.json'])) {
      expect(read(file), rel(file)).not.toMatch(/toast|spinner|carousel|snooze|streak|mark watered/iu);
    }
  });

  test('UX-DR113 hover never reveals anything: no hover-only affordances', () => {
    for (const { file, css } of all) {
      for (const { selector, body } of rules(css)) {
        if (!selector.includes(':hover')) {
          continue;
        }
        expect(body, `${file}: ${selector}`).not.toMatch(/\b(display|visibility|opacity|content|clip|width|height)\s*:/u);
      }
    }
  });

  test('UX-DR16 the focus ring is two-tone from the focus tokens, never orange', () => {
    const focusRules = all.flatMap(({ file, css }) => rules(css).filter(({ selector }) => selector.includes(':focus')).map((rule) => ({ file, ...rule })));
    expect(focusRules.length).toBeGreaterThan(0);
    for (const { file, selector, body } of focusRules) {
      expect(body, `${file}: ${selector}`).not.toMatch(/primary/u);
    }
    const global = focusRules.map(({ body }) => body).join('\n');
    expect(global).toContain('--cf-color-focus)');
    expect(global).toContain('--cf-color-focus-gap)');
    expect(global).toContain('--cf-spacing-focus-ring');
  });

  test('square corners, 1 px borders, no shadows outside the focus ring, colours only from tokens', () => {
    for (const { file, css } of all) {
      for (const { selector, body } of rules(css)) {
        expect(body, `${file}: ${selector}`).not.toMatch(/border-radius\s*:(?!\s*(?:0|var\(--cf-radius))/u);
        if (!selector.includes(':focus')) {
          expect(body, `${file}: ${selector}`).not.toMatch(/box-shadow\s*:(?!\s*none)/u);
        }
        expect(body, `${file}: ${selector}`).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(|hsla?\(/iu);
      }
    }
  });

  test('UX-DR59 the signature radial gradient appears only on the Sign-in surface', () => {
    for (const { file, css } of all) {
      if (file.endsWith('SignInCard.svelte')) {
        expect(css).toMatch(/radial-gradient\([^)]*--cf-color-primary[^)]*\)[^;]*--cf-color-secondary|radial-gradient\(.*--cf-color-primary.*--cf-color-secondary/su);
      } else {
        expect(css, file).not.toMatch(/gradient\(/u);
      }
    }
  });

  test('UX-DR126 buttons and segments grow with their text and never clip', () => {
    for (const { file, css } of all.filter(({ file: name }) => /Button|SegmentedChoice|SideNav|AppHeader/u.test(name))) {
      expect(css, file).not.toMatch(/text-overflow\s*:\s*ellipsis|white-space\s*:\s*nowrap|overflow\s*:\s*hidden/u);
      expect(css, file).not.toMatch(/(?:^|[;{\s])(?:height|max-height)\s*:/u);
    }
  });

  test('UX-DR100 interactive elements reserve at least 44 px', () => {
    const tokens = all.map(({ css }) => css).join('\n');
    expect(tokens).toMatch(/min-height\s*:\s*var\(--cf-spacing-button-height\)/u);
    expect(tokens).toMatch(/min-(?:height|width)\s*:\s*44px/u);
  });
});
