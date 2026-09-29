/** Theme choice of this browser. `system` follows `prefers-color-scheme`. */
export type Theme = 'system' | 'light' | 'dark';

export const themes: readonly Theme[] = ['system', 'light', 'dark'];

/** First-party cookie holding the choice on this browser; absent means System. */
export const themeCookieName = 'cf_theme';

const oneYearSeconds = 60 * 60 * 24 * 365;

/** Parses the cookie value; anything unknown is System. */
export function parseTheme(value: string | null | undefined): Theme {
  return value === 'light' || value === 'dark' ? value : 'system';
}

/** The `data-theme` value for `<html>`, or null for System (no attribute). */
export function dataThemeOf(theme: Theme): 'light' | 'dark' | null {
  return theme === 'system' ? null : theme;
}

/** The attribute text rendered into `<html>` on the server; empty for System. */
export function themeAttribute(theme: Theme): string {
  const value = dataThemeOf(theme);
  return value === null ? '' : `data-theme="${value}"`;
}

/** A `document.cookie` assignment that stores the theme, or removes the cookie for System. */
export function serializeThemeCookie(theme: Theme, secure: boolean): string {
  const attributes = ['Path=/', 'SameSite=Lax'];
  if (secure) {
    attributes.push('Secure');
  }
  if (theme === 'system') {
    return [`${themeCookieName}=`, 'Max-Age=0', ...attributes].join('; ');
  }
  return [`${themeCookieName}=${theme}`, `Max-Age=${String(oneYearSeconds)}`, ...attributes].join('; ');
}

/** Applies the theme to a document at once, with no reload. */
export function applyTheme(root: HTMLElement, theme: Theme): void {
  const value = dataThemeOf(theme);
  if (value === null) {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', value);
  }
}
