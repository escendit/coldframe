import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import type { FakeLot, FakeSite, FakeSitePost, Mode } from '../fixtures/fake-idp.ts';
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

/** Resets the fake Server's Sites (the default seed, one Site, or exactly `sites`) and its Lots. */
export async function resetSites(sites?: readonly FakeSite[], lots: readonly FakeLot[] = []): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/sites`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(sites === undefined ? { lots } : { sites, lots }),
  });
  expect(response.ok).toBe(true);
}

/** The fake Server's Sites and Lots, and every `POST /sites` and `POST …/lots` since the last reset. */
export async function serverSites(): Promise<{ sites: FakeSite[]; posts: FakeSitePost[]; lots: FakeLot[]; lotPosts: FakeSitePost[] }> {
  const response = await fetch(`${idpOrigin}/control/sites`);
  return (await response.json()) as { sites: FakeSite[]; posts: FakeSitePost[]; lots: FakeLot[]; lotPosts: FakeSitePost[] };
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
export const shellPages = ['/garden', '/alerts', '/devices', '/members', '/settings', '/settings/site', '/settings/appearance', '/sites/new'] as const;

/** Sets the theme cookie for the app origin before a page loads. */
export async function useTheme(page: Page, theme: 'light' | 'dark', origin: string): Promise<void> {
  await page.context().addCookies([{ name: 'cf_theme', value: theme, url: origin }]);
}

/** The largest text size on the web: 200 % zoom (UX-DR126), on a 1280 × 800 window. */
export async function largestText(page: Page): Promise<void> {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.addStyleTag({ content: 'html { zoom: 2 }' });
}

export async function axeClean(page: Page, label: string): Promise<void> {
  const results = await new AxeBuilder({ page }).analyze();
  const blocking = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`);
  expect(blocking, label).toEqual([]);
}

/** Nothing sticks out of the page sideways, and no text is clipped by its box. */
export async function nothingClipped(page: Page, label: string): Promise<void> {
  const problems = await page.evaluate(() => {
    const found: string[] = [];
    if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
      found.push('page scrolls sideways');
    }
    for (const element of document.querySelectorAll<HTMLElement>('main *, header *')) {
      const style = getComputedStyle(element);
      if (style.overflowX === 'hidden' || style.overflowY === 'hidden' || style.textOverflow === 'ellipsis') {
        if (element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1) {
          found.push(element.outerHTML.slice(0, 80));
        }
      }
    }
    return found;
  });
  expect(problems, label).toEqual([]);
}
