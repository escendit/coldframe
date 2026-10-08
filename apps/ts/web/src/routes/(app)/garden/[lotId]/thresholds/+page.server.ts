import { loadThresholds, saveThresholdsAction } from '$lib/server/thresholds';
import type { Actions, PageServerLoad } from './$types';

/**
 * The Thresholds of one Lot, read from the Server on every load: the Lot and each Sensor's Thresholds in force.
 * A Member may open it and sees the values read-only; the Server answers 403 to a change of theirs.
 */
export const load: PageServerLoad = async ({ locals, parent, cookies, params }) => {
  const { currentSite } = await parent();
  return loadThresholds(locals, currentSite, params.lotId, cookies);
};

export const actions = {
  save: ({ locals, request }) => saveThresholdsAction(locals, request),
} satisfies Actions;
