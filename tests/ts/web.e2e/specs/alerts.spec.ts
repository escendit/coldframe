import { expect, test, type Page } from '@playwright/test';
import type { FakeAlert, FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { alertReads, axeClean, nothingClipped, resetSites, setAlerts, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

const homeId = '0192a000-0000-7000-8000-00000000000a';
const home = { id: homeId, name: 'Home', role: 'Member' } as const;
const otherId = '0192a000-0000-7000-8000-00000000000b';
const orange = 'rgb(255, 119, 15)';

// A fixed zone keeps the times, and so the screenshots, the same on every machine.
test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Alerts.
  await resetSites();
});

/** Today at `time` (HH:mm) on the wall clock of the zone, as the Server would write it: always "today" on the page. */
function todayAt(time: string): string {
  const day = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  for (const offset of ['+02:00', '+01:00']) {
    const instant = new Date(`${day}T${time}:00.000${offset}`);
    const shown = new Intl.DateTimeFormat('en-GB', { timeZone: zone, hour: '2-digit', minute: '2-digit' }).format(instant);
    if (shown === time) {
      return instant.toISOString();
    }
  }
  throw new Error(`No ${time} today in ${zone}.`);
}

const lots: readonly FakeLot[] = [
  { id: 'lot-1', siteId: homeId, name: 'Tomatoes', claimed: true },
  { id: 'lot-2', siteId: homeId, name: 'Herbs', claimed: true },
];

function alert(id: string, lotName: string, opened: string, fields: Partial<FakeAlert> = {}): FakeAlert {
  return { id, siteId: homeId, kind: 'threshold', side: 'low', quantity: 'soil_moisture', lotId: 'lot-1', lotName, deviceId: '7c19000000000001', openedAt: todayAt(opened), ...fields };
}

/** One Alert per row variant and cause, seeded out of order: the Server orders them. */
function everyVariant(): FakeAlert[] {
  return [
    alert('a-closed-high', 'Chard', '02:10', { quantity: 'air_temperature', side: 'high', closedAt: todayAt('03:15'), reason: 'recovered' }),
    alert('a-battery', 'Lettuce', '03:00', { kind: 'battery', side: undefined }),
    alert('a-low', 'Peppers', '04:00', { quantity: 'air_temperature' }),
    alert('a-water', 'Tomatoes', '05:45'),
    alert('a-uncalibrated', 'Potatoes', '02:00', { kind: 'uncalibrated', side: undefined }),
    alert('a-wet', 'Herbs', '05:10', { side: 'high', lotId: 'lot-2' }),
    alert('a-silent', 'Peas', '06:00', { kind: 'silent', side: undefined }),
    alert('a-novel', 'Basil', '01:00', { kind: 'frost', side: undefined }),
    alert('a-closed-water', 'Strawberries', '05:45', { closedAt: todayAt('06:40'), reason: 'recovered' }),
    alert('a-closed-silent', 'Kale', '01:30', { kind: 'silent', side: undefined, closedAt: todayAt('04:20'), reason: 'removed' }),
    // Another Site's Alert never shows.
    alert('a-elsewhere', 'Elsewhere', '06:30', { siteId: otherId }),
  ];
}

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

async function prepare(page: Page, theme: 'light' | 'dark'): Promise<void> {
  await useTheme(page, theme, appUrl);
  await page.context().addCookies([{ name: 'cf_time_zone', value: zone, url: appUrl }]);
  await page.emulateMedia({ colorScheme: theme });
  await page.setViewportSize({ width: 1280, height: 800 });
}

function rows(page: Page, group: string) {
  return page.getByRole('list', { name: group }).getByRole('link');
}

function row(page: Page, id: string) {
  return page.locator(`[data-alert="${id}"] a`);
}

test.describe('Alerts', () => {
  for (const theme of themes) {
    test(`UX-DR14 UX-DR25 UX-DR26 UX-DR64 UX-DR98 every Alert row variant: Threshold, then Health, then Closed, newest first (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await setAlerts(everyVariant());
      await prepare(page, theme);
      await signInTo(page);
      await page.goto('/alerts');

      await expect(page.getByRole('heading', { level: 1, name: 'Alerts' })).toBeVisible();
      const headings = page.locator('main h2');
      await expect(headings).toHaveText(['Threshold Alerts', 'Health Alerts', 'Closed']);

      // One link per row, named by the condition and when it started; newest first in each group.
      await expect(rows(page, 'Threshold Alerts')).toHaveCount(3);
      await expect(rows(page, 'Threshold Alerts').nth(0)).toHaveAccessibleName('Tomatoes needs water, since 5:45 AM');
      await expect(rows(page, 'Threshold Alerts').nth(1)).toHaveAccessibleName('Herbs too wet, since 5:10 AM');
      await expect(rows(page, 'Threshold Alerts').nth(2)).toHaveAccessibleName('Peppers temperature too low, since 4:00 AM');
      await expect(rows(page, 'Health Alerts')).toHaveCount(4);
      await expect(rows(page, 'Health Alerts').nth(0)).toHaveAccessibleName("Node on Lot 'Peas' silent, since 6:00 AM");
      await expect(rows(page, 'Health Alerts').nth(1)).toHaveAccessibleName("Node battery low on Lot 'Lettuce', since 3:00 AM");
      await expect(rows(page, 'Health Alerts').nth(2)).toHaveAccessibleName("Soil Sensor on Lot 'Potatoes' needs Calibration, since 2:00 AM");
      await expect(rows(page, 'Health Alerts').nth(3)).toHaveAccessibleName("Health Alert on Lot 'Basil', since 1:00 AM");
      await expect(rows(page, 'Closed')).toHaveCount(3);
      await expect(rows(page, 'Closed').nth(0)).toHaveAccessibleName('Strawberries needs water, since 5:45 AM, closed 6:40 AM');
      await expect(rows(page, 'Closed').nth(1)).toHaveAccessibleName("Node on Lot 'Kale' silent, since 1:30 AM, closed 4:20 AM");
      await expect(rows(page, 'Closed').nth(2)).toHaveAccessibleName('Chard temperature too high, since 2:10 AM, closed 3:15 AM');
      await expect(page.getByText('Elsewhere')).toHaveCount(0);
      await expect(page.getByText('No open Alerts.')).toHaveCount(0);

      // The four variants: eyebrow, title, icon and shape.
      await expect(row(page, 'a-water')).toHaveText(/Needs water · 5:45 AM\s*Tomatoes needs water/u);
      await expect(row(page, 'a-water').locator('[data-icon="rain-drop"]')).toBeVisible();
      await expect(row(page, 'a-wet')).toHaveText(/Threshold Alert · above high · 5:10 AM\s*Herbs too wet/u);
      await expect(row(page, 'a-wet').locator('[data-icon="arrow--up"]')).toBeVisible();
      await expect(row(page, 'a-wet')).toHaveCSS('border-top-width', '2px');
      await expect(row(page, 'a-wet')).toHaveCSS('border-top-style', 'solid');
      await expect(row(page, 'a-low')).toHaveText(/Threshold Alert · below low · 4:00 AM\s*Peppers temperature too low/u);
      await expect(row(page, 'a-low').locator('[data-icon="arrow--down"]')).toBeVisible();
      await expect(row(page, 'a-silent')).toHaveText(/Health Alert\s*Node on Lot 'Peas' silent/u);
      await expect(row(page, 'a-silent').locator('[data-icon="help"]')).toBeVisible();
      await expect(row(page, 'a-silent')).toHaveCSS('border-top-style', 'dashed');
      await expect(row(page, 'a-silent').locator('.cf-hatch--plate')).toBeVisible();
      await expect(row(page, 'a-battery').locator('[data-icon="battery--low"]')).toBeVisible();
      await expect(row(page, 'a-uncalibrated').locator('[data-icon="tools"]')).toBeVisible();
      await expect(row(page, 'a-novel').locator('[data-icon="help"]')).toBeVisible();
      await expect(row(page, 'a-closed-water')).toHaveText(/Needs water · closed 6:40 AM\s*Strawberries needs water/u);
      await expect(row(page, 'a-closed-water')).toHaveCSS('border-top-width', '1px');
      await expect(row(page, 'a-closed-high')).toHaveText(/Threshold Alert · above high · closed 3:15 AM\s*Chard temperature too high/u);
      await expect(row(page, 'a-closed-silent')).toHaveText(/Health Alert · closed 4:20 AM\s*Node on Lot 'Kale' silent/u);

      // Uppercase is style only; the title is in the section type.
      await expect(row(page, 'a-water').locator('.cf-alert-row__eyebrow')).toHaveCSS('text-transform', 'uppercase');
      await expect(row(page, 'a-water').locator('.cf-alert-row__title')).toHaveCSS('font-size', '20px');

      // Orange is the needs-water row and nothing else: not too wet, not Health, not a closed needs-water Alert.
      const fills = await page.locator('[data-alert] a').evaluateAll((links) => links.filter((link) => getComputedStyle(link).backgroundColor === 'rgb(255, 119, 15)').map((link) => link.parentElement?.dataset.alert));
      expect(fills).toEqual(['a-water']);
      await expect(row(page, 'a-water')).toHaveCSS('background-color', orange);

      // No actions on Alerts, no rail and no count in the side nav.
      await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);
      await expect(page.getByRole('navigation').getByRole('link', { name: 'Alerts', exact: true })).toBeVisible();

      await axeClean(page, `alerts (${theme})`);
      await nothingClipped(page, `alerts (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-${theme}.png`, { fullPage: true });
    });

    test(`UX-DR82 UX-DR64 no Alerts: "No open Alerts." and nothing else (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await prepare(page, theme);
      await signInTo(page);
      await page.goto('/alerts');

      await expect(page.getByText('No open Alerts.')).toBeVisible();
      await expect(page.locator('main h2')).toHaveCount(0);
      await expect(page.locator('[data-alert]')).toHaveCount(0);
      await expect(page.getByText(/all good/iu)).toHaveCount(0);

      await axeClean(page, `alerts, empty (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-empty-${theme}.png`, { fullPage: true });
    });

    test(`UX-DR82 UX-DR64 only closed Alerts: "No open Alerts." and then Closed (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await setAlerts([alert('a-closed-water', 'Strawberries', '05:45', { closedAt: todayAt('06:40'), reason: 'recovered' })]);
      await prepare(page, theme);
      await signInTo(page);
      await page.goto('/alerts');

      await expect(page.getByText('No open Alerts.')).toBeVisible();
      await expect(page.locator('main h2')).toHaveText(['Closed']);
      await expect(rows(page, 'Closed')).toHaveCount(1);
      await expect(row(page, 'a-closed-water')).not.toHaveCSS('background-color', orange);

      await axeClean(page, `alerts, only closed (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-only-closed-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR26 a Threshold Alert row opens Lot detail of its Lot; a silent Node row opens Devices', async ({ page }) => {
    await resetSites([home], lots);
    await setAlerts([alert('a-wet', 'Herbs', '05:10', { side: 'high', lotId: 'lot-2' }), alert('a-silent', 'Peas', '06:00', { kind: 'silent', side: undefined })]);
    await signInTo(page);
    await page.goto('/alerts');

    await page.getByRole('link', { name: /^Herbs too wet, since/u }).click();
    await expect(page).toHaveURL(/\/garden\/lot-2$/u);
    await expect(page.getByRole('heading', { level: 1, name: 'Herbs' })).toBeVisible();

    await page.goto('/alerts');
    await page.getByRole('link', { name: /^Node on Lot 'Peas' silent, since/u }).click();
    await expect(page).toHaveURL(/\/devices$/u);
  });

  test('UX-DR64 the web app follows the cursor to the end: every Alert shows once', async ({ page }) => {
    await resetSites([home], lots);
    const many = Array.from({ length: 205 }, (_, index) => alert(`a-${String(index)}`, `Lot ${String(index)}`, '05:45'));
    await setAlerts(many);
    await signInTo(page);
    await page.goto('/alerts');

    await expect(page.locator('[data-alert]')).toHaveCount(205);
    expect(await page.locator('[data-alert]').evaluateAll((items) => new Set(items.map((item) => (item as HTMLElement).dataset.alert)).size)).toBe(205);
    expect(await alertReads()).toBe(2);
  });

  test('UX-DR64 a load that fails shows the notice with Try again and no rows; Try again loads the Alerts', async ({ page }) => {
    await resetSites([home], lots);
    await setAlerts([alert('a-water', 'Tomatoes', '05:45')], 503);
    await signInTo(page);
    await page.goto('/alerts');

    await expect(page.locator('#cf-alerts-notice')).toContainText("Can't reach your Server.");
    await expect(page.locator('[data-alert]')).toHaveCount(0);
    await expect(page.getByText('No open Alerts.')).toHaveCount(0);

    await setAlerts([alert('a-water', 'Tomatoes', '05:45')]);
    await page.getByRole('link', { name: 'Try again' }).click();
    await expect(row(page, 'a-water')).toBeVisible();
  });

  test('UX-DR64 the Alerts are read again when the page gets the focus back', async ({ page }) => {
    await resetSites([home], lots);
    await signInTo(page);
    await page.goto('/alerts');
    await expect(page.getByText('No open Alerts.')).toBeVisible();

    await setAlerts([alert('a-water', 'Tomatoes', '05:45')]);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(row(page, 'a-water')).toBeVisible();
    await expect(page.getByText('No open Alerts.')).toHaveCount(0);
  });
});
