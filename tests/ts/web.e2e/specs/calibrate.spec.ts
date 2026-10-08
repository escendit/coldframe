import { expect, test, type Page } from '@playwright/test';
import type { FakeCalibration, FakeLot, FakeSensorReading } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, nothingClipped, pushCalibrationReading, resetSites, serverSites, setLotSensors, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// Site IDs no other spec uses: the web app keeps the last good Lots per user and Site for the run.
const homeId = '0192a000-0000-7000-8000-0000000052a1';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const guestHome = { id: homeId, name: 'Home garden', role: 'Member' } as const;

/** The browser's clock: 07:17 in Zurich on 6 October. */
const now = new Date('2026-10-06T05:17:00.000Z');

const peppersSensor = '0192a000-0000-7000-8000-0000000052aa';
const lettuceSensor = '0192a000-0000-7000-8000-0000000052ab';

const soil = (sensorId: string, value = 1840, unit: FakeSensorReading['unit'] = 'raw', measuredAt = '2026-10-06T05:02:00.000Z'): FakeSensorReading => ({
  quantity: 'soil_moisture',
  value,
  unit,
  measuredAt,
  sensorId,
  calibratable: true,
});

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000052d${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T03:45:00.000Z', claimed: true, ...fields };
}

const node = (id: string) => ({ deviceId: id, batteryPercent: 62, charging: 'charging', lastSeenAt: '2026-10-06T05:02:00.000Z' }) as const;
const peppers = lot(1, 'Peppers', 'needsCalibration', { lastReadingAt: '2026-10-06T05:02:00.000Z', node: node('7c19000000000052'), sensors: [soil(peppersSensor)] });
const lettuce = lot(2, 'Lettuce', 'paused', { pausedBy: ['device'], node: node('7c19000000000053'), sensors: [soil(lettuceSensor)] });
const lots = [peppers, lettuce];

const stored = [{ readingSeq: 40, rawValue: 2900, measuredAt: '2026-10-06T05:02:00.000Z' }];
const peppersState: FakeCalibration = { sensorId: peppersSensor, siteId: homeId, readings: stored };
const lettuceState: FakeCalibration = { sensorId: lettuceSensor, siteId: homeId, readings: stored };

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  await resetSites();
});

async function prepare(page: Page, theme: 'light' | 'dark'): Promise<void> {
  await useTheme(page, theme, appUrl);
  await page.context().addCookies([{ name: 'cf_time_zone', value: zone, url: appUrl }]);
  await page.emulateMedia({ colorScheme: theme });
  await page.clock.setFixedTime(now);
}

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

test.describe('Calibrate from the app', () => {
  for (const theme of themes) {
    test(`dry, then wet, then the confirmation updates in place to the Lot's percentage, from the tile (${theme})`, async ({ page }) => {
      await resetSites([home], lots, [], null, [peppersState]);
      await prepare(page, theme);
      await signInTo(page);

      await page.getByRole('link', { name: 'Calibrate Peppers' }).click();
      await expect(page).toHaveURL(/\/calibrate$/u);
      await page.waitForLoadState('networkidle');

      // Step 1: waiting panel with the last raw value and time; Record dry waits for a Reading taken after the step started.
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('Calibrate Peppers');
      await expect(page.locator('[data-step="dry"]')).toBeVisible();
      await expect(page.locator('#cf-calibrate-waiting')).toContainText('Waiting for the next Reading');
      await expect(page.locator('#cf-calibrate-waiting')).toContainText('Last Reading: raw 2,900 at 7:02 AM');
      await expect(page.getByRole('button', { name: 'Record dry' })).toBeDisabled();
      await axeClean(page, `Calibrate, dry (${theme})`);
      await nothingClipped(page, `Calibrate, dry (${theme})`);
      await expect(page).toHaveScreenshot(`calibrate-dry-${theme}.png`, { fullPage: true });

      // A fresh Reading enables Record dry.
      await pushCalibrationReading(peppersSensor, { readingSeq: 41, rawValue: 3000, measuredAt: '2026-10-06T05:18:00.000Z' });
      const recordDry = page.getByRole('button', { name: 'Record dry' });
      await expect(recordDry).toBeEnabled({ timeout: 10_000 });
      await recordDry.click();

      // Step 2: the dry point is kept server-side; the flow moves to wet.
      await expect(page.locator('[data-step="wet"]')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Record wet' })).toBeDisabled();
      await axeClean(page, `Calibrate, wet (${theme})`);
      await expect(page).toHaveScreenshot(`calibrate-wet-${theme}.png`, { fullPage: true });

      // The alternative: pick a recent stored Reading.
      await pushCalibrationReading(peppersSensor, { readingSeq: 42, rawValue: 1200, measuredAt: '2026-10-06T05:19:00.000Z' });
      const recordWet = page.getByRole('button', { name: 'Record wet' });
      await expect(recordWet).toBeEnabled({ timeout: 10_000 });
      await recordWet.click();

      // Confirmation: both raw values, and no percentage before the Server stored a calibrated Reading.
      await expect(page.locator('[data-step="confirm"]')).toBeVisible();
      await expect(page.locator('#cf-calibrate-points')).toHaveText('Dry raw 3,000, wet raw 1,200.');
      await expect(page.locator('#cf-calibrate-result')).toHaveText('% appears with the next Reading');
      await expect(page).toHaveScreenshot(`calibrate-confirm-${theme}.png`, { fullPage: true });

      // The first calibrated Reading arrives (after the save, which the Server's clock stamps): the line updates in place.
      await setLotSensors(peppers.id, [soil(peppersSensor, 40, '%', new Date(Date.now() + 60_000).toISOString())]);
      await expect(page.locator('#cf-calibrate-result')).toHaveText('Peppers reads ~40 %', { timeout: 10_000 });
      await expect(page).toHaveScreenshot(`calibrate-confirm-percent-${theme}.png`, { fullPage: true });

      const { calibrationPosts } = await serverSites();
      expect(calibrationPosts.map((post) => post.body)).toEqual([{ dry: { readingSeq: 41 } }, { wet: { readingSeq: 42 } }]);
    });
  }

  test('a Calibration left after the dry point resumes at wet', async ({ page }) => {
    await resetSites([home], lots, [], null, [{ ...peppersState, pendingDry: 3000 }]);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${peppers.id}/calibrate`);
    await expect(page.locator('[data-step="wet"]')).toBeVisible();
  });

  test('indistinct points: the message says what happened, what did not change and what to do', async ({ page }) => {
    await resetSites([home], lots, [], null, [{ ...peppersState, pendingDry: 2900 }]);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${peppers.id}/calibrate`);
    await page.waitForLoadState('networkidle');
    await page.getByRole('button', { name: 'Use as wet' }).first().click();
    await expect(page.locator('#cf-calibrate-failure')).toContainText('too close together. Nothing was saved.');
    await expect(page.locator('[data-step="wet"]')).toBeVisible();
  });

  for (const theme of themes) {
    test(`a paused Device explains instead of waiting (${theme})`, async ({ page }) => {
      await resetSites([home], lots, [], null, [lettuceState]);
      await prepare(page, theme);
      await signInTo(page);
      await page.goto(`/garden/${lettuce.id}/calibrate`);
      await expect(page.locator('#cf-calibrate-paused')).toContainText('Readings resume after the Pause ends');
      await expect(page.getByText('Waiting for the next Reading')).toHaveCount(0);
      await axeClean(page, `Calibrate, paused (${theme})`);
      await expect(page).toHaveScreenshot(`calibrate-paused-${theme}.png`, { fullPage: true });
    });
  }

  test('Lot detail offers Calibrate to an Owner for a calibratable Sensor', async ({ page }) => {
    await resetSites([home], lots, [], null, [peppersState]);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${peppers.id}`);
    await page.getByRole('link', { name: 'Calibrate' }).click();
    await expect(page).toHaveURL(/\/calibrate$/u);
  });

  test('a Member sees no Calibrate on the tile or Lot detail, and the route sends them back to the Lot', async ({ page }) => {
    await resetSites([guestHome], lots, [], null, [peppersState]);
    await prepare(page, 'light');
    await signInTo(page);
    await expect(page.getByRole('link', { name: /Calibrate/u })).toHaveCount(0);
    await page.goto(`/garden/${peppers.id}`);
    await expect(page.getByRole('link', { name: /Calibrate/u })).toHaveCount(0);
    await page.goto(`/garden/${peppers.id}/calibrate`);
    await expect(page).toHaveURL(new RegExp(`/garden/${peppers.id}$`, 'u'));
  });
});
