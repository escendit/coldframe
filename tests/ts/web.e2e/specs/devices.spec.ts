import { expect, test, type Page } from '@playwright/test';
import type { FakeDevice } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, setMode, signInButton, useTheme } from './helpers.ts';

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

function row(page: Page, id: string) {
  return page.getByRole('list', { name: 'Hubs' }).getByRole('listitem').filter({ hasText: id });
}

async function noControls(page: Page): Promise<void> {
  await expect(page.locator('main').locator('form, button, input, select, textarea')).toHaveCount(0);
}

test.describe('Devices', () => {
  for (const theme of themes) {
    test(`UX-DR30 UX-DR65 UX-DR85 an Owner sees the Hubs with ID, status and last seen, and the mobile app notice (${theme}, largest text)`, async ({ page }) => {
      await resetSites(
        [home],
        [],
        [
          hub(hubId, true, '07:02'),
          hub(silentHubId, false, '06:40'),
          { id: '0a0a0a0a0a0a0a0a', siteId: homeId, kind: 'node', online: false },
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
      // By Device ID; the Node of the Server's list is not shown.
      await expect(rows).toHaveCount(2);
      await expect(rows.nth(0)).toContainText(silentHubId);
      await expect(rows.nth(0)).toContainText('Offline');
      await expect(rows.nth(0)).toContainText('Last seen 6:40 AM');
      await expect(rows.nth(1)).toContainText(hubId);
      await expect(rows.nth(1)).toContainText('Online');
      await expect(rows.nth(1)).toContainText('Last seen 7:02 AM');
      await expect(page.getByRole('heading', { name: 'Nodes' })).toHaveCount(0);

      // The full ID in the mono face; the status is a word with an icon, not a colour.
      await expect(rows.nth(1).locator('.cf-devices__id')).toHaveText(hubId);
      await expect(rows.nth(1).locator('.cf-devices__id')).toHaveCSS('font-family', /Ubuntu Mono/u);
      await expect(rows.nth(1).locator('[data-icon="checkmark--outline"]')).toBeVisible();
      await expect(rows.nth(0).locator('[data-icon="help"]')).toBeVisible();

      await expect(page.locator('#cf-devices-web-notice')).toHaveText('Adding a Hub or Node needs the Coldframe mobile app.');
      await noControls(page);

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
});
