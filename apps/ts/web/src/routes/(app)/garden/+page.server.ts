import { loadLots } from '$lib/server/site-settings';
import type { PageServerLoad } from './$types';

/** The current Site's Lots for the tile grid, in the Server's order (UX-DR20). */
export const load: PageServerLoad = async ({ locals, parent }) => {
  const { currentSite } = await parent();
  return loadLots(locals, currentSite);
};
