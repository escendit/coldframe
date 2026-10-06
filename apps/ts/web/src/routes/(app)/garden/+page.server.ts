import { loadGarden } from '$lib/server/lots';
import type { PageServerLoad } from './$types';

/**
 * The current Site's Lots for the tile grid, in the Server's order (UX-DR20). When the Server
 * cannot be reached, the last good Lots come back marked stale (UX-DR79).
 */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite, sitesStale } = await parent();
  return loadGarden(locals, currentSite, cookies, sitesStale);
};
