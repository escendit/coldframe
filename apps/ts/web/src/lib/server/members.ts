/**
 * The web app's calls to the Server's Members endpoints (Stories 9.1 and 9.2), and the Members page's load and
 * `invite`, `changeRole` and `removeMember` form actions. Server-only, like `sites.ts`: the browser never calls the Server (AD-14). The
 * Server authorizes both by the caller's Role on the Site (Owner), never by token claims (AD-4).
 */
import { fail, redirect, type ActionFailure } from '@sveltejs/kit';
import type { SiteMember } from '@coldframe/api-client';
import {
  checkEmail,
  isInviteRole,
  type InviteFailure,
  type InviteNotice,
  type InviteSuccess,
  type MemberChangeAction,
  type MemberChangeFailure,
  type MemberChangeNotice,
  type MemberChangeSuccess,
  type MemberEntry,
  type MembersNotice,
  membersAccessOf,
} from '$lib/members';
import type { Role } from '$lib/roles';
import type { Site } from '$lib/sites';
import { userKeyOf } from './last-good';
import { gardenPath, signedOutRedirect } from './shell';
import { call, type SitesDependencies, type SitesError, type SitesResult } from './sites';

type Locals = Pick<App.Locals, 'session'>;

/** `alreadyInvited` is the invitation endpoint's 409: that person is already invited or a Member. */
export type InviteError = Exclude<SitesError, 'lotClaimed' | 'lastOwner' | 'memberNotFound'> | 'alreadyInvited';

/** The Site's Memberships, Owners first, then by name, as the Server orders them. */
export async function listMembers(locals: Locals, siteId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<readonly SiteMember[]>> {
  const result = await call(locals, dependencies, (client) => client.GET('/sites/{siteId}/members', { params: { path: { siteId } } }));
  return 'ok' in result ? { ok: result.ok.members } : result;
}

/** Asks the Server to have Keycloak invite `email` with `role`. 202 has no body; the Server keeps nothing. */
export async function inviteToSite(
  locals: Locals,
  siteId: string,
  email: string,
  role: Role,
  dependencies: SitesDependencies = {},
): Promise<{ readonly ok: undefined } | { readonly error: InviteError }> {
  const result = await call(locals, dependencies, (client) => client.POST('/sites/{siteId}/invitations', { params: { path: { siteId } }, body: { email, role } }));
  if ('ok' in result) {
    return { ok: undefined };
  }
  // The only 409 of this endpoint is `already-invited`.
  switch (result.error) {
    case 'lotClaimed':
    case 'lastOwner':
      return { error: 'alreadyInvited' };
    case 'memberNotFound':
      return { error: 'notFound' };
    default:
      return { error: result.error };
  }
}

/** Changes a Member's Role (Owner). 204 has no body; the same Role again saves nothing (Story 9.2). */
export async function changeMemberRole(locals: Locals, siteId: string, userId: string, role: Role, dependencies: SitesDependencies = {}): Promise<SitesResult<undefined>> {
  const result = await call(locals, dependencies, (client) => client.PUT('/sites/{siteId}/members/{userId}', { params: { path: { siteId, userId } }, body: { role } }));
  return 'ok' in result ? { ok: undefined } : result;
}

/** Removes a Membership (Owner). 204 has no body (Story 9.2). */
export async function removeSiteMember(locals: Locals, siteId: string, userId: string, dependencies: SitesDependencies = {}): Promise<SitesResult<undefined>> {
  const result = await call(locals, dependencies, (client) => client.DELETE('/sites/{siteId}/members/{userId}', { params: { path: { siteId, userId } } }));
  return 'ok' in result ? { ok: undefined } : result;
}

export interface MembersData {
  readonly members: readonly MemberEntry[];
  readonly membersNotice: MembersNotice | null;
}

/**
 * The Members page (UX-DR73): Owners only. Anyone else, and a 403 from a Role lowered meanwhile, lands
 * on Garden, since the page is hidden for them (UX-DR76). A 401 signs out; any other failure is a notice
 * in place of the tiles, and the Invite form stays.
 */
export async function loadMembers(locals: Locals, site: Site | null, dependencies: SitesDependencies = {}): Promise<MembersData> {
  if (site === null || !membersAccessOf(site.role)) {
    redirect(303, gardenPath);
  }
  const result = await listMembers(locals, site.id, dependencies);
  if ('ok' in result) {
    const self = userKeyOf(locals);
    return { members: result.ok.map((member) => ({ ...member, self: self !== null && member.userId === self })), membersNotice: null };
  }
  switch (result.error) {
    case 'unauthorized':
      return signedOutRedirect();
    // The Role was lowered, or the Site deleted meanwhile: Garden reads the Sites again.
    case 'forbidden':
    case 'notFound':
      redirect(303, gardenPath);
      break;
    case 'unreachable':
    case 'certificate':
      return { members: [], membersNotice: result.error };
    default:
      return { members: [], membersNotice: 'unavailable' };
  }
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

const statusOf: Readonly<Record<InviteNotice, number>> = {
  alreadyInvited: 409,
  unavailable: 503,
  forbidden: 403,
  siteNotFound: 404,
  unexpected: 502,
  unreachable: 502,
  certificate: 502,
};

function memberChangeNoticeOf(error: Exclude<SitesError, 'unauthorized'>): MemberChangeNotice {
  switch (error) {
    case 'lastOwner':
    case 'unavailable':
    case 'forbidden':
    case 'unreachable':
    case 'certificate':
      return error;
    case 'memberNotFound':
      return 'memberGone';
    case 'notFound':
      return 'siteNotFound';
    // A 400 is a form this page did not send; a 409 other than last-owner and a 422 cannot come from these calls.
    case 'validation':
    case 'lotClaimed':
    case 'keyReused':
    case 'unexpected':
      return 'unexpected';
  }
}

const changeStatusOf: Readonly<Record<MemberChangeNotice, number>> = {
  lastOwner: 409,
  memberGone: 404,
  unavailable: 503,
  forbidden: 403,
  siteNotFound: 404,
  unexpected: 502,
  unreachable: 502,
  certificate: 502,
};

/**
 * Role change and removal (UX-DR73, Story 9.2): `siteId`, `siteName`, `userId`, `name`, `role` (the Role picked,
 * or for a removal the Member's Role) and `self`. A refusal keeps the editor on that Member with the Role picked;
 * a success answers what the notice names, and whether it was the signed-in User's own Membership.
 */
export async function memberChangeAction(
  action: MemberChangeAction,
  locals: Locals,
  request: Request,
  dependencies: SitesDependencies = {},
): Promise<MemberChangeSuccess | ActionFailure<MemberChangeFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const siteName = text(form, 'siteName');
  const userId = text(form, 'userId');
  const name = text(form, 'name');
  const submittedRole = text(form, 'role');
  const role: Role = isInviteRole(submittedRole) ? submittedRole : 'Member';
  const self = userKeyOf(locals) === userId;
  const failure = (notice: MemberChangeNotice): ActionFailure<MemberChangeFailure> => fail(changeStatusOf[notice], { action, userId, name, role, siteName, notice });

  if (userId === '' || (action === 'changeRole' && !isInviteRole(submittedRole))) {
    // The editor only sends a Member it shows and one of the three Roles; anything else is a form this page did not send.
    return fail(400, { action, userId, name, role, siteName, notice: 'unexpected' });
  }

  const result = action === 'changeRole' ? await changeMemberRole(locals, siteId, userId, role, dependencies) : await removeSiteMember(locals, siteId, userId, dependencies);
  if ('ok' in result) {
    return { action, done: true, name, role, siteName, self };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  return failure(memberChangeNoticeOf(result.error));
}

function noticeOf(error: Exclude<InviteError, 'unauthorized' | 'validation'>): InviteNotice {
  switch (error) {
    case 'alreadyInvited':
    case 'unavailable':
    case 'forbidden':
    case 'unreachable':
    case 'certificate':
      return error;
    case 'notFound':
      return 'siteNotFound';
    case 'keyReused':
    case 'unexpected':
      return 'unexpected';
  }
}

/**
 * The Invite form (UX-DR52). The email is checked as the Server checks it before it is asked; a refusal
 * keeps what was typed and picked. Sent, it answers the address the notice names.
 */
export async function inviteAction(locals: Locals, request: Request, dependencies: SitesDependencies = {}): Promise<InviteSuccess | ActionFailure<InviteFailure>> {
  const form = await request.formData();
  const siteId = text(form, 'siteId');
  const siteName = text(form, 'siteName');
  const email = text(form, 'email');
  const submittedRole = text(form, 'role');
  const role: Role = isInviteRole(submittedRole) ? submittedRole : 'Member';
  const failure = (status: number, fields: Partial<InviteFailure>): ActionFailure<InviteFailure> => fail(status, { email, role, emailError: null, notice: null, siteName, ...fields });

  const emailError = checkEmail(email);
  if (emailError !== null) {
    return failure(400, { emailError });
  }
  if (!isInviteRole(submittedRole)) {
    // The Segmented choice only sends the three Roles; anything else is a form this page did not send.
    return failure(400, { notice: 'unexpected' });
  }

  const trimmed = email.trim();
  const result = await inviteToSite(locals, siteId, trimmed, role, dependencies);
  if ('ok' in result) {
    return { done: true, email: trimmed };
  }
  if (result.error === 'unauthorized') {
    return signedOutRedirect();
  }
  if (result.error === 'validation') {
    return failure(400, { emailError: 'invalid' });
  }
  const notice = noticeOf(result.error);
  return failure(statusOf[notice], { notice });
}
