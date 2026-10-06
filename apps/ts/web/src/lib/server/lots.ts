/**
 * The web app's calls to the Server's Lot API. Server-only, like `sites.ts`: the browser never
 * calls the Server (AD-14). Lots arrive in the Server's order and are never re-sorted (UX-DR20).
 */
import type { Cookies } from '@sveltejs/kit';
import type { Lot } from '@coldframe/api-client';
import type { LotsNotice } from '$lib/lots';
import type { Site } from '$lib/sites';
import { isTimeZone } from './create-site';
import { lotsKey, readThrough, recall, userKeyOf, type LastGoodDependencies } from './last-good';
import { signedOutRedirect, timeZoneCookieName } from './shell';
import { call, type SitesDependencies, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

/** The Site's live Lots, in the Server's order. */
export async function listLots(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<readonly Lot[]>> {
  const result = await call(locals, dependencies, (client) => client.GET('/sites/{siteId}/lots', { params: { path: { siteId } } }));
  return 'ok' in result ? { ok: result.ok.lots } : result;
}

/** Creates a Lot. One Idempotency-Key per attempt: the same key for a retry of the same attempt. */
export function createLot(locals: Locals, siteId: string, name: string, idempotencyKey: string, dependencies: SitesDependencies = {}): Promise<SitesResult<Lot>> {
  return call(locals, dependencies, (client) =>
    client.POST('/sites/{siteId}/lots', {
      params: { path: { siteId }, header: { 'Idempotency-Key': idempotencyKey } },
      body: { name },
    }),
  );
}

export function renameLot(locals: Locals, siteId: string, lotId: string, name: string, dependencies: SitesDependencies = {}): Promise<SitesResult<Lot>> {
  return call(locals, dependencies, (client) => client.PATCH('/sites/{siteId}/lots/{lotId}', { params: { path: { siteId, lotId } }, body: { name } }));
}

/** Removes a Lot. A Lot that holds a Node is refused with `lotClaimed` and nothing changes. */
export function removeLot(locals: Locals, siteId: string, lotId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<undefined>> {
  return call<undefined>(locals, dependencies, async (client) => {
    const { response } = await client.DELETE('/sites/{siteId}/lots/{lotId}', { params: { path: { siteId, lotId } } });
    return { response };
  });
}

/** The Site overview's data: the Lots, and whether they are live. */
export interface GardenData {
  /** In the Server's order; in stale mode the Lots it last listed. Empty when there is a notice. */
  readonly lots: readonly Lot[];
  readonly lotsNotice: LotsNotice | null;
  /** Stale mode: when the data shown was last read from the Server (ISO-8601). Null while live. */
  readonly staleSince: string | null;
  /** When this load ran, ISO-8601: the clock of the server render. */
  readonly loadedAt: string;
  /** The time zone the user chose on this browser, or null: the page then uses the browser's. */
  readonly timeZone: string | null;
}

/**
 * The Lots of the Site overview. A read that fails for transport reasons is retried once; when
 * that fails too the last good Lots of this user and Site are served as stale (UX-DR79), and the
 * first read that succeeds is live again. `shellStale` is set when the shell's Sites already came
 * from the last good answer: the Server just failed twice, so it is not asked again. A 401 signs
 * out; with nothing kept, a failure is the existing notice in place of the tiles.
 */
export async function loadGarden(
  locals: Locals,
  site: Site | null,
  cookies: Pick<Cookies, 'get'>,
  shellStale: string | null,
  dependencies: LastGoodDependencies = {},
): Promise<GardenData> {
  const chosen = cookies.get(timeZoneCookieName);
  const base = { loadedAt: (dependencies.now?.() ?? new Date()).toISOString(), timeZone: isTimeZone(chosen) ? chosen : null };
  if (site === null) {
    return { lots: [], lotsNotice: null, staleSince: null, ...base };
  }
  const user = userKeyOf(locals);
  const key = user === null ? null : lotsKey(user, site.id);
  if (shellStale !== null) {
    const kept = recall<readonly Lot[]>(key, dependencies);
    return kept === undefined ? { lots: [], lotsNotice: 'unreachable', staleSince: shellStale, ...base } : { lots: kept.value, lotsNotice: null, staleSince: kept.fetchedAt, ...base };
  }
  const result = await readThrough(key, () => listLots(locals, site.id, dependencies), dependencies);
  if ('ok' in result) {
    return { lots: result.ok, lotsNotice: null, staleSince: result.stale ? result.fetchedAt : null, ...base };
  }
  if (result.error === 'unauthorized') {
    signedOutRedirect();
  }
  return { lots: [], lotsNotice: result.error === 'unreachable' || result.error === 'certificate' ? result.error : 'unavailable', staleSince: null, ...base };
}
