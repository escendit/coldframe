import { loadSiteSettings, siteSettingsAction } from '$lib/server/site-settings';
import type { Actions, PageServerLoad } from './$types';

/** The current Site's Lots, its Reminder cadence and a fresh Create Lot key. The Site and Role come from the shell. */
export const load: PageServerLoad = async ({ locals, parent }) => {
  const { currentSite } = await parent();
  return loadSiteSettings(locals, currentSite);
};

export const actions = {
  renameSite: ({ locals, request }) => siteSettingsAction('renameSite', locals, request),
  createLot: ({ locals, request }) => siteSettingsAction('createLot', locals, request),
  renameLot: ({ locals, request }) => siteSettingsAction('renameLot', locals, request),
  removeLot: ({ locals, request }) => siteSettingsAction('removeLot', locals, request),
  setReminderCadence: ({ locals, request }) => siteSettingsAction('setReminderCadence', locals, request),
} satisfies Actions;
