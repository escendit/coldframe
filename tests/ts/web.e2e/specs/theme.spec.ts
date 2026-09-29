import { expect, test } from '@playwright/test';
import { appUrl } from '../fixtures/ports.ts';
import { setMode, signIn } from './helpers.ts';

test.beforeEach(async ({ page }) => {
  await setMode('normal');
  await signIn(page);
});

test.describe('Appearance', () => {
  test('UX-DR71 UX-DR75 Settings lists Appearance and Account, and Appearance hosts the Theme switcher', async ({ page }) => {
    await page.goto('/settings');
    await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Account' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await page.getByRole('link', { name: /Appearance/u }).click();
    await expect(page).toHaveURL(/\/settings\/appearance$/u);
    await expect(page.getByRole('heading', { level: 1, name: 'Appearance' })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Theme' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Settings', exact: true })).toHaveAttribute('aria-current', 'page');
  });

  test('UX-DR15 UX-DR53 UX-DR75 choosing Dark or Light applies at once, with no Save, and survives a reload', async ({ page, context }) => {
    await page.goto('/settings/appearance');
    const html = page.locator('html');
    await expect(html).not.toHaveAttribute('data-theme');
    await expect(page.getByRole('button', { name: 'System' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('button', { name: /save/iu })).toHaveCount(0);

    await page.getByRole('button', { name: 'Dark' }).click();
    await expect(html).toHaveAttribute('data-theme', 'dark');
    await expect(page.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(38, 38, 38)');

    // Rendered on the server: the first byte of HTML already carries the theme, so no flash.
    const response = await page.request.get(`${appUrl}/settings/appearance`);
    expect(await response.text()).toMatch(/<html lang="en" data-theme="dark">/u);
    await page.reload();
    await expect(html).toHaveAttribute('data-theme', 'dark');
    await expect(page.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');

    await page.getByRole('button', { name: 'Light' }).click();
    await expect(html).toHaveAttribute('data-theme', 'light');
    await page.reload();
    await expect(html).toHaveAttribute('data-theme', 'light');

    await page.getByRole('button', { name: 'System' }).click();
    await expect(html).not.toHaveAttribute('data-theme');
    const themeCookies = (await context.cookies(appUrl)).filter((cookie) => cookie.name === 'cf_theme');
    expect(themeCookies).toEqual([]);
    await page.reload();
    await expect(html).not.toHaveAttribute('data-theme');
  });

  test('UX-DR15 System follows prefers-color-scheme', async ({ page }) => {
    await page.goto('/garden');
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(38, 38, 38)');
    await page.emulateMedia({ colorScheme: 'light' });
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(254, 254, 254)');
  });

  test('UX-DR15 an invalid theme cookie is treated as System', async ({ page, context }) => {
    await context.addCookies([{ name: 'cf_theme', value: 'purple', url: appUrl }]);
    await page.goto('/settings/appearance');
    await expect(page.locator('html')).not.toHaveAttribute('data-theme');
    await expect(page.getByRole('button', { name: 'System' })).toHaveAttribute('aria-pressed', 'true');
  });
});
