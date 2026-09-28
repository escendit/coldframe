import { expect, test } from '@playwright/test';
import { appUrl, unreachableUrl, untrustedUrl } from '../fixtures/ports.ts';
import { copy, issuedTokens, notice, setMode, signIn, signInButton } from './helpers.ts';

test.beforeEach(async () => {
  await setMode('normal');
});

test.describe('Sign in on the web', () => {
  test('UX-DR59 UX-DR60 signed out, the app opens on the Sign-in card with one SIGN IN and nothing else', async ({ page }) => {
    for (const path of ['/', '/garden']) {
      await page.goto(path);
      await expect(page).toHaveURL(/\/signin$/u);
    }
    const card = page.locator('.cf-signin-card');
    await expect(card.getByRole('heading', { level: 1, name: 'Coldframe' })).toBeVisible();
    await expect(page.getByRole('button')).toHaveCount(1);
    await expect(signInButton(page)).toBeVisible();
    await expect(page.getByRole('link')).toHaveCount(0);
    await expect(page.getByRole('textbox')).toHaveCount(0);
    await expect(notice(page)).toHaveCount(0);
    await expect(signInButton(page)).toHaveCSS('text-transform', 'uppercase');

    // The gradient is on the surface only and no text sits directly on it.
    const surface = page.locator('.cf-signin-surface');
    await expect(surface).toHaveCSS('background-image', /radial-gradient/u);
    const textOutsideCard = await surface.evaluate((element) => {
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      const card = element.querySelector('.cf-signin-card');
      const stray: string[] = [];
      for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
        if (node.textContent?.trim() !== '' && card?.contains(node) !== true) {
          stray.push(node.textContent ?? '');
        }
      }
      return stray;
    });
    expect(textOutsideCard).toEqual([]);
  });

  test('UX-DR58 UX-DR60 AD-14 signing in lands on Garden in the shell; the browser holds only httpOnly cookies and no token', async ({ page, context }) => {
    const bodies: string[] = [];
    page.on('response', (response) => {
      if (response.url().startsWith(appUrl) && response.status() < 300) {
        bodies.push(response.url());
      }
    });
    const texts: Promise<string>[] = [];
    page.on('response', (response) => {
      if (response.url().startsWith(appUrl) && response.status() < 300) {
        texts.push(response.text().catch(() => ''));
      }
    });

    await signIn(page);
    const nav = page.getByRole('navigation', { name: 'Main' });
    for (const label of ['Garden', 'Alerts', 'Devices', 'Members', 'Settings']) {
      await expect(nav.getByRole('link', { name: label, exact: true })).toBeVisible();
    }
    await expect(nav.getByRole('link', { name: 'Garden', exact: true })).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('heading', { level: 1, name: 'Garden' })).toBeVisible();
    await expect(page.getByRole('img', { name: 'Signed in as Simon Novak' })).toHaveText('SN');

    // A client-side navigation fetches __data.json; it must carry no token either.
    await nav.getByRole('link', { name: 'Alerts', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Alerts' })).toBeVisible();
    await page.reload();

    const cookies = await context.cookies(appUrl);
    expect(cookies.length).toBeGreaterThan(0);
    for (const cookie of cookies) {
      if (cookie.name !== 'cf_theme') {
        expect(cookie.httpOnly, cookie.name).toBe(true);
      }
    }

    const tokens = await issuedTokens();
    expect(tokens.length).toBeGreaterThanOrEqual(3);
    const browserState = await page.evaluate(() => ({
      cookie: document.cookie,
      local: JSON.stringify(Object.entries(localStorage)),
      session: JSON.stringify(Object.entries(sessionStorage)),
      html: document.documentElement.outerHTML,
    }));
    const responses = await Promise.all(texts);
    expect(bodies.some((url) => url.includes('__data.json'))).toBe(true);
    for (const token of tokens) {
      // Any recognisable slice of a token counts as a leak.
      const signature = token.split('.')[2] ?? token;
      const places: [string, string][] = [...Object.entries(browserState), ...responses.map((text, index): [string, string] => [`response ${String(index)}`, text])];
      for (const [where, text] of places) {
        expect(text.includes(token) || text.includes(signature), `token found in ${where}`).toBe(false);
      }
      for (const cookie of cookies) {
        expect(cookie.value.includes(signature), `token in cookie ${cookie.name}`).toBe(false);
      }
    }
  });

  test('UX-DR92 UX-DR104 Server unreachable: the unreachable notice with Try again, announced, no hand-off to Keycloak', async ({ page }) => {
    await page.goto(`${unreachableUrl}/signin`);
    await signInButton(page).click();
    await expect(page).toHaveURL(`${unreachableUrl}/signin?notice=unreachable`);
    await expect(notice(page)).toContainText(copy.unreachable);
    await expect(notice(page).getByRole('link', { name: 'Try again' })).toBeVisible();
    await expect(page.getByRole('alert')).toHaveText(copy.unreachable);
    await expect(page.getByRole('status')).toHaveText('');
  });

  test('UX-DR92 UX-DR60 a failed probe keeps the return path, and Try again carries it', async ({ page }) => {
    await page.goto(`${unreachableUrl}/devices`);
    await expect(page).toHaveURL(`${unreachableUrl}/signin?returnTo=%2Fdevices`);
    await signInButton(page).click();
    await expect(page).toHaveURL(`${unreachableUrl}/signin?notice=unreachable&returnTo=%2Fdevices`);
    await expect(notice(page).getByRole('link', { name: 'Try again' })).toHaveAttribute('href', '/signin/start?returnTo=%2Fdevices');
  });

  test('UX-DR92 untrusted certificate: the certificate notice, with no action and no way to continue', async ({ page }) => {
    await page.goto(`${untrustedUrl}/signin`);
    await signInButton(page).click();
    await expect(page).toHaveURL(`${untrustedUrl}/signin?notice=certificate`);
    await expect(notice(page)).toContainText(copy.certificate);
    await expect(notice(page).getByRole('link')).toHaveCount(0);
    await expect(notice(page).getByRole('button')).toHaveCount(0);
    await expect(page.getByText(/continue anyway/iu)).toHaveCount(0);
  });

  test('UX-DR92 Keycloak returns an error: the Keycloak notice with Try again', async ({ page }) => {
    await setMode('error');
    await page.goto('/signin');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/signin\?notice=keycloak$/u);
    await expect(notice(page)).toContainText(copy.keycloak);
    await expect(notice(page).getByRole('link', { name: 'Try again' })).toBeVisible();
  });

  test('UX-DR92 the code exchange fails: the Keycloak notice', async ({ page }) => {
    await setMode('token-error');
    await page.goto('/signin');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/signin\?notice=keycloak$/u);
    await expect(notice(page)).toContainText(copy.keycloak);
  });

  test('UX-DR92 the issuer is down: the Keycloak notice, and Try again works once it is back', async ({ page }) => {
    await setMode('discovery-error');
    await page.goto('/signin');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/signin\?notice=keycloak$/u);
    await expect(notice(page)).toContainText(copy.keycloak);

    await setMode('normal');
    await notice(page).getByRole('link', { name: 'Try again' }).click();
    await expect(page).toHaveURL(/\/garden$/u);
  });

  test('UX-DR92 cancelled at Keycloak: back on Sign in with no notice', async ({ page }) => {
    await setMode('access_denied');
    await page.goto('/signin');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/signin$/u);
    await expect(notice(page)).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveText('');
  });

  test('UX-DR93 the refresh is rejected after the access token expires: the signed-out notice with Sign in', async ({ page }) => {
    await setMode('short-lived');
    await signIn(page);
    await page.waitForTimeout(1_500);
    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Alerts', exact: true }).click();
    await expect(page).toHaveURL(/\/signin\?notice=signed-out/u);
    await expect(notice(page)).toContainText(copy.signedOut);
    await expect(notice(page).getByRole('link', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('status')).toHaveText(copy.signedOut);

    // The notice is shown once: the marker is cleared.
    await page.goto('/signin');
    await expect(notice(page)).toHaveCount(0);

    await setMode('normal');
  });

  test('UX-DR93 UX-DR71 a deliberate sign-out from Settings shows no notice', async ({ page }) => {
    await signIn(page);
    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Settings', exact: true }).click();
    await page.getByRole('button', { name: 'Sign out' }).click();
    const dialog = page.getByRole('dialog', { name: 'Sign out of Coldframe on this browser?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('link', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/\/signin$/u);
    await expect(notice(page)).toHaveCount(0);

    await page.goto('/garden');
    await expect(page).toHaveURL(/\/signin$/u);
    await expect(notice(page)).toHaveCount(0);
  });

  test('UX-DR60 an unsafe return path falls back to Garden', async ({ page }) => {
    await page.goto('/signin/start?returnTo=https://evil.example/');
    await expect(page).toHaveURL(/\/garden$/u);
  });

  test('UX-DR60 the return path survives sign-in', async ({ page }) => {
    await page.goto('/devices');
    await expect(page).toHaveURL(/\/signin\?returnTo=%2Fdevices$/u);
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/devices$/u);
  });
});
