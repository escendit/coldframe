import { loadLotDetail } from '$lib/server/lot-detail';
import type { PageServerLoad } from './$types';

/**
 * One Lot with its Node, latest Readings and 30-day history, read from the Server on every load.
 * When the Server cannot be reached the last good detail comes back marked stale (UX-DR79).
 */
export const load: PageServerLoad = async ({ locals, parent, cookies, params }) => {
  const { currentSite, sitesStale } = await parent();
  return loadLotDetail(locals, currentSite, params.lotId, cookies, sitesStale);
};
