import { isActionFailure, isRedirect, type ActionFailure } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import MembersPage from '../../../apps/ts/web/src/routes/(app)/members/+page.svelte';
import CreateSitePage from '../../../apps/ts/web/src/routes/(app)/sites/new/+page.svelte';
import SettingsPage from '../../../apps/ts/web/src/routes/(app)/settings/+page.svelte';
import MemberTile from '$lib/components/MemberTile.svelte';
import { messages } from '$lib/i18n';
import {
  checkEmail,
  displayNameOf,
  initialsOf,
  inviteNoticeOf,
  inviteRoles,
  membersAccessOf,
  membershipRefreshMs,
  type InviteFailure,
  type InviteSuccess,
  type MemberEntry,
  type MembersNotice,
} from '$lib/members';
import { inviteAction, inviteToSite, listMembers, loadMembers } from '$lib/server/members';
import type { Site } from '$lib/sites';
import { fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { block, read, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const selfId = 'b5f1c0de-0000-4000-8000-000000000001';
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { sub: selfId, name: 'Simon Novak' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home', role: 'Owner' };
const membersPath = `/sites/${siteId}/members`;
const invitationsPath = `/sites/${siteId}/invitations`;

const simon = { userId: selfId, name: 'Simon Novak', email: 'simon@example.com', role: 'Owner' as const };
const ana = { userId: 'a0000000-0000-4000-8000-000000000002', name: 'Ana Lima', email: 'ana@example.com', role: 'Member' as const };

const catalogue = messages() as Readonly<Record<string, string>>;

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

async function redirectOf(action: () => unknown): Promise<{ status: number; location: string }> {
  try {
    await action();
  } catch (error) {
    if (isRedirect(error)) {
      return { status: error.status, location: error.location };
    }
    throw error;
  }
  throw new Error('Expected a redirect.');
}

function formRequest(fields: Readonly<Record<string, string>>): Request {
  return new Request('http://localhost/members', { method: 'POST', body: new URLSearchParams(fields) });
}

async function failureOf(result: Promise<InviteSuccess | ActionFailure<InviteFailure>>): Promise<{ status: number; data: InviteFailure }> {
  const value = await result;
  if ('done' in value) {
    throw new Error('Expected a failure.');
  }
  expect(isActionFailure(value)).toBe(true);
  return { status: value.status, data: value.data };
}

const inviteFields = { siteId, siteName: 'Home', email: ' ana@example.com ', role: 'Member' };

describe('Members copy (Story 9.1)', () => {
  test('UX-DR52 the Role descriptions, the action and the confirmation are the UX copy verbatim', () => {
    expect(catalogue['members.roleDescription.Member']).toBe("Member sees everything and gets the same Alerts, but can't change settings.");
    expect(catalogue['members.roleDescription.Administrator']).toBe('Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.');
    expect(catalogue['members.roleDescription.Owner']).toBe('Owner can do everything, including inviting people and changing Roles.');
    expect(catalogue['members.sendInvite']).toBe('Send invite');
    expect(catalogue['members.sent']).toBe("Invitation sent to {email}. They accept from the email, on the garden's Wi-Fi.");
    expect(catalogue['members.alreadyInvited']).toBe('That person is already invited or a member of this Site.');
    expect(catalogue['members.notSent']).toBe('The invitation was not sent. Try again.');
  });

  test('UX-DR90 the Create Site hint is the UX copy verbatim', () => {
    expect(catalogue['createSite.invitationHint']).toBe('Accepted an invitation? It can take a minute to appear.');
  });
});

describe('Members rules', () => {
  test('UX-DR76 UX-DR73 only an Owner manages Members', () => {
    expect(membersAccessOf('Owner')).toBe(true);
    expect(membersAccessOf('Administrator')).toBe(false);
    expect(membersAccessOf('Member')).toBe(false);
    expect(membersAccessOf(null)).toBe(false);
  });

  test('UX-DR52 the Role choice is Member, Administrator, Owner in that order', () => {
    expect(inviteRoles).toEqual(['Member', 'Administrator', 'Owner']);
  });

  test('UX-DR52 an email is checked as the Server checks it: trimmed, one @, at most 254 characters', () => {
    expect(checkEmail(' ana@example.com ')).toBeNull();
    expect(checkEmail('')).toBe('blank');
    expect(checkEmail('   ')).toBe('blank');
    expect(checkEmail('ana')).toBe('invalid');
    expect(checkEmail('ana@@example.com')).toBe('invalid');
    expect(checkEmail('a@b@example.com')).toBe('invalid');
    expect(checkEmail('@example.com')).toBe('invalid');
    expect(checkEmail('ana@')).toBe('invalid');
    expect(checkEmail('ana lima@example.com')).toBe('invalid');
    expect(checkEmail('ana@example.com, bo@example.com')).toBe('invalid');
    expect(checkEmail(`${'a'.repeat(242)}@example.com`)).toBeNull();
    expect(checkEmail(`${'a'.repeat(243)}@example.com`)).toBe('invalid');
  });

  test('UX-DR51 initials come from the name, else the email; the name falls back to the email', () => {
    expect(initialsOf({ name: 'Simon Novak' })).toBe('SN');
    expect(initialsOf({ name: 'ana maria lima' })).toBe('AL');
    expect(initialsOf({ name: 'Ana' })).toBe('A');
    expect(initialsOf({ name: '', email: 'bo.berg@example.com' })).toBe('BB');
    expect(initialsOf({ name: '' })).toBe('?');
    expect(initialsOf({ name: ' ', email: '@example.com' })).toBe('?');
    expect(displayNameOf({ name: ' ', email: 'bo@example.com' })).toBe('bo@example.com');
    expect(displayNameOf({ name: 'Ana Lima', email: 'ana@example.com' })).toBe('Ana Lima');
  });

  test('UX-DR52 a 409 and a refusal offer no Try again; Keycloak down, no answer and an error do; a certificate never', () => {
    expect(inviteNoticeOf('alreadyInvited')).toEqual({ message: 'members.alreadyInvited', tryAgain: false });
    expect(inviteNoticeOf('unavailable')).toEqual({ message: 'members.notSent', tryAgain: true });
    expect(inviteNoticeOf('forbidden').tryAgain).toBe(false);
    expect(inviteNoticeOf('siteNotFound').tryAgain).toBe(false);
    expect(inviteNoticeOf('unexpected').tryAgain).toBe(true);
    expect(inviteNoticeOf('unreachable')).toEqual({ message: 'notice.unreachable', tryAgain: true });
    expect(inviteNoticeOf('certificate')).toEqual({ message: 'notice.certificate', tryAgain: false });
  });
});

describe('The Members calls (AD-14)', () => {
  test('UX-DR73 the list is GET /sites/{siteId}/members with the token', async () => {
    const fake = fakeServer(() => jsonResponse(200, { members: [simon, ana] }));
    expect(await listMembers(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: [simon, ana] });
    expect(fake.seen.map((seen) => `${seen.method} ${seen.path} ${seen.authorization ?? ''}`)).toEqual([`GET ${membersPath} Bearer access-token-1`]);
  });

  test('UX-DR52 an invitation is POST /sites/{siteId}/invitations with {email, role}; 202 with no body is sent', async () => {
    const fake = fakeServer(() => new Response(null, { status: 202 }));
    expect(await inviteToSite(locals, siteId, 'ana@example.com', 'Administrator', { serverUrl, fetch: fake.fetch })).toEqual({ ok: undefined });
    expect(fake.seen.map((seen) => `${seen.method} ${seen.path} ${seen.body}`)).toEqual([`POST ${invitationsPath} {"email":"ana@example.com","role":"Administrator"}`]);
  });

  test('UX-DR52 a 409 is already invited', async () => {
    const fake = fakeServer(() => problemResponse(409, 'already-invited'));
    expect(await inviteToSite(locals, siteId, 'ana@example.com', 'Member', { serverUrl, fetch: fake.fetch })).toEqual({ error: 'alreadyInvited' });
  });

  test('the browser never calls the Server: only the server module names the two paths', () => {
    const page = read(`${webSrc}/routes/(app)/members/+page.svelte`);
    expect(page).not.toMatch(/\/invitations|api-client|\$lib\/server/u);
  });
});

describe('Members load', () => {
  test('UX-DR73 an Owner gets every Membership in the Server order, the signed-in User marked', async () => {
    const fake = fakeServer(() => jsonResponse(200, { members: [simon, ana] }));
    const data = await loadMembers(locals, home, { serverUrl, fetch: fake.fetch });
    expect(data).toEqual({ members: [{ ...simon, self: true }, { ...ana, self: false }], membersNotice: null });
  });

  test('UX-DR76 an Administrator, a Member or no Site go to Garden without asking the Server', async () => {
    for (const site of [{ ...home, role: 'Administrator' as const }, { ...home, role: 'Member' as const }, null]) {
      const fake = fakeServer(() => jsonResponse(200, { members: [] }));
      expect(await redirectOf(() => loadMembers(locals, site, { serverUrl, fetch: fake.fetch }))).toEqual({ status: 303, location: '/garden' });
      expect(fake.seen).toEqual([]);
    }
  });

  test('UX-DR76 a 403 (the Role changed meanwhile) or a 404 (the Site deleted meanwhile) goes to Garden; a 401 signs out', async () => {
    expect(await redirectOf(() => loadMembers(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(403, 'forbidden')).fetch }))).toEqual({ status: 303, location: '/garden' });
    expect(await redirectOf(() => loadMembers(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(404, 'site-not-found')).fetch }))).toEqual({ status: 303, location: '/garden' });
    const expired = await redirectOf(() => loadMembers(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
  });

  test('UX-DR73 a list that cannot be read is a notice in place of the tiles', async () => {
    const down = await loadMembers(locals, home, { serverUrl, fetch: fakeServer(() => problemResponse(503, 'identity-provider-unavailable')).fetch });
    expect(down).toEqual({ members: [], membersNotice: 'unavailable' });
    const unreachable = await loadMembers(locals, home, { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) });
    expect(unreachable.membersNotice).toBe('unreachable');
  });
});

describe('The invite form action', () => {
  test('UX-DR52 a valid email and Role is sent trimmed and answers with the address', async () => {
    const fake = fakeServer(() => new Response(null, { status: 202 }));
    expect(await inviteAction(locals, formRequest(inviteFields), { serverUrl, fetch: fake.fetch })).toEqual({ done: true, email: 'ana@example.com' });
    expect(fake.seen.map((seen) => `${seen.method} ${seen.path} ${seen.body}`)).toEqual([`POST ${invitationsPath} {"email":"ana@example.com","role":"Member"}`]);
  });

  test('UX-DR52 a blank or invalid email is one line under the field and nothing is sent', async () => {
    const fake = fakeServer(() => new Response(null, { status: 202 }));
    const blank = await failureOf(inviteAction(locals, formRequest({ ...inviteFields, email: '  ' }), { serverUrl, fetch: fake.fetch }));
    expect(blank).toEqual({ status: 400, data: { email: '  ', role: 'Member', emailError: 'blank', notice: null, siteName: 'Home' } });
    const invalid = await failureOf(inviteAction(locals, formRequest({ ...inviteFields, email: 'ana' }), { serverUrl, fetch: fake.fetch }));
    expect(invalid.data.emailError).toBe('invalid');
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR52 a Role the form does not offer is not sent', async () => {
    const fake = fakeServer(() => new Response(null, { status: 202 }));
    const failure = await failureOf(inviteAction(locals, formRequest({ ...inviteFields, role: 'Gardener' }), { serverUrl, fetch: fake.fetch }));
    expect(failure.data).toMatchObject({ role: 'Member', notice: 'unexpected' });
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR52 the refusals keep the values: 409, 503, 403, 404, 400, no answer, a certificate, anything else', async () => {
    const cases: [() => Response | Promise<Response>, number, Partial<InviteFailure>][] = [
      [() => problemResponse(409, 'already-invited'), 409, { notice: 'alreadyInvited' }],
      [() => problemResponse(503, 'identity-provider-unavailable'), 503, { notice: 'unavailable' }],
      [() => problemResponse(403, 'forbidden'), 403, { notice: 'forbidden' }],
      [() => problemResponse(404, 'site-not-found'), 404, { notice: 'siteNotFound' }],
      [() => problemResponse(400, 'validation'), 400, { emailError: 'invalid', notice: null }],
      [() => Promise.reject(fetchFailed('ECONNREFUSED')), 502, { notice: 'unreachable' }],
      [() => Promise.reject(fetchFailed('UNABLE_TO_VERIFY_LEAF_SIGNATURE')), 502, { notice: 'certificate' }],
      [() => problemResponse(500, 'boom'), 502, { notice: 'unexpected' }],
    ];
    for (const [answer, status, fields] of cases) {
      const failure = await failureOf(inviteAction(locals, formRequest({ ...inviteFields, role: 'Administrator' }), { serverUrl, fetch: fakeServer(answer).fetch }));
      expect(failure.status, JSON.stringify(fields)).toBe(status);
      expect(failure.data).toMatchObject({ email: ' ana@example.com ', role: 'Administrator', siteName: 'Home', ...fields });
    }
  });

  test('UX-DR52 a 401 signs out', async () => {
    const { location } = await redirectOf(() => inviteAction(locals, formRequest(inviteFields), { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(location).toContain('/.oidc/signout');
  });
});

function membersPage(members: readonly MemberEntry[], form: InviteSuccess | InviteFailure | null = null, membersNotice: MembersNotice | null = null): string {
  const data = { user: { displayName: 'Simon Novak', initials: 'SN' }, theme: 'system', sites: [home], currentSite: home, sitesNotice: null, sitesStale: null, members, membersNotice };
  return render(MembersPage, { props: { data, form, params: {} } as never }).body;
}

const tiles = [
  { ...simon, self: true },
  { ...ana, self: false },
];

describe('Members surface', () => {
  test('UX-DR73 UX-DR51 one tile per Membership, with initials, name and Role, and no pending invitations', () => {
    const body = membersPage(tiles);
    const list = block(body, 'cf-members__list');
    expect(text(list)).toBe('SN Simon Novak You Owner AL Ana Lima Member');
    expect(list).toMatch(/aria-labelledby="cf-members-list"/u);
    expect(body).toMatch(/<h2 id="cf-members-list"[^>]*>Members of Home<\/h2>/u);
    expect(body).not.toMatch(/pending/iu);
  });

  test('UX-DR52 the Invite form: an email field, the Role choice with Member selected and its description, Send invite', () => {
    const body = membersPage(tiles);
    const form = block(body, 'cf-members__invite');
    expect(form).toContain('action="?/invite"');
    expect(form).toMatch(/<input[^>]*type="email"[^>]*name="email"|<input[^>]*name="email"[^>]*type="email"/u);
    const segments = [...form.matchAll(/<button[^>]*class="cf-segmented__segment[^"]*"[^>]*>([\s\S]*?)<\/button>/gu)].map((match) => text(match[1] ?? ''));
    expect(segments).toEqual(['Member', 'Administrator', 'Owner']);
    expect(form).toMatch(/<button[^>]*aria-pressed="true"[^>]*>[\s\S]*?Member/u);
    expect(form).toMatch(/<input[^>]*type="hidden"[^>]*name="role"[^>]*value="Member"/u);
    expect(text(form)).toContain("Member sees everything and gets the same Alerts, but can't change settings.");
    expect(text(form)).toContain('Send invite');
  });

  test('UX-DR52 sent: the form is empty again and the notice names the address and the garden\'s Wi-Fi', () => {
    const body = membersPage(tiles, { done: true, email: 'ana@example.com' });
    expect(text(body)).toContain("Invitation sent to ana@example.com. They accept from the email, on the garden's Wi-Fi.");
    expect(body).toMatch(/<input[^>]*name="email"[^>]*value=""|<input(?![^>]*value=)[^>]*name="email"/u);
    expect(text(block(body, 'cf-members__list'))).toBe('SN Simon Novak You Owner AL Ana Lima Member');
  });

  test('UX-DR52 already invited keeps the values with the notice, and no Try again', () => {
    const body = membersPage(tiles, { email: 'bo@example.com', role: 'Administrator', emailError: null, notice: 'alreadyInvited', siteName: 'Home' });
    expect(text(body)).toContain('That person is already invited or a member of this Site.');
    expect(body).toMatch(/<input[^>]*value="bo@example.com"/u);
    expect(body).toMatch(/<input[^>]*type="hidden"[^>]*name="role"[^>]*value="Administrator"/u);
    expect(text(body)).toContain('Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.');
    expect(body).not.toContain('id="cf-invite-notice-retry"');
  });

  test('UX-DR52 not sent (Keycloak down) offers Try again with the same address and Role', () => {
    const body = membersPage(tiles, { email: 'bo@example.com', role: 'Owner', emailError: null, notice: 'unavailable', siteName: 'Home' });
    expect(text(body)).toContain('The invitation was not sent. Try again.');
    const retry = /<form[^>]*id="cf-invite-notice-retry"[\s\S]*?<\/form>/u.exec(body)?.[0] ?? '';
    expect(retry).toContain('action="?/invite"');
    expect(retry).toMatch(/name="email"[^>]*value="bo@example.com"/u);
    expect(retry).toMatch(/name="role"[^>]*value="Owner"/u);
  });

  test('UX-DR52 an invalid email is one line under the field, linked to it', () => {
    const body = membersPage(tiles, { email: 'ana', role: 'Member', emailError: 'invalid', notice: null, siteName: 'Home' });
    expect(body).toMatch(/aria-invalid="true"/u);
    expect(body).toMatch(/<p id="cf-invite-email-reason"[^>]*>Enter one email address, like ana@example.com.<\/p>/u);
  });

  test('UX-DR73 a list that could not be read is a notice with Try again, and the Invite form stays', () => {
    const body = membersPage([], null, 'unavailable');
    expect(text(body)).toContain("Your Server couldn't list the Members. Nothing was changed.");
    expect(body).toContain('action="?/invite"');
  });
});

describe('Member tile', () => {
  test('UX-DR51 the signed-in User has the primary initials square; everyone else the layer one', () => {
    const own = render(MemberTile, { props: { member: { ...simon, self: true } } }).body;
    const other = render(MemberTile, { props: { member: { ...ana, self: false } } }).body;
    expect(own).toMatch(/class="cf-member-tile__initials cf-member-tile__initials--self[ "]/u);
    expect(other).toMatch(/class="cf-member-tile__initials[ "]/u);
    expect(other).not.toContain('initials--self');
    expect(own).toMatch(/aria-hidden="true"[^>]*>SN</u);
  });

  test('UX-DR51 the Role line is upper case by style only, and the tile is display-only in Story 9.1', () => {
    const body = render(MemberTile, { props: { member: { ...ana, self: false } } }).body;
    expect(body).toMatch(/class="cf-member-tile__role[ "][^>]*>Member</u);
    expect(body).not.toMatch(/<button|<a /u);
    const css = read(`${webSrc}/lib/components/MemberTile.svelte`);
    expect(css).toMatch(/\.cf-member-tile__role\s*\{[^}]*text-transform:\s*uppercase/u);
    expect(css).toMatch(/\.cf-member-tile__initials--self\s*\{[^}]*var\(--cf-color-primary\)[^}]*var\(--cf-color-ink-on-bright\)/u);
    expect(css).toMatch(/\.cf-member-tile__initials\s*\{[^}]*var\(--cf-color-layer-01\)[^}]*var\(--cf-color-text-primary\)/u);
  });
});

describe('Settings index', () => {
  function settings(role: Site['role']): string {
    const site = { ...home, role };
    const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [site], currentSite: site, sitesNotice: null };
    return render(SettingsPage, { props: { data, params: {} } as never }).body;
  }

  test('UX-DR71 UX-DR76 an Owner finds Members between My notifications and Site settings', () => {
    const body = settings('Owner');
    const row = body.indexOf('href="/members"');
    expect(row).toBeGreaterThan(body.indexOf('href="/settings/notifications"'));
    expect(row).toBeLessThan(body.indexOf('href="/settings/site"'));
  });

  test('UX-DR76 an Administrator or a Member has no Members row', () => {
    expect(settings('Administrator')).not.toContain('href="/members"');
    expect(settings('Member')).not.toContain('href="/members"');
  });
});

describe('Create Site while an invitation is on its way', () => {
  function createSite(sites: readonly Site[]): string {
    const data = { user: { displayName: 'Ana Lima', initials: 'AL' }, theme: 'system', sites, currentSite: sites[0] ?? null, sitesNotice: null, sitesStale: null, idempotencyKey: 'k', chosenTimeZone: null };
    return render(CreateSitePage, { props: { data, form: null, params: {} } as never }).body;
  }

  test('UX-DR90 with no Site, Create Site says an accepted invitation can take a minute', () => {
    expect(text(createSite([]))).toContain('Accepted an invitation? It can take a minute to appear.');
  });

  test('UX-DR90 from New Site, with a Site already, there is no hint', () => {
    expect(text(createSite([home]))).not.toContain('Accepted an invitation?');
  });

  test('UX-DR90 the Memberships are read again every 10 s while there is none, and the first Site opens', () => {
    expect(membershipRefreshMs).toBe(10_000);
    const page = read(`${webSrc}/routes/(app)/sites/new/+page.svelte`);
    expect(page).toContain('invalidateAll()');
    expect(page).toContain('membershipRefreshMs');
    expect(page).toMatch(/goto\(['"]\/garden['"]/u);
  });
});
