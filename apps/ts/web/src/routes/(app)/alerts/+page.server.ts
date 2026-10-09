import { loadAlerts } from '$lib/server/alerts';
import type { PageServerLoad } from './$types';

/** The current Site's Alerts, read from the Server on every load. The Site comes from the shell. */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite } = await parent();
  return loadAlerts(locals, currentSite, cookies);
};
