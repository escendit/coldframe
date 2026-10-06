import { loadDevices } from '$lib/server/devices';
import type { PageServerLoad } from './$types';

/** The current Site's Devices, read from the Server on every load. The Site and Role come from the shell. */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite } = await parent();
  return loadDevices(locals, currentSite, cookies);
};
