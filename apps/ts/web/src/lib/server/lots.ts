/**
 * The web app's calls to the Server's Lot API. Server-only, like `sites.ts`: the browser never
 * calls the Server (AD-14). Lots arrive in the Server's order and are never re-sorted (UX-DR20).
 */
import type { Lot } from '@coldframe/api-client';
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
