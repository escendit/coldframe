import { expect, test } from '@playwright/test';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, serverSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

// A fixed zone keeps the detected time zone, and so the screenshots, the same on every machine.
test.use({ timezoneId: 'Europe/Zurich' });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site.
  await resetSites();
});

test.describe('Create Site and the empty Garden', () => {
  for (const theme of themes) {
    test(`UX-DR61 UX-DR21 UX-DR54 UX-DR62 UX-DR82 UX-DR23 sign in with no Site, create Home, land on its empty Garden (${theme}, largest text)`, async ({ page }) => {
      await resetSites([]);
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });

      await page.goto('/');
      await signInButton(page).click();
      // No Membership: every app route opens Create Site.
      await expect(page).toHaveURL(/\/sites\/new$/u);
      await expect(page.getByRole('heading', { level: 1, name: 'Create Site' })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Back to Garden' })).toHaveCount(0);
      await page.goto('/garden');
      await expect(page).toHaveURL(/\/sites\/new$/u);

      await largestText(page);
      const detected = await page.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone);
      await expect(page.getByText(`Is your time zone ${detected}?`)).toBeVisible();
      await page.getByRole('textbox', { name: 'Site name' }).fill('Home');
      await axeClean(page, `create site (${theme})`);
      await nothingClipped(page, `create site (${theme})`);
      await expect(page).toHaveScreenshot(`create-site-${theme}.png`, { fullPage: true });

      await page.getByRole('button', { name: 'Confirm' }).click();
      await expect(page.getByText(`Your time zone is ${detected}.`)).toBeVisible();
      await page.getByRole('button', { name: 'Create Site' }).click();

      await expect(page).toHaveURL(/\/garden$/u);
      const { posts, sites } = await serverSites();
      expect(posts).toHaveLength(1);
      expect(posts[0]?.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/u);
      // AD-11: the time zone stays on this browser; the Server gets {name} only.
      expect(posts[0]?.body).toEqual({ name: 'Home' });
      expect(sites.map((site) => [site.name, site.role])).toEqual([['Home', 'Owner']]);

      await largestText(page);
      const summary = page.locator('.cf-site-summary');
      await expect(summary.getByText('Home', { exact: true })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'No Readings yet' })).toBeVisible();
      await expect(page.getByText("Nothing is measuring, so there's no status to show.")).toBeVisible();
      const steps = page.getByRole('list', { name: 'First steps' }).getByRole('listitem');
      await expect(steps).toHaveCount(4);
      await expect(steps.nth(0)).toContainText('Add a Hub');
      await expect(steps.nth(0)).toHaveCSS('background-color', 'rgb(255, 119, 15)');
      await expect(steps.nth(1)).toHaveCSS('border-top-style', 'dashed');
      await expect(page.getByText('Adding a Hub or Node needs the Coldframe mobile app.')).toBeVisible();

      const tabs = page.getByRole('tablist', { name: 'Sites' }).getByRole('tab');
      await expect(tabs).toHaveText([/Home\s*·\s*Owner/u, 'New Site']);
      await expect(tabs.nth(0)).toHaveAttribute('aria-selected', 'true');
      await expect(page.getByRole('tab', { name: 'Home · Owner' })).toBeVisible();
      await expect(page.getByRole('tab', { name: 'New Site' })).toBeVisible();

      await axeClean(page, `garden (${theme})`);
      await nothingClipped(page, `garden (${theme})`);
      await expect(page).toHaveScreenshot(`garden-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR23 UX-DR22 the switcher changes the whole app; the Site menu holds Site settings', async ({ page }) => {
    await resetSites([
      { id: '0192a000-0000-7000-8000-00000000000a', name: 'Home', role: 'Owner' },
      { id: '0192a000-0000-7000-8000-00000000000b', name: 'Allotment', role: 'Member' },
    ]);
    await page.goto('/');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/garden$/u);
    await expect(page.getByRole('tab', { name: 'Home · Owner' })).toHaveAttribute('aria-selected', 'true');

    await page.getByRole('tab', { name: 'Allotment · Member' }).click();
    await expect(page).toHaveURL(/\/garden$/u);
    await expect(page.getByRole('tab', { name: 'Allotment · Member' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('.cf-site-summary').getByText('Allotment', { exact: true })).toBeVisible();
    await expect(page.getByText('Only Owners and Administrators can add Devices.')).toBeVisible();
    // The choice persists on this browser.
    await page.reload();
    await expect(page.getByRole('tab', { name: 'Allotment · Member' })).toHaveAttribute('aria-selected', 'true');

    const trigger = page.getByRole('button', { name: 'Site menu for Allotment' });
    await trigger.click();
    const menu = page.getByRole('menu');
    await expect(menu.getByRole('menuitem')).toHaveText(['Site settings']);
    await page.keyboard.press('Escape');
    await expect(menu).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await trigger.click();
    await menu.getByRole('menuitem', { name: 'Site settings' }).click();
    await expect(page).toHaveURL(/\/settings\/site$/u);

    await page.getByRole('tab', { name: 'New Site' }).click();
    await expect(page).toHaveURL(/\/sites\/new$/u);
    await expect(page.getByRole('tab', { name: 'New Site' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('link', { name: 'Back to Garden' }).click();
    await expect(page).toHaveURL(/\/garden$/u);
  });

  test('UX-DR61 a blank name is refused on the field and nothing reaches the Server', async ({ page }) => {
    await resetSites([]);
    await page.goto('/');
    await signInButton(page).click();
    await expect(page).toHaveURL(/\/sites\/new$/u);
    await page.getByRole('button', { name: 'Create Site' }).click();
    const field = page.getByRole('textbox', { name: 'Site name' });
    await expect(field).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('Enter a name for the Site.')).toBeVisible();
    expect((await serverSites()).posts).toEqual([]);
  });
});
