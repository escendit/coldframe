import { expect, test, type Locator, type Page } from '@playwright/test';
import type { FakeDevice, FakeHealthAlert, FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, nothingClipped, resetSites, setAlerts, setMode, signInButton, useTheme } from './helpers.ts';

/**
 * Story 7.3: the low-battery Alert in the web app. A Health row that says the level the Alert opened with,
 * in the light and the dark theme, leading to the Node's row on Devices, which shows the newest level and
 * the charger status as before.
 */
const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// A Site ID no other spec uses: the web app keeps the last good Lots per user and Site for the whole run.
const homeId = '0192a000-0000-7000-8000-0000000073a1';
const home = { id: homeId, name: 'Home garden', role: 'Member' } as const;
const lettuceNodeId = '9c0d1e2f3a4b5c68';
const spareNodeId = '7c19000000000004';
const herbsNodeId = '4a5b6c7d8e9f0a1b';

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Lots, Devices or Alerts.
  await resetSites();
});

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000073b${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T05:03:00.000Z', ...fields };
}

const lots: readonly FakeLot[] = [lot(1, 'Lettuce', 'ok', { claimed: true }), lot(2, 'Herbs', 'ok', { claimed: true })];

/** Today at `time` (HH:mm) on the wall clock of the zone: always "today" on the page. */
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

/** The Node of Lettuce reports 14 % and not charging; the others are well. */
function devices(): FakeDevice[] {
  return [
    { id: lettuceNodeId, siteId: homeId, kind: 'node', online: false, lotId: lots[0]?.id ?? '', lotName: 'Lettuce', lastSeenAt: todayAt('00:30'), batteryPercent: 14, charging: 'notCharging' },
    { id: herbsNodeId, siteId: homeId, kind: 'node', online: false, lotId: lots[1]?.id ?? '', lotName: 'Herbs', lastSeenAt: todayAt('00:32'), batteryPercent: 62, charging: 'charging' },
  ];
}

function healthAlert(id: string, opened: string, fields: Partial<FakeHealthAlert> = {}): FakeHealthAlert {
  return { id, siteId: homeId, kind: 'battery', deviceId: lettuceNodeId, deviceKind: 'node', openedAt: todayAt(opened), ...fields };
}

/** A low Node on Lettuce (14 %), a low Node on no Lot (9 %), one whose Alert states no level, and one that recovered. */
function healthAlerts(): FakeHealthAlert[] {
  return [
    healthAlert('b-lettuce', '00:30', { lotId: lots[0]?.id ?? '', lotName: 'Lettuce', batteryPercent: 14 }),
    healthAlert('b-spare', '00:20', { deviceId: spareNodeId, batteryPercent: 9 }),
    healthAlert('b-unstated', '00:10', { deviceId: herbsNodeId, lotId: lots[1]?.id ?? '', lotName: 'Herbs' }),
    healthAlert('b-closed', '00:05', { lotId: lots[0]?.id ?? '', lotName: 'Lettuce', batteryPercent: 12, closedAt: todayAt('00:25'), reason: 'recovered' }),
  ];
}

function alertRow(page: Page, id: string): Locator {
  return page.locator(`[data-alert="${id}"] a`);
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

test.describe('A low battery on Alerts', () => {
  for (const theme of themes) {
    test(`UX-DR25 UX-DR26 Health rows of a low battery say the level the Alert opened with (${theme})`, async ({ page }) => {
      await resetSites([home], lots, devices());
      await prepare(page, theme);
      await signInTo(page);
      await setAlerts([], null, healthAlerts());
      await page.goto('/alerts');

      await expect(page.locator('main h2')).toHaveText(['Health Alerts', 'Closed']);
      // As the Server lists them.
      const rows = page.getByRole('list', { name: 'Health Alerts' }).getByRole('link');
      await expect(rows).toHaveCount(3);
      await expect(rows.nth(0)).toHaveAccessibleName("Node battery 14 %, not charging — Lot 'Lettuce', since 12:30 AM");
      await expect(rows.nth(1)).toHaveAccessibleName('Node 7C19 battery 9 %, not charging, since 12:20 AM');
      await expect(rows.nth(2)).toHaveAccessibleName("Node battery low on Lot 'Herbs', since 12:10 AM");
      await expect(alertRow(page, 'b-lettuce')).toHaveText(/Health Alert\s*Node battery 14 %, not charging — Lot 'Lettuce'$/u);
      await expect(alertRow(page, 'b-spare')).toHaveText(/Health Alert\s*Node 7C19 battery 9 %, not charging$/u);
      // Hatched and dashed with the low-battery icon; a closed row is the outline and names no level.
      for (const id of ['b-lettuce', 'b-spare', 'b-unstated']) {
        await expect(alertRow(page, id)).toHaveCSS('border-top-style', 'dashed');
        await expect(alertRow(page, id).locator('.cf-hatch--plate')).toBeVisible();
        await expect(alertRow(page, id).locator('[data-icon="battery--low"]')).toBeVisible();
      }
      await expect(alertRow(page, 'b-closed')).toHaveText(/Health Alert · closed 12:25 AM\s*Node battery low on Lot 'Lettuce'$/u);
      await expect(alertRow(page, 'b-closed')).toHaveCSS('border-top-style', 'solid');
      // Each row leads to the row of its Node; an Alert has no action.
      await expect(alertRow(page, 'b-lettuce')).toHaveAttribute('href', `/devices#device-${lettuceNodeId}`);
      await expect(alertRow(page, 'b-spare')).toHaveAttribute('href', `/devices#device-${spareNodeId}`);
      await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);

      await axeClean(page, `Alerts, battery (${theme})`);
      await nothingClipped(page, `Alerts, battery (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-battery-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR26 UX-DR30 a low-battery row opens Devices at its Node, which shows the level and the charger status', async ({ page }) => {
    await resetSites([home], lots, devices());
    await signInTo(page);
    await setAlerts([], null, healthAlerts());
    await page.goto('/alerts');

    await page.getByRole('link', { name: /^Node battery 14 %, not charging — Lot 'Lettuce'/u }).click();
    await expect(page).toHaveURL(new RegExp(`/devices#device-${lettuceNodeId}$`, 'u'));
    const row = page.locator(`#device-${lettuceNodeId}`);
    await expect(row).toBeInViewport();
    await expect(row.locator('.cf-devices__id')).toHaveText(lettuceNodeId);
    // As before this story: the newest report's level and charger status, with the low-battery icon below 20 %.
    await expect(row).toContainText('14 %');
    await expect(row).toContainText('not charging');
    await expect(row.locator('[data-icon="battery--low"]')).toBeVisible();
    await expect(page.locator(`#device-${herbsNodeId} [data-icon="battery--low"]`)).toHaveCount(0);
  });
});
