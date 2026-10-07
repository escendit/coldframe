import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import DevicesPage from '../../../apps/ts/web/src/routes/(app)/devices/+page.svelte';
import { devicesAccessOf, devicesNoticeOf, hubsOf, lastSeenText, lotChoicesOf, nodesOf, statusOf, type DeviceListItem, type DevicesNotice } from '$lib/devices';
import type { Lot } from '$lib/lots';
import { deviceAction, listDevices, loadDevices, moveDevice, unassignDevice } from '$lib/server/devices';
import type { Role } from '$lib/roles';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';

const serverUrl = new URL('https://server.example');
const locals = { session: { identity: { authenticated: true, accessTokenRaw: 'access-token-1', idToken: { name: 'Simon Novak' } } } };

const siteId = '0192a000-0000-7000-8000-00000000000a';
const home: Site = { id: siteId, name: 'Home', role: 'Owner' };
const now = new Date('2026-10-06T07:04:00.000Z');
const online: DeviceListItem = { id: '3f2a9c0d1e4b5a67', kind: 'hub', lastSeenAt: '2026-10-06T07:03:30.000Z', online: true };
const offline: DeviceListItem = { id: '1b00aa11bb22cc33', kind: 'hub', lastSeenAt: '2026-10-06T06:54:00.000Z', online: false };
const neverSeen: DeviceListItem = { id: '7c19000000000001', kind: 'hub', online: false };
const node: DeviceListItem = { id: '0a0a0a0a0a0a0a0a', kind: 'node', lotId: '0192a000-0000-7000-8000-000000000011', online: false };

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

function page(
  role: Role,
  devices: readonly DeviceListItem[],
  devicesNotice: DevicesNotice | null = null,
  timeZone: string | null = 'UTC',
  lots: readonly Lot[] = [],
  form: unknown = null,
): string {
  const site = { ...home, role };
  const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: [site], currentSite: site, sitesNotice: null, devices, devicesNotice, lots, loadedAt: now.toISOString(), timeZone };
  return render(DevicesPage, { props: { data, form, params: {} } as never }).body;
}

function row(body: string, id: string): string {
  const match = new RegExp(`<li[^>]*data-device="${id}"[\\s\\S]*?</li>`, 'u').exec(body);
  if (match === null) {
    throw new Error(`No row for ${id}.`);
  }
  return match[0];
}

describe('The Devices list call (AD-14)', () => {
  test('UX-DR65 listDevices sends the token and returns every Device as the Server listed it', async () => {
    const fake = fakeServer(() => jsonResponse(200, { devices: [node, online] }));
    expect(await listDevices(locals, siteId, { serverUrl, fetch: fake.fetch })).toEqual({ ok: [node, online] });
    expect(fake.seen).toEqual([{ method: 'GET', path: `/sites/${siteId}/devices`, authorization: 'Bearer access-token-1', key: null, body: '' }]);
  });

  test('no current Site → no Devices and no call', async () => {
    const fake = fakeServer(() => jsonResponse(200, { devices: [] }));
    expect(await loadDevices(locals, null, new FakeCookies(), { serverUrl, fetch: fake.fetch, now: () => now })).toEqual({
      devices: [],
      devicesNotice: null,
      lots: [],
      loadedAt: '2026-10-06T07:04:00.000Z',
      timeZone: null,
    });
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR65 every load reads the Server again, with the clock and the chosen time zone', async () => {
    const answers = [
      { devices: [online] },
      { devices: [{ ...online, online: false }] },
    ];
    const fake = fakeServer(() => jsonResponse(200, answers.shift()));
    const cookies = new FakeCookies({ cf_time_zone: 'Europe/Zurich' });
    const dependencies = { serverUrl, fetch: fake.fetch, now: () => now };
    const first = await loadDevices(locals, home, cookies, dependencies);
    const second = await loadDevices(locals, home, cookies, dependencies);
    expect(first).toEqual({ devices: [online], devicesNotice: null, lots: [], loadedAt: '2026-10-06T07:04:00.000Z', timeZone: 'Europe/Zurich' });
    // The heartbeat stopped: the reload says offline, with the unchanged last-seen time.
    expect(second.devices).toEqual([{ ...online, online: false }]);
    expect(fake.seen).toHaveLength(2);
  });

  test('an unknown time zone cookie is ignored', async () => {
    const fake = fakeServer(() => jsonResponse(200, { devices: [] }));
    const data = await loadDevices(locals, home, new FakeCookies({ cf_time_zone: 'Mars/Olympus' }), { serverUrl, fetch: fake.fetch });
    expect(data.timeZone).toBeNull();
    expect(Number.isNaN(Date.parse(data.loadedAt))).toBe(false);
  });

  test('UX-DR65 a failed load keeps no rows, so nothing stays Online; a 401 signs out', async () => {
    const cookies = new FakeCookies();
    const unreachable = await loadDevices(locals, home, cookies, { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')), now: () => now });
    expect(unreachable).toMatchObject({ devices: [], devicesNotice: 'unreachable' });
    const down = await loadDevices(locals, home, cookies, { serverUrl, fetch: fakeServer(() => problemResponse(500, 'internal')).fetch });
    expect(down).toMatchObject({ devices: [], devicesNotice: 'unreachable' });
    const certificate = await loadDevices(locals, home, cookies, { serverUrl, fetch: () => Promise.reject(fetchFailed('DEPTH_ZERO_SELF_SIGNED_CERT')) });
    expect(certificate).toMatchObject({ devices: [], devicesNotice: 'certificate' });
    const expired = await redirectOf(() => loadDevices(locals, home, cookies, { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
    expect(devicesNoticeOf('unreachable')).toEqual({ message: 'devices.unreachable', tryAgain: true });
    expect(devicesNoticeOf('certificate')).toEqual({ message: 'notice.certificate', tryAgain: false });
    expect(devicesNoticeOf(null)).toBeNull();
  });
});

describe('Devices rows', () => {
  test('UX-DR30 only Hubs are shown, by Device ID', () => {
    expect(hubsOf([node, online, offline, neverSeen]).map((device) => device.id)).toEqual([offline.id, online.id, neverSeen.id]);
  });

  test('UX-DR30 the status is the Server\'s word with an icon, never recomputed from the last-seen time', () => {
    expect(statusOf(online)).toEqual({ label: 'devices.online', icon: 'checkmark--outline' });
    expect(statusOf(offline)).toEqual({ label: 'devices.offline', icon: 'help' });
    // A fresh last-seen time does not make a Device online: only the Server's flag does.
    expect(statusOf({ ...online, online: false }).label).toBe('devices.offline');
  });

  test('UX-DR30 last seen follows the Voice rules in the caller\'s time zone; never seen says so', () => {
    expect(lastSeenText(online, now, 'en-GB', 'Europe/Zurich')).toBe('Last seen 09:03');
    expect(lastSeenText(online, now, 'en-GB', 'UTC')).toBe('Last seen 07:03');
    expect(lastSeenText({ ...offline, lastSeenAt: '2026-10-04T18:00:00.000Z' }, now, 'en-GB', 'UTC')).toBe('Last seen Sun');
    expect(lastSeenText(neverSeen, now, 'en-GB', 'UTC')).toBe('Not seen yet');
  });

  test('UX-DR84 UX-DR85 the mobile app notice is for Owners and Administrators only', () => {
    expect(devicesAccessOf('Owner')).toEqual({ mobileAppNotice: true, canManageNodes: true });
    expect(devicesAccessOf('Administrator')).toEqual({ mobileAppNotice: true, canManageNodes: true });
    expect(devicesAccessOf('Member')).toEqual({ mobileAppNotice: false, canManageNodes: false });
  });
});

describe('Devices surface', () => {
  test('UX-DR30 UX-DR65 a Hubs section lists each Hub with its full Device ID, status and last seen', () => {
    const body = page('Member', [online, offline]);
    expect(body).toMatch(/<h2[^>]*class="cf-section-title"[^>]*>Hubs<\/h2>/u);
    expect(text(body)).toMatch(/Devices Hubs 1b00aa11bb22cc33 Offline Last seen 6:54 AM 3f2a9c0d1e4b5a67 Online Last seen 7:03 AM$/u);
    expect(body).toMatch(/<span class="cf-devices__id[^"]*">3f2a9c0d1e4b5a67<\/span>/u);
    expect(text(body)).not.toContain('Nodes');
  });

  test('UX-DR30 a Nodes section follows Hubs: Lot name, Device ID, last seen, battery and charging, in the Server order', () => {
    const tomatoes: DeviceListItem = { id: '7c19000000000001', kind: 'node', lotId: 'l1', lotName: 'Tomatoes', online: false, lastSeenAt: '2026-10-06T07:02:00.000Z', batteryPercent: 62, charging: 'charging' };
    const peppers: DeviceListItem = { id: '7c19000000000002', kind: 'node', lotId: 'l2', lotName: 'Peppers', online: false, lastSeenAt: '2026-10-06T06:30:00.000Z', batteryPercent: 14, charging: 'notCharging' };
    const spare: DeviceListItem = { id: '7c19000000000003', kind: 'node', online: false };
    const body = page('Member', [online, tomatoes, peppers, spare]);
    expect(body).toMatch(/<h2[^>]*class="cf-section-title"[^>]*>Nodes<\/h2>/u);
    expect(body.indexOf('>Hubs<')).toBeLessThan(body.indexOf('>Nodes<'));
    // The page keeps the order the Server listed: no re-sort by Device ID or Lot name.
    expect([...body.matchAll(/data-device="([^"]+)"/gu)].map((match) => match[1])).toEqual([online.id, tomatoes.id, peppers.id, spare.id]);
    expect(text(row(body, tomatoes.id))).toBe('7c19000000000001 Tomatoes Last seen 7:02 AM 62 % charging');
    expect(text(row(body, peppers.id))).toBe('7c19000000000002 Peppers Last seen 6:30 AM 14 % not charging');
    expect(row(body, peppers.id)).toContain('data-icon="battery--low"');
    expect(row(body, tomatoes.id)).not.toContain('battery--low');
    expect(text(row(body, spare.id))).toBe('7c19000000000003 Not in a Lot Not seen yet —');
    expect(nodesOf([online, peppers, tomatoes]).map((device) => device.id)).toEqual([peppers.id, tomatoes.id]);
  });

  test('UX-DR30 an online Hub reads Online with an icon; no colour-only status', () => {
    const online3f2a = row(page('Member', [online]), online.id);
    expect(online3f2a).toContain('data-online="true"');
    expect(online3f2a).toContain('data-icon="checkmark--outline"');
    expect(text(online3f2a)).toBe('3f2a9c0d1e4b5a67 Online Last seen 7:03 AM');
  });

  test('UX-DR30 a Hub whose heartbeat stopped reads Offline with the unchanged last-seen time', () => {
    const stopped = row(page('Member', [{ ...online, online: false }]), online.id);
    expect(stopped).toContain('data-online="false"');
    expect(stopped).toContain('data-icon="help"');
    expect(text(stopped)).toBe('3f2a9c0d1e4b5a67 Offline Last seen 7:03 AM');
  });

  test('UX-DR30 a Hub that never sent a heartbeat is Offline and Not seen yet', () => {
    expect(text(row(page('Member', [neverSeen]), neverSeen.id))).toBe('7c19000000000001 Offline Not seen yet');
  });

  test('UX-DR30 the last-seen time is told in the chosen time zone', () => {
    expect(text(row(page('Member', [online], null, 'Europe/Zurich'), online.id))).toMatch(/Last seen 9:03 AM$/u);
  });

  test('UX-DR30 no Devices: the empty state', () => {
    const body = page('Member', []);
    expect(text(body)).toBe('Devices No Devices yet.');
    expect(body).not.toContain('cf-section-title');
  });

  test('UX-DR85 an Owner or Administrator on the web gets the notice in place of Add actions, and no form, button or input', () => {
    for (const role of ['Owner', 'Administrator'] as const) {
      for (const devices of [[], [online]]) {
        const body = page(role, devices);
        expect(text(body)).toContain('Adding a Hub or Node needs the Coldframe mobile app.');
        expect(body).toContain('id="cf-devices-web-notice"');
        expect(body).not.toMatch(/<(?:form|button|input|select|textarea)\b/u);
        expect(body).not.toMatch(/<a\b/u);
        expect(text(body)).not.toMatch(/Add a (?:Hub|Node)/u);
      }
    }
  });

  test('UX-DR84 a Member sees the list only: no notice, no Add action, no control', () => {
    for (const devices of [[], [online]]) {
      const body = page('Member', devices);
      expect(body).not.toContain('cf-inline-notice');
      expect(text(body)).not.toContain('mobile app');
      expect(body).not.toMatch(/<(?:form|button|input|select|textarea|a)\b/u);
      expect(body).not.toMatch(/disabled/u);
    }
  });

  test('UX-DR65 a failed load shows the notice with Try again and no rows', () => {
    const body = page('Owner', [], 'unreachable');
    expect(text(body)).toBe("Devices Can't reach your Server. Try again");
    expect(body).toMatch(/href="\/devices"/u);
    expect(body).not.toContain('data-device');
    expect(text(body)).not.toMatch(/Online|No Devices yet|mobile app/u);
  });

  test('a certificate failure shows the certificate notice with no action', () => {
    const body = page('Member', [], 'certificate');
    expect(text(body)).toContain("Your Server's certificate isn't trusted");
    expect(body).not.toMatch(/<a\b/u);
  });
});

const tomatoesLot: Lot = { id: 'l1', name: 'Tomatoes', status: 'ok', statusSince: '2026-10-06T07:00:00.000Z' };
const peppersLot: Lot = { id: 'l2', name: 'Peppers', status: 'noNode', statusSince: '2026-10-06T07:00:00.000Z' };
const herbsLot: Lot = { id: 'l3', name: 'Herbs', status: 'needsWater', statusSince: '2026-10-06T07:00:00.000Z' };
const assigned: DeviceListItem = { id: '7c19000000000001', kind: 'node', lotId: 'l1', lotName: 'Tomatoes', online: false };
const unassignedNode: DeviceListItem = { id: '7c19000000000002', kind: 'node', online: false };
const lots = [tomatoesLot, peppersLot, herbsLot];

describe('Move or unassign a Node (UX-DR31)', () => {
  test('UX-DR31 an Administrator sees move and unassign on an assigned Node row; an unassigned Node only has move', () => {
    for (const role of ['Owner', 'Administrator'] as const) {
      const body = page(role, [assigned, unassignedNode], null, 'UTC', lots);
      const assignedRow = text(row(body, assigned.id));
      expect(assignedRow).toContain('Move');
      expect(assignedRow).toContain('Unassign');
      expect(row(body, assigned.id)).toContain('action="?/moveNode"');
      const spare = text(row(body, unassignedNode.id));
      expect(spare).toContain('Move');
      expect(spare).not.toContain('Unassign');
    }
  });

  test('UX-DR31 a Member sees neither move nor unassign, and no form, control or dialog', () => {
    const body = page('Member', [assigned, unassignedNode], null, 'UTC', lots);
    expect(text(body)).not.toMatch(/Move|Unassign/u);
    expect(body).not.toMatch(/<(?:form|button|input|select|textarea|dialog|details|a)\b/u);
  });

  test('UX-DR31 unassign requires confirmation: a dialog names the Node and the button only opens it', () => {
    const body = page('Administrator', [assigned], null, 'UTC', lots);
    expect(body).toMatch(/<dialog[^>]*id="cf-unassign-node"|<dialog[^>]*aria-labelledby="cf-unassign-node-title"/u);
    // The form that unassigns is submitted by the dialog's action, never by the row button.
    expect(row(body, assigned.id)).not.toContain('action="?/unassignNode"');
    expect(body).toContain('action="?/unassignNode"');
    expect(body).toContain('Unassign Node');
  });

  test('UX-DR31 occupied Lots are not selectable and say "Has a Node"; the current Lot is marked and not selectable', () => {
    const body = page('Administrator', [assigned], null, 'UTC', lots);
    const choice = (id: string): string => new RegExp(`<label[^>]*data-lot="${id}"[\\s\\S]*?</label>`, 'u').exec(body)?.[0] ?? '';
    expect(choice('l2')).not.toContain('disabled');
    expect(text(choice('l2'))).toBe('Peppers');
    expect(choice('l3')).toContain('disabled');
    expect(text(choice('l3'))).toBe('Herbs Has a Node');
    expect(choice('l1')).toContain('disabled');
    expect(text(choice('l1'))).toBe('Tomatoes Current Lot');
    expect(lotChoicesOf(lots, assigned)).toEqual([
      { id: 'l1', name: 'Tomatoes', current: true, hasNode: false },
      { id: 'l2', name: 'Peppers', current: false, hasNode: false },
      { id: 'l3', name: 'Herbs', current: false, hasNode: true },
    ]);
    expect(lotChoicesOf(lots, unassignedNode).filter((lot) => lot.hasNode).map((lot) => lot.id)).toEqual(['l1', 'l3']);
  });

  test('UX-DR31 loading as an Administrator reads the Lots when there are Nodes; a Member or a Hub-only Site reads none', async () => {
    const answer = (request: Request): Response => (new URL(request.url).pathname.endsWith('/devices') ? jsonResponse(200, { devices: [assigned] }) : jsonResponse(200, { lots }));
    const admin = fakeServer(answer);
    const loaded = await loadDevices(locals, { ...home, role: 'Administrator' }, new FakeCookies(), { serverUrl, fetch: admin.fetch, now: () => now });
    expect(loaded.lots).toEqual(lots);
    const member = fakeServer(answer);
    expect((await loadDevices(locals, { ...home, role: 'Member' }, new FakeCookies(), { serverUrl, fetch: member.fetch })).lots).toEqual([]);
    expect(member.seen.map((seen) => seen.path)).toEqual([`/sites/${siteId}/devices`]);
    const hubsOnly = fakeServer(() => jsonResponse(200, { devices: [online] }));
    expect((await loadDevices(locals, home, new FakeCookies(), { serverUrl, fetch: hubsOnly.fetch })).lots).toEqual([]);
    expect(hubsOnly.seen).toHaveLength(1);
  });

  test('UX-DR31 move and unassign call the Server with the token and the Lot', async () => {
    const fake = fakeServer(() => jsonResponse(200, { id: assigned.id, kind: 'node', siteId }));
    expect(await moveDevice(locals, siteId, assigned.id, 'l2', { serverUrl, fetch: fake.fetch })).toEqual({ ok: { id: assigned.id, kind: 'node', siteId } });
    await unassignDevice(locals, siteId, assigned.id, { serverUrl, fetch: fake.fetch });
    expect(fake.seen).toEqual([
      { method: 'POST', path: `/sites/${siteId}/devices/${assigned.id}/move`, authorization: 'Bearer access-token-1', key: null, body: '{"lotId":"l2"}' },
      { method: 'POST', path: `/sites/${siteId}/devices/${assigned.id}/unassign`, authorization: 'Bearer access-token-1', key: null, body: '' },
    ]);
  });

  test('UX-DR31 the actions answer done, or the Server\'s refusal as a notice for that Node', async () => {
    const formOf = (fields: Record<string, string>): Request => {
      const body = new URLSearchParams(fields);
      return new Request('http://localhost/devices', { method: 'POST', body, headers: { 'content-type': 'application/x-www-form-urlencoded' } });
    };
    const fields = { siteId, deviceId: assigned.id, lotId: 'l2' };
    const done = fakeServer(() => jsonResponse(200, { id: assigned.id, kind: 'node', siteId }));
    expect(await deviceAction('moveNode', locals, formOf(fields), { serverUrl, fetch: done.fetch })).toEqual({ action: 'moveNode', deviceId: assigned.id, done: true });
    expect(await deviceAction('unassignNode', locals, formOf(fields), { serverUrl, fetch: done.fetch })).toEqual({ action: 'unassignNode', deviceId: assigned.id, done: true });

    const refusals: [number, string, number][] = [
      [409, 'lotClaimed', 409],
      [403, 'forbidden', 403],
      [404, 'notFound', 404],
      [500, 'unexpected', 502],
    ];
    for (const [status, notice, failureStatus] of refusals) {
      const fake = fakeServer(() => problemResponse(status, 'x'));
      const result = (await deviceAction('moveNode', locals, formOf(fields), { serverUrl, fetch: fake.fetch })) as { status: number; data: unknown };
      expect(result.status).toBe(failureStatus);
      expect(result.data).toEqual({ action: 'moveNode', deviceId: assigned.id, notice });
    }
    const expired = await redirectOf(() => deviceAction('moveNode', locals, formOf(fields), { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
  });

  test('UX-DR31 a refused move shows its reason beside that Node only', () => {
    const body = page('Administrator', [assigned, unassignedNode], null, 'UTC', lots, { action: 'moveNode', deviceId: assigned.id, notice: 'lotClaimed' });
    expect(text(row(body, assigned.id))).toContain('That Lot already has a Node. Choose another Lot.');
    expect(text(row(body, unassignedNode.id))).not.toContain('That Lot already has a Node');
  });
});
