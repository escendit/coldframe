import { expect, test, type Page } from '@playwright/test';
import type { FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, failReads, nothingClipped, resetSites, serverSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

// Site IDs no other spec uses: the web app keeps the last good Sites and Lots per user and Site
// for the whole run, and these tests decide what is kept for theirs.
const homeId = '0192a000-0000-7000-8000-0000000047a1';
const allotmentId = '0192a000-0000-7000-8000-0000000047a2';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const allotment = { id: allotmentId, name: 'Allotment', role: 'Member' } as const;

/** The browser's clock in the live tests: 07:17 in Zurich, so every time and duration on the tiles is fixed. */
const now = new Date('2026-10-06T05:17:00.000Z');

function lot(index: number, name: string, status: string, fields: Partial<FakeLot> = {}): FakeLot {
  return { id: `0192a000-0000-7000-8000-00000047b${String(index).padStart(3, '0')}`, siteId: homeId, name, status, statusSince: '2026-10-06T03:45:00.000Z', ...fields };
}

/** One Lot per variant of the tile, seeded out of order: the fake Server lists them in the Server's order. */
const lots: readonly FakeLot[] = [
  lot(1, 'Potatoes', 'noNode'),
  lot(2, 'Strawberries', 'paused', { pausedBy: ['device'], pausedUntil: '2026-10-31T23:00:00.000Z' }),
  lot(3, 'Lettuce', 'paused', { pausedBy: ['device', 'site'] }),
  lot(4, 'Herbs', 'ok', { lastReadingAt: '2026-10-06T05:03:00.000Z', moisturePercent: 35.2, lowThresholdPercent: 25 }),
  lot(5, 'Greenhouse bench by the south wall', 'ok', { lastReadingAt: '2026-10-06T05:03:00.000Z' }),
  lot(6, 'Beans', 'unknown', { unknownCause: 'node', statusSince: '2026-10-06T05:05:00.000Z', lastReadingAt: '2026-10-05T23:05:00.000Z', moisturePercent: 41 }),
  lot(7, 'Peas', 'unknown', { unknownCause: 'hub', statusSince: '2026-10-06T05:05:00.000Z', lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 38 }),
  lot(8, 'Peppers', 'needsCalibration', { statusSince: '2026-10-05T16:00:00.000Z', lastReadingAt: '2026-10-06T05:02:00.000Z' }),
  lot(9, 'Tomatoes', 'needsWater', { lastReadingAt: '2026-10-06T05:02:00.000Z', moisturePercent: 21.7, lowThresholdPercent: 30 }),
];

/** Window widths and the columns the Lot grid has there (its own width decides, UX-DR108). */
const widths = [
  [360, 1],
  [600, 2],
  [1100, 3],
  [1440, 4],
] as const;

const liveNames = [
  'Tomatoes, needs water, about 20 percent, low 30 percent, Reading 7:02 AM',
  'Peppers, needs Calibration, no percentage until calibrated',
  'Beans, unknown, Node silent for 6 hours, last about 40 percent at 1:05 AM',
  'Peas, unknown, Hub silent for 12 minutes, last about 40 percent at 7:02 AM',
  'Herbs, OK, about 35 percent, low 25 percent',
  'Greenhouse bench by the south wall, OK',
  'Strawberries, paused until November 1',
  'Lettuce, paused with the Site',
  'Potatoes, no Node, add a Node',
];

const wasNames = ['Tomatoes, was needs water', 'Peppers, was needs Calibration', 'Beans, was unknown', 'Peas, was unknown', 'Herbs, was OK', 'Greenhouse bench by the south wall, was OK', 'Strawberries, was paused', 'Lettuce, was paused', 'Potatoes, was no Node'];

test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site, no Lots and a Server that answers.
  await resetSites();
});

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

async function prepare(page: Page, theme: 'light' | 'dark'): Promise<void> {
  await useTheme(page, theme, appUrl);
  await page.context().addCookies([{ name: 'cf_time_zone', value: zone, url: appUrl }]);
  await page.emulateMedia({ colorScheme: theme });
}

function tiles(page: Page) {
  return page.getByRole('list', { name: 'Lots' }).locator('a.cf-lot-tile');
}

function tile(page: Page, name: string) {
  return page.locator('.cf-lot-tile', { has: page.locator('.cf-lot-tile__name', { hasText: new RegExp(`^${name}$`, 'u') }) });
}

async function columns(page: Page): Promise<number> {
  return page.locator('.cf-lot-grid').evaluate((grid) => getComputedStyle(grid).gridTemplateColumns.split(' ').length);
}

/** Tiles whose content does not fit their box. */
async function clippedTiles(page: Page): Promise<(string | null)[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll<HTMLElement>('.cf-lot-tile')]
      .filter((element) => element.scrollHeight > element.clientHeight + 1 || element.scrollWidth > element.clientWidth + 1)
      .map((element) => element.getAttribute('aria-label')),
  );
}

/** What tells a tile apart with colour and text removed: its edge, its fill and its icon. */
async function shapes(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll<HTMLElement>('.cf-lot-tile')].map((element) => {
      const style = getComputedStyle(element);
      const edge = style.borderTopStyle === 'none' ? 'no border' : `${style.borderTopWidth} ${style.borderTopStyle}`;
      const fill = element.querySelector('.cf-hatch') !== null ? 'hatch' : element.querySelector('.cf-lot-tile__level') !== null ? 'level' : 'flat';
      return `${edge}, ${fill}, ${element.querySelector('.cf-icon')?.getAttribute('data-icon') ?? ''}`;
    }),
  );
}

/** The sign of a refetch: the window gets focus again. */
async function focusTab(page: Page): Promise<void> {
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
}

/**
 * Stale mode shows the time of the last successful refresh, which is the time of this run. The
 * tests check those texts as they are; this fixes them afterwards so the screenshots can be compared.
 */
async function fixStaleTimes(page: Page): Promise<void> {
  await page.evaluate(() => {
    for (const foot of document.querySelectorAll('.cf-lot-tile--stale .cf-lot-tile__foot')) {
      foot.textContent = 'as of 7:02 AM';
    }
    const age = document.querySelector('.cf-stale-header__age');
    const detail = document.querySelector('.cf-stale-header__detail');
    if (age === null || detail === null) {
      throw new Error('No stale header.');
    }
    age.textContent = '2 h 12 min old';
    detail.textContent = detail.textContent.replace(/^Last data [^.]+\./u, 'Last data 7:02 AM.');
  });
}

const clockTime = String.raw`\d{1,2}:\d{2} [AP]M`;

test.describe('Lot status on the Site overview', () => {
  for (const theme of themes) {
    test(`UX-DR18 UX-DR17 UX-DR77 UX-DR97 UX-DR98 UX-DR108 UX-DR128 UX-DR129 every Lot status variant at 1, 2, 3 and 4 columns (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await prepare(page, theme);
      await page.clock.setFixedTime(now);
      await signInTo(page);

      // The headline counts the Server's statuses; the tiles keep the Server's order.
      await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
      await expect(page.locator('.cf-site-summary__subline')).toHaveText('1 needs Calibration · 2 unknown · 2 OK · 2 paused · 1 without Node');
      await expect(tiles(page)).toHaveCount(lots.length);
      for (const [index, name] of liveNames.entries()) {
        await expect(tiles(page).nth(index)).toHaveAccessibleName(name);
      }
      await expect(page.locator('main').locator('.cf-lot-grid').locator('a.cf-lot-tile')).toHaveCount(9);
      await expect(page.locator('main').locator('.cf-lot-grid').locator('button, [tabindex]')).toHaveCount(0);

      await expect(tile(page, 'Tomatoes')).toHaveText(/Tomatoes\s*Needs water\s*~20\s*7:02 AM · low 30%/u);
      await expect(tile(page, 'Peppers')).toHaveText(/Peppers\s*Needs Calibration\s*raw\s*no % until calibrated/u);
      await expect(tile(page, 'Beans')).toHaveText(/Beans\s*Silent · unknown\s*6 h\s*was ~40% at 1:05 AM/u);
      await expect(tile(page, 'Peas')).toHaveText(/Peas\s*Hub silent · unknown\s*12 min\s*was ~40% at 7:02 AM/u);
      await expect(tile(page, 'Herbs')).toHaveText(/Herbs\s*OK\s*~35\s*7:03 AM · low 25%/u);
      await expect(tile(page, 'Strawberries')).toHaveText(/Strawberries\s*Paused\s*—\s*until Nov 1/u);
      await expect(tile(page, 'Lettuce')).toHaveText(/Lettuce\s*Paused by Site\s*—\s*paused/u);
      await expect(tile(page, 'Potatoes')).toHaveText(/Potatoes\s*No Node\s*\+\s*add a Node/u);
      // Uppercase is style only.
      await expect(tile(page, 'Tomatoes').locator('.cf-lot-tile__status')).toHaveCSS('text-transform', 'uppercase');

      for (const [width, expected] of widths) {
        await page.setViewportSize({ width, height: 800 });
        expect(await columns(page), `columns at ${String(width)} px`).toBe(expected);
        expect(await clippedTiles(page), `tiles at ${String(width)} px`).toEqual([]);
        await axeClean(page, `Lot status, ${String(expected)} columns (${theme})`);
        await nothingClipped(page, `Lot status, ${String(expected)} columns (${theme})`);
        await expect(page).toHaveScreenshot(`lots-${String(expected)}-columns-${theme}.png`, { fullPage: true });
      }
    });

    test(`UX-DR19 UX-DR24 UX-DR79 UX-DR98 UX-DR106 the Server stops answering: stale header and stale tiles at 1, 2, 3 and 4 columns (${theme})`, async ({ page }) => {
      await resetSites([home], lots);
      await prepare(page, theme);
      await signInTo(page);
      await expect(tiles(page)).toHaveCount(lots.length);

      await failReads('all');
      await page.reload();

      // The stale header replaces the summary header; nothing below is drawn or said as live.
      const header = page.locator('.cf-stale-header');
      await expect(header.locator('.cf-stale-header__title')).toHaveText("Home garden · can't reach your Server");
      await expect(header.locator('[data-icon="cloud--offline"]')).toBeVisible();
      await expect(page.getByRole('heading', { level: 2 })).toHaveText(/^\d+ min old$/u);
      await expect(header.locator('.cf-stale-header__detail')).toHaveText(new RegExp(`^Last data ${clockTime}\\. You may be away from home, or the Server is down\\. Nothing below is live\\.$`, 'u'));
      await expect(page.locator('.cf-site-summary')).toHaveCount(0);
      await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(lots.length);
      await expect(page.locator('.cf-lot-tile__value, .cf-lot-tile__level, .cf-lot-tile__low, .cf-lot-tile .cf-hatch')).toHaveCount(0);
      for (const [index, name] of wasNames.entries()) {
        await expect(tiles(page).nth(index)).toHaveAccessibleName(new RegExp(`^${name}, not live, as of ${clockTime}$`, 'u'));
      }
      await expect(tile(page, 'Tomatoes')).toHaveText(new RegExp(`Tomatoes\\s*Was needs water\\s*as of ${clockTime}`, 'u'));
      await expect(tile(page, 'Peas')).toHaveText(/Was Hub silent · unknown/u);
      await expect(tile(page, 'Tomatoes')).toHaveCSS('border-top-style', 'solid');
      await expect(tile(page, 'Tomatoes')).toHaveCSS('border-top-width', '1px');
      await expect(tile(page, 'Tomatoes')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
      expect(new Set(await shapes(page))).toEqual(new Set(['1px solid, flat, cloud--offline']));
      // Entering stale mode is announced politely, with the time of the last good data.
      await expect(page.getByRole('status')).toHaveText(new RegExp(`^Can't reach your Server\\. Showing data from ${clockTime}\\.$`, 'u'));
      await expect(page.getByRole('alert')).toHaveText('');

      await fixStaleTimes(page);
      for (const [width, expected] of widths) {
        await page.setViewportSize({ width, height: 800 });
        expect(await columns(page), `columns at ${String(width)} px`).toBe(expected);
        expect(await clippedTiles(page), `tiles at ${String(width)} px`).toEqual([]);
        await axeClean(page, `stale, ${String(expected)} columns (${theme})`);
        await nothingClipped(page, `stale, ${String(expected)} columns (${theme})`);
        await expect(page).toHaveScreenshot(`lots-stale-${String(expected)}-columns-${theme}.png`, { fullPage: true });
      }
    });
  }

  test('UX-DR99 UX-DR18 with colour and text removed every status still differs by edge, fill and icon', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await page.clock.setFixedTime(now);
    await signInTo(page);
    await expect(tiles(page)).toHaveCount(lots.length);
    expect(await shapes(page)).toEqual([
      'no border, level, rain-drop',
      '2px dashed, hatch, tools',
      '1px dashed, hatch, help',
      '1px dashed, hatch, help',
      '1px solid, level, checkmark--outline',
      '1px solid, flat, checkmark--outline',
      '2px solid, flat, pause--outline',
      '2px solid, flat, pause--outline',
      '1px dotted, flat, add',
    ]);
    // Hatched tiles keep their text on a solid plate of the hatch ground.
    const plate = tile(page, 'Beans').locator('.cf-lot-tile__top');
    await expect(plate).toHaveCSS('background-color', 'rgb(244, 244, 244)');
    await expect(tile(page, 'Beans').locator('.cf-hatch__line')).toHaveCSS('stroke', 'rgb(141, 141, 141)');
    await expect(tile(page, 'Beans').locator('.cf-hatch__line')).toHaveCSS('stroke-width', '1.5px');
    // The soil level stands at the Reading's percentage of the tile, under a 2 px edge.
    const level = await tile(page, 'Herbs').evaluate((element) => {
      const fill = element.querySelector<HTMLElement>('.cf-lot-tile__level');
      return fill === null ? null : { share: fill.offsetHeight / element.clientHeight, edge: getComputedStyle(fill).borderTopWidth };
    });
    expect(level?.edge).toBe('2px');
    expect(level?.share).toBeGreaterThan(0.34);
    expect(level?.share).toBeLessThan(0.37);
  });

  test('UX-DR97 UX-DR17 in one column the value sits directly under the status label and the tile grows with its content', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await page.clock.setFixedTime(now);
    await signInTo(page);
    await expect(tiles(page)).toHaveCount(lots.length);

    const gap = (name: string): Promise<number> =>
      tile(page, name).evaluate((element) => {
        const status = element.querySelector('.cf-lot-tile__status');
        const value = element.querySelector('.cf-lot-tile__value');
        if (status === null || value === null) {
          throw new Error('The tile has no status or value.');
        }
        return value.getBoundingClientRect().top - status.getBoundingClientRect().bottom;
      });

    await page.setViewportSize({ width: 1440, height: 800 });
    expect(await columns(page)).toBe(4);
    const apart = await gap('Tomatoes');
    // 320 px reflow: the narrowest window the app supports.
    await page.setViewportSize({ width: 320, height: 800 });
    expect(await columns(page)).toBe(1);
    for (const name of ['Tomatoes', 'Beans', 'Peppers', 'Strawberries', 'Potatoes']) {
      expect(await gap(name), name).toBeLessThan(32);
    }
    expect(apart).toBeGreaterThan(32);
    // Tiles never shrink: each stays at least 1 : 0.82, and nothing is cut off.
    const sizes = await page.evaluate(() => [...document.querySelectorAll<HTMLElement>('.cf-lot-tile')].map((element) => element.offsetHeight / element.offsetWidth));
    for (const ratio of sizes) {
      expect(ratio).toBeGreaterThanOrEqual(0.81);
    }
    expect(await clippedTiles(page)).toEqual([]);
    await nothingClipped(page, 'one column at 320 px');
  });

  test('UX-DR112 UX-DR79 UX-DR106 UX-DR24 refetch on focus enters stale mode, the age ticks without an announcement, and the first success is live again', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await page.clock.install();
    await signInTo(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    const status = page.getByRole('status');
    await expect(status).toHaveText('');

    // Looking at the tab again asks the Server again; nothing asks on a timer.
    const { lotReads } = await serverSites();
    await focusTab(page);
    await expect.poll(async () => (await serverSites()).lotReads).toBe(lotReads + 1);
    await page.clock.runFor(5 * 60_000);
    expect((await serverSites()).lotReads).toBe(lotReads + 1);
    await expect(status).toHaveText('');

    // A refresh and its one retry fail: stale mode, announced once.
    await failReads('all');
    await focusTab(page);
    const age = page.locator('.cf-stale-header__age');
    await expect(age).toHaveText(/^\d min old$/u);
    await page.clock.runFor(200);
    const entered = new RegExp(`^Can't reach your Server\\. Showing data from ${clockTime}\\.$`, 'u');
    await expect(status).toHaveText(entered);
    await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(lots.length);

    // Site menu items stay visible, disabled with the reason.
    await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
    const item = page.getByRole('menuitem');
    await expect(item).toHaveText(/Site settings\s*Needs your Server/u);
    await expect(item).toHaveAttribute('aria-disabled', 'true');
    await expect(page.getByRole('menu').getByRole('link')).toHaveCount(0);
    await page.keyboard.press('Escape');

    // Still unreachable: the same stale view, and nothing is announced again.
    await focusTab(page);
    await page.clock.runFor(200);
    await expect(status).toHaveText(entered);

    // The age ticks with the minutes and is never announced.
    await page.clock.fastForward('02:12:00');
    await expect(age).toHaveText(/^2 h 1[2-9] min old$/u);
    await expect(status).toHaveText(entered);
    await expect(status).not.toContainText('old');
    await expect(age).not.toHaveAttribute('aria-live', /.*/u);

    // The first successful refresh leaves stale mode.
    await failReads('none');
    await focusTab(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    await page.clock.runFor(200);
    await expect(status).toHaveText('Live again.');
    await expect(page.locator('.cf-stale-header')).toHaveCount(0);
    await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(0);
    await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
    await expect(page.getByRole('menuitem', { name: 'Site settings' })).toHaveAttribute('href', '/settings/site');
  });

  test('UX-DR79 UX-DR112 UX-DR106 a browser that cannot reach the web app keeps its Lots as stale, and is live again once it can', async ({ page }) => {
    await resetSites([home], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    const status = page.getByRole('status');

    // The laptop leaves the home network: a look at the tab cannot load anything.
    await page.context().setOffline(true);
    await focusTab(page);
    await expect(page.locator('.cf-stale-header__title')).toHaveText("Home garden · can't reach your Server");
    await expect(page.locator('.cf-stale-header__detail')).toHaveText(/^Last data \d{1,2}:\d{2} [AP]M\. /u);
    await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(lots.length);
    await expect(page.locator('.cf-lot-tile--stale .cf-lot-tile__foot').first()).toHaveText(/^as of \d{1,2}:\d{2} [AP]M$/u);
    await expect(status).toHaveText(/^Can't reach your Server\. Showing data from \d{1,2}:\d{2} [AP]M\.$/u);
    await expect(page.getByRole('heading', { level: 1, name: 'Garden' })).toBeVisible();

    await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(/Site settings\s*Needs your Server/u);
    await expect(page.getByRole('menuitem')).toHaveAttribute('aria-disabled', 'true');
    await page.keyboard.press('Escape');

    // Back online: the next look loads again and leaves stale mode.
    await page.context().setOffline(false);
    await focusTab(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Tomatoes needs water' })).toBeVisible();
    await expect(page.locator('.cf-stale-header, .cf-lot-tile--stale')).toHaveCount(0);
    await expect(status).toHaveText('Live again.');
    await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
    await expect(page.getByRole('menuitem', { name: 'Site settings' })).toHaveAttribute('href', '/settings/site');
  });

  test('UX-DR79 UX-DR80 stale mode is per Site and only on the overview: nothing kept shows the existing notice, and Devices and Site settings have no stale mode', async ({ page }) => {
    await resetSites([home, allotment], lots);
    await prepare(page, 'light');
    await signInTo(page);
    await expect(tiles(page)).toHaveCount(lots.length);

    // The Lots cannot be read, the Sites can: Home garden shows its last good Lots as stale.
    await failReads('lots');
    await page.reload();
    await expect(page.locator('.cf-stale-header__title')).toHaveText("Home garden · can't reach your Server");
    await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(lots.length);

    // The Site menu is disabled when only the Lots are stale, too.
    await page.getByRole('button', { name: 'Site menu for Home garden' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(/Site settings\s*Needs your Server/u);
    await expect(page.getByRole('menuitem')).toHaveAttribute('aria-disabled', 'true');
    await page.keyboard.press('Escape');

    // Allotment was never read: no old tiles, no skeleton, the existing notice.
    await page.getByRole('tab', { name: 'Allotment · Member' }).click();
    await expect(page.getByRole('tab', { name: 'Allotment · Member' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('#cf-lots-notice')).toContainText("Can't reach your Coldframe Server.");
    await expect(page.locator('.cf-stale-header, .cf-lot-grid')).toHaveCount(0);
    await expect(page.locator('main [class*="skeleton"]')).toHaveCount(0);
    await expect(page.getByRole('heading', { level: 2, name: 'No Readings yet' })).toBeVisible();

    // Nothing answers at all: the overview is stale from the last good Sites; other pages keep their notices.
    await failReads('all');
    await page.getByRole('tab', { name: 'Home garden · Owner' }).click();
    await expect(page.locator('.cf-stale-header__title')).toHaveText("Home garden · can't reach your Server");
    await expect(page.locator('.cf-lot-tile--stale')).toHaveCount(lots.length);
    for (const path of ['/devices', '/settings/site']) {
      await page.goto(path);
      await expect(page.locator('#cf-sites-notice')).toContainText("Can't reach your Coldframe Server, so your Sites can't be shown.");
      await expect(page.locator('.cf-stale-header, .cf-lot-tile')).toHaveCount(0);
    }

    await failReads('none');
    await page.goto('/garden');
    await expect(page.locator('.cf-stale-header')).toHaveCount(0);
    await expect(tiles(page)).toHaveCount(lots.length);
  });
});
