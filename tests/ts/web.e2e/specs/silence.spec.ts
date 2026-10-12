import { expect, test, type Locator, type Page } from '@playwright/test';
import type { FakeDevice, FakeHealthAlert, FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, liveConnections, nothingClipped, resetSites, sendLive, serverSites, setAlerts, setMode, signInButton, useTheme } from './helpers.ts';

/**
 * Story 7.2: silence in the web app. A Hub-silent notice above the tiles, silent Device rows with the
 * Silence Window row action, and the Health Alert rows, each in the light and the dark theme.
 */
const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// A Site ID no other spec uses: the web app keeps the last good Lots per user and Site for the whole run.
const homeId = '0192a000-0000-7000-8000-0000000072a1';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const hubId = '3f2a9c0d1e4b5a67';
const heardHubId = '1b00aa11bb22cc33';
const beansNodeId = '9c0d1e4b5a673f2a';
const spareNodeId = '7c19000000000003';

/** The browser's clock on the overview: 07:10 in Zurich, so every duration on the tiles and in the notice is fixed. */
const now = new Date('2026-10-06T05:10:00.000Z');

const minute = 60_000;
const hour = 60 * minute;

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Lots, Devices or Alerts.
  await resetSites();
});

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000072b${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T05:03:00.000Z', ...fields };
}

/** The Node of Beans was last heard at 01:05; the Hub behind Peas and Lettuce sent its last heartbeat 12 min ago. Seeded out of order. */
const overviewLots: readonly FakeLot[] = [
  lot(1, 'Herbs', 'ok', { lastReadingAt: '2026-10-06T05:03:00.000Z', moisturePercent: 35.2, lowThresholdPercent: 25 }),
  lot(2, 'Beans', 'unknown', { unknownCause: 'node', statusSince: '2026-10-06T05:05:00.000Z', lastReadingAt: '2026-10-05T23:05:00.000Z', silentSince: '2026-10-05T23:05:00.000Z', moisturePercent: 41 }),
  lot(3, 'Peas', 'unknown', { unknownCause: 'hub', silentHubId: hubId, silentSince: '2026-10-06T04:58:00.000Z', lastReadingAt: '2026-10-06T04:50:00.000Z', moisturePercent: 38 }),
  lot(4, 'Lettuce', 'unknown', { unknownCause: 'hub', silentHubId: hubId, silentSince: '2026-10-06T04:58:00.000Z' }),
  lot(5, 'Tomatoes', 'ok', { lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 52 }),
];

const overviewDevices: readonly FakeDevice[] = [
  { id: hubId, siteId: homeId, kind: 'hub', online: false, lastSeenAt: '2026-10-06T04:58:00.000Z', silentSince: '2026-10-06T04:58:00.000Z' },
  { id: heardHubId, siteId: homeId, kind: 'hub', online: true, lastSeenAt: '2026-10-06T05:09:30.000Z' },
];

const hubNotice = "Hub 3F2A silent for 12 min — Lots behind it can't be read.";

/**
 * Devices and Alerts tell a silence against the web app's own clock, which is the clock of this run:
 * a time this long ago, with room for the test to load the page before the next whole unit.
 */
function ago(milliseconds: number): string {
  return new Date(Date.now() - milliseconds).toISOString();
}

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

const devicesLots: readonly FakeLot[] = [lot(1, 'Beans', 'unknown', { unknownCause: 'node', claimed: true }), lot(2, 'Herbs', 'ok', { claimed: true }), lot(3, 'Basil', 'noNode')];

/** A silent Hub (12 min), a silent Node on Beans with its own 8 h window (6 h), and a Hub and a Node that are heard. */
function silentDevices(): FakeDevice[] {
  const hubSince = ago(12 * minute + 20_000);
  const nodeSince = ago(6 * hour + 30 * minute);
  return [
    { id: hubId, siteId: homeId, kind: 'hub', online: false, lastSeenAt: hubSince, silentSince: hubSince },
    { id: heardHubId, siteId: homeId, kind: 'hub', online: true, lastSeenAt: todayAt('07:02') },
    { id: beansNodeId, siteId: homeId, kind: 'node', online: false, lotId: devicesLots[0]?.id ?? '', lotName: 'Beans', lastSeenAt: nodeSince, silentSince: nodeSince, batteryPercent: 14, charging: 'notCharging', silenceWindowSeconds: 28_800 },
    { id: '7c19000000000002', siteId: homeId, kind: 'node', online: false, lotId: devicesLots[1]?.id ?? '', lotName: 'Herbs', lastSeenAt: todayAt('07:02'), batteryPercent: 62, charging: 'charging' },
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

function tiles(page: Page): Locator {
  return page.getByRole('list', { name: 'Lots' }).locator('a.cf-lot-tile');
}

function tile(page: Page, name: string): Locator {
  return page.locator('.cf-lot-tile', { has: page.locator('.cf-lot-tile__name', { hasText: new RegExp(`^${name}$`, 'u') }) });
}

function deviceRow(page: Page, id: string): Locator {
  return page.locator(`[data-device="${id}"]`);
}

function windowOf(page: Page, id: string): Locator {
  return deviceRow(page, id).locator('details.cf-devices__window');
}

/** Opens Devices with the silences seeded just before, so their durations are the ones the test names. */
async function openDevices(page: Page, role: 'Owner' | 'Administrator' | 'Member', devices: readonly FakeDevice[] = silentDevices()): Promise<void> {
  await resetSites([{ ...home, role }], devicesLots, devices);
  await page.goto('/devices');
  await expect(page.getByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
  // Hydrated: the forms keep what is typed only once the page runs in the browser.
  await page.waitForLoadState('networkidle');
}

/**
 * The last-seen time of a silent Device is the time of this run. The tests check it as it is; this
 * fixes it afterwards so the screenshots can be compared.
 */
async function fixLastSeen(page: Page): Promise<void> {
  await page.evaluate(() => {
    const fixed = ['Last seen 6:58 AM', 'Last seen 12:40 AM'];
    for (const [index, seen] of [...document.querySelectorAll('[data-silent="true"] .cf-devices__seen')].entries()) {
      seen.textContent = fixed[index] ?? '';
    }
  });
}

function healthAlert(id: string, opened: string, fields: Partial<FakeHealthAlert> = {}): FakeHealthAlert {
  return { id, siteId: homeId, kind: 'silent', deviceId: beansNodeId, deviceKind: 'node', openedAt: todayAt(opened), ...fields };
}

/** A silent Node on Beans (6 h), a silent Hub (5 min), a silent Node on no Lot (6 h) and a Hub whose Alert closed. */
function healthAlerts(): FakeHealthAlert[] {
  return [
    healthAlert('h-closed', '03:00', { deviceId: heardHubId, deviceKind: 'hub', silentSince: todayAt('02:55'), closedAt: todayAt('03:20'), reason: 'recovered' }),
    healthAlert('h-spare', '00:20', { deviceId: spareNodeId, silentSince: ago(6 * hour + 40 * minute) }),
    healthAlert('h-hub', '00:40', { deviceId: hubId, deviceKind: 'hub', silentSince: ago(5 * minute + 20_000) }),
    healthAlert('h-beans', '00:30', { lotId: devicesLots[0]?.id ?? '', lotName: 'Beans', silentSince: ago(6 * hour + 30 * minute) }),
  ];
}

function alertRow(page: Page, id: string): Locator {
  return page.locator(`[data-alert="${id}"] a`);
}

test.describe('Silence on the Site overview', () => {
  for (const theme of themes) {
    test(`UX-DR81 UX-DR18 a silent Hub: the notice above the tiles, and the unknown Lots above every OK Lot (${theme})`, async ({ page }) => {
      await resetSites([home], overviewLots, overviewDevices);
      await prepare(page, theme);
      await page.clock.setFixedTime(now);
      await signInTo(page);

      // The notice names the Hub by four hex digits and says how long it has been silent; it has no action.
      const notice = page.locator(`#cf-hub-silent-${hubId}`);
      await expect(notice).toHaveText(hubNotice);
      await expect(notice.locator('a, button')).toHaveCount(0);
      await expect(page.locator('[id^="cf-hub-silent-"]')).toHaveCount(1);
      // Announced politely.
      await expect(page.getByRole('status')).toHaveText(hubNotice);
      await expect(page.getByRole('alert')).toHaveText('');
      // Above the tiles.
      const noticeBottom = await notice.evaluate((element) => element.getBoundingClientRect().bottom);
      const gridTop = await page.locator('.cf-lot-grid').evaluate((element) => element.getBoundingClientRect().top);
      expect(noticeBottom).toBeLessThanOrEqual(gridTop);

      // The Server's order: the unknown Lots stand before every OK Lot. Nothing is sorted here.
      await expect(tiles(page).locator('.cf-lot-tile__name')).toHaveText(['Beans', 'Peas', 'Lettuce', 'Herbs', 'Tomatoes']);
      await expect(tile(page, 'Beans')).toHaveText(/Beans\s*Silent · unknown\s*6 h\s*was ~40% at 1:05 AM/u);
      await expect(tile(page, 'Beans')).toHaveAccessibleName('Beans, unknown, Node silent for 6 hours, last about 40 percent at 1:05 AM');
      await expect(tile(page, 'Beans')).toHaveCSS('border-top-style', 'dashed');
      await expect(tile(page, 'Beans').locator('.cf-hatch')).toBeVisible();
      await expect(tile(page, 'Peas')).toHaveText(/Peas\s*Hub silent · unknown\s*12 min\s*was ~40% at 6:50 AM/u);
      await expect(tile(page, 'Lettuce')).toHaveText(/Lettuce\s*Hub silent · unknown\s*12 min\s*no Readings yet/u);
      await expect(page.getByRole('heading', { level: 2, name: "3 Lots can't be read" })).toBeVisible();

      await axeClean(page, `overview, Hub silent (${theme})`);
      await nothingClipped(page, `overview, Hub silent (${theme})`);
      await expect(page).toHaveScreenshot(`overview-hub-silent-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR78 Lot detail of a Lot behind the silent Hub names the Hub', async ({ page }) => {
    await resetSites([home], overviewLots, overviewDevices);
    await prepare(page, 'light');
    await page.clock.setFixedTime(now);
    await signInTo(page);
    await tile(page, 'Peas').click();
    await expect(page.getByRole('heading', { level: 1, name: 'Peas' })).toBeVisible();
    await expect(page.getByText("Hub 3F2A is silent; Lots behind it can't be read.")).toBeVisible();
  });

  test('UX-DR81 a silent Hub with no Lot behind it still shows the notice; when it recovers the notice is gone on the next load', async ({ page }) => {
    const okLots = overviewLots.filter((candidate) => candidate.status === 'ok');
    await resetSites([home], okLots, overviewDevices);
    await prepare(page, 'light');
    await page.clock.setFixedTime(now);
    await signInTo(page);

    await expect(page.locator(`#cf-hub-silent-${hubId}`)).toHaveText(hubNotice);
    await expect(tiles(page).locator('.cf-lot-tile__name')).toHaveText(['Herbs', 'Tomatoes']);
    await expect(page.locator('.cf-lot-tile--unknown')).toHaveCount(0);

    // The Hub is heard again: its Alert closed, and the Server lists no silent Hub.
    await resetSites(
      [home],
      okLots,
      overviewDevices.map((device) => ({ id: device.id, siteId: device.siteId, kind: device.kind, online: true, lastSeenAt: '2026-10-06T05:09:40.000Z' })),
    );
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.locator('[id^="cf-hub-silent-"]')).toHaveCount(0);
    await expect(tiles(page)).toHaveCount(2);
  });
});

test.describe('Silence and live updates', () => {
  test('a `devices` hint reads the overview again: the Hub notice appears when the Hub turns silent, without a reload', async ({ page }) => {
    const okLots = overviewLots.filter((candidate) => candidate.status === 'ok');
    const heard = overviewDevices.map((device) => ({ id: device.id, siteId: device.siteId, kind: device.kind, online: true, lastSeenAt: '2026-10-06T05:09:40.000Z' }));
    await resetSites([home], okLots, heard);
    await prepare(page, 'light');
    await page.clock.setFixedTime(now);
    await signInTo(page);
    await liveConnections(1);
    await expect(tiles(page)).toHaveCount(2);
    await expect(page.locator('[id^="cf-hub-silent-"]')).toHaveCount(0);
    await page.evaluate(() => {
      (window as unknown as { marked: boolean }).marked = true;
    });

    // The Hub's silent Alert opened: the Server touches `devices`, and the overview reads the Lots again.
    await resetSites([home], overviewLots, overviewDevices);
    expect(await sendLive('readmodel.changed', { resource: 'devices', id: hubId, version: 3 })).toBe(1);

    await expect(page.locator(`#cf-hub-silent-${hubId}`)).toHaveText(hubNotice);
    await expect(page.getByRole('status')).toHaveText(hubNotice);
    await expect(tiles(page).locator('.cf-lot-tile__name')).toHaveText(['Beans', 'Peas', 'Lettuce', 'Herbs', 'Tomatoes']);
    expect(await page.evaluate(() => (window as unknown as { marked?: boolean }).marked)).toBe(true);
  });
});

test.describe('Silence on Devices', () => {
  for (const theme of themes) {
    test(`UX-DR30 UX-DR84 a silent Hub and a silent Node, and the open Silence Window form of an Owner (${theme})`, async ({ page }) => {
      await resetSites([home], devicesLots);
      await prepare(page, theme);
      await signInTo(page);
      await openDevices(page, 'Owner');

      // The help icon and "Silent 12 min" beside last seen; upper case is style only.
      const hub = deviceRow(page, hubId);
      await expect(hub.locator('.cf-devices__silent')).toHaveText('Silent 12 min');
      await expect(hub.locator('.cf-devices__silent')).toHaveCSS('text-transform', 'uppercase');
      await expect(hub.locator('.cf-devices__silent [data-icon="help"]')).toBeVisible();
      await expect(hub.locator('.cf-devices__seen')).toHaveText(/^Last seen \S+/u);
      const node = deviceRow(page, beansNodeId);
      await expect(node.locator('.cf-devices__silent')).toHaveText('Silent 6 h');
      await expect(node.locator('.cf-devices__silent [data-icon="help"]')).toBeVisible();
      await expect(node.locator('.cf-devices__id')).toHaveText(beansNodeId);
      // A Device that is heard has no silence.
      await expect(deviceRow(page, heardHubId).locator('.cf-devices__silent')).toHaveCount(0);
      await expect(page.locator('.cf-devices__silent')).toHaveCount(2);

      // The row action: the value in force, then a number with a unit, Save, and Use default while the Device has its own.
      await expect(windowOf(page, hubId).locator('summary')).toHaveText('Silence Window · 5 min');
      await expect(windowOf(page, hubId).getByRole('button', { name: 'Use default' })).toHaveCount(0);
      await expect(windowOf(page, heardHubId).locator('summary')).toHaveText('Silence Window · 5 min');
      await windowOf(page, beansNodeId).locator('summary').click();
      const form = windowOf(page, beansNodeId);
      await expect(form.locator('summary')).toHaveText('Silence Window · 8 h');
      await expect(form.getByRole('spinbutton', { name: 'Silence Window' })).toHaveValue('8');
      await expect(form.getByRole('combobox', { name: 'Unit' })).toHaveValue('h');
      await expect(form.getByRole('combobox', { name: 'Unit' }).locator('option')).toHaveText(['min', 'h', 'd']);
      await expect(form.getByRole('button', { name: 'Save' })).toBeVisible();
      await expect(form.getByRole('button', { name: 'Use default' })).toBeVisible();
      await expect(page.locator('main [disabled]:not([name="lotId"])')).toHaveCount(0);

      await axeClean(page, `Devices, silent (${theme})`);
      await nothingClipped(page, `Devices, silent (${theme})`);
      await fixLastSeen(page);
      await expect(page).toHaveScreenshot(`devices-silent-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR84 an Administrator saves a Silence Window of 8 h on a Node and returns it to the default', async ({ page }) => {
    await resetSites([home], devicesLots);
    await signInTo(page);
    const devices = silentDevices().map((device) => (device.id === beansNodeId ? { ...device, silenceWindowSeconds: undefined } : device));
    await openDevices(page, 'Administrator', devices);

    const form = windowOf(page, beansNodeId);
    await expect(form.locator('summary')).toHaveText('Silence Window · 6 h');
    await form.locator('summary').click();
    await expect(form.getByRole('button', { name: 'Use default' })).toHaveCount(0);
    await form.getByRole('spinbutton', { name: 'Silence Window' }).fill('8');
    await form.getByRole('combobox', { name: 'Unit' }).selectOption('h');
    await form.getByRole('button', { name: 'Save' }).click();

    await expect(form.locator('summary')).toHaveText('Silence Window · 8 h');
    await expect(page.getByRole('status')).toHaveText('Silence Window saved.');
    expect((await serverSites()).silenceWindowPuts).toEqual([{ deviceId: beansNodeId, body: { windowSeconds: 28_800 } }]);

    await form.getByRole('button', { name: 'Use default' }).click();
    await expect(form.locator('summary')).toHaveText('Silence Window · 6 h');
    await expect(form.getByRole('button', { name: 'Use default' })).toHaveCount(0);
    expect((await serverSites()).silenceWindowPuts).toEqual([
      { deviceId: beansNodeId, body: { windowSeconds: 28_800 } },
      { deviceId: beansNodeId, body: {} },
    ]);

    // A Hub has the same action: 10 min in place of its 5 min.
    const hubForm = windowOf(page, hubId);
    await hubForm.locator('summary').click();
    await hubForm.getByRole('spinbutton', { name: 'Silence Window' }).fill('10');
    await hubForm.getByRole('combobox', { name: 'Unit' }).selectOption('min');
    await hubForm.getByRole('button', { name: 'Save' }).click();
    await expect(hubForm.locator('summary')).toHaveText('Silence Window · 10 min');
  });

  test('UX-DR84 a value outside 1 min to 30 days is refused before anything is sent, with the range in the message', async ({ page }) => {
    await resetSites([home], devicesLots);
    await signInTo(page);
    await openDevices(page, 'Owner');

    const form = windowOf(page, beansNodeId);
    await form.locator('summary').click();
    const amount = form.getByRole('spinbutton', { name: 'Silence Window' });
    for (const [value, unit] of [
      ['31', 'd'],
      ['0', 'min'],
      ['1.5', 'h'],
      ['', 'h'],
    ] as const) {
      await amount.fill(value);
      await form.getByRole('combobox', { name: 'Unit' }).selectOption(unit);
      await form.getByRole('button', { name: 'Save' }).click();
      await expect(form.locator('.cf-devices__hint')).toHaveText('Enter a whole number from 1 min to 30 d.');
      await expect(amount).toHaveAttribute('aria-invalid', 'true');
      await expect(amount).toHaveValue(value);
    }
    await expect(page.getByRole('alert')).toHaveText('Enter a whole number from 1 min to 30 d.');
    expect((await serverSites()).silenceWindowPuts).toEqual([]);
    await expect(form.locator('summary')).toHaveText('Silence Window · 8 h');

    // A value in range is accepted afterwards, and the message is gone.
    await amount.fill('30');
    await form.getByRole('combobox', { name: 'Unit' }).selectOption('d');
    await form.getByRole('button', { name: 'Save' }).click();
    await expect(form.locator('summary')).toHaveText('Silence Window · 30 d');
    await expect(form.locator('.cf-devices__hint')).not.toHaveText('Enter a whole number from 1 min to 30 d.');
    expect((await serverSites()).silenceWindowPuts).toEqual([{ deviceId: beansNodeId, body: { windowSeconds: 2_592_000 } }]);
  });

  test('UX-DR84 a Member sees the silence and last seen, and no Silence Window control', async ({ page }) => {
    await resetSites([{ ...home, role: 'Member' }], devicesLots);
    await signInTo(page);
    await openDevices(page, 'Member');

    await expect(deviceRow(page, hubId).locator('.cf-devices__silent')).toHaveText('Silent 12 min');
    await expect(deviceRow(page, hubId).locator('.cf-devices__seen')).toHaveText(/^Last seen \S+/u);
    await expect(deviceRow(page, beansNodeId).locator('.cf-devices__silent')).toHaveText('Silent 6 h');
    await expect(page.getByText('Silence Window')).toHaveCount(0);
    await expect(page.locator('details.cf-devices__window')).toHaveCount(0);
    // Hidden, never disabled.
    await expect(page.locator('main').locator('form, button, input, select, textarea, details, [disabled]')).toHaveCount(0);
  });

  test('UX-DR84 a caller who lost the Role after the page loaded gets "You can\'t change this on ‹Site›" and nothing changes', async ({ page }) => {
    await resetSites([home], devicesLots);
    await signInTo(page);
    const devices = silentDevices();
    await openDevices(page, 'Administrator', devices);
    const form = windowOf(page, hubId);
    await form.locator('summary').click();

    // Demoted meanwhile: the Server answers 403.
    await resetSites([{ ...home, role: 'Member' }], devicesLots, devices);
    await form.getByRole('spinbutton', { name: 'Silence Window' }).fill('10');
    await form.getByRole('button', { name: 'Save' }).click();

    await expect(page.locator(`#cf-hub-${hubId}-notice`)).toHaveText("You can't change this on Home garden. Ask an Owner or Administrator.");
    expect((await serverSites()).silenceWindowPuts).toEqual([]);
  });
});

test.describe('Silence on Alerts', () => {
  for (const theme of themes) {
    test(`UX-DR25 UX-DR117 UX-DR26 Health rows of a silent Node and a silent Hub, and the closed one (${theme})`, async ({ page }) => {
      await resetSites([{ ...home, role: 'Member' }], devicesLots);
      await prepare(page, theme);
      await signInTo(page);
      await setAlerts([], null, healthAlerts());
      await page.goto('/alerts');

      await expect(page.locator('main h2')).toHaveText(['Health Alerts', 'Closed']);
      await expect(page.getByText('No open Alerts.')).toHaveCount(0);
      // Newest first, as the Server lists them.
      const rows = page.getByRole('list', { name: 'Health Alerts' }).getByRole('link');
      await expect(rows).toHaveCount(3);
      await expect(rows.nth(0)).toHaveAccessibleName('Hub 3F2A silent for 5 min, since 12:40 AM');
      await expect(rows.nth(1)).toHaveAccessibleName("Node on Lot 'Beans' silent for 6 h, since 12:30 AM");
      await expect(rows.nth(2)).toHaveAccessibleName('Node 7C19 silent for 6 h, since 12:20 AM');
      await expect(alertRow(page, 'h-hub')).toHaveText(/Health Alert\s*Hub 3F2A silent for 5 min/u);
      await expect(alertRow(page, 'h-beans')).toHaveText(/Health Alert\s*Node on Lot 'Beans' silent for 6 h/u);
      await expect(alertRow(page, 'h-spare')).toHaveText(/Health Alert\s*Node 7C19 silent for 6 h/u);
      // Hatched and dashed with the help icon; a closed row is the outline and drops the duration.
      for (const id of ['h-hub', 'h-beans', 'h-spare']) {
        await expect(alertRow(page, id)).toHaveCSS('border-top-style', 'dashed');
        await expect(alertRow(page, id).locator('.cf-hatch--plate')).toBeVisible();
        await expect(alertRow(page, id).locator('[data-icon="help"]')).toBeVisible();
      }
      await expect(alertRow(page, 'h-closed')).toHaveText(/Health Alert · closed 3:20 AM\s*Hub 1B00 silent$/u);
      await expect(alertRow(page, 'h-closed')).toHaveCSS('border-top-style', 'solid');
      // Each row leads to the row of its Device.
      await expect(alertRow(page, 'h-hub')).toHaveAttribute('href', `/devices#device-${hubId}`);
      await expect(alertRow(page, 'h-beans')).toHaveAttribute('href', `/devices#device-${beansNodeId}`);
      await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);

      await axeClean(page, `Alerts, Health (${theme})`);
      await nothingClipped(page, `Alerts, Health (${theme})`);
      await expect(page).toHaveScreenshot(`alerts-health-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR26 a Health row opens Devices at the row of its Device', async ({ page }) => {
    await resetSites([{ ...home, role: 'Member' }], devicesLots, silentDevices());
    await signInTo(page);
    await setAlerts([], null, healthAlerts());
    await page.goto('/alerts');

    await page.getByRole('link', { name: /^Hub 3F2A silent for 5 min/u }).click();
    await expect(page).toHaveURL(new RegExp(`/devices#device-${hubId}$`, 'u'));
    await expect(page.locator(`#device-${hubId}`)).toBeInViewport();
    await expect(page.locator(`#device-${hubId} .cf-devices__id`)).toHaveText(hubId);
  });
});
