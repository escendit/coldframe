import type { MessageKey } from '$lib/i18n';
import { hasRole, type Role } from '$lib/roles';

export type SiteMenuAction = 'pause' | 'resume' | 'settings';

export interface SiteMenuItem {
  readonly action: SiteMenuAction;
  readonly label: MessageKey;
  /** Disabled only in stale mode, with the reason "Needs your Server". */
  readonly disabled: boolean;
  /** Where the item leads. Null for Pause and Resume, which act where they are: the shell opens the Pause modal or sends the Resume. */
  readonly href: string | null;
}

export interface SiteMenuOptions {
  /** Whether the Site is paused (`Site.paused`, from the Server), which turns Pause into Resume. */
  readonly paused?: boolean;
  /** Stale mode: every item stays visible but disabled. */
  readonly stale?: boolean;
}

/**
 * Items of the Site menu (UX-DR22): Pause or Resume for Administrator and Owner (hidden for Members,
 * never disabled for them), then Site settings, which opens the Site settings surface. In stale mode
 * every item is disabled.
 */
export function siteMenuItems(role: Role, options: SiteMenuOptions = {}): readonly SiteMenuItem[] {
  const disabled = options.stale === true;
  const items: SiteMenuItem[] = [];
  if (hasRole(role, 'Administrator')) {
    items.push(
      options.paused === true
        ? { action: 'resume', label: 'siteMenu.resume', disabled, href: null }
        : { action: 'pause', label: 'siteMenu.pause', disabled, href: null },
    );
  }
  items.push({ action: 'settings', label: 'siteMenu.settings', disabled, href: '/settings/site' });
  return items;
}
