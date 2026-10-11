import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import NotificationsPage from '../../../apps/ts/web/src/routes/(app)/settings/notifications/+page.svelte';
import alertFixture from '../../../packages/asyncapi/fixtures/live.notification.alert.json' with { type: 'json' };
import reminderFixture from '../../../packages/asyncapi/fixtures/live.notification.reminder.json' with { type: 'json' };
import summaryFixture from '../../../packages/asyncapi/fixtures/live.notification.summary.json' with { type: 'json' };
import {
  browserNotificationsKey,
  browserNotificationsOn,
  routeOf,
  showDelivered,
  turnOffBrowserNotifications,
  turnOnBrowserNotifications,
  type NotificationScope,
} from '$lib/browser-notifications';
import { t } from '$lib/i18n';
import type { NotificationDelivered } from '$lib/live/live';
import { read, webSrc } from './helpers.ts';

interface Shown {
  readonly title: string;
  readonly options?: NotificationOptions;
  onclick: (() => void) | null;
  closed: boolean;
}

/** A browser as the module sees it: the Notification API with a permission, and its storage. */
function browser(permission: NotificationPermission, answer: NotificationPermission | Error = permission) {
  const shown: Shown[] = [];
  const stored = new Map<string, string>();
  let prompts = 0;
  let focused = 0;
  class FakeNotification {
    static permission = permission;
    onclick: (() => void) | null = null;
    closed = false;

    readonly title: string;
    readonly options?: NotificationOptions;

    constructor(title: string, options?: NotificationOptions) {
      this.title = title;
      if (options !== undefined) {
        this.options = options;
      }
      shown.push(this);
    }

    static requestPermission(): Promise<NotificationPermission> {
      prompts += 1;
      if (answer instanceof Error) {
        return Promise.reject(answer);
      }
      FakeNotification.permission = answer;
      return Promise.resolve(answer);
    }

    close(): void {
      this.closed = true;
    }
  }
  const scope = {
    Notification: FakeNotification,
    localStorage: {
      getItem: (key: string) => stored.get(key) ?? null,
      setItem: (key: string, value: string) => void stored.set(key, value),
      removeItem: (key: string) => void stored.delete(key),
    },
    focus: () => {
      focused += 1;
    },
  };
  return {
    scope: scope as unknown as NotificationScope,
    api: FakeNotification,
    shown,
    stored,
    prompts: () => prompts,
    focused: () => focused,
  };
}

const alert = alertFixture.message as NotificationDelivered;

describe('Browser notifications while Coldframe is open', () => {
  test('UX-DR123 the toggle is off by default and nothing asks for permission before it is turned on', () => {
    const fresh = browser('default');
    expect(browserNotificationsOn(fresh.scope)).toBe(false);
    expect(showDelivered(alert, () => undefined, fresh.scope)).toBe(false);
    expect(fresh.prompts()).toBe(0);
    expect(fresh.shown).toEqual([]);
    // Granted elsewhere (another app of this origin) is still off: the toggle is the User's choice here.
    const granted = browser('granted');
    expect(browserNotificationsOn(granted.scope)).toBe(false);
    expect(showDelivered(alert, () => undefined, granted.scope)).toBe(false);
    expect(granted.shown).toEqual([]);
  });

  test('UX-DR123 turning it on asks the browser once; allowed, it stays on and is kept for this browser', async () => {
    const asked = browser('default', 'granted');
    expect(await turnOnBrowserNotifications(asked.scope)).toBe('on');
    expect(asked.prompts()).toBe(1);
    expect(asked.stored.get(browserNotificationsKey)).toBe('on');
    expect(browserNotificationsOn(asked.scope)).toBe(true);
    // Already allowed: no prompt at all.
    const allowed = browser('granted');
    expect(await turnOnBrowserNotifications(allowed.scope)).toBe('on');
    expect(allowed.prompts()).toBe(0);
    expect(browserNotificationsOn(allowed.scope)).toBe(true);
  });

  test('UX-DR89 a browser that blocks notifications returns the toggle to off with the reason', async () => {
    const denied = browser('denied');
    expect(await turnOnBrowserNotifications(denied.scope)).toBe('blocked');
    // The browser would not ask again: no prompt.
    expect(denied.prompts()).toBe(0);
    const refuses = browser('default', 'denied');
    expect(await turnOnBrowserNotifications(refuses.scope)).toBe('blocked');
    expect(refuses.prompts()).toBe(1);
    const none = { localStorage: refuses.scope.localStorage } as NotificationScope;
    expect(await turnOnBrowserNotifications(none)).toBe('blocked');
    for (const scope of [denied.scope, refuses.scope, none]) {
      expect(browserNotificationsOn(scope)).toBe(false);
    }
    expect(refuses.stored.has(browserNotificationsKey)).toBe(false);
    expect(t('notifications.browser.blocked')).toBe("Your browser blocks notifications for Coldframe. Allow them in this site's browser settings, then turn this on again.");
  });

  test('UX-DR89 a prompt that is dismissed returns the toggle to off without a notice', async () => {
    const dismissed = browser('default', 'default');
    expect(await turnOnBrowserNotifications(dismissed.scope)).toBe('dismissed');
    expect(browserNotificationsOn(dismissed.scope)).toBe(false);
    const broken = browser('default', new Error('not allowed here'));
    expect(await turnOnBrowserNotifications(broken.scope)).toBe('dismissed');
    expect(browserNotificationsOn(broken.scope)).toBe(false);
  });

  test('UX-DR123 permission revoked later: the toggle is off, nothing is shown, and it must be turned on again', async () => {
    const page = browser('default', 'granted');
    await turnOnBrowserNotifications(page.scope);
    page.api.permission = 'denied';
    expect(showDelivered(alert, () => undefined, page.scope)).toBe(false);
    expect(browserNotificationsOn(page.scope)).toBe(false);
    expect(page.shown).toEqual([]);
    // Allowed again in the browser settings does not turn it on behind the User's back.
    page.api.permission = 'granted';
    expect(browserNotificationsOn(page.scope)).toBe(false);
  });

  test('turning it off stops notifications and keeps the permission as it is', async () => {
    const page = browser('granted');
    await turnOnBrowserNotifications(page.scope);
    turnOffBrowserNotifications(page.scope);
    expect(browserNotificationsOn(page.scope)).toBe(false);
    expect(showDelivered(alert, () => undefined, page.scope)).toBe(false);
    expect(page.api.permission).toBe('granted');
  });

  test('a browser without storage keeps the choice for the open page only', async () => {
    const page = browser('granted');
    const scope = {
      Notification: page.api,
      get localStorage(): Storage {
        throw new DOMException('denied', 'SecurityError');
      },
    } as unknown as NotificationScope;
    expect(browserNotificationsOn(scope)).toBe(false);
    expect(await turnOnBrowserNotifications(scope)).toBe('on');
    expect(browserNotificationsOn(scope)).toBe(true);
    turnOffBrowserNotifications(scope);
    expect(browserNotificationsOn(scope)).toBe(false);
  });

  test.each([
    ['alert', alertFixture],
    ['reminder', reminderFixture],
    ['summary', summaryFixture],
  ] as const)('UX-DR123 a delivered %s shows the Server’s title and body, tagged with its collapse ID, and a click opens its route', async (_kind, fixture) => {
    const page = browser('granted');
    await turnOnBrowserNotifications(page.scope);
    const opened: string[] = [];
    const message = fixture.message as NotificationDelivered;
    expect(showDelivered(message, (route) => opened.push(route), page.scope)).toBe(true);
    expect(page.shown).toHaveLength(1);
    const notification = page.shown[0];
    expect(notification?.title).toBe(message.title);
    expect(notification?.options).toEqual({ body: message.body, tag: message.collapseId });
    expect(routeOf(message)).toBe(fixture.route);
    notification?.onclick?.();
    expect(page.focused()).toBe(1);
    expect(opened).toEqual([fixture.route]);
    expect(notification?.closed).toBe(true);
  });

  test('UX-DR123 one Alert appears once per device: a repeated delivery and a second tab carry the same tag', async () => {
    const first = browser('granted');
    const second = browser('granted');
    await turnOnBrowserNotifications(first.scope);
    await turnOnBrowserNotifications(second.scope);
    showDelivered(alert, () => undefined, first.scope);
    showDelivered(alert, () => undefined, first.scope);
    showDelivered(alert, () => undefined, second.scope);
    const tags = [...first.shown, ...second.shown].map((notification) => notification.options?.tag);
    expect(tags).toEqual([alert.collapseId, alert.collapseId, alert.collapseId]);
  });

  test('a kind this client does not know opens the overview of its Site, and a text without a body has none', async () => {
    expect(routeOf({ ...alert, kind: 'digest' })).toBe(`/garden?site=${alert.siteId}`);
    expect(routeOf({ kind: alert.kind, siteId: alert.siteId })).toBe(`/garden?site=${alert.siteId}`);
    const page = browser('granted');
    await turnOnBrowserNotifications(page.scope);
    showDelivered({ ...alert, body: '' }, () => undefined, page.scope);
    expect(page.shown[0]?.options).toEqual({ tag: alert.collapseId });
  });

  test('the Notification API is looked up when it is used, and the browser never writes a text of its own', () => {
    const source = read(`${webSrc}/lib/browser-notifications.ts`);
    expect(source).toMatch(/scope: NotificationScope = globalThis/u);
    expect(source).not.toMatch(/\bt\(|i18n|needs water/u);
    // Only a received notification.delivered is ever shown.
    const users = ['lib/live/index.ts', 'lib/live/live.ts', 'lib/live/hub.ts', 'routes/(app)/+layout.svelte', 'routes/(app)/settings/notifications/+page.svelte'].filter((file) => read(`${webSrc}/${file}`).includes('showDelivered('));
    expect(users).toEqual(['lib/live/index.ts']);
    expect(read(`${webSrc}/lib/live/index.ts`)).toMatch(/onNotification: \(message\) => \{\s*showDelivered\(message, options\.open\);/u);
    for (const file of ['lib/live/index.ts', 'lib/live/live.ts', 'lib/live/hub.ts', 'lib/browser-notifications.ts']) {
      expect(read(`${webSrc}/${file}`).match(/new \w*Notification\w*\(|new api\(/gu) ?? [], file).toHaveLength(file === 'lib/browser-notifications.ts' ? 1 : 0);
    }
  });
});

const home = { id: '0192a000-0000-7000-8000-00000000000a', name: 'Home garden', role: 'Owner' } as const;

function page(site: typeof home | null): string {
  const data = {
    user: { displayName: 'Simon', initials: 'S' },
    theme: 'system',
    sites: site === null ? [] : [site],
    currentSite: site,
    sitesNotice: null,
    sitesStale: null,
    settings: { window: { from: '07:00', to: '22:00' }, timeZoneConfirmed: false },
    siteSettings: site === null ? null : { muted: false, siteReminderCadence: 'daily' },
    notice: null,
    siteNotice: null,
  };
  return render(NotificationsPage, { props: { data, form: null, params: {} } as never }).body;
}

describe('My notifications: the browser toggle', () => {
  test('UX-DR123 the toggle is a switch named "Browser notifications while Coldframe is open", off until the browser is asked, with no notice', () => {
    const body = page(home);
    expect(body).toMatch(/<input[^>]*id="cf-browser-notifications"[^>]*type="checkbox"[^>]*role="switch"/u);
    expect(body).toMatch(/<span class="cf-toggle__label[^"]*">Browser notifications while Coldframe is open<\/span>/u);
    const toggle = /<input[^>]*id="cf-browser-notifications"[^>]*>/u.exec(body)?.[0] ?? '';
    expect(toggle).not.toMatch(/\schecked/u);
    // It is this browser's own: it is no field of a form and reaches no Server.
    expect(toggle).not.toMatch(/\sname=/u);
    expect(body).not.toContain('cf-browser-notifications-blocked');
    expect(body).not.toContain('Your browser blocks notifications');
    expect(body).toContain('Only in this browser, and only while Coldframe is open in a tab.');
  });

  test('UX-DR89 the blocked notice is an Inline notice from the catalogue, shown only after turning the toggle on failed', () => {
    const source = read(`${webSrc}/routes/(app)/settings/notifications/+page.svelte`);
    expect(source).toMatch(/\{#if browserBlocked\}\s*<InlineNotice id="cf-browser-notifications-blocked" message=\{t\('notifications\.browser\.blocked'\)\} \/>/u);
    expect(source).toMatch(/browserBlocked = result === 'blocked';/u);
    expect(source).toMatch(/browserOn = result === 'on';/u);
    // The prompt comes only from the User's change of the toggle.
    expect(source.match(/turnOnBrowserNotifications\(/gu)).toHaveLength(1);
    expect(source).toMatch(/onchange=\{\(checked: boolean\) => \{\s*void changeBrowserNotifications\(checked\);/u);
  });

  test('without a Site there is nothing to be notified of, so no toggle', () => {
    expect(page(null)).not.toContain('cf-browser-notifications');
  });
});
