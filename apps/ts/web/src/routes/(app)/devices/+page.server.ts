import { deviceAction, loadDevices, silenceWindowAction } from '$lib/server/devices';
import { pauseActions } from '$lib/server/pause';
import type { Actions, PageServerLoad } from './$types';

/** The current Site's Devices, read from the Server on every load. The Site and Role come from the shell. */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite } = await parent();
  return loadDevices(locals, currentSite, cookies);
};

export const actions = {
  moveNode: ({ locals, request }) => deviceAction('moveNode', locals, request),
  unassignNode: ({ locals, request }) => deviceAction('unassignNode', locals, request),
  setSilenceWindow: ({ locals, request }) => silenceWindowAction(locals, request),
  // Pause and Resume of a Device row, or of the Site from the Pause modal (Story 8.2).
  ...pauseActions,
} satisfies Actions;
