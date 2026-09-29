import { expect, test, type Page } from '@playwright/test';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, serverSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

const homeId = '0192a000-0000-7000-8000-00000000000a';
const home = { id: homeId, name: 'Home', role: 'Owner' } as const;

test.use({ timezoneId: 'Europe/Zurich' });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Lots.
  await resetSites();
});

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

/** The row of one Lot in Site settings, found by its name field. */
function lotRow(page: Page, lot: string) {
  return page.locator('li', { has: page.getByRole('textbox', { name: `Name of ${lot}`, exact: true }) });
}

test.describe('Site settings and Lots', () => {
  for (const theme of themes) {
    test(`UX-DR74 UX-DR18 UX-DR20 an Owner renames the Site, creates Tomatoes and Beans, and Garden shows them as no-Node tiles (${theme}, largest text)`, async ({ page }) => {
      await resetSites([home]);
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });
      await signInTo(page);

      await page.goto('/settings');
      await page.getByRole('link', { name: /Site settings/u }).click();
      await expect(page).toHaveURL(/\/settings\/site$/u);
      await largestText(page);

      const siteName = page.getByRole('textbox', { name: 'Site name' });
      await siteName.fill('Home garden');
      await page.getByRole('button', { name: 'Rename Site' }).click();
      await expect(page.getByRole('tab', { name: 'Home garden · Owner' })).toBeVisible();
      await expect(siteName).toHaveValue('Home garden');

      const lotName = page.getByRole('textbox', { name: 'Lot name', exact: true });
      await lotName.fill('Tomatoes');
      await page.getByRole('button', { name: 'Create Lot' }).click();
      await expect(page.getByRole('textbox', { name: 'Name of Tomatoes' })).toBeVisible();
      await expect(lotName).toHaveValue('');
      await lotName.fill('Beans');
      await page.getByRole('button', { name: 'Create Lot' }).click();
      await expect(page.getByRole('textbox', { name: 'Name of Beans' })).toBeVisible();

      const { sites, lots, lotPosts } = await serverSites();
      expect(sites.map((site) => site.name)).toEqual(['Home garden']);
      expect(lots.map((lot) => lot.name)).toEqual(['Tomatoes', 'Beans']);
      // One key per attempt: two creations, two keys.
      expect(lotPosts.map((post) => post.body)).toEqual([{ name: 'Tomatoes' }, { name: 'Beans' }]);
      expect(new Set(lotPosts.map((post) => post.idempotencyKey)).size).toBe(2);

      await axeClean(page, `site settings (${theme})`);
      await nothingClipped(page, `site settings (${theme})`);
      // Submitting leaves the page scrolled; the full-page capture starts at the top.
      await page.evaluate(() => {
        window.scrollTo(0, 0);
      });
      await expect(page).toHaveScreenshot(`site-settings-${theme}.png`, { fullPage: true });

      await page.goto('/garden');
      await largestText(page);
      await expect(page.locator('.cf-site-summary').getByText('Home garden', { exact: true })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'No Readings yet' })).toBeVisible();
      const tiles = page.getByRole('list', { name: 'Lots' }).getByRole('img');
      await expect(tiles).toHaveCount(2);
      await expect(tiles.nth(0)).toHaveAccessibleName('Tomatoes, no Node, add a Node');
      await expect(tiles.nth(1)).toHaveAccessibleName('Beans, no Node, add a Node');
      await expect(tiles.nth(0)).toHaveCSS('border-top-style', 'dotted');
      await expect(tiles.nth(0)).toContainText('+');
      await expect(tiles.nth(0)).toContainText('add a Node');
      await axeClean(page, `garden with Lots (${theme})`);
      await nothingClipped(page, `garden with Lots (${theme})`);
      await expect(page).toHaveScreenshot(`garden-lots-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR74 rename a Lot, remove one after confirming, and a Lot holding a Node refuses removal', async ({ page }) => {
    await resetSites(
      [home],
      [
        { id: '0192a000-0000-7000-8000-000000000011', siteId: homeId, name: 'Tomatoes', claimed: true },
        { id: '0192a000-0000-7000-8000-000000000012', siteId: homeId, name: 'Beans' },
      ],
    );
    await signInTo(page);
    await page.goto('/settings/site');

    const beans = lotRow(page, 'Beans');
    await beans.getByRole('textbox').fill('Runner beans');
    await beans.getByRole('button', { name: 'Rename Lot' }).click();
    await expect(page.getByRole('textbox', { name: 'Name of Runner beans' })).toHaveValue('Runner beans');

    await lotRow(page, 'Runner beans').getByRole('button', { name: 'Remove Lot' }).click();
    const dialog = page.getByRole('dialog', { name: 'Remove Lot Runner beans?' });
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText('Its history stays in Coldframe.');
    await dialog.getByRole('button', { name: 'Remove Lot' }).click();
    await expect(page.getByRole('textbox', { name: 'Name of Runner beans' })).toHaveCount(0);
    expect((await serverSites()).lots.find((lot) => lot.name === 'Runner beans')?.removed).toBe(true);

    await lotRow(page, 'Tomatoes').getByRole('button', { name: 'Remove Lot' }).click();
    await page.getByRole('dialog', { name: 'Remove Lot Tomatoes?' }).getByRole('button', { name: 'Remove Lot' }).click();
    await expect(page.getByText('Move or unassign the Node on Tomatoes first.')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Name of Tomatoes' })).toBeVisible();
    expect((await serverSites()).lots.find((lot) => lot.name === 'Tomatoes')?.removed).toBeUndefined();

    // A Lot holding a Node is not no-Node: Garden shows only its name (UX-DR18).
    await page.goto('/garden');
    await expect(page.getByRole('list', { name: 'Lots' }).getByRole('img')).toHaveAccessibleName('Tomatoes');
  });

  test('UX-DR84 a Member sees the Site and Lots read-only with one notice; the Site menu opens Site settings', async ({ page }) => {
    await resetSites([{ id: homeId, name: 'Allotment', role: 'Member' }], [{ id: '0192a000-0000-7000-8000-000000000011', siteId: homeId, name: 'Tomatoes' }]);
    await signInTo(page);
    await page.getByRole('button', { name: 'Site menu for Allotment' }).click();
    await page.getByRole('menuitem', { name: 'Site settings' }).click();
    await expect(page).toHaveURL(/\/settings\/site$/u);

    const main = page.locator('main');
    await expect(main.getByText('Only Owners and Administrators can change Lots.')).toBeVisible();
    await expect(main.getByText('Allotment', { exact: true })).toBeVisible();
    await expect(main.getByText('Tomatoes', { exact: true })).toBeVisible();
    await expect(main.getByRole('textbox')).toHaveCount(0);
    await expect(main.getByRole('button')).toHaveCount(0);
    await axeClean(page, 'site settings, Member');
  });
});
