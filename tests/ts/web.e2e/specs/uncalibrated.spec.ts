import { expect, test, type Locator, type Page } from '@playwright/test';
import type { FakeCalibration, FakeHealthAlert, FakeLot, FakeSensorReading } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, nothingClipped, resetSites, setAlerts, setMode, signInButton, useTheme } from './helpers.ts';

/**
 * Story 7.4: the uncalibrated Sensor Alert in the web app. A Health row with the tools icon that names the Lot
 * whose soil Sensor needs Calibration, in the light and the dark theme, leading to Lot detail, where Calibrate
 * is offered to an Administrator and not to a Member. Once calibrated the row is a Closed row.
 */
const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// A Site ID no other spec uses: the web app keeps the last good Lots per user and Site for the whole run.
const homeId = '0192a000-0000-7000-8000-0000000074a1';
const home = { id: homeId, name: 'Home garden', role: 'Administrator' } as const;
const guestHome = { id: homeId, name: 'Home garden', role: 'Member' } as const;
const peppersNodeId = '7e1f2a3b4c5d6e70';
const herbsNodeId = '4a5b6c7d8e9f0a74';
const peppersSensor = '0192a000-0000-7000-8000-0000000074aa';

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Lots, Devices or Alerts.
  await resetSites();
});

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

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000074b${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T05:03:00.000Z', claimed: true, ...fields };
}

/** Peppers has a Node whose soil Sensor was never calibrated: a raw count, and a Sensor that calls for Calibration. */
function lots(): FakeLot[] {
  const soil: FakeSensorReading = { quantity: 'soil_moisture', value: 1840, unit: 'raw', measuredAt: todayAt('00:30'), sensorId: peppersSensor, calibratable: true };
  return [
    lot(1, 'Peppers', 'needsCalibration', {
      lastReadingAt: todayAt('00:30'),
      node: { deviceId: peppersNodeId, batteryPercent: 62, charging: 'charging', lastSeenAt: todayAt('00:30') },
      sensors: [soil],
    }),
    lot(2, 'Herbs', 'ok'),
  ];
}

const calibrations: readonly FakeCalibration[] = [{ sensorId: peppersSensor, siteId: homeId, readings: [] }];

function healthAlert(id: string, opened: string, fields: Partial<FakeHealthAlert> = {}): FakeHealthAlert {
  return { id, siteId: homeId, kind: 'uncalibrated', deviceId: peppersNodeId, deviceKind: 'node', openedAt: todayAt(opened), ...fields };
}

/** The soil Sensor of Peppers needs Calibration; the one of Herbs was calibrated a few minutes ago. */
function healthAlerts(): FakeHealthAlert[] {
  const [peppers, herbs] = lots();
  return [
    healthAlert('u-peppers', '00:30', { lotId: peppers?.id ?? '', lotName: 'Peppers' }),
    healthAlert('u-herbs', '00:05', { deviceId: herbsNodeId, lotId: herbs?.id ?? '', lotName: 'Herbs', closedAt: todayAt('00:25'), reason: 'calibrated' }),
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

test.describe('An uncalibrated Sensor on Alerts', () => {
  for (const theme of themes) {
    test(`UX-DR25 UX-DR26 the Health row of an uncalibrated Sensor names its Lot with the tools icon; a calibrated one is Closed (${theme})`, async ({ page }) => {
      await resetSites([home], lots(), [], null, calibrations);
      await prepare(page, theme);
      await signInTo(page);
      await setAlerts([], null, healthAlerts());
      await page.goto('/alerts');

      await expect(page.locator('main h2')).toHaveText(['Health Alerts', 'Closed']);
      const open = page.getByRole('list', { name: 'Health Alerts' }).getByRole('link');
      await expect(open).toHaveCount(1);
      await expect(open.first()).toHaveAccessibleName("Soil Sensor on Lot 'Peppers' needs Calibration, since 12:30 AM");
      await expect(alertRow(page, 'u-peppers')).toHaveText(/Health Alert\s*Soil Sensor on Lot 'Peppers' needs Calibration$/u);
      // Hatched and dashed with the tools icon; the closed row is the outline and keeps its title and icon.
      await expect(alertRow(page, 'u-peppers')).toHaveCSS('border-top-style', 'dashed');
      await expect(alertRow(page, 'u-peppers').locator('.cf-hatch--plate')).toBeVisible();
      await expect(alertRow(page, 'u-peppers').locator('[data-icon="tools"]')).toBeVisible();
      const closed = page.getByRole('list', { name: 'Closed' }).getByRole('link');
      await expect(closed).toHaveCount(1);
      await expect(alertRow(page, 'u-herbs')).toHaveText(/Health Alert · closed 12:25 AM\s*Soil Sensor on Lot 'Herbs' needs Calibration$/u);
      await expect(alertRow(page, 'u-herbs')).toHaveCSS('border-top-style', 'solid');
      await expect(alertRow(page, 'u-herbs').locator('[data-icon="tools"]')).toBeVisible();
      // Each row leads to Lot detail of its Lot, never to Devices; an Alert has no action and no body.
      const [peppers, herbs] = lots();
      await expect(alertRow(page, 'u-peppers')).toHaveAttribute('href', `/garden/${peppers?.id ?? ''}`);
      await expect(alertRow(page, 'u-herbs')).toHaveAttribute('href', `/garden/${herbs?.id ?? ''}`);
      await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);

      await axeClean(page, `Alerts, uncalibrated (${theme})`);
      await nothingClipped(page, `Alerts, uncalibrated (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-uncalibrated-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR26 the row opens Lot detail of its Lot, where an Administrator finds Calibrate', async ({ page }) => {
    const [peppers] = lots();
    await resetSites([home], lots(), [], null, calibrations);
    await signInTo(page);
    await setAlerts([], null, healthAlerts());
    await page.goto('/alerts');

    await page.getByRole('link', { name: /^Soil Sensor on Lot 'Peppers' needs Calibration, since/u }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${peppers?.id ?? ''}$`, 'u'));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Peppers');
    const calibrate = page.getByRole('link', { name: 'Calibrate', exact: true });
    await expect(calibrate).toBeVisible();
    await expect(calibrate).toHaveAttribute('href', `/garden/${peppers?.id ?? ''}/calibrate`);
  });

  test('UX-DR26 a Member follows the same row to Lot detail and is offered no Calibrate', async ({ page }) => {
    const [peppers] = lots();
    await resetSites([guestHome], lots(), [], null, calibrations);
    await signInTo(page);
    await setAlerts([], null, healthAlerts());
    await page.goto('/alerts');

    await page.getByRole('link', { name: /^Soil Sensor on Lot 'Peppers' needs Calibration, since/u }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${peppers?.id ?? ''}$`, 'u'));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Peppers');
    await expect(page.getByRole('link', { name: /Calibrate/u })).toHaveCount(0);
  });
});
