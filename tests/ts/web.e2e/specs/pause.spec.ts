import { expect, test, type Locator, type Page } from '@playwright/test';
import type { FakeDevice, FakeHistoryDay, FakeLot, FakeSensorReading, FakeSite } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, failNextPause, nothingClipped, pauseCalls, resetSites, setMode, signInButton, useTheme } from './helpers.ts';

/**
 * Story 8.2: Pause and Resume in the web app. The Pause modal from the Site menu, Lot detail and a Devices
 * row; the paused Site on the overview; paused Devices rows; Resume in one tap; the refusals; and what a
 * Member sees. Each new or changed paused surface is compared in the light and the dark theme.
 */
const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// A Site ID no other spec uses: the web app keeps the last good Lots per user and Site for the whole run.
const homeId = '0192a000-0000-7000-8000-0000000082a1';
const home: FakeSite = { id: homeId, name: 'Home garden', role: 'Owner' };
const hubId = '3f2a9c0d1e4b5a67';
const otherHubId = '1b00aa11bb22cc33';
const herbsNodeId = '7c19000000000001';
const tomatoesNodeId = '9c0d1e4b5a673f2a';

/** The browser's clock: 07:17 in Zurich on 6 October, so every time on the tiles, the hero and the chart is fixed. */
const now = new Date('2026-10-06T05:17:00.000Z');

/**
 * The next 1 March that lies ahead of this run, as the date field gives it: the fake Server, like the real
 * one, refuses an end that has passed. The pages show the day without its year, so the screenshots stay.
 */
function nextMarch(): string {
  const today = new Date().toISOString().slice(0, 10);
  const year = new Date().getUTCFullYear();
  const thisYear = `${String(year)}-03-01`;
  return thisYear > today ? thisYear : `${String(year + 1)}-03-01`;
}

const until = nextMarch();
const untilInstant = `${until}T00:00:00.000Z`;

const reading = (quantity: FakeSensorReading['quantity'], value: number, unit: FakeSensorReading['unit']): FakeSensorReading => ({ quantity, value, unit, measuredAt: '2026-10-06T05:02:00.000Z' });
const sensors = [reading('soil_moisture', 1840, 'raw'), reading('air_temperature', 14.4, '°C'), reading('relative_humidity', 78.2, '%')];

function days(): readonly FakeHistoryDay[] {
  return Array.from({ length: 10 }, (_unused, index) => {
    const day = new Date(Date.UTC(2026, 9, 6) - (9 - index) * 86_400_000).toISOString().slice(0, 10);
    return { day, low: 1500 + ((index * 7) % 5) * 80, high: 2100, readingCount: 96 };
  });
}

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000082b${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T03:45:00.000Z', ...fields };
}

const herbs = lot(1, 'Herbs', 'ok', {
  claimed: true,
  lastReadingAt: '2026-10-06T05:02:00.000Z',
  moisturePercent: 35.2,
  lowThresholdPercent: 25,
  node: { deviceId: herbsNodeId, batteryPercent: 62, charging: 'charging', lastSeenAt: '2026-10-06T05:02:00.000Z' },
  sensors,
  history: { soil_moisture: days() },
});
const tomatoes = lot(2, 'Tomatoes', 'needsWater', { claimed: true, lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 21.7, lowThresholdPercent: 30 });
const potatoes = lot(3, 'Potatoes', 'noNode');
const lots: readonly FakeLot[] = [herbs, tomatoes, potatoes];

/** Two Hubs, and the Nodes of Herbs and Tomatoes. No Device was seen yet, so no row shows a time of this run. */
const devices: readonly FakeDevice[] = [
  { id: hubId, siteId: homeId, kind: 'hub', online: false },
  { id: otherHubId, siteId: homeId, kind: 'hub', online: false },
  { id: herbsNodeId, siteId: homeId, kind: 'node', online: false, lotId: herbs.id, lotName: 'Herbs', batteryPercent: 62, charging: 'charging' },
  { id: tomatoesNodeId, siteId: homeId, kind: 'node', online: false, lotId: tomatoes.id, lotName: 'Tomatoes', batteryPercent: 80, charging: 'notCharging' },
];

const pausedSite: FakeSite = { ...home, paused: true, pausedUntil: untilInstant };
const line = 'No Alerts are sent while the Site is paused.';
const memberNotice = 'Only Owners and Administrators can resume it.';
const forbidden = "You can't change this on Home garden. Ask an Owner or Administrator.";
const unreachable = "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.";

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Lots or Devices.
  await resetSites();
});

async function prepare(page: Page, theme: 'light' | 'dark' = 'light'): Promise<void> {
  await useTheme(page, theme, appUrl);
  await page.context().addCookies([{ name: 'cf_time_zone', value: zone, url: appUrl }]);
  await page.emulateMedia({ colorScheme: theme });
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.clock.setFixedTime(now);
}

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
  // Hydrated: the Site menu and the Pause modal answer only once the page runs in the browser.
  await page.waitForLoadState('networkidle');
}

async function open(page: Page, path: string, heading: string): Promise<void> {
  await page.goto(path);
  await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
  await page.waitForLoadState('networkidle');
}

function tile(page: Page, name: string): Locator {
  return page.locator('.cf-lot-tile', { has: page.locator('.cf-lot-tile__name', { hasText: new RegExp(`^${name}$`, 'u') }) });
}

function deviceRow(page: Page, id: string): Locator {
  return page.locator(`[data-device="${id}"]`);
}

function modal(page: Page): Locator {
  return page.getByRole('dialog', { name: 'Pause' });
}

function segments(page: Page): Locator {
  return modal(page).getByRole('group', { name: 'Pause scope' }).getByRole('button');
}

async function siteMenu(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
  return page.getByRole('menu', { name: 'Site menu for Home garden' });
}

/** Opens the Site menu and chooses Pause: the modal with Whole Site. */
async function openSitePause(page: Page): Promise<void> {
  const menu = await siteMenu(page);
  await menu.getByRole('menuitem', { name: 'Pause Home garden' }).click();
  await expect(modal(page)).toBeVisible();
}

const sitePath = `/sites/${homeId}/pause`;
const devicePath = (id: string): string => `/sites/${homeId}/devices/${id}/pause`;

test.describe('The Pause modal', () => {
  for (const theme of themes) {
    test(`UX-DR46 UX-DR22 from the Site menu: Whole Site alone, the Until date and its helper; a refused date marks the field (${theme})`, async ({ page }) => {
      await resetSites([home], lots, devices);
      await prepare(page, theme);
      await signInTo(page);

      const menu = await siteMenu(page);
      await expect(menu.getByRole('menuitem')).toHaveText(['Pause Home garden', 'Site settings']);
      await menu.getByRole('menuitem', { name: 'Pause Home garden' }).click();
      await expect(modal(page)).toBeVisible();
      await expect(page.getByRole('menu')).toHaveCount(0);

      // There is no Device here, so Whole Site is the only segment; the primary names the result.
      await expect(segments(page)).toHaveText(['Whole Site']);
      await expect(segments(page)).toHaveAttribute('aria-pressed', 'true');
      const date = modal(page).getByLabel('Until');
      await expect(date).toHaveAttribute('type', 'date');
      await expect(date).toHaveValue('');
      await expect(modal(page).getByText('Leave empty to pause until someone resumes.')).toBeVisible();
      await expect(modal(page).getByRole('button', { name: 'Cancel' })).toBeVisible();
      await expect(modal(page).getByRole('button', { name: 'Pause Home garden' })).toBeVisible();
      await expect(modal(page).locator('[disabled]')).toHaveCount(0);
      await axeClean(page, `Pause modal, Site (${theme})`);
      await expect(page).toHaveScreenshot(`pause-modal-site-${theme}.png`);

      // A date that has passed: the Server answers 400, the modal stays and says it under the field.
      await date.fill('2020-01-01');
      await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();
      await expect(modal(page).getByText('Choose a date after today.')).toBeVisible();
      await expect(modal(page)).toBeVisible();
      await expect(date).toHaveAttribute('aria-invalid', 'true');
      await expect(date).toHaveValue('2020-01-01');
      await expect(modal(page).getByText('Leave empty to pause until someone resumes.')).toHaveCount(0);
      await expect(page.getByRole('alert')).toHaveText('Choose a date after today.');
      expect(await pauseCalls()).toEqual([{ method: 'PUT', path: sitePath, body: { until: '2020-01-01' }, status: 400 }]);
      await axeClean(page, `Pause modal, refused date (${theme})`);
      await expect(page).toHaveScreenshot(`pause-modal-date-refused-${theme}.png`);

      // Cancel changes nothing, and the next opening starts empty.
      await modal(page).getByRole('button', { name: 'Cancel' }).click();
      await expect(modal(page)).toHaveCount(0);
      await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
      await openSitePause(page);
      await expect(modal(page).getByLabel('Until')).toHaveValue('');
      await expect(modal(page).getByText('Choose a date after today.')).toHaveCount(0);
    });

    test(`UX-DR46 UX-DR70 from a Devices row: This Device selected beside Whole Site, and the primary follows the scope (${theme})`, async ({ page }) => {
      await resetSites([home], lots, devices);
      await prepare(page, theme);
      await signInTo(page);
      await open(page, '/devices', 'Devices');

      await deviceRow(page, herbsNodeId).getByRole('button', { name: 'Pause' }).click();
      await expect(modal(page)).toBeVisible();
      await expect(segments(page)).toHaveText(['This Device', 'Whole Site']);
      await expect(segments(page).nth(0)).toHaveAttribute('aria-pressed', 'true');
      await expect(segments(page).nth(1)).toHaveAttribute('aria-pressed', 'false');
      await expect(modal(page).getByRole('button', { name: 'Pause 7C19' })).toBeVisible();
      await axeClean(page, `Pause modal, Device (${theme})`);
      await expect(page).toHaveScreenshot(`pause-modal-device-${theme}.png`);

      await segments(page).nth(1).click();
      await expect(segments(page).nth(1)).toHaveAttribute('aria-pressed', 'true');
      await expect(modal(page).getByRole('button', { name: 'Pause Home garden' })).toBeVisible();
      await expect(modal(page).getByRole('button', { name: 'Pause 7C19' })).toHaveCount(0);
      await segments(page).nth(0).click();
      await expect(modal(page).getByRole('button', { name: 'Pause 7C19' })).toBeVisible();
      // Nothing was sent by choosing: the primary button commits.
      expect(await pauseCalls()).toEqual([]);
    });
  }
});

test.describe('Pause and Resume of the Site', () => {
  for (const theme of themes) {
    test(`UX-DR70 UX-DR83 an Owner pauses the Site until a date: the headline, the line and every tile paused by the Site; Resume is one tap (${theme})`, async ({ page }) => {
      await resetSites([home], lots, devices);
      await prepare(page, theme);
      await signInTo(page);
      await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
      await expect(page.getByText(line)).toHaveCount(0);

      await openSitePause(page);
      await modal(page).getByLabel('Until').fill(until);
      await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();

      // The date goes out as the calendar date that was picked; the page then shows what the Server says.
      await expect(modal(page)).toHaveCount(0);
      expect(await pauseCalls()).toEqual([{ method: 'PUT', path: sitePath, body: { until }, status: 200 }]);
      const headline = page.getByRole('heading', { level: 2, name: 'Paused until Mar 1' });
      await expect(headline).toBeVisible();
      await expect(headline).toHaveClass(/cf-site-summary__headline--paused/u);
      await expect(page.locator('.cf-site-summary__line')).toHaveText(line);
      await expect(page.getByRole('status')).toHaveText('Home garden paused.');
      for (const name of ['Herbs', 'Tomatoes']) {
        await expect(tile(page, name)).toHaveText(new RegExp(`${name}\\s*Paused by Site\\s*—\\s*until Mar 1`, 'u'));
        await expect(tile(page, name)).toHaveClass(/cf-lot-tile--paused/u);
        await expect(tile(page, name).locator('[data-icon="pause--outline"]')).toBeVisible();
      }
      await expect(tile(page, 'Herbs')).toHaveAccessibleName('Herbs, paused until March 1');
      await expect(tile(page, 'Potatoes')).toHaveText(/Potatoes\s*No Node/u);
      // No attribution, no "OK", and an Owner needs no notice about who may resume.
      await expect(page.getByText(/Paused by \w+ \(/u)).toHaveCount(0);
      await expect(page.getByText(memberNotice)).toHaveCount(0);
      await axeClean(page, `overview, Site paused (${theme})`);
      await nothingClipped(page, `overview, Site paused (${theme})`);
      await expect(page).toHaveScreenshot(`overview-site-paused-${theme}.png`, { fullPage: true });

      // The menu now offers Resume in place of Pause: one tap, no modal.
      const menu = await siteMenu(page);
      await expect(menu.getByRole('menuitem')).toHaveText(['Resume Home garden', 'Site settings']);
      await menu.getByRole('menuitem', { name: 'Resume Home garden' }).click();
      await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.locator('.cf-site-summary__line')).toHaveCount(0);
      await expect(page.locator('.cf-lot-tile--paused')).toHaveCount(0);
      await expect(tile(page, 'Herbs')).toHaveText(/Herbs\s*OK/u);
      await expect(page.getByRole('status')).toHaveText('Home garden resumed.');
      expect((await pauseCalls()).at(-1)).toEqual({ method: 'DELETE', path: sitePath, body: {}, status: 200 });
      await expect((await siteMenu(page)).getByRole('menuitem')).toHaveText(['Pause Home garden', 'Site settings']);
    });
  }

  test('UX-DR70 UX-DR22 from any page, with no date: an empty body, and the headline reads Paused', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }], lots, devices);
    await prepare(page);
    await signInTo(page);
    await open(page, '/alerts', 'Alerts');

    await openSitePause(page);
    await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();
    await expect(modal(page)).toHaveCount(0);
    expect(await pauseCalls()).toEqual([{ method: 'PUT', path: sitePath, body: {}, status: 200 }]);
    // Still on Alerts: the browser posted to the web app, which called the Server.
    await expect(page).toHaveURL(/\/alerts$/u);
    await expect((await siteMenu(page)).getByRole('menuitem')).toHaveText(['Resume Home garden', 'Site settings']);
    await page.keyboard.press('Escape');

    await open(page, '/garden', 'Garden');
    await expect(page.getByRole('heading', { level: 2, name: 'Paused', exact: true })).toBeVisible();
    await expect(page.locator('.cf-site-summary__line')).toHaveText(line);
    await expect(tile(page, 'Herbs')).toHaveText(/Herbs\s*Paused by Site\s*—\s*paused/u);
  });

  test('UX-DR84 a Role lost meanwhile: the 403 notice in the modal, nothing changed', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }], lots, devices);
    await prepare(page);
    await signInTo(page);
    await openSitePause(page);

    await failNextPause(403);
    await modal(page).getByLabel('Until').fill(until);
    await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();

    await expect(modal(page).locator('#cf-site-pause-notice')).toHaveText(forbidden);
    await expect(modal(page)).toBeVisible();
    await expect(modal(page).getByRole('button', { name: 'Try again' })).toHaveCount(0);
    await expect(modal(page).getByLabel('Until')).toHaveValue(until);
    await modal(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    await expect(page.getByText(line)).toHaveCount(0);
  });

  test('UX-DR70 a Server that cannot be reached: the notice with Try again, and the modal keeps the entries', async ({ page }) => {
    await resetSites([home], lots, devices);
    await prepare(page);
    await signInTo(page);
    await openSitePause(page);

    await failNextPause('drop');
    await modal(page).getByLabel('Until').fill(until);
    await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();
    await expect(modal(page).locator('#cf-site-pause-notice')).toHaveText(new RegExp(`^${unreachable.replaceAll('.', '\\.')}\\s*Try again$`, 'u'));
    await expect(modal(page).getByLabel('Until')).toHaveValue(until);
    await expect(modal(page).getByLabel('Until')).not.toHaveAttribute('aria-invalid', 'true');

    // The Server answers again: Try again sends the same Pause.
    await modal(page).getByRole('button', { name: 'Try again' }).click();
    await expect(modal(page)).toHaveCount(0);
    await expect(page.getByRole('heading', { level: 2, name: 'Paused until Mar 1' })).toBeVisible();
    expect((await pauseCalls()).map((call) => [call.body, call.status])).toEqual([
      [{ until }, 0],
      [{ until }, 200],
    ]);

    // A Resume that gets no answer leaves its notice above the page, with Try again.
    await failNextPause('drop');
    await (await siteMenu(page)).getByRole('menuitem', { name: 'Resume Home garden' }).click();
    const notice = page.locator('#cf-site-resume-notice');
    await expect(notice).toHaveText(new RegExp(`^${unreachable.replaceAll('.', '\\.')}\\s*Try again$`, 'u'));
    await expect(page.getByRole('heading', { level: 2, name: 'Paused until Mar 1' })).toBeVisible();
    await notice.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    await expect(notice).toHaveCount(0);
  });
});

test.describe('Pause on Devices', () => {
  for (const theme of themes) {
    test(`UX-DR30 UX-DR83 UX-DR70 paused rows: Paused with Resume, and Paused by Site with the line and no Resume (${theme})`, async ({ page }) => {
      // The Hub and the Node of Herbs are paused by themselves; the Site is not.
      const own = devices.map((device) => (device.id === hubId ? { ...device, paused: true } : device.id === herbsNodeId ? { ...device, paused: true, pausedUntil: '2026-11-01T00:00:00.000Z' } : device));
      await resetSites([home], lots, own);
      await prepare(page, theme);
      await signInTo(page);
      await open(page, '/devices', 'Devices');

      const hub = deviceRow(page, hubId);
      await expect(hub.locator('.cf-devices__paused')).toHaveText('Paused');
      await expect(hub.locator('.cf-devices__paused')).toHaveCSS('text-transform', 'uppercase');
      await expect(hub.locator('.cf-devices__paused [data-icon="pause--outline"]')).toBeVisible();
      await expect(hub.getByRole('button', { name: 'Resume 3F2A' })).toBeVisible();
      await expect(hub.getByRole('button', { name: 'Pause' })).toHaveCount(0);
      const node = deviceRow(page, herbsNodeId);
      await expect(node.locator('.cf-devices__paused')).toHaveText('Paused');
      await expect(node.locator('.cf-devices__paused-until')).toHaveText('until Nov 1');
      await expect(node.getByRole('button', { name: 'Resume 7C19' })).toBeVisible();
      // A Device that is not paused has no paused state, and Pause.
      await expect(deviceRow(page, otherHubId).locator('.cf-devices__paused')).toHaveCount(0);
      await expect(deviceRow(page, otherHubId).getByRole('button', { name: 'Pause' })).toBeVisible();
      await expect(page.locator('.cf-devices__paused')).toHaveCount(2);
      await expect(page.getByText(/Resume the Site/u)).toHaveCount(0);
      await axeClean(page, `Devices, paused (${theme})`);
      await nothingClipped(page, `Devices, paused (${theme})`);
      await expect(page).toHaveScreenshot(`devices-paused-${theme}.png`, { fullPage: true });

      // The Site is paused too: every row reads Paused by Site. Only a Device's own Pause has Resume.
      await resetSites([pausedSite], lots, own);
      await open(page, '/devices', 'Devices');
      await expect(page.locator('.cf-devices__paused')).toHaveText(['Paused by Site', 'Paused by Site', 'Paused by Site', 'Paused by Site']);
      const siteHub = deviceRow(page, otherHubId);
      await expect(siteHub).toHaveAttribute('data-paused', 'site');
      await expect(siteHub.locator('.cf-devices__paused-until')).toHaveText('until Mar 1');
      await expect(siteHub.getByText('Resume the Site to resume this Hub')).toBeVisible();
      await expect(siteHub.getByRole('button', { name: /Resume/u })).toHaveCount(0);
      await expect(siteHub.getByRole('button', { name: 'Pause' })).toBeVisible();
      const siteNode = deviceRow(page, tomatoesNodeId);
      await expect(siteNode.getByText('Resume the Site to resume this Node')).toBeVisible();
      await expect(siteNode.getByRole('button', { name: /Resume/u })).toHaveCount(0);
      // Paused by both: Resume lifts the Device's own Pause, and the line is not said yet.
      await expect(deviceRow(page, hubId)).toHaveAttribute('data-paused', 'device site');
      await expect(deviceRow(page, hubId).getByRole('button', { name: 'Resume 3F2A' })).toBeVisible();
      await expect(deviceRow(page, hubId).getByText(/Resume the Site/u)).toHaveCount(0);
      await axeClean(page, `Devices, paused by Site (${theme})`);
      await nothingClipped(page, `Devices, paused by Site (${theme})`);
      await expect(page).toHaveScreenshot(`devices-paused-by-site-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR70 an Administrator pauses a Node from its row and resumes it in one tap; Whole Site from a row pauses the Site only', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }], lots, devices);
    await prepare(page);
    await signInTo(page);
    await open(page, '/devices', 'Devices');

    const node = deviceRow(page, herbsNodeId);
    await node.getByRole('button', { name: 'Pause' }).click();
    await modal(page).getByRole('button', { name: 'Pause 7C19' }).click();
    await expect(modal(page)).toHaveCount(0);
    await expect(node.locator('.cf-devices__paused')).toHaveText('Paused');
    await expect(node).toHaveAttribute('data-paused', 'device');
    await expect(page.getByRole('status')).toHaveText('7C19 paused.');
    await expect(page.locator('.cf-devices__paused')).toHaveCount(1);
    expect(await pauseCalls()).toEqual([{ method: 'PUT', path: devicePath(herbsNodeId), body: {}, status: 200 }]);

    await node.getByRole('button', { name: 'Resume 7C19' }).click();
    await expect(node.locator('.cf-devices__paused')).toHaveCount(0);
    await expect(node.getByRole('button', { name: 'Pause' })).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('status')).toHaveText('7C19 resumed.');
    expect((await pauseCalls()).at(-1)).toEqual({ method: 'DELETE', path: devicePath(herbsNodeId), body: {}, status: 200 });

    // A Hub has the same action; Whole Site chosen there leaves the Hub's own source alone.
    await deviceRow(page, hubId).getByRole('button', { name: 'Pause' }).click();
    await expect(modal(page).getByRole('button', { name: 'Pause 3F2A' })).toBeVisible();
    await segments(page).nth(1).click();
    await modal(page).getByLabel('Until').fill(until);
    await modal(page).getByRole('button', { name: 'Pause Home garden' }).click();
    await expect(modal(page)).toHaveCount(0);
    await expect(page.locator('.cf-devices__paused')).toHaveText(['Paused by Site', 'Paused by Site', 'Paused by Site', 'Paused by Site']);
    await expect(deviceRow(page, hubId)).toHaveAttribute('data-paused', 'site');
    expect((await pauseCalls()).at(-1)).toEqual({ method: 'PUT', path: sitePath, body: { until }, status: 200 });
  });

  test('UX-DR70 UX-DR84 a Device that is gone, a Role lost and a Server out of reach each leave their notice, and nothing changes', async ({ page }) => {
    await resetSites([home], lots, devices);
    await prepare(page);
    await signInTo(page);
    await open(page, '/devices', 'Devices');

    // The Node was removed after the page loaded: 404.
    await deviceRow(page, tomatoesNodeId).getByRole('button', { name: 'Pause' }).click();
    await resetSites(
      [home],
      lots,
      devices.filter((device) => device.id !== tomatoesNodeId),
    );
    await modal(page).getByLabel('Until').fill(until);
    await modal(page).getByRole('button', { name: 'Pause 9C0D' }).click();
    await expect(modal(page).locator('#cf-device-pause-notice')).toHaveText('That Device is gone. Reload the page.');
    await expect(modal(page).getByLabel('Until')).toHaveValue(until);
    await modal(page).getByRole('button', { name: 'Cancel' }).click();

    // Resume of a paused Hub: first a 403, then no answer, then it works.
    await resetSites(
      [home],
      lots,
      devices.map((device) => (device.id === hubId ? { ...device, paused: true } : device)),
    );
    await open(page, '/devices', 'Devices');
    const hub = deviceRow(page, hubId);
    await failNextPause(403);
    await hub.getByRole('button', { name: 'Resume 3F2A' }).click();
    const notice = page.locator(`#cf-resume-${hubId}-notice`);
    await expect(notice).toHaveText(forbidden);
    await expect(hub.locator('.cf-devices__paused')).toHaveText('Paused');
    await failNextPause('drop');
    await hub.getByRole('button', { name: 'Resume 3F2A' }).click();
    await expect(notice).toHaveText(new RegExp(`^${unreachable.replaceAll('.', '\\.')}\\s*Try again$`, 'u'));
    await hub.getByRole('button', { name: 'Try again' }).click();
    await expect(hub.locator('.cf-devices__paused')).toHaveCount(0);
    await expect(hub.getByRole('button', { name: 'Pause' })).toBeVisible();
  });
});

test.describe('Pause on Lot detail', () => {
  for (const theme of themes) {
    test(`UX-DR70 UX-DR78 a Lot paused by its Node offers Resume; paused by the Site only it says to resume the Site, and Pause stays (${theme})`, async ({ page }) => {
      const own = devices.map((device) => (device.id === herbsNodeId ? { ...device, paused: true, pausedUntil: '2026-11-01T00:00:00.000Z' } : device));
      await resetSites([home], lots, own);
      await prepare(page, theme);
      await signInTo(page);
      await open(page, `/garden/${herbs.id}`, 'Herbs');

      const hero = page.locator('.cf-hero');
      await expect(hero).toHaveClass(/cf-hero--paused/u);
      await expect(hero.locator('.cf-hero__label')).toHaveText('Paused');
      await expect(hero.locator('.cf-hero__note')).toHaveText(['Paused until Nov 1']);
      const actions = page.getByRole('group', { name: `Actions for Node ${herbsNodeId}` });
      await expect(actions.getByRole('button')).toHaveText(['Resume 7C19']);
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await axeClean(page, `Lot detail, paused with Resume (${theme})`);
      await nothingClipped(page, `Lot detail, paused with Resume (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-paused-resume-${theme}.png`, { fullPage: true });

      // The Site is paused and the Node is not: no Resume here, the line how to resume, and Pause for the Node.
      await resetSites([pausedSite], lots, devices);
      await open(page, `/garden/${herbs.id}`, 'Herbs');
      await expect(hero.locator('.cf-hero__label')).toHaveText('Paused by Site');
      await expect(hero.locator('.cf-hero__note')).toHaveText(['Paused until Mar 1', 'Paused with the Site', 'Resume the Site to resume this Node']);
      await expect(actions.getByRole('button')).toHaveText(['Pause']);
      await expect(page.getByRole('button', { name: /^Resume/u })).toHaveCount(0);
      await axeClean(page, `Lot detail, paused by Site (${theme})`);
      await nothingClipped(page, `Lot detail, paused by Site (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-paused-by-site-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR70 an Owner pauses the Node of a Lot until a date from Lot detail, and resumes it in one tap', async ({ page }) => {
    await resetSites([home], lots, devices);
    await prepare(page);
    await signInTo(page);
    await tile(page, 'Herbs').click();
    await expect(page.getByRole('heading', { level: 1, name: 'Herbs' })).toBeVisible();
    await page.waitForLoadState('networkidle');

    const actions = page.getByRole('group', { name: `Actions for Node ${herbsNodeId}` });
    await actions.getByRole('button', { name: 'Pause' }).click();
    await expect(segments(page)).toHaveText(['This Device', 'Whole Site']);
    await expect(segments(page).nth(0)).toHaveAttribute('aria-pressed', 'true');
    await modal(page).getByLabel('Until').fill(until);
    await modal(page).getByRole('button', { name: 'Pause 7C19' }).click();

    await expect(modal(page)).toHaveCount(0);
    await expect(page.locator('.cf-hero__label')).toHaveText('Paused');
    await expect(page.locator('.cf-hero__note')).toHaveText(['Paused until Mar 1']);
    expect(await pauseCalls()).toEqual([{ method: 'PUT', path: devicePath(herbsNodeId), body: { until }, status: 200 }]);

    await actions.getByRole('button', { name: 'Resume 7C19' }).click();
    await expect(page.locator('.cf-hero__label')).toHaveText('OK');
    await expect(actions.getByRole('button')).toHaveText(['Pause']);
    expect((await pauseCalls()).at(-1)).toEqual({ method: 'DELETE', path: devicePath(herbsNodeId), body: {}, status: 200 });
  });
});

test.describe('A Member and Pause', () => {
  const allotment: FakeSite = { ...pausedSite, role: 'Member' };

  for (const theme of themes) {
    test(`UX-DR84 UX-DR83 a Member sees the paused Site and a paused Lot with the one Inline notice, and no Pause or Resume (${theme})`, async ({ page }) => {
      await resetSites([allotment], lots, devices);
      await prepare(page, theme);
      await signInTo(page);

      await expect(page.getByRole('heading', { level: 2, name: 'Paused until Mar 1' })).toBeVisible();
      await expect(page.locator('.cf-site-summary__line')).toHaveText(line);
      await expect(page.locator('#cf-site-paused-notice')).toHaveText(memberNotice);
      await expect(page.getByText(memberNotice)).toHaveCount(1);
      await expect(tile(page, 'Herbs')).toHaveText(/Herbs\s*Paused by Site/u);
      // Hidden, never disabled: the menu has Site settings alone, and the page no control.
      await expect((await siteMenu(page)).getByRole('menuitem')).toHaveText(['Site settings']);
      await page.keyboard.press('Escape');
      await expect(page.locator('main').locator('form, button, input, dialog, [disabled]')).toHaveCount(0);
      await expect(page.locator('dialog')).toHaveCount(0);
      await axeClean(page, `overview, paused, Member (${theme})`);
      await nothingClipped(page, `overview, paused, Member (${theme})`);
      await expect(page).toHaveScreenshot(`overview-paused-member-${theme}.png`, { fullPage: true });

      await tile(page, 'Herbs').click();
      await expect(page.getByRole('heading', { level: 1, name: 'Herbs' })).toBeVisible();
      await page.waitForLoadState('networkidle');
      await expect(page.locator('.cf-hero__note')).toHaveText(['Paused until Mar 1', 'Paused with the Site']);
      await expect(page.locator('#cf-lot-paused-notice')).toHaveText(memberNotice);
      await expect(page.getByText(memberNotice)).toHaveCount(1);
      await expect(page.getByRole('button', { name: /Pause|Resume/u })).toHaveCount(0);
      await expect(page.getByText(/Resume the Site/u)).toHaveCount(0);
      await expect(page.locator('main').locator('form, dialog, [disabled]')).toHaveCount(0);
      await axeClean(page, `Lot detail, paused, Member (${theme})`);
      await nothingClipped(page, `Lot detail, paused, Member (${theme})`);
      await expect(page).toHaveScreenshot(`lot-detail-paused-member-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR84 a Member sees the paused Devices rows and no Pause or Resume there', async ({ page }) => {
    await resetSites([allotment], lots, devices);
    await prepare(page);
    await signInTo(page);
    await open(page, '/devices', 'Devices');

    await expect(page.locator('.cf-devices__paused')).toHaveText(['Paused by Site', 'Paused by Site', 'Paused by Site', 'Paused by Site']);
    await expect(page.getByText(/Resume/u)).toHaveCount(0);
    await expect(page.locator('main').locator('form, button, input, select, textarea, details, dialog, [disabled]')).toHaveCount(0);
  });
});
