import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import type {
  FailingReads,
  FakeAlert,
  FakeCalibration,
  FakeCalibrationPost,
  FakeCalibrationReading,
  FakeDevice,
  FakeDeviceAction,
  FakeHealthAlert,
  FakeLiveControl,
  FakeLiveState,
  FakeLot,
  FakeNotificationsSeed,
  FakeNotificationsState,
  FakeSilenceWindowPut,
  FakeSite,
  FakeSitePost,
  FakeThresholds,
  FakeThresholdsPut,
  Mode,
} from '../fixtures/fake-idp.ts';
import { idpOrigin } from '../fixtures/ports.ts';

/** Switches the fake IdP's behaviour. */
export async function setMode(mode: Mode): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/mode`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ mode }),
  });
  expect(response.ok).toBe(true);
}

/**
 * Resets the fake Server's Sites (the default seed, one Site, or exactly `sites`), its Lots and its
 * Devices, and clears its Alerts. `devicesStatus` makes the Devices list answer that status instead.
 */
export async function resetSites(
  sites?: readonly FakeSite[],
  lots: readonly FakeLot[] = [],
  devices: readonly FakeDevice[] = [],
  devicesStatus: number | null = null,
  calibrations: readonly FakeCalibration[] = [],
  thresholds: readonly FakeThresholds[] = [],
): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/sites`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ ...(sites === undefined ? {} : { sites }), lots, devices, calibrations, thresholds, ...(devicesStatus === null ? {} : { devicesStatus }) }),
  });
  expect(response.ok).toBe(true);
}

/** The fake Server's Sites and Lots, every `POST /sites` and `POST …/lots`, and how often the Lots were read, since the last reset. */
export async function serverSites(): Promise<{
  sites: FakeSite[];
  posts: FakeSitePost[];
  lots: FakeLot[];
  lotPosts: FakeSitePost[];
  lotReads: number;
  devices: FakeDevice[];
  deviceActions: FakeDeviceAction[];
  calibrations: FakeCalibration[];
  calibrationPosts: FakeCalibrationPost[];
  thresholds: FakeThresholds[];
  thresholdPuts: FakeThresholdsPut[];
  silenceWindowPuts: FakeSilenceWindowPut[];
}> {
  const response = await fetch(`${idpOrigin}/control/sites`);
  return (await response.json()) as {
    sites: FakeSite[];
    posts: FakeSitePost[];
    lots: FakeLot[];
    lotPosts: FakeSitePost[];
    lotReads: number;
    devices: FakeDevice[];
    deviceActions: FakeDeviceAction[];
    calibrations: FakeCalibration[];
    calibrationPosts: FakeCalibrationPost[];
    thresholds: FakeThresholds[];
    thresholdPuts: FakeThresholdsPut[];
    silenceWindowPuts: FakeSilenceWindowPut[];
  };
}

/**
 * Seeds the fake Server's Alerts and (Story 7.2) its Health Alerts; `alertsStatus` makes their list answer that status
 * instead. `resetSites` clears them all.
 */
export async function setAlerts(alerts: readonly FakeAlert[], alertsStatus: number | null = null, healthAlerts: readonly FakeHealthAlert[] = []): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/alerts`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ alerts, healthAlerts, ...(alertsStatus === null ? {} : { alertsStatus }) }),
  });
  expect(response.ok).toBe(true);
}

/** How often the Alerts were read from the fake Server since they were last seeded. */
export async function alertReads(): Promise<number> {
  const response = await fetch(`${idpOrigin}/control/alerts`);
  return ((await response.json()) as { alertReads: number }).alertReads;
}

/**
 * Seeds the fake Server's notification settings (the user's own, per Site, and the Sites' cadences) and the
 * status its next write answers. A field left out is the default; `resetSites` resets them all.
 */
export async function setNotifications(seed: FakeNotificationsSeed = {}): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/notifications`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(seed),
  });
  expect(response.ok).toBe(true);
}

/** The fake Server's notification settings, and every write to them since they were last seeded. */
export async function serverNotifications(): Promise<FakeNotificationsState> {
  const response = await fetch(`${idpOrigin}/control/notifications`);
  return (await response.json()) as FakeNotificationsState;
}

/** What the fake hub (Story 6.6) was asked, and how many connections are open. */
export async function liveState(): Promise<FakeLiveState> {
  const response = await fetch(`${idpOrigin}/control/live`);
  return (await response.json()) as FakeLiveState;
}

/** Drives the fake hub; answers how many connections a sent message reached. */
export async function controlLive(control: FakeLiveControl): Promise<number> {
  const response = await fetch(`${idpOrigin}/control/live`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(control),
  });
  expect(response.ok).toBe(true);
  return ((await response.json()) as { delivered: number }).delivered;
}

/** Waits until exactly `count` tabs hold an open hub connection. */
export async function liveConnections(count: number): Promise<void> {
  await expect.poll(async () => (await liveState()).connections, { timeout: 15_000 }).toBe(count);
}

/** The fake hub sends one message to every open connection. */
export async function sendLive(target: 'notification.delivered' | 'readmodel.changed', argument: unknown): Promise<number> {
  return controlLive({ send: { target, argument } });
}

/** A new stored Reading of a Sensor arrives at the fake Server. */
export async function pushCalibrationReading(sensorId: string, reading: FakeCalibrationReading): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/calibration-reading`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ sensorId, reading }),
  });
  expect(response.ok).toBe(true);
}

/** Replaces the Sensors a Lot's detail shows, as when a calibrated Reading is stored. */
export async function setLotSensors(lotId: string, sensors: readonly unknown[]): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/lot-sensors`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ lotId, sensors }),
  });
  expect(response.ok).toBe(true);
}

/** Changes fields of a Lot at the fake Server, as when its first calibrated Reading is stored. */
export async function patchLot(lotId: string, fields: Partial<FakeLot>): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/lot-fields`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ lotId, fields }),
  });
  expect(response.ok).toBe(true);
}

/**
 * Makes the fake Server drop the connection on its Sites and Lots reads (`all`), on the Lots only
 * (`lots`), or answer again (`none`): a Server that stops being reachable after it answered.
 * `resetSites` turns it off.
 */
export async function failReads(failing: FailingReads): Promise<void> {
  const response = await fetch(`${idpOrigin}/control/reads`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ failing }),
  });
  expect(response.ok).toBe(true);
}

/** Every token string the fake IdP has issued so far. */
export async function issuedTokens(): Promise<string[]> {
  const response = await fetch(`${idpOrigin}/control/tokens`);
  const body = (await response.json()) as { tokens: string[] };
  return body.tokens;
}

/** The SIGN IN button (uppercase is CSS only, so its accessible name is "Sign in"). */
export function signInButton(page: Page) {
  return page.getByRole('button', { name: 'Sign in' });
}

/** Opens the app signed out and signs in through the fake IdP; ends on Garden. */
export async function signIn(page: Page, path = '/'): Promise<void> {
  await page.goto(path);
  await expect(page).toHaveURL(/\/signin/u);
  await signInButton(page).click();
  await expect(page).toHaveURL(/\/garden$/u);
}

/** The Sign-in card's inline notice. */
export function notice(page: Page) {
  return page.locator('.cf-signin-card .cf-inline-notice');
}

export const copy = {
  unreachable: "Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.",
  certificate: "Your Server's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain.",
  keycloak: "Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed.",
  signedOut: "You're signed out. Sign in again to see live data.",
} as const;

/** Pages of this story, by path, that need a signed-in session. */
export const shellPages = ['/garden', '/alerts', '/devices', '/members', '/settings', '/settings/notifications', '/settings/site', '/settings/appearance', '/sites/new'] as const;

/** Sets the theme cookie for the app origin before a page loads. */
export async function useTheme(page: Page, theme: 'light' | 'dark', origin: string): Promise<void> {
  await page.context().addCookies([{ name: 'cf_theme', value: theme, url: origin }]);
}

/** The largest text size on the web: 200 % zoom (UX-DR126), on a 1280 × 800 window. */
export async function largestText(page: Page): Promise<void> {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.addStyleTag({ content: 'html { zoom: 2 }' });
}

export async function axeClean(page: Page, label: string): Promise<void> {
  const results = await new AxeBuilder({ page }).analyze();
  const blocking = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`);
  expect(blocking, label).toEqual([]);
}

/** Nothing sticks out of the page sideways, and no text is clipped by its box. */
export async function nothingClipped(page: Page, label: string): Promise<void> {
  const problems = await page.evaluate(() => {
    const found: string[] = [];
    if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) {
      found.push('page scrolls sideways');
    }
    for (const element of document.querySelectorAll<HTMLElement>('main *, header *')) {
      const style = getComputedStyle(element);
      if (style.overflowX === 'hidden' || style.overflowY === 'hidden' || style.textOverflow === 'ellipsis') {
        if (element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1) {
          found.push(element.outerHTML.slice(0, 80));
        }
      }
    }
    return found;
  });
  expect(problems, label).toEqual([]);
}
