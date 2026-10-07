import { expect, test, type Page } from '@playwright/test';
import type { FakeHistoryDay, FakeLot, FakeSensorReading } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, failReads, largestText, nothingClipped, resetSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// Site IDs no other spec uses: the web app keeps the last good Lots per user and Site for the run.
const homeId = '0192a000-0000-7000-8000-0000000048a1';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;

/** The browser's clock: 07:17 in Zurich on 6 October, so every time, duration and chart date is fixed. */
const now = new Date('2026-10-06T05:17:00.000Z');

const reading = (quantity: FakeSensorReading['quantity'], value: number, unit: FakeSensorReading['unit']): FakeSensorReading => ({ quantity, value, unit, measuredAt: '2026-10-06T05:02:00.000Z' });
const allSensors = [reading('soil_moisture', 1840, 'raw'), reading('air_temperature', 14.4, '°C'), reading('relative_humidity', 78.2, '%'), reading('gas_resistance', 142.37, 'kΩ')];

/** Daily bars for the 30 UTC days up to 6 October with gaps: every third day has no Readings. */
function days(base: number, spread: number, step: number): readonly FakeHistoryDay[] {
  const result: FakeHistoryDay[] = [];
  for (let offset = 29; offset >= 0; offset--) {
    if (offset % 3 === 2) {
      continue;
    }
    const day = new Date(Date.UTC(2026, 9, 6) - offset * 86_400_000).toISOString().slice(0, 10);
    const low = base + ((offset * 7) % 11) * step;
    result.push({ day, low, high: low + spread, readingCount: 96 });
  }
  return result;
}

const history = {
  soil_moisture: days(1500, 400, 60),
  air_temperature: days(6, 9, 1),
  relative_humidity: days(55, 20, 2),
  gas_resistance: days(120, 40, 5),
};

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000048d${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T03:45:00.000Z', ...fields };
}

const node = (id: string, battery: number | undefined, charging: 'charging' | 'notCharging' | undefined) => ({
  deviceId: id,
  ...(battery === undefined ? {} : { batteryPercent: battery }),
  ...(charging === undefined ? {} : { charging }),
  lastSeenAt: '2026-10-06T05:02:00.000Z',
});

const tomatoes = lot(1, 'Tomatoes', 'ok', { claimed: true, lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 35.2, lowThresholdPercent: 25, node: node('7c19000000000001', 62, 'charging'), sensors: allSensors, history });
const cucumbers = lot(2, 'Cucumbers', 'needsWater', { claimed: true, lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 15, lowThresholdPercent: 30, node: node('7c19000000000002', 80, 'notCharging'), sensors: allSensors, history });
const peppers = lot(3, 'Peppers', 'needsCalibration', { claimed: true, statusSince: '2026-10-05T16:00:00.000Z', lastReadingAt: '2026-10-06T05:02:00.000Z', node: node('7c19000000000003', 14, 'notCharging'), sensors: allSensors, history });
const beans = lot(4, 'Beans', 'unknown', { claimed: true, unknownCause: 'node', statusSince: '2026-10-06T05:05:00.000Z', lastReadingAt: '2026-10-05T23:05:00.000Z', node: { deviceId: '7c19000000000004' }, sensors: [reading('soil_moisture', 1700, 'raw')], history: { soil_moisture: history.soil_moisture } });
const lettuce = lot(5, 'Lettuce', 'paused', { claimed: true, pausedBy: ['device', 'site'], node: node('7c19000000000005', 55, 'charging'), sensors: allSensors, history });
const potatoes = lot(6, 'Potatoes', 'noNode');
const lots = [tomatoes, cucumbers, peppers, beans, lettuce, potatoes];

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

function sensorCells(page: Page) {
  return page.locator('.cf-cells--sensors .cf-cell');
}

async function openLot(page: Page, target: FakeLot): Promise<void> {
  await page.goto(`/garden/${target.id}`);
  await expect(page.getByRole('heading', { level: 1, name: target.name })).toBeVisible();
  // Hydrated: the chart answers taps only once the page runs in the browser.
  await page.waitForLoadState('networkidle');
}

/** Stale mode shows the time of the last successful read, which is this run's: fixed afterwards for the screenshots. */
async function fixStaleTimes(page: Page): Promise<void> {
  await page.evaluate(() => {
    const age = document.querySelector('.cf-stale-header__age');
    const detail = document.querySelector('.cf-stale-header__detail');
    const asOf = document.querySelector('.cf-hero__since');
    if (age === null || detail === null || asOf === null) {
      throw new Error('No stale header.');
    }
    age.textContent = '2 h 12 min old';
    detail.textContent = detail.textContent.replace(/^Last data [^.]+\./u, 'Last data 7:02 AM.');
    asOf.textContent = 'as of 7:02 AM';
  });
}

test.describe('Lot detail', () => {
  for (const theme of themes) {
    test(`UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 UX-DR78 UX-DR98 every state: hero, 3-up Sensor cells, 30-day chart, 2-up Device cells (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await prepare(page, theme);
      await signInTo(page);

      // Live with percentage: neutral hero, ~35, low 25 % and the ±5 % reading time.
      await openLot(page, tomatoes);
      const hero = page.locator('.cf-hero');
      await expect(hero).toContainText('OK');
      await expect(hero).toContainText('since 5:45 AM');
      await expect(hero.locator('.cf-hero__value')).toHaveText('~35 %');
      await expect(hero.locator('.cf-hero__meta')).toHaveText(/low 25%\s*±5 % · 7:02 AM/u);
      await expect(hero.locator('[data-icon="checkmark--outline"]')).toBeVisible();
      await expect(hero).toHaveAccessibleName('Tomatoes, OK, about 35 percent, low 25 percent');
      await expect(page.locator('.cf-hero--orange')).toHaveCount(0);

      // The Sensor cells: label over value, the Reading time, in the Server's units.
      await expect(sensorCells(page)).toHaveText([
        /Soil moisture\s*raw 1,840\s*7:02 AM/u,
        /Temperature\s*14 °C\s*7:02 AM/u,
        /Humidity\s*78 %\s*7:02 AM/u,
        /Air \(gas\)\s*142 kΩ\s*7:02 AM/u,
      ]);
      expect(await page.locator('.cf-cells--sensors').evaluate((list) => getComputedStyle(list).gridTemplateColumns.split(' ').length)).toBe(3);

      // The chart: bars only for days with Readings, a text summary, no Threshold band.
      await expect(page.locator('.cf-chart__bar')).toHaveCount(history.soil_moisture.length);
      await expect(page.locator('.cf-chart__plot')).toHaveAttribute('aria-label', /^Soil moisture, 30 days, lowest raw [\d,]+ on [A-Z][a-z]{2} \d+, \d+ days with Readings\.$/u);
      await expect(page.locator('.cf-chart__axis')).toHaveText(/Sep 7\s*Oct 6/u);

      // The Device cells: battery with charging, last seen with the cadence.
      const device = page.locator('.cf-cells--device .cf-cell');
      await expect(device).toHaveText([/Node 7c19000000000001\s*62 %\s*charging/u, /Last seen\s*7:02 AM\s*every 15 min/u]);
      expect(await page.locator('.cf-cells--device').evaluate((list) => getComputedStyle(list).gridTemplateColumns.split(' ').length)).toBe(2);
      await expect(page.locator('.cf-cells--device [data-icon="battery--low"]')).toHaveCount(0);

      await axeClean(page, `Lot detail, ok (${theme})`);
      await nothingClipped(page, `Lot detail, ok (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-ok-${theme}.png`, { fullPage: true });

      // Needs water: the hero is orange, and only then.
      await openLot(page, cucumbers);
      await expect(page.locator('.cf-hero--orange')).toHaveCount(1);
      await expect(page.locator('.cf-hero')).toContainText('Needs water');
      await expect(page.locator('.cf-hero__value')).toHaveText('~15 %');
      await expect(page.locator('.cf-hero [data-icon="rain-drop"]')).toBeVisible();
      await axeClean(page, `Lot detail, needs water (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-needs-water-${theme}.png`, { fullPage: true });

      // Needs calibration: the raw count, never a percentage; a low battery shows battery--low.
      await openLot(page, peppers);
      await expect(page.locator('.cf-hero')).toContainText('Needs Calibration');
      await expect(page.locator('.cf-hero__value')).toHaveText('raw 1,840');
      await expect(page.locator('.cf-hero__note')).toHaveText('no % until calibrated');
      await expect(page.locator('.cf-hero__value')).not.toContainText('%');
      await expect(page.locator('.cf-hero .cf-hatch')).toHaveCount(1);
      await expect(page.locator('.cf-cells--device [data-icon="battery--low"]')).toBeVisible();
      await expect(page.locator('.cf-cells--device .cf-cell').first()).toContainText('14 %');
      await axeClean(page, `Lot detail, needs calibration (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-needs-calibration-${theme}.png`, { fullPage: true });

      // Unknown by its Node: how long, what to do, missing optionals as a dash; one Sensor, so no picker.
      await openLot(page, beans);
      await expect(page.locator('.cf-hero')).toContainText('Silent · unknown');
      await expect(page.locator('.cf-hero__value')).toHaveText('6 h');
      await expect(page.locator('.cf-hero__note')).toHaveText('Check power or range.');
      await expect(page.locator('.cf-cells--device .cf-cell').first()).toContainText('—');
      await expect(page.locator('.cf-segmented')).toHaveCount(0);
      await axeClean(page, `Lot detail, unknown (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-unknown-${theme}.png`, { fullPage: true });

      // Paused by the Site: the Owner is told how to resume.
      await openLot(page, lettuce);
      await expect(page.locator('.cf-hero')).toContainText('Paused by Site');
      await expect(page.locator('.cf-hero__note')).toHaveText(['Paused with the Site', 'Resume the Site to resume this Node']);
      await axeClean(page, `Lot detail, paused (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-paused-${theme}.png`, { fullPage: true });

      // No Node: an empty detail that sends the Member or Admin to the mobile app.
      await openLot(page, potatoes);
      await expect(page.locator('.cf-hero')).toContainText('No Node');
      await expect(page.locator('#cf-lot-no-node')).toHaveText('Add a Node from the mobile app.');
      await expect(page.locator('.cf-cell, .cf-chart')).toHaveCount(0);
      await axeClean(page, `Lot detail, no Node (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-no-node-${theme}.png`, { fullPage: true });
    });

    test(`UX-DR19 UX-DR79 UX-DR63 the Server stops answering: the stale header, and no live value in the hero, cells or Device cells (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await prepare(page, theme);
      await signInTo(page);
      await openLot(page, tomatoes);
      await expect(sensorCells(page).first()).toContainText('raw 1,840');

      await failReads('all');
      // Stale mode shows the time of the last successful read, which is this run's: the browser clock must agree
      // with it, or the time would be shown as a date.
      await page.clock.setFixedTime(new Date());
      await page.reload();

      await expect(page.locator('.cf-stale-header__title')).toHaveText("Home garden · can't reach your Server");
      await expect(page.locator('.cf-hero')).toContainText('Was OK');
      await expect(page.locator('.cf-hero__value')).toHaveCount(0);
      await expect(page.locator('.cf-hero__since')).toHaveText(/^as of \d{1,2}:\d{2} [AP]M$/u);
      await expect(page.locator('.cf-hero [data-icon="cloud--offline"]')).toBeVisible();
      await expect(page.locator('main')).not.toContainText(/raw 1,840|14 °C|78 %|142 kΩ|62 %|~35/u);
      await expect(sensorCells(page)).toHaveText([/Soil moisture\s*—/u, /Temperature\s*—/u, /Humidity\s*—/u, /Air \(gas\)\s*—/u]);

      await fixStaleTimes(page);
      await axeClean(page, `Lot detail, stale (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-stale-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR63 a Lot tile opens its Lot detail, the no-Node tile too, and Back returns to Garden', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await signInTo(page);

    await page.getByRole('link', { name: /^Tomatoes, OK/u }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${tomatoes.id}$`, 'u'));
    await expect(page.getByRole('heading', { level: 1, name: 'Tomatoes' })).toBeVisible();

    await page.getByRole('link', { name: 'Back to Garden' }).click();
    await expect(page).toHaveURL(/\/garden$/u);
    await page.getByRole('link', { name: 'Potatoes, no Node, add a Node' }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${potatoes.id}$`, 'u'));
    await expect(page.locator('#cf-lot-no-node')).toHaveText('Add a Node from the mobile app.');
  });

  test('UX-DR33 a tap on a bar shows that day, the Sensor picker switches the quantity, and keys move between days', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await openLot(page, tomatoes);

    const plot = page.locator('.cf-chart__plot');
    const readout = page.locator('.cf-chart__readout');
    // Before anything is picked the readout names the newest day.
    await expect(readout).toHaveText(/^Oct 6: lowest raw [\d,]+$/u);
    const first = await page.locator('.cf-chart__bar').first().getAttribute('data-day');
    await plot.scrollIntoViewIfNeeded();
    const box = await plot.boundingBox();
    expect(box).not.toBeNull();
    // A tap on the far left picks the oldest bar.
    await page.mouse.click((box?.x ?? 0) + 3, (box?.y ?? 0) + (box?.height ?? 0) / 2);
    const picked = page.locator('.cf-chart__bar--picked');
    await expect(picked).toHaveCount(1);
    await expect(picked).toHaveAttribute('data-day', first ?? '');
    await expect(readout).toHaveText(/^Sep \d+: lowest raw [\d,]+$/u);
    await plot.focus();
    await page.keyboard.press('ArrowRight');
    await expect(picked).not.toHaveAttribute('data-day', first ?? '');

    // The picker switches to temperature: min and max in the readout, a new summary label.
    await page.getByRole('button', { name: 'Temperature' }).click();
    await expect(page.getByRole('button', { name: 'Temperature' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('button', { name: 'Soil moisture' })).toHaveAttribute('aria-pressed', 'false');
    await expect(readout).toHaveText(/^Oct 6: \d+ °C to \d+ °C$/u);
    await expect(plot).toHaveAttribute('aria-label', /^Temperature, 30 days, lowest \d+ °C on/u);
    await expect(page.locator('.cf-chart__bar')).toHaveCount(history.air_temperature.length);
    // Days without Readings are gaps: no bar for them.
    const slots = new Set(await page.locator('.cf-chart__bar').evaluateAll((bars) => bars.map((bar) => bar.getAttribute('data-day'))));
    expect(slots.has('2026-10-04')).toBe(false);
  });

  test('UX-DR63 UX-DR126 at the largest text the cells go one column and nothing is clipped', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await openLot(page, tomatoes);
    await page.setViewportSize({ width: 360, height: 800 });
    await largestText(page);

    expect(await page.locator('.cf-cells--sensors').evaluate((list) => getComputedStyle(list).gridTemplateColumns.split(' ').length)).toBe(1);
    expect(await page.locator('.cf-cells--device').evaluate((list) => getComputedStyle(list).gridTemplateColumns.split(' ').length)).toBe(1);
    await nothingClipped(page, 'Lot detail at the largest text');
    await axeClean(page, 'Lot detail at the largest text');
  });

  test('UX-DR27 a Member is not told to resume the Site', async ({ page }) => {
    await resetSites([{ ...home, role: 'Member' }], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await openLot(page, lettuce);
    await expect(page.locator('.cf-hero__note')).toHaveText(['Paused with the Site']);
    await expect(page.locator('main').getByRole('button', { name: /Resume|Pause|Calibrate|Thresholds/u })).toHaveCount(0);
  });

  test('UX-DR63 a Lot the Site does not have is a notice, not a hero', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await page.goto('/garden/0192a000-0000-7000-8000-000000000999');
    await expect(page.locator('#cf-lot-notice')).toContainText('Home garden has no such Lot.');
    await expect(page.locator('.cf-hero')).toHaveCount(0);
  });
});
