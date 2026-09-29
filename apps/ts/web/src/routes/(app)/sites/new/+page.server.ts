import { createSiteAction, loadCreateSite } from '$lib/server/create-site';
import type { Actions, PageServerLoad } from './$types';

export const load: PageServerLoad = ({ cookies }) => loadCreateSite(cookies);

export const actions = {
  default: ({ locals, request, cookies }) => createSiteAction(locals, request, cookies),
} satisfies Actions;
