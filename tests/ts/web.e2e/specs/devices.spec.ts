import { expect, test, type Page } from '@playwright/test';
import type { FakeDevice, FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, serverSites, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;
const zone = 'Europe/Zurich';

const homeId = '0192a000-0000-7000-8000-00000000000a';
const home = { id: homeId, name: 'Home', role: 'Owner' } as const;
const hubId = '3f2a9c0d1e4b5a67';
const silentHubId = '1b00aa11bb22cc33';

// A fixed zone keeps the last-seen times, and so the screenshots, the same on every machine.
test.use({ timezoneId: zone });

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site and no Devices.
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

function hub(id: string, online: boolean, lastSeen: string | null): FakeDevice {
  return { id, siteId: homeId, kind: 'hub', online, ...(lastSeen === null ? {} : { lastSeenAt: todayAt(lastSeen) }) };
}

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

const gardenLots: readonly FakeLot[] = [
  { id: 'lot-1', siteId: homeId, name: 'Peppers', claimed: true },
  { id: 'lot-2', siteId: homeId, name: 'Tomatoes', claimed: true },
  { id: 'lot-3', siteId: homeId, name: 'Basil' },
  { id: 'lot-4', siteId: homeId, name: 'Herbs', claimed: true },
];

function node(id: string, lotId?: string, lotName?: string): FakeDevice {
  return { id, siteId: homeId, kind: 'node', online: false, ...(lotId === undefined ? {} : { lotId, lotName }) };
}

function row(page: Page, id: string) {
  return page.getByRole('list', { name: 'Hubs' }).getByRole('listitem').filter({ hasText: id });
}

async function noControls(page: Page): Promise<void> {
  await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);
}

test.describe('Devices', () => {
  for (const theme of themes) {
    test(`UX-DR30 UX-DR65 UX-DR85 an Owner sees the Hubs, then the Nodes by Lot with battery, charging and last seen, and the mobile app notice (${theme}, largest text)`, async ({ page }) => {
      await resetSites(
        [home],
        gardenLots,
        [
          hub(hubId, true, '07:02'),
          hub(silentHubId, false, '06:40'),
          { id: '7c19000000000003', siteId: homeId, kind: 'node', online: false },
          { id: '7c19000000000002', siteId: homeId, kind: 'node', online: false, lotId: 'lot-2', lotName: 'Tomatoes', lastSeenAt: todayAt('06:30'), batteryPercent: 14, charging: 'notCharging' },
          { id: '7c19000000000001', siteId: homeId, kind: 'node', online: false, lotId: 'lot-1', lotName: 'Peppers', lastSeenAt: todayAt('07:02'), batteryPercent: 62, charging: 'charging' },
        ],
      );
      await useTheme(page, theme, appUrl);
      await page.context().addCookies([{ name: 'cf_time_zone', value: zone, url: appUrl }]);
      await page.emulateMedia({ colorScheme: theme });
      await signInTo(page);

      await page.goto('/devices');
      await largestText(page);

      await expect(page.getByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
      await expect(page.getByRole('heading', { level: 2, name: 'Hubs' })).toBeVisible();
      const rows = page.getByRole('list', { name: 'Hubs' }).getByRole('listitem');
      // Hubs by Device ID.
      await expect(rows).toHaveCount(2);
      await expect(rows.nth(0)).toContainText(silentHubId);
      await expect(rows.nth(0)).toContainText('Offline');
      await expect(rows.nth(0)).toContainText('Last seen 6:40 AM');
      await expect(rows.nth(1)).toContainText(hubId);
      await expect(rows.nth(1)).toContainText('Online');
      await expect(rows.nth(1)).toContainText('Last seen 7:02 AM');

      // Nodes follow Hubs in the Server's order: by Lot name, the unassigned one last.
      await expect(page.getByRole('heading', { level: 2, name: 'Nodes' })).toBeVisible();
      const nodes = page.getByRole('list', { name: 'Nodes' }).getByRole('listitem');
      await expect(nodes).toHaveCount(3);
      await expect(nodes.nth(0)).toContainText('7c19000000000001');
      await expect(nodes.nth(0)).toContainText('Peppers');
      await expect(nodes.nth(0)).toContainText('Last seen 7:02 AM');
      await expect(nodes.nth(0)).toContainText('62 %');
      await expect(nodes.nth(0)).toContainText('charging');
      await expect(nodes.nth(0).locator('[data-icon="battery--low"]')).toHaveCount(0);
      await expect(nodes.nth(1)).toContainText('Tomatoes');
      await expect(nodes.nth(1)).toContainText('14 %');
      await expect(nodes.nth(1)).toContainText('not charging');
      await expect(nodes.nth(1).locator('[data-icon="battery--low"]')).toBeVisible();
      await expect(nodes.nth(2)).toContainText('7c19000000000003');
      await expect(nodes.nth(2)).toContainText('Not in a Lot');

      // The full ID in the mono face; the status is a word with an icon, not a colour.
      await expect(rows.nth(1).locator('.cf-devices__id')).toHaveText(hubId);
      await expect(rows.nth(1).locator('.cf-devices__id')).toHaveCSS('font-family', /Ubuntu Mono/u);
      await expect(rows.nth(1).locator('[data-icon="checkmark--outline"]')).toBeVisible();
      await expect(rows.nth(0).locator('[data-icon="help"]')).toBeVisible();

      await expect(page.locator('#cf-devices-web-notice')).toHaveText('Adding a Hub or Node needs the Coldframe mobile app.');

      // UX-DR31: an Owner may move every Node and unassign the ones on a Lot; the Hubs have no actions.
      await expect(nodes.nth(0).getByRole('button', { name: 'Unassign' })).toBeVisible();
      await expect(nodes.nth(2).getByRole('button', { name: 'Unassign' })).toHaveCount(0);
      await expect(page.getByRole('list', { name: 'Hubs' }).locator('form, button, input, select, textarea, summary')).toHaveCount(0);

      await axeClean(page, `devices (${theme})`);
      await nothingClipped(page, `devices (${theme})`);
      await expect(page).toHaveScreenshot(`devices-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR30 a reload after the heartbeat stopped reads Offline with the unchanged last-seen time', async ({ page }) => {
    await resetSites([home], [], [hub(hubId, true, '07:02')]);
    await signInTo(page);
    await page.goto('/devices');
    // No chosen time zone: the browser's is used.
    await expect(row(page, hubId)).toContainText('Online');
    await expect(row(page, hubId)).toContainText('Last seen 7:02 AM');

    await resetSites([home], [], [hub(hubId, false, '07:02')]);
    await page.reload();
    await expect(row(page, hubId)).toContainText('Offline');
    await expect(row(page, hubId)).toContainText('Last seen 7:02 AM');
    await expect(row(page, hubId)).not.toContainText('Online');
  });

  test('UX-DR30 a Hub that never sent a heartbeat is Offline and Not seen yet', async ({ page }) => {
    await resetSites([home], [], [hub(hubId, false, null)]);
    await signInTo(page);
    await page.goto('/devices');
    await expect(row(page, hubId)).toContainText('Offline');
    await expect(row(page, hubId)).toContainText('Not seen yet');
  });

  test('UX-DR30 UX-DR85 no Devices: the empty state, and the notice for an Administrator', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }]);
    await signInTo(page);
    await page.goto('/devices');
    await expect(page.getByText('No Devices yet.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Hubs' })).toHaveCount(0);
    await expect(page.locator('#cf-devices-web-notice')).toHaveText('Adding a Hub or Node needs the Coldframe mobile app.');
    await noControls(page);
  });

  test('UX-DR84 a Member sees the list with no notice and no control', async ({ page }) => {
    await resetSites([{ ...home, role: 'Member' }], [], [hub(hubId, true, '07:02')]);
    await signInTo(page);
    await page.goto('/devices');
    await expect(row(page, hubId)).toContainText('Online');
    await expect(page.locator('main .cf-inline-notice')).toHaveCount(0);
    await expect(page.getByText('Adding a Hub or Node needs the Coldframe mobile app.')).toHaveCount(0);
    await noControls(page);
    await expect(page.locator('main a')).toHaveCount(0);
  });

  test('UX-DR65 a reload that fails shows no rows and the notice with Try again, which loads the list again', async ({ page }) => {
    await resetSites([home], [], [hub(hubId, true, '07:02')]);
    await signInTo(page);
    await page.goto('/devices');
    await expect(row(page, hubId)).toContainText('Online');

    await resetSites([home], [], [hub(hubId, true, '07:02')], 503);
    await page.reload();
    await expect(page.locator('#cf-devices-notice')).toContainText("Can't reach your Server.");
    await expect(page.getByText(hubId)).toHaveCount(0);
    await expect(page.getByText('Online')).toHaveCount(0);
    await expect(page.locator('#cf-devices-web-notice')).toHaveCount(0);

    await resetSites([home], [], [hub(hubId, true, '07:02')]);
    await page.getByRole('link', { name: 'Try again' }).click();
    await expect(row(page, hubId)).toContainText('Online');
  });

  test('UX-DR31 an Administrator moves a Node: occupied Lots are not selectable, and the list shows the new Lot', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }], gardenLots, [node('7c19000000000001', 'lot-1', 'Peppers'), node('7c19000000000002', 'lot-2', 'Tomatoes')]);
    await signInTo(page);
    await page.goto('/devices');
    const peppers = page.getByRole('list', { name: 'Nodes' }).getByRole('listitem').filter({ hasText: '7c19000000000001' });

    await peppers.getByText('Move', { exact: true }).click();
    // The Node's own Lot and a Lot another Node holds are disabled; "Has a Node" says why.
    await expect(peppers.getByRole('radio', { name: /Tomatoes/u })).toBeDisabled();
    await expect(peppers.getByRole('radio', { name: /Herbs/u })).toBeDisabled();
    await expect(peppers.getByRole('radio', { name: /Peppers/u })).toBeDisabled();
    await expect(peppers.getByText('Has a Node')).toHaveCount(2);
    await peppers.getByRole('radio', { name: 'Basil' }).check();
    await peppers.getByRole('button', { name: 'Move Node 7c19000000000001' }).click();

    const moved = page.getByRole('list', { name: 'Nodes' }).getByRole('listitem').filter({ hasText: '7c19000000000001' });
    await expect(moved.locator('.cf-devices__lot')).toHaveText('Basil');
    expect((await serverSites()).deviceActions).toEqual([{ action: 'move', deviceId: '7c19000000000001', lotId: 'lot-3' }]);
  });

  test('UX-DR31 unassign asks first, naming the Node; Cancel changes nothing and Unassign does it', async ({ page }) => {
    await resetSites([{ ...home, role: 'Administrator' }], gardenLots, [node('7c19000000000001', 'lot-1', 'Peppers')]);
    await signInTo(page);
    await page.goto('/devices');
    const peppers = page.getByRole('list', { name: 'Nodes' }).getByRole('listitem').filter({ hasText: '7c19000000000001' });

    await peppers.getByRole('button', { name: 'Unassign' }).click();
    const dialog = page.getByRole('dialog', { name: 'Unassign Node 7c19000000000001?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
    expect((await serverSites()).deviceActions).toEqual([]);

    await peppers.getByRole('button', { name: 'Unassign' }).click();
    await dialog.getByRole('button', { name: 'Unassign' }).click();
    await expect(page.getByRole('list', { name: 'Nodes' }).getByRole('listitem').filter({ hasText: '7c19000000000001' }).locator('.cf-devices__lot')).toHaveText('Not in a Lot');
    expect((await serverSites()).deviceActions).toEqual([{ action: 'unassign', deviceId: '7c19000000000001' }]);
  });

  test('UX-DR31 a Member sees neither move nor unassign', async ({ page }) => {
    await resetSites([{ ...home, role: 'Member' }], gardenLots, [node('7c19000000000001', 'lot-1', 'Peppers')]);
    await signInTo(page);
    await page.goto('/devices');
    await expect(page.getByRole('list', { name: 'Nodes' }).getByRole('listitem')).toContainText('Peppers');
    await expect(page.getByText('Move', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Unassign' })).toHaveCount(0);
    await noControls(page);
  });
});
