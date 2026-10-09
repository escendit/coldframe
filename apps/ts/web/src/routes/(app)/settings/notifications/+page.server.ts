import { loadNotifications, notificationsAction } from '$lib/server/notifications';
import type { Actions, PageServerLoad } from './$types';

/** My settings and, with a current Site, my settings for it. The Site comes from the shell. */
export const load: PageServerLoad = async ({ locals, parent, cookies }) => {
  const { currentSite } = await parent();
  return loadNotifications(locals, currentSite, cookies);
};

export const actions = {
  saveWindow: ({ locals, request, cookies }) => notificationsAction('saveWindow', locals, request, cookies),
  chooseTimeZone: ({ locals, request, cookies }) => notificationsAction('chooseTimeZone', locals, request, cookies),
  setMute: ({ locals, request, cookies }) => notificationsAction('setMute', locals, request, cookies),
  setCadence: ({ locals, request, cookies }) => notificationsAction('setCadence', locals, request, cookies),
} satisfies Actions;
