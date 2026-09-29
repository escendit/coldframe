import type { Site } from '@coldframe/api-client';
import type { MessageKey } from '$lib/i18n';

export type { Site, SiteRole } from '@coldframe/api-client';

/** The Site a signed-in page shows, and the user's Sites in the Server's order. */
export interface SitesData {
  readonly sites: readonly Site[];
  readonly currentSite: Site | null;
  /** Set when the Server could not list the Sites. */
  readonly sitesNotice: 'unreachable' | 'certificate' | 'unavailable' | null;
}

/**
 * The shell's notice when the Sites could not be listed: its copy, and whether it offers Try
 * again. A certificate failure is never retried insecurely (AD-13), so it has no action.
 */
export function sitesNoticeOf(notice: SitesData['sitesNotice']): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'sites.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    case 'unavailable':
      return { message: 'sites.unavailable', tryAgain: true };
    default:
      return null;
  }
}

/** Trimmed length limits of a Site name (the Server's contract). */
export const siteNameMaxLength = 100;

export type NameError = 'blank' | 'tooLong' | 'invalid';

/** Checks a Site name the way the Server does: 1 to 100 characters after trimming. */
export function checkSiteName(name: string): NameError | null {
  const trimmed = name.trim();
  if (trimmed === '') {
    return 'blank';
  }
  return trimmed.length > siteNameMaxLength ? 'tooLong' : null;
}

/** Catalogue key of each Role's label. */
export const roleLabel: Readonly<Record<Site['role'], MessageKey>> = {
  Owner: 'role.Owner',
  Administrator: 'role.Administrator',
  Member: 'role.Member',
};
