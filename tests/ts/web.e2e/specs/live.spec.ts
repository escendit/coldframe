import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type Page } from '@playwright/test';
import { shortLivedSeconds, type FakeLot } from '../fixtures/fake-idp.ts';
import { appUrl } from '../fixtures/ports.ts';
import { alertReads, axeClean, controlLive, liveConnections, liveState, nothingClipped, patchLot, resetSites, sendLive, serverSites, setAlerts, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

// Site IDs no other spec uses (the web app keeps the last good Sites and Lots per user and Site).
const homeId = '0192a000-0000-7000-8000-0000000066a1';
const home = { id: homeId, name: 'Home garden', role: 'Owner' } as const;
const allotmentId = '0192a000-0000-7000-8000-0000000066a2';
const allotment = { id: allotmentId, name: 'Allotment', role: 'Member' } as const;
const tomatoesId = '0192a000-0000-7000-8000-0000000066b1';
const beansId = '0192a000-0000-7000-8000-0000000066b2';

const tomatoes: FakeLot = { id: tomatoesId, siteId: homeId, name: 'Tomatoes', status: 'ok', statusSince: '2026-10-06T03:45:00.000Z', lastReadingAt: '2026-10-06T05:03:00.000Z', moisturePercent: 35, lowThresholdPercent: 30 };
const beans: FakeLot = { id: beansId, siteId: allotmentId, name: 'Beans', status: 'ok', statusSince: '2026-10-06T03:45:00.000Z', lastReadingAt: '2026-10-06T05:03:00.000Z', moisturePercent: 45, lowThresholdPercent: 30 };

const toggleName = 'Browser notifications while Coldframe is open';
const blockedCopy = "Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again.";
const storageKey = 'cf_browser_notifications';

interface Fixture {
  readonly message: { readonly kind: string; readonly siteId: string; readonly siteName: string; readonly lotId?: string; readonly alertId?: string; readonly collapseId: string; readonly title: string; readonly body: string };
}

/** The contract's example of a delivery, for a Site and Lot the fake Server holds. */
function delivery(kind: 'alert' | 'reminder' | 'summary', siteId = homeId, lotId = tomatoesId): Fixture['message'] {
  const path = fileURLToPath(new URL(`../../../../packages/asyncapi/fixtures/live.notification.${kind}.json`, import.meta.url));
  const { message } = JSON.parse(readFileSync(path, 'utf8')) as Fixture;
  return { ...message, siteId, ...(message.lotId === undefined ? {} : { lotId }) };
}

interface Recorded {
  readonly title: string;
  readonly body: string | null;
  readonly tag: string | null;
}

/**
 * Replaces the browser's Notification API before any page script runs: it starts with `permission`,
 * answers a prompt with `answer`, and records what is shown. The permission outlives a reload.
 */
async function stubNotifications(page: Page, permission: NotificationPermission, answer: NotificationPermission = permission): Promise<void> {
  await page.addInitScript(
    ({ initial, answered }) => {
      const state = window as unknown as { cfShown: { title: string; body: string | null; tag: string | null; click: () => void }[]; cfPrompts: number };
      state.cfShown = [];
      state.cfPrompts = 0;
      class StubNotification {
        static get permission(): string {
          return sessionStorage.getItem('cf-test-permission') ?? initial;
        }

        static requestPermission(): Promise<string> {
          state.cfPrompts += 1;
          sessionStorage.setItem('cf-test-permission', answered);
          return Promise.resolve(answered);
        }

        onclick: (() => void) | null = null;

        constructor(title: string, options?: { body?: string; tag?: string }) {
          state.cfShown.push({ title, body: options?.body ?? null, tag: options?.tag ?? null, click: () => this.onclick?.() });
        }

        close(): void {
          // Nothing to close.
        }
      }
      Object.defineProperty(window, 'Notification', { configurable: true, writable: true, value: StubNotification });
    },
    { initial: permission, answered: answer },
  );
}

async function shown(page: Page): Promise<Recorded[]> {
  return page.evaluate(() => (window as unknown as { cfShown: Recorded[] }).cfShown.map(({ title, body, tag }) => ({ title, body, tag })));
}

async function prompts(page: Page): Promise<number> {
  return page.evaluate(() => (window as unknown as { cfPrompts: number }).cfPrompts);
}

async function signInTo(page: Page): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

async function openMyNotifications(page: Page): Promise<void> {
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await page.getByRole('link', { name: /My notifications/u }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'My notifications' })).toBeVisible();
}

/** Marks the document, so a test can tell that the page was never loaded again. */
async function mark(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as unknown as { cfMarked: boolean }).cfMarked = true;
  });
}

async function stillMarked(page: Page): Promise<boolean> {
  return page.evaluate(() => (window as unknown as { cfMarked?: boolean }).cfMarked === true);
}

test.use({ timezoneId: 'Europe/Zurich' });

test.beforeEach(async () => {
  await setMode('normal');
  await resetSites([home], [tomatoes]);
  await controlLive({ reset: true, drop: true });
});

test.afterEach(async ({ page }) => {
  // Close the tab first, so its connection is gone before the next test counts them.
  await page.close();
  await controlLive({ reset: true, drop: true });
  await resetSites();
});

test.describe('Live updates through the web origin', () => {
  test('the browser reaches the hub only on the web origin, and the Server sees the session’s access token and nothing else of it', async ({ page }) => {
    const hubRequests: string[] = [];
    const elsewhere: string[] = [];
    page.on('request', (request) => {
      const url = new URL(request.url());
      if (url.pathname.startsWith('/live')) {
        hubRequests.push(`${request.method()} ${url.origin}${url.pathname}`);
      }
      if (url.origin !== appUrl && !url.pathname.includes('/realms/')) {
        elsewhere.push(request.url());
      }
    });
    await signInTo(page);
    await liveConnections(1);

    expect(hubRequests).toContain(`POST ${appUrl}/live/negotiate`);
    expect(hubRequests).toContain(`GET ${appUrl}/live`);
    expect(hubRequests.every((entry) => entry.includes(appUrl))).toBe(true);
    expect(elsewhere).toEqual([]);
    // No WebSocket: Server-Sent Events only.
    expect(hubRequests.some((entry) => entry.startsWith('GET') && entry.endsWith('/live'))).toBe(true);

    const { requests } = await liveState();
    expect(requests.map((request) => `${request.method} ${request.path}`).slice(0, 3)).toEqual(['POST /live/negotiate', 'GET /live', 'POST /live']);
    for (const request of requests) {
      expect(request.bearer, `${request.method} ${request.path}`).toBe(true);
      // No cookie, and no token in a URL.
      expect(request.headers.filter((name) => !['accept', 'authorization', 'content-type'].includes(name))).toEqual([]);
      expect(request.query.filter((name) => !['id', 'negotiateVersion'].includes(name))).toEqual([]);
    }
    // Nothing connects twice, and the page shows no sign of the connection.
    expect(requests.filter((request) => request.path === '/live/negotiate')).toHaveLength(1);
    await expect(page.getByText(/connected|reconnect|live again/iu)).toHaveCount(0);
  });

  test('without a session the hub route answers 401 and the Server is never asked', async () => {
    const anonymous = await playwrightRequest.newContext({ baseURL: appUrl });
    for (const [method, path] of [
      ['POST', '/live/negotiate?negotiateVersion=1'],
      ['GET', '/live?id=x'],
      ['POST', '/live?id=x'],
      ['DELETE', '/live?id=x'],
    ] as const) {
      const response = await anonymous.fetch(path, { method, headers: { origin: appUrl }, maxRedirects: 0 });
      expect(response.status(), `${method} ${path}`).toBe(401);
    }
    await anonymous.dispose();
    expect((await liveState()).requests).toEqual([]);
  });

  test('a hint for what the open page shows refetches it without a reload; any other hint does nothing', async ({ page }) => {
    await signInTo(page);
    await liveConnections(1);
    await mark(page);
    await expect(page.getByRole('link', { name: /^Tomatoes, OK/u })).toBeVisible();
    const before = (await serverSites()).lotReads;

    // A resource no page shows is dropped. (Since Story 7.2 a `devices` hint does concern the overview: `silence.spec.ts`.)
    expect(await sendLive('readmodel.changed', { resource: 'members', id: '7c19000000000001', version: 11 })).toBe(1);
    await patchLot(tomatoesId, { status: 'needsWater', moisturePercent: 20 });
    await page.waitForTimeout(500);
    expect((await serverSites()).lotReads).toBe(before);
    await expect(page.getByRole('link', { name: /^Tomatoes, OK/u })).toBeVisible();

    await sendLive('readmodel.changed', { resource: 'lots', id: tomatoesId, version: 12 });
    await expect(page.getByRole('link', { name: /^Tomatoes, needs water/u })).toBeVisible();
    expect(await stillMarked(page)).toBe(true);

    // A burst of hints during one refetch causes one more, not one each.
    const settled = (await serverSites()).lotReads;
    for (let version = 13; version < 23; version += 1) {
      await sendLive('readmodel.changed', { resource: 'readings', id: tomatoesId, version });
    }
    await page.waitForTimeout(1000);
    const reads = (await serverSites()).lotReads - settled;
    expect(reads).toBeGreaterThanOrEqual(1);
    expect(reads).toBeLessThan(10);
    expect(await stillMarked(page)).toBe(true);
  });

  test('Lot detail follows its own Lot only, and the Alerts list follows Alerts', async ({ page }) => {
    await signInTo(page);
    await liveConnections(1);
    await page.getByRole('link', { name: /^Tomatoes, OK/u }).click();
    await expect(page).toHaveURL(new RegExp(`/garden/${tomatoesId}$`, 'u'));
    await expect(page.getByRole('heading', { level: 1, name: 'Tomatoes' })).toBeVisible();
    await mark(page);

    await patchLot(tomatoesId, { name: 'Tomatoes south' });
    await sendLive('readmodel.changed', { resource: 'lots', id: beansId, version: 30 });
    await sendLive('readmodel.changed', { resource: 'alerts', id: 'a-1', version: 31 });
    await page.waitForTimeout(500);
    await expect(page.getByRole('heading', { level: 1, name: 'Tomatoes', exact: true })).toBeVisible();
    await sendLive('readmodel.changed', { resource: 'lots', id: tomatoesId, version: 32 });
    await expect(page.getByRole('heading', { level: 1, name: 'Tomatoes south' })).toBeVisible();

    await page.getByRole('link', { name: 'Alerts', exact: true }).click();
    await expect(page.getByText('No open Alerts.')).toBeVisible();
    await setAlerts([{ id: 'a-1', siteId: homeId, kind: 'threshold', side: 'low', quantity: 'soil_moisture', lotId: tomatoesId, lotName: 'Tomatoes south', deviceId: '7c19000000000001', openedAt: new Date().toISOString() }]);
    const reads = await alertReads();
    await sendLive('readmodel.changed', { resource: 'devices', id: '7c19000000000001', version: 33 });
    await page.waitForTimeout(500);
    expect(await alertReads()).toBe(reads);
    await sendLive('readmodel.changed', { resource: 'alerts', id: 'a-1', version: 34 });
    await expect(page.getByRole('link', { name: /^Tomatoes south needs water/u })).toBeVisible();
    // The same connection served every page: one per tab.
    expect(await stillMarked(page)).toBe(true);
    expect((await liveState()).requests.filter((request) => request.path === '/live/negotiate')).toHaveLength(1);
  });

  test('a lost connection comes back quietly and the open page refetches once', async ({ page }) => {
    await signInTo(page);
    await liveConnections(1);
    await mark(page);
    const notices = await page.locator('.cf-inline-notice').count();
    await patchLot(tomatoesId, { status: 'needsWater', moisturePercent: 20 });
    // The Server closes the connection, as when its token expired; the hint for the change was never sent.
    await controlLive({ drop: true });
    await liveConnections(1);
    await expect(page.getByRole('link', { name: /^Tomatoes, needs water/u })).toBeVisible();
    expect(await stillMarked(page)).toBe(true);
    // No notice says that the connection was lost or is back.
    await expect(page.locator('.cf-inline-notice')).toHaveCount(notices);
    const negotiations = (await liveState()).requests.filter((request) => request.path === '/live/negotiate');
    expect(negotiations).toHaveLength(2);
    expect(negotiations.every((request) => request.bearer)).toBe(true);
  });

  test('a reconnect after the access token expired carries the refreshed token, not the expired one', async ({ page }) => {
    // Access tokens live for a few seconds here, and the refresh is accepted.
    await setMode('short-refresh');
    await signInTo(page);
    await liveConnections(1);
    await mark(page);
    const first = (await liveState()).requests.find((request) => request.path === '/live/negotiate');
    expect(first?.token?.expired).toBe(false);

    // The token the connection was opened with runs out; the Server closes the connection.
    await page.waitForTimeout(shortLivedSeconds * 1_000 + 1_000);
    await controlLive({ drop: true });
    await liveConnections(1);

    const negotiations = (await liveState()).requests.filter((request) => request.path === '/live/negotiate');
    expect(negotiations).toHaveLength(2);
    const again = negotiations[1];
    expect(again?.bearer).toBe(true);
    expect(again?.token?.expired).toBe(false);
    expect(again?.token?.id).not.toBe(first?.token?.id);
    // The page was not loaded again to get there, and the session is still signed in.
    expect(await stillMarked(page)).toBe(true);
    await expect(page).toHaveURL(/\/garden$/u);
  });

  test('a 401 from the hub ends the retries until the next navigation', async ({ page }) => {
    await signInTo(page);
    await liveConnections(1);
    const notices = await page.locator('.cf-inline-notice').count();
    await controlLive({ refuse: true, drop: true });
    const negotiations = async (): Promise<number> => (await liveState()).requests.filter((request) => request.path === '/live/negotiate').length;
    await expect.poll(negotiations).toBe(2);
    // Longer than the first back-off steps: nothing is tried again.
    await page.waitForTimeout(4000);
    expect(await negotiations()).toBe(2);
    await expect(page.locator('.cf-inline-notice')).toHaveCount(notices);

    await controlLive({ refuse: false });
    await page.getByRole('link', { name: 'Alerts', exact: true }).click();
    await expect(page).toHaveURL(/\/alerts$/u);
    await liveConnections(1);
    expect(await negotiations()).toBe(3);
  });
});

test.describe('Browser notifications while Coldframe is open', () => {
  for (const theme of themes) {
    test(`UX-DR123 I turn the toggle on, the browser asks once, and a delivery shows the Server’s words (${theme})`, async ({ page }) => {
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });
      await stubNotifications(page, 'default', 'granted');
      await signInTo(page);
      await liveConnections(1);
      await openMyNotifications(page);

      const toggle = page.getByRole('switch', { name: toggleName });
      await expect(toggle).not.toBeChecked();
      await expect(page.getByText('Only in this browser, and only while Coldframe is open in a tab.')).toBeVisible();
      // Nothing asked for permission yet, and a delivery shows nothing while the toggle is off.
      expect(await prompts(page)).toBe(0);
      expect(await sendLive('notification.delivered', delivery('alert'))).toBe(1);
      await page.waitForTimeout(300);
      expect(await shown(page)).toEqual([]);

      await toggle.check();
      await expect(toggle).toBeChecked();
      expect(await prompts(page)).toBe(1);
      expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBe('on');
      await expect(page.locator('#cf-browser-notifications-blocked')).toHaveCount(0);
      await axeClean(page, `my notifications, browser notifications on (${theme})`);
      await nothingClipped(page, `my notifications, browser notifications on (${theme})`);
      // The click left the page scrolled; the full-page capture starts at the top.
      await page.evaluate(() => {
        window.scrollTo(0, 0);
      });
      await expect(page).toHaveScreenshot(`my-notifications-browser-on-${theme}.png`, { fullPage: true });

      // Kept for this browser: still on after a reload, without another prompt.
      await page.reload();
      await expect(page.getByRole('switch', { name: toggleName })).toBeChecked();
      expect(await prompts(page)).toBe(0);
      await liveConnections(1);

      const alert = delivery('alert');
      await sendLive('notification.delivered', alert);
      await expect.poll(() => shown(page)).toEqual([{ title: 'Tomatoes needs water', body: '~20 % in the soil, your low is 30 %.', tag: alert.collapseId }]);
      // The same delivery again carries the same tag, so the browser replaces the notification.
      await sendLive('notification.delivered', alert);
      await expect.poll(async () => (await shown(page)).map((notification) => notification.tag)).toEqual([alert.collapseId, alert.collapseId]);

      // A click opens Lot detail of the Alert's Lot.
      await page.evaluate(() => {
        (window as unknown as { cfShown: { click: () => void }[] }).cfShown[0]?.click();
      });
      await expect(page).toHaveURL(new RegExp(`/garden/${tomatoesId}$`, 'u'));
      await expect(page.getByRole('heading', { level: 1, name: 'Tomatoes' })).toBeVisible();

      // Turned off again: nothing more is shown, and nothing stays stored.
      await openMyNotifications(page);
      await page.getByRole('switch', { name: toggleName }).uncheck();
      expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBeNull();
      const before = (await shown(page)).length;
      await sendLive('notification.delivered', delivery('reminder'));
      await page.waitForTimeout(300);
      expect(await shown(page)).toHaveLength(before);
    });

    test(`UX-DR89 a browser that blocks notifications returns the toggle to off and says why (${theme})`, async ({ page }) => {
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });
      await stubNotifications(page, 'default', 'denied');
      await signInTo(page);
      await liveConnections(1);
      await openMyNotifications(page);

      const toggle = page.getByRole('switch', { name: toggleName });
      await toggle.click();
      await expect(toggle).not.toBeChecked();
      const notice = page.locator('#cf-browser-notifications-blocked');
      await expect(notice).toHaveText(blockedCopy);
      await expect(page.getByRole('alert')).toHaveText(blockedCopy);
      expect(await prompts(page)).toBe(1);
      expect(await page.evaluate((key) => localStorage.getItem(key), storageKey)).toBeNull();
      await axeClean(page, `my notifications, notifications blocked (${theme})`);
      await nothingClipped(page, `my notifications, notifications blocked (${theme})`);
      // The click left the page scrolled; the full-page capture starts at the top.
      await page.evaluate(() => {
        window.scrollTo(0, 0);
      });
      await expect(page).toHaveScreenshot(`my-notifications-blocked-${theme}.png`, { fullPage: true });

      // Blocked now: the browser is not asked again, and a delivery shows nothing.
      await toggle.click();
      await expect(toggle).not.toBeChecked();
      await expect(notice).toHaveText(blockedCopy);
      expect(await prompts(page)).toBe(1);
      await sendLive('notification.delivered', delivery('alert'));
      await page.waitForTimeout(300);
      expect(await shown(page)).toEqual([]);
    });
  }

  test('UX-DR89 a prompt that is dismissed returns the toggle to off without a notice', async ({ page }) => {
    await stubNotifications(page, 'default', 'default');
    await signInTo(page);
    await openMyNotifications(page);
    const toggle = page.getByRole('switch', { name: toggleName });
    await toggle.click();
    await expect(toggle).not.toBeChecked();
    expect(await prompts(page)).toBe(1);
    await expect(page.locator('#cf-browser-notifications-blocked')).toHaveCount(0);
  });

  test('UX-DR89 a browser without the Notification API is told the same', async ({ page }) => {
    await page.addInitScript(() => {
      Reflect.deleteProperty(window, 'Notification');
    });
    await signInTo(page);
    await openMyNotifications(page);
    const toggle = page.getByRole('switch', { name: toggleName });
    await toggle.click();
    await expect(toggle).not.toBeChecked();
    await expect(page.locator('#cf-browser-notifications-blocked')).toHaveText(blockedCopy);
  });

  test('UX-DR123 permission taken away later: the toggle renders off and nothing is shown', async ({ page }) => {
    await stubNotifications(page, 'default', 'granted');
    await signInTo(page);
    await openMyNotifications(page);
    await page.getByRole('switch', { name: toggleName }).check();
    await expect(page.getByRole('switch', { name: toggleName })).toBeChecked();
    // The User blocks Coldframe in the browser's settings.
    await page.evaluate(() => {
      sessionStorage.setItem('cf-test-permission', 'denied');
    });
    await page.reload();
    await expect(page.getByRole('switch', { name: toggleName })).not.toBeChecked();
    await expect(page.locator('#cf-browser-notifications-blocked')).toHaveCount(0);
    await liveConnections(1);
    await sendLive('notification.delivered', delivery('alert'));
    await page.waitForTimeout(300);
    expect(await shown(page)).toEqual([]);
  });

  test('UX-DR123 a summary opens the overview of its Site, and two tabs show one Alert under one tag', async ({ page, context }) => {
    await resetSites([home, allotment], [tomatoes, beans]);
    await stubNotifications(page, 'granted');
    await signInTo(page);
    await openMyNotifications(page);
    await page.getByRole('switch', { name: toggleName }).check();
    // Already allowed: turning it on asks nothing.
    expect(await prompts(page)).toBe(0);

    const second = await context.newPage();
    await stubNotifications(second, 'granted');
    await second.goto('/alerts');
    await liveConnections(2);

    const alert = delivery('alert');
    expect(await sendLive('notification.delivered', alert)).toBe(2);
    await expect.poll(() => shown(page)).toHaveLength(1);
    await expect.poll(() => shown(second)).toHaveLength(1);
    // One per device: both tabs name the same tag, which the browser shows once.
    expect((await shown(page))[0]?.tag).toBe(alert.collapseId);
    expect((await shown(second))[0]?.tag).toBe(alert.collapseId);
    await second.close();
    await liveConnections(1);

    // The summary of another Site: the click switches to that Site and opens its overview.
    const summary = delivery('summary', allotmentId);
    await sendLive('notification.delivered', summary);
    await expect.poll(async () => (await shown(page)).at(-1)).toEqual({ title: summary.title, body: summary.body, tag: summary.collapseId });
    await page.evaluate(() => {
      (window as unknown as { cfShown: { click: () => void }[] }).cfShown.at(-1)?.click();
    });
    await expect(page).toHaveURL(/\/garden$/u);
    await expect(page.getByRole('link', { name: /^Beans, OK/u })).toBeVisible();
  });
});
