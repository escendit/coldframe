import { expect, test, type Page } from '@playwright/test';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, serverNotifications, setMode, setNotifications, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

const homeId = '0192a000-0000-7000-8000-00000000000a';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const mePath = '/me/notification-settings';
const sitePath = `/sites/${homeId}/notification-settings`;

test.use({ timezoneId: 'Europe/Zurich' });

test.beforeEach(async () => {
  await setMode('normal');
  await resetSites([home]);
});

test.afterEach(async () => {
  // Other specs expect the default Site and untouched notification settings.
  await resetSites();
});

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

async function openMyNotifications(page: Page): Promise<void> {
  await page.goto('/settings');
  await page.getByRole('link', { name: /My notifications/u }).click();
  await expect(page).toHaveURL(/\/settings\/notifications$/u);
  await expect(page.getByRole('heading', { level: 1, name: 'My notifications' })).toBeVisible();
}

/** The writes the fake Server received on one path, as `METHOD body` lines. */
async function writesTo(path: string): Promise<string[]> {
  return (await serverNotifications()).writes.filter((write) => write.path === path).map((write) => `${write.method} ${JSON.stringify(write.body)}`);
}

async function toTop(page: Page): Promise<void> {
  // Submitting leaves the page scrolled; the full-page capture starts at the top.
  await page.evaluate(() => {
    window.scrollTo(0, 0);
  });
}

test.describe('My notifications', () => {
  for (const theme of themes) {
    test(`UX-DR72 UX-DR47 UX-DR48 UX-DR49 UX-DR50 I set my window, confirm my zone, mute the Site and pick my cadence, and the Server keeps each (${theme}, largest text)`, async ({ page }) => {
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });
      await signInTo(page);
      await openMyNotifications(page);
      await largestText(page);

      // A new User: 07:00 to 22:00, and the browser's zone proposed for Confirm or Change.
      const from = page.getByLabel('From', { exact: true });
      const to = page.getByLabel('To', { exact: true });
      await expect(from).toHaveValue('07:00');
      await expect(to).toHaveValue('22:00');
      await expect(page.getByText('07:00 to 22:00', { exact: true })).toBeVisible();
      await expect(page.getByText('Outside this window, anything waits for one summary at 07:00.')).toBeVisible();
      await expect(page.getByText('Is your time zone Europe/Zurich?')).toBeVisible();
      const mute = page.getByRole('switch', { name: 'Mute Home garden' });
      await expect(mute).not.toBeChecked();
      const cadence = page.getByRole('group', { name: 'My Reminder cadence' });
      await expect(cadence.getByRole('button')).toHaveText(['Use Site setting', 'Daily', 'Every 2 days']);
      await expect(cadence.getByRole('button', { name: 'Use Site setting' })).toHaveAttribute('aria-pressed', 'true');
      await expect(cadence).toContainText('Site setting: Daily');
      // The 24 h bar is decorative.
      await expect(page.locator('.cf-window__bar')).toHaveAttribute('aria-hidden', 'true');
      // Stories 6.5 and 6.6 own these.
      await expect(page.getByText(/Browser notifications|Notifications are off/u)).toHaveCount(0);

      await axeClean(page, `my notifications, new User (${theme})`);
      await nothingClipped(page, `my notifications, new User (${theme})`);
      await expect(page).toHaveScreenshot(`my-notifications-${theme}.png`, { fullPage: true });

      // "From 06:30" alone is a window: the end stays 22:00. The preview follows what is typed.
      await from.fill('06:30');
      await to.fill('');
      await expect(page.getByText('06:30 to 22:00', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Save' }).click();
      await expect(page.getByLabel('To', { exact: true })).toHaveValue('22:00');
      await expect(page.getByText('Outside this window, anything waits for one summary at 06:30.')).toBeVisible();

      await page.getByRole('button', { name: 'Confirm' }).click();
      await expect(page.getByText('Your time zone is Europe/Zurich.')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm' })).toHaveCount(0);

      await mute.check();
      await expect(page.getByRole('switch', { name: 'Mute Home garden' })).toBeChecked();
      await cadence.getByRole('button', { name: 'Every 2 days' }).click();
      await expect(cadence.getByRole('button', { name: 'Every 2 days' })).toHaveAttribute('aria-pressed', 'true');

      const state = await serverNotifications();
      expect(state.settings).toEqual({ window: { from: '06:30', to: '22:00' }, timeZone: 'Europe/Zurich', timeZoneConfirmed: true });
      expect(state.siteSettings).toEqual([{ siteId: homeId, muted: true, reminderCadence: 'every2Days' }]);
      // Each control sent the other current value too, as the Server replaces both.
      expect(await writesTo(sitePath)).toEqual(['PUT {"muted":true}', 'PUT {"muted":true,"reminderCadence":"every2Days"}']);
      expect(await writesTo(mePath)).toContain('PATCH {"window":{"from":"06:30"}}');
      expect(await writesTo(mePath)).toContain('PATCH {"timeZone":"Europe/Zurich"}');

      // The next load from the Server shows every change.
      await page.reload();
      await largestText(page);
      await expect(page.getByLabel('From', { exact: true })).toHaveValue('06:30');
      await expect(page.getByText('Your time zone is Europe/Zurich.')).toBeVisible();
      await expect(page.getByRole('switch', { name: 'Mute Home garden' })).toBeChecked();
      await expect(page.getByRole('group', { name: 'My Reminder cadence' }).getByRole('button', { name: 'Every 2 days' })).toHaveAttribute('aria-pressed', 'true');
      await axeClean(page, `my notifications, set (${theme})`);
      await nothingClipped(page, `my notifications, set (${theme})`);
      await toTop(page);
      await expect(page).toHaveScreenshot(`my-notifications-set-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR72 a Member has the same surface: the settings are their own', async ({ page }) => {
    await resetSites([{ id: homeId, name: 'Allotment', role: 'Member' }]);
    await setNotifications({ cadences: [{ siteId: homeId, cadence: 'every2Days' }] });
    await signInTo(page);
    await openMyNotifications(page);
    const cadence = page.getByRole('group', { name: 'My Reminder cadence' });
    await expect(cadence).toContainText('Site setting: Every 2 days');
    await cadence.getByRole('button', { name: 'Daily' }).click();
    await expect(cadence.getByRole('button', { name: 'Daily' })).toHaveAttribute('aria-pressed', 'true');
    await page.getByRole('switch', { name: 'Mute Allotment' }).check();
    await expect(page.getByRole('switch', { name: 'Mute Allotment' })).toBeChecked();
    expect((await serverNotifications()).siteSettings).toEqual([{ siteId: homeId, muted: true, reminderCadence: 'daily' }]);
    // Back to the Site setting: the request carries no cadence of my own.
    await cadence.getByRole('button', { name: 'Use Site setting' }).click();
    await expect(cadence.getByRole('button', { name: 'Use Site setting' })).toHaveAttribute('aria-pressed', 'true');
    expect((await serverNotifications()).siteSettings).toEqual([{ siteId: homeId, muted: true }]);
  });

  test('UX-DR47 a window that ends before it starts stays on the fields and nothing reaches the Server', async ({ page }) => {
    await signInTo(page);
    await openMyNotifications(page);
    await page.getByLabel('From', { exact: true }).fill('09:00');
    await page.getByLabel('To', { exact: true }).fill('08:00');
    // Not a window: the preview stays on the one in force.
    await expect(page.getByText('07:00 to 22:00', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('The start must be before the end.')).toBeVisible();
    await expect(page.getByLabel('To', { exact: true })).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByLabel('From', { exact: true })).toHaveValue('09:00');
    expect((await writesTo(mePath)).filter((write) => write.includes('window'))).toEqual([]);
  });

  test('UX-DR48 Change picks another zone from the searchable list; it is my choice from then on', async ({ page }) => {
    await signInTo(page);
    await openMyNotifications(page);
    await page.getByRole('button', { name: 'Change' }).click();
    await page.getByRole('textbox', { name: 'Search time zones' }).fill('vienna');
    await page.getByRole('list', { name: 'Time zones' }).getByRole('button', { name: 'Europe/Vienna' }).click();
    await expect(page.getByText('Your time zone is Europe/Vienna.')).toBeVisible();
    expect((await serverNotifications()).settings).toMatchObject({ timeZone: 'Europe/Vienna', timeZoneConfirmed: true });
    // The other pages format with the Server's zone.
    const cookies = await page.context().cookies(appUrl);
    expect(cookies.find((cookie) => cookie.name === 'cf_time_zone')?.value).toBe('Europe%2FVienna');
  });

  test('UX-DR72 a save that fails returns the control to the Server’s value, and Try again repeats it', async ({ page }) => {
    await signInTo(page);
    await openMyNotifications(page);

    await setNotifications({ settings: (await serverNotifications()).settings, failNext: { site: 503, mine: 503 } });
    await page.getByRole('switch', { name: 'Mute Home garden' }).check();
    const notice = page.locator('#cf-notifications');
    await expect(notice).toContainText('Not saved: your Server could not be reached. Nothing was changed.');
    await expect(page.getByRole('switch', { name: 'Mute Home garden' })).not.toBeChecked();
    await notice.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('switch', { name: 'Mute Home garden' })).toBeChecked();
    await expect(notice).toHaveCount(0);
    expect((await serverNotifications()).siteSettings).toEqual([{ siteId: homeId, muted: true }]);

    await page.getByLabel('From', { exact: true }).fill('06:30');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(notice).toContainText('Not saved: your Server could not be reached. Nothing was changed.');
    await expect(page.getByLabel('From', { exact: true })).toHaveValue('07:00');
    await notice.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByLabel('From', { exact: true })).toHaveValue('06:30');
    expect((await serverNotifications()).settings.window).toEqual({ from: '06:30', to: '22:00' });
  });
});

test.describe('Hand-over of the time zone (DW-23)', () => {
  test('UX-DR48 a zone confirmed on this browser before goes to the Server as my choice, once per browser session', async ({ page }) => {
    await page.context().addCookies([{ name: 'cf_time_zone', value: 'Europe/Vienna', url: appUrl }]);
    await signInTo(page);
    expect(await writesTo(mePath)).toEqual(['PATCH {"timeZone":"Europe/Vienna"}']);
    await page.goto('/alerts');
    await openMyNotifications(page);
    await expect(page.getByText('Your time zone is Europe/Vienna.')).toBeVisible();
    expect(await writesTo(mePath)).toEqual(['PATCH {"timeZone":"Europe/Vienna"}']);
  });

  test('UX-DR48 with no choice anywhere the browser zone goes as the detected one and is still asked about', async ({ page }) => {
    await signInTo(page);
    expect(await writesTo(mePath)).toEqual(['PATCH {"detectedTimeZone":"Europe/Zurich"}']);
    expect((await serverNotifications()).settings).toMatchObject({ timeZone: 'Europe/Zurich', timeZoneConfirmed: false });
    await openMyNotifications(page);
    await expect(page.getByText('Is your time zone Europe/Zurich?')).toBeVisible();
    const cookies = await page.context().cookies(appUrl);
    expect(cookies.find((cookie) => cookie.name === 'cf_time_zone')).toBeUndefined();
  });

  test('UX-DR48 a zone I chose elsewhere wins on a browser in another zone, and nothing is sent', async ({ page }) => {
    await setNotifications({ settings: { window: { from: '07:00', to: '22:00' }, timeZone: 'America/New_York', timeZoneConfirmed: true } });
    await page.context().addCookies([{ name: 'cf_time_zone', value: 'Europe/Vienna', url: appUrl }]);
    await signInTo(page);
    await openMyNotifications(page);
    await expect(page.getByText('Your time zone is America/New_York.')).toBeVisible();
    await expect(page.getByText(/Is your time zone/u)).toHaveCount(0);
    expect(await writesTo(mePath)).toEqual([]);
    const cookies = await page.context().cookies(appUrl);
    expect(cookies.find((cookie) => cookie.name === 'cf_time_zone')?.value).toBe('America%2FNew_York');
  });
});
