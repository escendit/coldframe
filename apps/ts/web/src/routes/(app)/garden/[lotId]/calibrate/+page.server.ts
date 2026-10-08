import { calibrateAction, loadCalibrate } from '$lib/server/calibrate';
import type { Actions, PageServerLoad } from './$types';

/**
 * The Calibrate flow of one Lot, read from the Server on every load: the Lot, its calibratable Sensor
 * and that Sensor's Calibration state with its recent stored Readings. A Member is sent back to the Lot.
 */
export const load: PageServerLoad = async ({ locals, parent, cookies, params }) => {
  const { currentSite } = await parent();
  return loadCalibrate(locals, currentSite, params.lotId, cookies);
};

export const actions = {
  record: ({ locals, request }) => calibrateAction(locals, request),
} satisfies Actions;
