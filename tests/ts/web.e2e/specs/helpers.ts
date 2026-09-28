import { expect, type Page } from '@playwright/test';
import type { FakeSite, FakeSitePost, Mode } from '../fixtures/fake-idp.ts';
import { idpOrigin } from '../fixtures/ports.ts';

/** Switches the fake IdP's behaviour. */
export async function setMode(mode: Mode): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/mode`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ mode }),
  });
  expect(response.ok).toBe(true);
}

/** Resets the fake Server's Sites: the default seed (one Site), or exactly `sites`. */
export async function resetSites(sites?: readonly FakeSite[]): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/sites`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(sites === undefined ? {} : { sites }),
  });
  expect(response.ok).toBe(true);
}

/** The fake Server's Sites and every `POST /sites` it received since the last reset. */
export async function serverSites(): Promise<{ sites: FakeSite[]; posts: FakeSitePost[] }> {
  const response = await fetch(`${idpOrigin}/control/sites`);
  return (await response.json()) as { sites: FakeSite[]; posts: FakeSitePost[] };
}

/** Every token string the fake IdP has issued so far. */
export async function issuedTokens(): Promise<string[]> {
  const response = await fetch(`${idpOrigin}/control/tokens`);
  const body = (await response.json()) as { tokens: string[] };
  return body.tokens;
}

/** The SIGN IN button (uppercase is CSS only, so its accessible name is "Sign in"). */
export function signInButton(page: Page) {
  return page.getByRole('button', { name: 'Sign in' });
}

/** Opens the app signed out and signs in through the fake IdP; ends on Garden. */
export async function signIn(page: Page, path = '/'): Promise<void> {
  await page.goto(path);
  await expect(page).toHaveURL(/\/signin/u);
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

/** The Sign-in card's inline notice. */
export function notice(page: Page) {
  return page.locator('.cf-signin-card .cf-inline-notice');
}

export const copy = {
  unreachable: "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.",
  certificate: "Your Server's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain.",
  keycloak: "Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed.",
  signedOut: "You're signed out. Sign in again to see live data.",
} as const;

/** Pages of this story, by path, that need a signed-in session. */
export const shellPages = ['/garden', '/alerts', '/devices', '/members', '/settings', '/settings/appearance', '/sites/new'] as const;

/** Sets the theme cookie for the app origin before a page loads. */
export async function useTheme(page: Page, theme: 'light' | 'dark', origin: string): Promise<void> {
  await page.context().addCookies([{ name: 'cf_theme', value: theme, url: origin }]);
}
