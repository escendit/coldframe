/**
 * "Browser notifications while Coldframe is open" (Story 6.6). The choice belongs to this browser,
 * like the permission it follows: it is kept in the browser's storage and never reaches the
 * Server. A notification is shown only for a `notification.delivered` message, with the Server's
 * words; nothing here writes a text.
 */
import type { NotificationDelivered } from '@coldframe/api-client';

/** Where this browser keeps the choice. Present with `on` while turned on, absent otherwise. */
export const browserNotificationsKey = 'cf_browser_notifications';

const on = 'on';

interface NotificationLike {
  onclick: ((event: Event) => unknown) | null;
  close(): void;
}

interface NotificationApi {
  readonly permission: NotificationPermission;
  requestPermission(): Promise<NotificationPermission>;
  new (title: string, options?: NotificationOptions): NotificationLike;
}

/** What the module needs of the browser; `globalThis` in the app, and looked up at every call. */
export interface NotificationScope {
  readonly Notification?: NotificationApi;
  readonly localStorage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  readonly focus?: () => void;
}

/** The choice when the browser gives no storage: it holds for the open page only. */
const unstored = new WeakMap<object, boolean>();

function storageOf(scope: NotificationScope): NotificationScope['localStorage'] {
  try {
    return scope.localStorage;
  } catch {
    return undefined;
  }
}

function stored(scope: NotificationScope): boolean {
  try {
    const storage = storageOf(scope);
    if (storage !== undefined) {
      return storage.getItem(browserNotificationsKey) === on;
    }
  } catch {
    // Storage that cannot be read is no storage.
  }
  return unstored.get(scope) === true;
}

function store(scope: NotificationScope, value: boolean): void {
  unstored.set(scope, value);
  try {
    const storage = storageOf(scope);
    if (value) {
      storage?.setItem(browserNotificationsKey, on);
    } else {
      storage?.removeItem(browserNotificationsKey);
    }
  } catch {
    // Kept for the open page only.
  }
}

/**
 * Whether notifications are on in this browser: turned on here, and still allowed by the browser.
 * A permission that was taken away turns the choice off, so allowing it again does not turn it on
 * behind the User's back.
 */
export function browserNotificationsOn(scope: NotificationScope = globalThis): boolean {
  if (!stored(scope)) {
    return false;
  }
  if (scope.Notification?.permission !== 'granted') {
    store(scope, false);
    return false;
  }
  return true;
}

/**
 * The User turned the toggle on: the only place that asks the browser for permission. `blocked`
 * when the browser has no notifications, blocks them or the User refused; `dismissed` when the
 * prompt was closed without an answer. Either way the toggle goes back to off.
 */
export async function turnOnBrowserNotifications(scope: NotificationScope = globalThis): Promise<'on' | 'blocked' | 'dismissed'> {
  const api = scope.Notification;
  if (api === undefined || api.permission === 'denied') {
    return 'blocked';
  }
  let permission: NotificationPermission = api.permission;
  if (permission !== 'granted') {
    try {
      permission = await api.requestPermission();
    } catch {
      return 'dismissed';
    }
  }
  if (permission === 'granted') {
    store(scope, true);
    return 'on';
  }
  return permission === 'denied' ? 'blocked' : 'dismissed';
}

export function turnOffBrowserNotifications(scope: NotificationScope = globalThis): void {
  store(scope, false);
}

/**
 * Where a click leads: an Alert or a Reminder to Lot detail, a summary (and a kind this client does
 * not know) to the Site overview. `?site=` makes the shell switch to the notification's Site.
 */
export function routeOf(message: Pick<NotificationDelivered, 'kind' | 'siteId' | 'lotId'>): string {
  const site = new URLSearchParams({ site: message.siteId }).toString();
  const lotId = message.kind === 'alert' || message.kind === 'reminder' ? (message.lotId ?? '') : '';
  return lotId === '' ? `/garden?${site}` : `/garden/${encodeURIComponent(lotId)}?${site}`;
}

/**
 * Shows one delivery as a browser notification, when they are on in this browser. The collapse ID
 * is the tag, so a repeated delivery or a second tab replaces the notification instead of adding
 * one. A click focuses the tab and opens the route. Returns whether it was shown.
 */
export function showDelivered(message: NotificationDelivered, open: (route: string) => void, scope: NotificationScope = globalThis): boolean {
  const api = scope.Notification;
  if (api === undefined || !browserNotificationsOn(scope)) {
    return false;
  }
  try {
    const notification = new api(message.title, { ...(message.body === '' ? {} : { body: message.body }), tag: message.collapseId });
    notification.onclick = () => {
      scope.focus?.();
      notification.close();
      open(routeOf(message));
    };
    return true;
  } catch {
    // A browser that refuses the constructor (some mobile browsers) shows nothing.
    return false;
  }
}
