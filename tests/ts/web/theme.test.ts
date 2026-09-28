import { describe, expect, test } from 'vitest';
import { dataThemeOf, parseTheme, serializeThemeCookie, themeAttribute, themeCookieName } from '$lib/theme';
import { themeHandle, themePlaceholder } from '$lib/server/theme';
import { fakeEvent } from './fakes.ts';

describe('theme cookie', () => {
  test('UX-DR15 parses light and dark, anything else is System', () => {
    expect(parseTheme('light')).toBe('light');
    expect(parseTheme('dark')).toBe('dark');
    for (const value of [undefined, null, '', 'system', 'DARK', 'purple', 'dark; x=1']) {
      expect(parseTheme(value)).toBe('system');
    }
  });

  test('UX-DR15 System renders no data-theme attribute', () => {
    expect(dataThemeOf('system')).toBeNull();
    expect(themeAttribute('system')).toBe('');
    expect(themeAttribute('dark')).toBe('data-theme="dark"');
    expect(themeAttribute('light')).toBe('data-theme="light"');
  });

  test('UX-DR15 serialises a persistent first-party cookie and removes it for System', () => {
    const dark = serializeThemeCookie('dark', true);
    expect(dark).toContain(`${themeCookieName}=dark`);
    expect(dark).toContain('Path=/');
    expect(dark).toContain('SameSite=Lax');
    expect(dark).toContain('Secure');
    expect(dark).toMatch(/Max-Age=\d{6,}/u);
    expect(serializeThemeCookie('light', false)).not.toContain('Secure');
    expect(serializeThemeCookie('system', false)).toContain('Max-Age=0');
  });

  test('UX-DR15 the handle renders the stored theme into <html> on the server', async () => {
    const html = `<html lang="en" ${themePlaceholder}>`;
    for (const [cookie, expected] of [
      ['dark', '<html lang="en" data-theme="dark">'],
      ['light', '<html lang="en" data-theme="light">'],
      ['bogus', '<html lang="en" >'],
    ] as const) {
      const event = fakeEvent('/garden', { [themeCookieName]: cookie });
      let transformed = '';
      await themeHandle({
        event,
        resolve: async (_event, options) => {
          transformed = (await options?.transformPageChunk?.({ html, done: true })) ?? '';
          return new Response(transformed);
        },
      });
      expect(transformed).toBe(expected);
    }
  });
});
