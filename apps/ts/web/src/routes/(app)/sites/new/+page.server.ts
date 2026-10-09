import { createSiteAction, loadCreateSite } from '$lib/server/create-site';
import type { Actions, PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ cookies, parent }) => {
  // The layout's load hands the time zone over and writes `cf_time_zone` from the Server's answer. Loads run
  // at the same time, so this one waits for it: a User whose chosen zone the Server holds is shown that zone.
  await parent();
  return loadCreateSite(cookies);
};

export const actions = {
  default: ({ locals, request, cookies }) => createSiteAction(locals, request, cookies),
} satisfies Actions;
