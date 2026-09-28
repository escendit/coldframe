import type { MessageKey } from '$lib/i18n';
import { hasRole, type Role } from '$lib/roles';

export type SiteMenuAction = 'pause' | 'resume' | 'settings';

export interface SiteMenuItem {
  readonly action: SiteMenuAction;
  readonly label: MessageKey;
  /** Disabled only in stale mode, with the reason "Needs your Server". */
  readonly disabled: boolean;
  readonly href: string | null;
}

export interface SiteMenuOptions {
  /** Pause arrives with the Pause story; until then Pause and Resume are not rendered. */
  readonly pauseAvailable?: boolean;
  /** Whether the Site is paused, which turns Pause into Resume. */
  readonly paused?: boolean;
  /** Stale mode: every item stays visible but disabled. */
  readonly stale?: boolean;
}

/**
 * Items of the Site menu (UX-DR22): Pause or Resume for Administrator and Owner (hidden for
 * Members, and only once Pause exists), then Site settings. In stale mode every item is disabled.
 */
export function siteMenuItems(role: Role, options: SiteMenuOptions = {}): readonly SiteMenuItem[] {
  const disabled = options.stale === true;
  const items: SiteMenuItem[] = [];
  if (options.pauseAvailable === true && hasRole(role, 'Administrator')) {
    items.push(
      options.paused === true
        ? { action: 'resume', label: 'siteMenu.resume', disabled, href: null }
        : { action: 'pause', label: 'siteMenu.pause', disabled, href: null },
    );
  }
  items.push({ action: 'settings', label: 'siteMenu.settings', disabled, href: '/settings' });
  return items;
}
