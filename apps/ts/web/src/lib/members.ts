import type { SiteMember } from '@coldframe/api-client';
import type { MessageKey } from '$lib/i18n';
import type { Role } from '$lib/roles';

export type { SiteMember } from '@coldframe/api-client';

/**
 * Members management is for Owners only (UX-DR76): the side nav item, the Settings row and the page are
 * hidden for an Administrator or a Member, never disabled. The Server refuses them with 403 as well.
 */
export function membersAccessOf(role: Role | null | undefined): boolean {
  return role === 'Owner';
}

/** The Roles of the Invite form, in the order of its Segmented choice (UX-DR52). Member is the default. */
export const inviteRoles: readonly Role[] = ['Member', 'Administrator', 'Owner'];

export function isInviteRole(value: unknown): value is Role {
  return typeof value === 'string' && (inviteRoles as readonly string[]).includes(value);
}

/** The one-line description under the Role choice; it follows the selection (UX-DR52). */
export const roleDescription: Readonly<Record<Role, MessageKey>> = {
  Member: 'members.roleDescription.Member',
  Administrator: 'members.roleDescription.Administrator',
  Owner: 'members.roleDescription.Owner',
};

/** The Server's limit on an email address (`InvitationRequest.email`). */
export const emailMaxLength = 254;

export type EmailError = 'blank' | 'invalid';

/**
 * Checks an email the way the Server does, before it is asked: trimmed, one address with exactly one
 * `@`, something on both sides, no spaces, a dot-free local part allowed, at most 254 characters.
 */
export function checkEmail(email: string): EmailError | null {
  const trimmed = email.trim();
  if (trimmed === '') {
    return 'blank';
  }
  if (trimmed.length > emailMaxLength || /[\s,;<>()]/u.test(trimmed)) {
    return 'invalid';
  }
  const parts = trimmed.split('@');
  if (parts.length !== 2) {
    return 'invalid';
  }
  const [local = '', domain = ''] = parts;
  return local === '' || domain === '' || domain.startsWith('.') || domain.endsWith('.') ? 'invalid' : null;
}

function firstLetter(word: string): string {
  const [letter] = Array.from(word);
  return letter === undefined ? '' : letter.toLocaleUpperCase('en');
}

/**
 * The letters of a Member tile's initials square (UX-DR51): first and last word of the name, else of
 * the email's local part, else `?` when neither has a letter.
 */
export function initialsOf(member: Pick<SiteMember, 'name' | 'email'>): string {
  const name = member.name.trim();
  const source = name !== '' ? name : (member.email ?? '').split('@')[0] ?? '';
  const words = source.split(/[\s._-]+/u).filter((word) => word !== '');
  const first = words[0] ?? '';
  const last = words.length > 1 ? (words[words.length - 1] ?? '') : '';
  const initials = firstLetter(first) + firstLetter(last);
  return initials !== '' ? initials : '?';
}

/** The name a tile shows: Keycloak's name, else the email. */
export function displayNameOf(member: Pick<SiteMember, 'name' | 'email'>): string {
  const name = member.name.trim();
  return name !== '' ? name : (member.email ?? '');
}

/** One tile of the Members list: the Server's Membership, and whether it is the signed-in User. */
export interface MemberEntry extends SiteMember {
  readonly self: boolean;
}

/** Set when the Members could not be listed; the copy says so in place of the tiles. */
export type MembersNotice = 'unreachable' | 'certificate' | 'unavailable';

export function membersNoticeOf(notice: MembersNotice | null): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'notice.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    case 'unavailable':
      return { message: 'members.unavailable', tryAgain: true };
    default:
      return null;
  }
}

/**
 * Why an invitation was not sent. `unavailable` is a 503: Keycloak unreachable, a server error or the operation
 * budget ran out (Try again). A failed email send is not reported to the Server, so it never shows here.
 */
export type InviteNotice = 'alreadyInvited' | 'unavailable' | 'forbidden' | 'siteNotFound' | 'unexpected' | 'unreachable' | 'certificate';

/** An invitation that was not sent: the form keeps its values. */
export interface InviteFailure {
  readonly email: string;
  readonly role: Role;
  readonly emailError: EmailError | null;
  readonly notice: InviteNotice | null;
  /** The Site name the notices mention. */
  readonly siteName: string;
}

/** An invitation Keycloak sent: the form clears and the notice names the address. */
export interface InviteSuccess {
  readonly done: true;
  readonly email: string;
}

/** The copy of an Invite notice, and whether Try again can help (a certificate and a refusal cannot). */
export function inviteNoticeOf(notice: InviteNotice): { readonly message: MessageKey; readonly tryAgain: boolean } {
  switch (notice) {
    case 'alreadyInvited':
      return { message: 'members.alreadyInvited', tryAgain: false };
    case 'unavailable':
      return { message: 'members.notSent', tryAgain: true };
    case 'forbidden':
      return { message: 'members.forbidden', tryAgain: false };
    case 'siteNotFound':
      return { message: 'members.siteNotFound', tryAgain: false };
    case 'unexpected':
      return { message: 'members.unexpected', tryAgain: true };
    case 'unreachable':
      return { message: 'notice.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
  }
}

/**
 * Whether a Member is the Site's only Owner (UX-DR51): that tile is locked with "A Site always keeps at least one
 * Owner." and offers no Role change or removal. The Server checks the same rule against its own Owners.
 */
export function isLastOwner(members: readonly Pick<SiteMember, 'role'>[], member: Pick<SiteMember, 'role'>): boolean {
  return member.role === 'Owner' && members.filter((candidate) => candidate.role === 'Owner').length === 1;
}

/** The Role line of a tile an Owner can open ("Member · change"), or of the locked last Owner ("Owner · last one"). */
export function roleLineOf(member: Pick<SiteMember, 'role'>, lastOwner: boolean): { readonly key: MessageKey; readonly role: Role } {
  return { key: lastOwner ? 'members.lastOwnerLine' : 'members.roleLine', role: member.role };
}

/** The two changes an Owner makes to a Membership (Story 9.2). */
export type MemberChangeAction = 'changeRole' | 'removeMember';

/**
 * Why a Role change or removal did not happen. `lastOwner` is the 409 `last-owner` (one line under the Role
 * field), `memberGone` the 404 `member-not-found` (the list is read again), `unavailable` a 503 (Try again).
 */
export type MemberChangeNotice = 'lastOwner' | 'memberGone' | 'unavailable' | 'forbidden' | 'siteNotFound' | 'unexpected' | 'unreachable' | 'certificate';

/** A Role change or removal that did not happen: the editor stays open on that Member with what was picked. */
export interface MemberChangeFailure {
  readonly action: MemberChangeAction;
  readonly userId: string;
  /** The name the notices use. */
  readonly name: string;
  /** The Role picked (for a removal, the Member's Role). */
  readonly role: Role;
  readonly siteName: string;
  readonly notice: MemberChangeNotice;
}

/** A Role change or removal the Server saved. `self` is the signed-in User's own Membership: the page reads the Sites again. */
export interface MemberChangeSuccess {
  readonly action: MemberChangeAction;
  readonly done: true;
  readonly name: string;
  readonly role: Role;
  readonly siteName: string;
  readonly self: boolean;
}

/** The copy of a refused change, and whether Try again can help. `lastOwner` is shown under the Role field instead. */
export function memberChangeNoticeOf(notice: MemberChangeNotice): { readonly message: MessageKey; readonly tryAgain: boolean } {
  switch (notice) {
    case 'lastOwner':
      return { message: 'members.lastOwner', tryAgain: false };
    case 'memberGone':
      return { message: 'members.memberGone', tryAgain: false };
    case 'unavailable':
      return { message: 'members.changeNotSaved', tryAgain: true };
    case 'forbidden':
      return { message: 'members.changeForbidden', tryAgain: false };
    case 'siteNotFound':
      return { message: 'members.changeSiteNotFound', tryAgain: false };
    case 'unexpected':
      return { message: 'members.changeUnexpected', tryAgain: true };
    case 'unreachable':
      return { message: 'notice.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
  }
}

/** The success notice: "Ana Lima is now Administrator." or "Ana Lima was removed from Home." */
export function memberChangedNoticeOf(action: MemberChangeAction): MessageKey {
  return action === 'changeRole' ? 'members.roleChanged' : 'members.removed';
}

/** How often Create Site reads the Memberships again while the User has no Site (UX-DR90). */
export const membershipRefreshMs = 10_000;
