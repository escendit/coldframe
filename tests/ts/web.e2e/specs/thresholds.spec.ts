import { expect, test, type Page } from '@playwright/test';
import type { FakeCalibration, FakeHistoryDay, FakeLot, FakeSensorReading, FakeThresholds } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, patchLot, pushCalibrationReading, resetSites, serverSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// Site IDs no other spec uses: the web app keeps the last good Lots per user and Site for the run.
const homeId = '0192a000-0000-7000-8000-0000000054a1';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const guestHome = { id: homeId, name: 'Home garden', role: 'Member' } as const;

/** The browser's clock: 07:17 in Zurich on 6 October. */
const now = new Date('2026-10-06T05:17:00.000Z');

const tomatoesSoil = '0192a000-0000-7000-8000-0000000054aa';
const tomatoesAir = '0192a000-0000-7000-8000-0000000054ab';
const peppersSoil = '0192a000-0000-7000-8000-0000000054ac';

const reading = (quantity: FakeSensorReading['quantity'], value: number, unit: FakeSensorReading['unit'], sensorId: string, calibratable: boolean): FakeSensorReading => ({
  quantity,
  value,
  unit,
  measuredAt: '2026-10-06T05:02:00.000Z',
  sensorId,
  calibratable,
});

/** Daily soil lows in percent for the 30 UTC days up to 6 October: some days dip under 30 %, every third day has no Readings. */
function percentDays(): readonly FakeHistoryDay[] {
  const result: FakeHistoryDay[] = [];
  for (let offset = 29; offset >= 0; offset--) {
    if (offset % 3 === 2) {
      continue;
    }
    const day = new Date(Date.UTC(2026, 9, 6) - offset * 86_400_000).toISOString().slice(0, 10);
    const low = 20 + ((offset * 7) % 9) * 5;
    result.push({ day, low, high: Math.min(100, low + 25), readingCount: 96 });
  }
  return result;
}

const node = (id: string) => ({ deviceId: id, batteryPercent: 62, charging: 'charging', lastSeenAt: '2026-10-06T05:02:00.000Z' }) as const;

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000054d${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T03:45:00.000Z', claimed: true, ...fields };
}

const tomatoes = lot(1, 'Tomatoes', 'ok', {
  lastReadingAt: '2026-10-06T05:02:00.000Z',
  moisturePercent: 40,
  lowThresholdPercent: 30,
  node: node('7c19000000000054'),
  sensors: [reading('soil_moisture', 40, '%', tomatoesSoil, true), reading('air_temperature', 14.4, '°C', tomatoesAir, false)],
  history: { soil_moisture: percentDays() },
  historyUnits: { soil_moisture: '%' },
});
const peppers = lot(2, 'Peppers', 'needsCalibration', {
  lastReadingAt: '2026-10-06T05:02:00.000Z',
  node: node('7c19000000000055'),
  sensors: [reading('soil_moisture', 1840, 'raw', peppersSoil, true)],
});

const thresholds: readonly FakeThresholds[] = [
  { sensorId: tomatoesSoil, siteId: homeId, unit: '%', low: { kind: 'default', value: 30 }, high: { kind: 'cleared' } },
  { sensorId: tomatoesAir, siteId: homeId, unit: '°C', low: { kind: 'cleared' }, high: { kind: 'cleared' }, proposedLow: 7.5 },
  { sensorId: peppersSoil, siteId: homeId, unit: '%', low: { kind: 'default', value: 30 }, high: { kind: 'cleared' } },
];
const stored = [{ readingSeq: 40, rawValue: 2900, measuredAt: '2026-10-06T05:02:00.000Z' }];
const peppersState: FakeCalibration = { sensorId: peppersSoil, siteId: homeId, readings: stored };

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

test.describe('Thresholds in the app', () => {
  for (const theme of themes) {
    test(`UX-DR5 UX-DR32 UX-DR33 UX-DR45 UX-DR69 set a low Threshold: the tile shows ~% and OK, the chart shows the band and solid below-low bars (${theme}, largest text)`, async ({ page }) => {
      await resetSites([home], [tomatoes], [], null, [], thresholds);
      await prepare(page, theme);
      await signInTo(page);

      // The calibrated, in-range tile: ~40 % and OK.
      const tile = page.getByRole('link', { name: /^Tomatoes/u });
      await expect(tile).toContainText('OK');
      await expect(tile).toContainText('~40');

      await page.goto(`/garden/${tomatoes.id}`);
      await page.waitForLoadState('networkidle');
      await expect(page.locator('.cf-chart__band')).toHaveCount(1);
      await expect(page.locator('.cf-chart__low-line')).toHaveCount(1);
      expect(await page.locator('.cf-chart__bar--below-low').count()).toBeGreaterThan(0);
      await expect(page.locator('.cf-chart__legend')).toHaveText('solid bar = below 30 %');
      await expect(page.locator('#cf-detail-thresholds')).toHaveText('Soil moisture: low 30 %, high none');
      await largestText(page);
      await nothingClipped(page, `Lot detail with band (${theme})`);
      await expect(page).toHaveScreenshot(`thresholds-chart-${theme}.png`, { fullPage: true });

      // Open Thresholds from Lot detail, type a low, Save.
      await page.getByRole('link', { name: 'Set Thresholds' }).click();
      await expect(page).toHaveURL(/\/thresholds$/u);
      await page.waitForLoadState('networkidle');
      await largestText(page);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('Thresholds for Tomatoes');
      await expect(page.locator('.cf-threshold__no-high').first()).toBeVisible();
      // Nothing is edited yet, so there is nothing to save.
      await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();
      await axeClean(page, `Thresholds (${theme})`);
      await nothingClipped(page, `Thresholds (${theme})`);
      await expect(page).toHaveScreenshot(`thresholds-modal-${theme}.png`, { fullPage: true });

      await page.getByLabel('Low for Soil moisture').fill('25');
      await page.getByRole('button', { name: 'Save' }).click();
      await expect(page).toHaveURL(new RegExp(`/garden/${tomatoes.id}$`, 'u'));
      await page.waitForLoadState('networkidle');
      await expect(page.locator('.cf-chart__legend')).toHaveText('solid bar = below 25 %');
      await largestText(page);
      await expect(page).toHaveScreenshot(`thresholds-chart-after-${theme}.png`, { fullPage: true });

      const { thresholdPuts } = await serverSites();
      expect(thresholdPuts.map((put) => put.body)).toEqual([{ low: { kind: 'override', value: 25 } }]);
    });
  }

  test('UX-DR69 Low must stay below high shows inline and Save is disabled while it is invalid', async ({ page }) => {
    await resetSites([home], [tomatoes], [], null, [], thresholds);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${tomatoes.id}/thresholds`);
    await page.waitForLoadState('networkidle');

    await page.getByRole('button', { name: 'Add high' }).first().click();
    await page.getByLabel('High for Soil moisture').fill('20');
    await expect(page.getByText('Low must stay below high.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled();
    await page.getByLabel('High for Soil moisture').fill('70');
    await expect(page.getByText('Low must stay below high.')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  test('a Sensor with no default offers the proposed low when Alerts are turned on', async ({ page }) => {
    await resetSites([home], [tomatoes], [], null, [], thresholds);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${tomatoes.id}/thresholds`);
    await page.waitForLoadState('networkidle');

    await page.getByRole('button', { name: 'Turn on Alerts' }).click();
    await expect(page.getByLabel('Low for Temperature')).toHaveValue('7.5');
    await expect(page.getByLabel('High for Temperature')).toHaveCount(0);
  });

  test('UX-DR91 a Server that cannot save keeps the edits, says not saved, and a retry saves', async ({ page }) => {
    await resetSites([home], [tomatoes], [], null, [], thresholds.map((entry) => (entry.sensorId === tomatoesSoil ? { ...entry, failNextPut: 503 } : entry)));
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${tomatoes.id}/thresholds`);
    await page.waitForLoadState('networkidle');

    await page.getByLabel('Low for Soil moisture').fill('25');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('#cf-thresholds-failure')).toContainText('Not saved.');
    await expect(page.getByLabel('Low for Soil moisture')).toHaveValue('25');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${tomatoes.id}$`, 'u'));
  });

  test('UX-DR84 a Member sees Thresholds read-only: no edit control on Lot detail or on the page', async ({ page }) => {
    await resetSites([guestHome], [tomatoes], [], null, [], thresholds);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${tomatoes.id}`);
    await expect(page.locator('#cf-detail-thresholds')).toHaveText('Soil moisture: low 30 %, high none');
    await expect(page.getByRole('link', { name: 'Set Thresholds' })).toHaveCount(0);
    await page.getByRole('link', { name: 'View Thresholds' }).click();
    await expect(page.locator('#cf-thresholds-readonly')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save' })).toHaveCount(0);
    await expect(page.locator('input[type="number"]')).toHaveCount(0);
    await expect(page.getByRole('slider')).toHaveCount(0);
  });

  test('UX-DR45 calibrate, then go on to Thresholds, set a low: the Garden tile shows ~% and OK', async ({ page }) => {
    await resetSites([home], [peppers], [], null, [peppersState], thresholds);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto(`/garden/${peppers.id}/calibrate`);
    await page.waitForLoadState('networkidle');

    await pushCalibrationReading(peppersSoil, { readingSeq: 41, rawValue: 3000, measuredAt: '2026-10-06T05:18:00.000Z' });
    await expect(page.getByRole('button', { name: 'Record dry' })).toBeEnabled({ timeout: 10_000 });
    await page.getByRole('button', { name: 'Record dry' }).click();
    await expect(page.locator('[data-step="wet"]')).toBeVisible();
    await pushCalibrationReading(peppersSoil, { readingSeq: 42, rawValue: 1200, measuredAt: '2026-10-06T05:19:00.000Z' });
    await expect(page.getByRole('button', { name: 'Record wet' })).toBeEnabled({ timeout: 10_000 });
    await page.getByRole('button', { name: 'Record wet' }).click();
    await expect(page.locator('[data-step="confirm"]')).toBeVisible();

    // The first calibrated Reading is stored: the Lot is OK at about 40 %.
    await patchLot(peppers.id, { status: 'ok', moisturePercent: 40, lowThresholdPercent: 30, sensors: [reading('soil_moisture', 40, '%', peppersSoil, true)] });
    await page.getByRole('link', { name: 'Set Thresholds' }).click();
    await expect(page).toHaveURL(/\/thresholds$/u);
    await page.waitForLoadState('networkidle');
    await page.getByLabel('Low for Soil moisture').fill('25');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${peppers.id}$`, 'u'));

    await page.goto('/garden');
    const tile = page.getByRole('link', { name: /^Peppers/u });
    await expect(tile).toContainText('OK');
    await expect(tile).toContainText('~40');
    await expect(tile).toContainText('low 25');
  });
});
