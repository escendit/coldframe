import { inviteAction, loadMembers } from '$lib/server/members';
import type { Actions, PageServerLoad } from './$types';

/** The current Site's Memberships (Owner only; anyone else goes to Garden). The Site and Role come from the shell. */
export const load: PageServerLoad = async ({ locals, parent }) => {
  const { currentSite } = await parent();
  return loadMembers(locals, currentSite);
};

export const actions = {
  invite: ({ locals, request }) => inviteAction(locals, request),
} satisfies Actions;
