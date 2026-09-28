import { redirect, type Cookies } from '@sveltejs/kit';
import type { Site, SitesData } from '$lib/sites';
import type { DisplayUser } from '$lib/user';
import { oidcRoutes, signinUrl } from './auth-handle';
import { guardShell } from './guard';
import { listSites, type SitesDependencies } from './sites';
import { getConfig } from './runtime';

/** The Site this browser shows. First-party, httpOnly; holds a Site ID only. */
export const siteCookieName = 'cf_site';

/** The time zone the user confirmed or picked on this browser (AD-11: the User's, not the Site's). */
export const timeZoneCookieName = 'cf_time_zone';

export const createSitePath = '/sites/new';

const oneYearSeconds = 60 * 60 * 24 * 365;

type Locals = Pick<App.Locals, 'session' | 'sessionEnded'>;

function secureCookies(): boolean {
  try {
    return getConfig().sessionCookieSecure;
  } catch {
    return true;
  }
}

/** Remembers a per-browser choice (current Site, time zone) for a year. */
export function rememberChoice(cookies: Cookies, name: string, value: string): void {
  cookies.set(name, value, { path: '/', httpOnly: true, sameSite: 'lax', secure: secureCookies(), maxAge: oneYearSeconds });
}

/**
 * A 401 from the Server means the session is no longer accepted: sign out locally and land on
 * Sign in with the signed-out notice, the same end as a session that expired (UX-DR93).
 */
export function signedOutRedirect(): never {
  redirect(303, `${oidcRoutes.signout}?redirect_uri=${encodeURIComponent(signinUrl('signed-out'))}`);
}

/** The listed Site with this ID, else the first one: a stale or unknown choice falls back. */
export function pickCurrentSite(sites: readonly Site[], chosen: string | undefined): Site | null {
  return sites.find((site) => site.id === chosen) ?? sites[0] ?? null;
}

/**
 * Loads the shell: the guard, then the caller's Sites from the Server. No Membership → Create
 * Site. `?site=` switches the current Site for this browser and is dropped from the URL.
 */
export async function loadShell(
  locals: Locals,
  url: URL,
  cookies: Cookies,
  dependencies: SitesDependencies = {},
): Promise<{ user: DisplayUser } & SitesData> {
  const { user } = guardShell(locals, url);
  const result = await listSites(locals, dependencies);
  if ('error' in result) {
    if (result.error === 'unauthorized') {
      signedOutRedirect();
    }
    const sitesNotice = result.error === 'unreachable' || result.error === 'certificate' ? result.error : 'unavailable';
    return { user, sites: [], currentSite: null, sitesNotice };
  }
  const sites = result.ok;
  const onCreateSite = url.pathname === createSitePath;
  if (sites.length === 0 && !onCreateSite) {
    redirect(303, createSitePath);
  }

  const requested = url.searchParams.get('site');
  if (requested !== null) {
    if (sites.some((site) => site.id === requested)) {
      rememberChoice(cookies, siteCookieName, requested);
    }
    const target = new URL(url);
    target.searchParams.delete('site');
    redirect(303, `${target.pathname}${target.search}`);
  }

  return { user, sites, currentSite: pickCurrentSite(sites, cookies.get(siteCookieName)), sitesNotice: null };
}
