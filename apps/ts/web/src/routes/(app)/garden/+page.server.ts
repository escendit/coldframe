import { loadGarden } from '$lib/server/lots';
import { pauseActions } from '$lib/server/pause';
import type { Actions, PageServerLoad } from './$types';

/**
 * The current Site's Lots for the tile grid, in the Server's order (UX-DR20). When the Server
 * cannot be reached, the last good Lots come back marked stale (UX-DR79).
 */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite, sitesStale } = await parent();
  return loadGarden(locals, currentSite, cookies, sitesStale);
};

/**
 * Pause and Resume of the Site or a Device (Story 8.2). The Site menu of every page posts here, so
 * these two are the web app's one way to the Server's pause endpoints from the shell.
 */
export const actions = { ...pauseActions } satisfies Actions;
