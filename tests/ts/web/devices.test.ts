import { isRedirect } from '@sveltejs/kit';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import DevicesPage from '../../../apps/ts/web/src/routes/(app)/devices/+page.svelte';
import {
  deviceRowId,
  devicesAccessOf,
  devicesNoticeOf,
  hubsOf,
  lastSeenText,
  lotChoicesOf,
  nodesOf,
  silenceWindowParts,
  silenceWindowSeconds,
  silenceWindowText,
  silentText,
  statusOf,
  type DeviceListItem,
  type DevicesNotice,
} from '$lib/devices';
import type { Lot } from '$lib/lots';
import { deviceAction, listDevices, loadDevices, moveDevice, setSilenceWindow, silenceWindowAction, unassignDevice } from '$lib/server/devices';
import type { Role } from '$lib/roles';
import type { Site } from '$lib/sites';
import { FakeCookies, fakeServer, fetchFailed, jsonResponse, problemResponse } from './fakes.ts';
import { read, webSrc } from './helpers.ts';

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
    expect(devicesAccessOf('Owner')).toEqual({ mobileAppNotice: true, canManageNodes: true, canSetSilenceWindow: true });
    expect(devicesAccessOf('Administrator')).toEqual({ mobileAppNotice: true, canManageNodes: true, canSetSilenceWindow: true });
    expect(devicesAccessOf('Member')).toEqual({ mobileAppNotice: false, canManageNodes: false, canSetSilenceWindow: false });
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

  test('UX-DR85 an Owner or Administrator on the web gets the notice in place of Add actions; the only control of a Hub row is its Silence Window', () => {
    for (const role of ['Owner', 'Administrator'] as const) {
      for (const devices of [[], [online]]) {
        const body = page(role, devices);
        expect(text(body)).toContain('Adding a Hub or Node needs the Coldframe mobile app.');
        expect(body).toContain('id="cf-devices-web-notice"');
        // Story 7.2 gives every Device row the Silence Window action; nothing else is a control, and nothing adds a Device.
        const withoutWindow = body.replace(/<details class="cf-devices__window[\s\S]*?<\/details>/gu, '');
        expect(withoutWindow).not.toMatch(/<(?:form|button|input|select|textarea)\b/u);
        expect(body.match(/<form\b/gu) ?? []).toHaveLength(devices.length);
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

/** Story 7.2: silent rows and the Silence Window. The page's clock is 07:04 UTC. */
const silentNode: DeviceListItem = {
  id: '9c0d1e4b5a673f2a',
  kind: 'node',
  lotId: 'l1',
  lotName: 'Beans',
  online: false,
  lastSeenAt: '2026-10-06T01:00:00.000Z',
  silentSince: '2026-10-06T01:00:00.000Z',
  silenceWindowSeconds: 28_800,
  silenceWindowOverridden: true,
};
const silentHub: DeviceListItem = { id: '3f2a9c0d1e4b5a67', kind: 'hub', online: false, lastSeenAt: '2026-10-06T06:52:00.000Z', silentSince: '2026-10-06T06:52:00.000Z', silenceWindowSeconds: 300, silenceWindowOverridden: false };
const heardHub: DeviceListItem = { ...online, id: '1b00aa11bb22cc33', silenceWindowSeconds: 300, silenceWindowOverridden: false };

describe('Silent Device rows (Story 7.2)', () => {
  test('UX-DR30 UX-DR117 a silent row says "Silent 6 h": now minus silent-since, rounded down', () => {
    expect(silentText(silentNode, now)).toBe('Silent 6 h');
    expect(silentText(silentHub, now)).toBe('Silent 12 min');
    expect(silentText({ silentSince: '2026-10-03T07:00:00.000Z' }, now)).toBe('Silent 3 d');
    // Not silent: nothing. A silent-since that cannot be read is still a silent Device.
    expect(silentText(online, now)).toBeNull();
    expect(silentText({ silentSince: 'x' }, now)).toBe('Silent');
  });

  test('UX-DR30 a silent Hub and a silent Node show the help icon and the silence beside last seen; upper case is style only', () => {
    const body = page('Member', [silentHub, silentNode, heardHub]);
    expect(text(row(body, silentHub.id))).toBe('3f2a9c0d1e4b5a67 Offline Silent 12 min Last seen 6:52 AM');
    expect(text(row(body, silentNode.id))).toBe('9c0d1e4b5a673f2a Beans Silent 6 h Last seen 1:00 AM —');
    for (const id of [silentHub.id, silentNode.id]) {
      expect(row(body, id)).toMatch(/<span class="cf-devices__silent[^"]*">[\s\S]*?data-icon="help"[\s\S]*?Silent/u);
      expect(row(body, id)).toContain('data-silent="true"');
    }
    expect(body).not.toMatch(/SILENT/u);
    expect(read(`${webSrc}/routes/(app)/devices/+page.svelte`)).toMatch(/\.cf-devices__silent \{[^}]*text-transform: uppercase/u);
    // A Device that is heard has the row it had.
    expect(text(row(body, heardHub.id))).toBe('1b00aa11bb22cc33 Online Last seen 7:03 AM');
    expect(row(body, heardHub.id)).not.toContain('data-silent');
    expect(row(body, heardHub.id)).not.toContain('cf-devices__silent');
  });

  test('UX-DR26 every row has the anchor a Health Alert links to', () => {
    expect(deviceRowId(silentHub.id)).toBe('device-3f2a9c0d1e4b5a67');
    const body = page('Member', [silentHub, silentNode]);
    expect(row(body, silentHub.id)).toContain('id="device-3f2a9c0d1e4b5a67"');
    expect(row(body, silentNode.id)).toContain('id="device-9c0d1e4b5a673f2a"');
  });
});

describe('Silence Window (Story 7.2)', () => {
  const formOf = (fields: Record<string, string>): Request => new Request('http://localhost/devices', { method: 'POST', body: new URLSearchParams(fields), headers: { 'content-type': 'application/x-www-form-urlencoded' } });
  const windowOf = (body: string, id: string): string => /<details class="cf-devices__window[\s\S]*?<\/details>/u.exec(row(body, id))?.[0] ?? '';

  test('UX-DR84 a number with a unit is whole seconds from 1 min to 30 days; anything else is refused before it is sent', () => {
    expect(silenceWindowSeconds('8', 'h')).toBe(28_800);
    expect(silenceWindowSeconds('1', 'min')).toBe(60);
    expect(silenceWindowSeconds(' 30 ', 'd')).toBe(2_592_000);
    expect(silenceWindowSeconds('720', 'h')).toBe(2_592_000);
    for (const [amount, unit] of [['0', 'min'], ['31', 'd'], ['721', 'h'], ['43201', 'min'], ['1.5', 'h'], ['-2', 'h'], ['', 'h'], ['six', 'h'], ['1e3', 'min'], ['6', 's'], ['6', '']] as const) {
      expect(silenceWindowSeconds(amount, unit), `${amount} ${unit}`).toBeNull();
    }
  });

  test('UX-DR84 the window in force reads in the largest unit that divides it', () => {
    expect(silenceWindowText({ silenceWindowSeconds: 21_600 })).toBe('6 h');
    expect(silenceWindowText({ silenceWindowSeconds: 300 })).toBe('5 min');
    expect(silenceWindowText({ silenceWindowSeconds: 172_800 })).toBe('2 d');
    expect(silenceWindowText({ silenceWindowSeconds: 5400 })).toBe('90 min');
    expect(silenceWindowParts(28_800)).toEqual({ amount: 8, unit: 'h' });
    // Whole seconds the form cannot express read as the nearest minute; a Server that sends none shows no value.
    expect(silenceWindowText({ silenceWindowSeconds: 90 })).toBe('2 min');
    expect(silenceWindowText({})).toBeNull();
    expect(silenceWindowParts(0)).toBeNull();
  });

  test('UX-DR84 an Owner and an Administrator get the row action on Hubs and Nodes: the value in force, a number, a unit and Save', () => {
    for (const role of ['Owner', 'Administrator'] as const) {
      const body = page(role, [silentHub, silentNode], null, 'UTC', lots);
      const hubWindow = windowOf(body, silentHub.id);
      expect(text(hubWindow)).toBe('Silence Window · 5 min Silence Window min h d From 1 min to 30 d. Without its own value a Node uses 6 h and a Hub 5 min. Save');
      expect(hubWindow).toContain('action="?/setSilenceWindow"');
      expect(hubWindow).toMatch(/<input[^>]*type="number"[^>]*name="amount"[^>]*value="5"/u);
      expect(hubWindow).toMatch(/<option value="min" selected[^>]*>min<\/option>/u);
      expect(hubWindow).toContain(`name="deviceId" value="${silentHub.id}"`);
      // Use default only while the Device has its own window.
      expect(text(hubWindow)).not.toContain('Use default');
      const nodeWindow = windowOf(body, silentNode.id);
      expect(text(nodeWindow)).toMatch(/^Silence Window · 8 h Silence Window min h d .* Save Use default$/u);
      expect(nodeWindow).toMatch(/<input[^>]*name="amount"[^>]*value="8"/u);
      expect(nodeWindow).toMatch(/<option value="h" selected[^>]*>h<\/option>/u);
      expect(nodeWindow).toContain('name="useDefault"');
      expect(hubWindow + nodeWindow).not.toMatch(/disabled/u);
    }
  });

  test('UX-DR84 a Member sees the silence and last seen and no Silence Window control: hidden, never disabled', () => {
    const body = page('Member', [silentHub, silentNode], null, 'UTC', lots);
    expect(text(row(body, silentHub.id))).toBe('3f2a9c0d1e4b5a67 Offline Silent 12 min Last seen 6:52 AM');
    expect(text(body)).not.toContain('Silence Window');
    expect(body).not.toContain('setSilenceWindow');
    expect(body).not.toMatch(/<(?:form|button|input|select|textarea|dialog|details|a)\b/u);
    expect(body).not.toMatch(/disabled/u);
  });

  test('UX-DR84 setSilenceWindow puts the seconds, or an empty body for the default, with the token', async () => {
    const fake = fakeServer((request) => jsonResponse(200, request.headers.get('content-length') === '2' ? { windowSeconds: 21_600, overridden: false } : { windowSeconds: 28_800, overridden: true }));
    expect(await setSilenceWindow(locals, siteId, silentNode.id, 28_800, { serverUrl, fetch: fake.fetch })).toEqual({ ok: { windowSeconds: 28_800, overridden: true } });
    await setSilenceWindow(locals, siteId, silentNode.id, null, { serverUrl, fetch: fake.fetch });
    expect(fake.seen).toEqual([
      { method: 'PUT', path: `/sites/${siteId}/devices/${silentNode.id}/silence-window`, authorization: 'Bearer access-token-1', key: null, body: '{"windowSeconds":28800}' },
      { method: 'PUT', path: `/sites/${siteId}/devices/${silentNode.id}/silence-window`, authorization: 'Bearer access-token-1', key: null, body: '{}' },
    ]);
  });

  test('UX-DR84 the action saves a valid value and returns to the default', async () => {
    const fake = fakeServer(() => jsonResponse(200, { windowSeconds: 28_800, overridden: true }));
    const dependencies = { serverUrl, fetch: fake.fetch };
    expect(await silenceWindowAction(locals, formOf({ siteId, deviceId: silentNode.id, amount: '8', unit: 'h' }), dependencies)).toEqual({ action: 'setSilenceWindow', deviceId: silentNode.id, done: true });
    expect(await silenceWindowAction(locals, formOf({ siteId, deviceId: silentNode.id, useDefault: '1' }), dependencies)).toEqual({ action: 'setSilenceWindow', deviceId: silentNode.id, done: true });
    expect(fake.seen.map((request) => request.body)).toEqual(['{"windowSeconds":28800}', '{}']);
  });

  test('UX-DR84 an out-of-range value is refused before the Server is asked, and the form gets what was entered back', async () => {
    const fake = fakeServer(() => jsonResponse(200, { windowSeconds: 60, overridden: true }));
    for (const [amount, unit] of [['0', 'min'], ['31', 'd'], ['90.5', 'min'], ['abc', 'h']] as const) {
      const result = (await silenceWindowAction(locals, formOf({ siteId, deviceId: silentNode.id, amount, unit }), { serverUrl, fetch: fake.fetch })) as { status: number; data: unknown };
      expect(result.status).toBe(400);
      expect(result.data).toEqual({ action: 'setSilenceWindow', deviceId: silentNode.id, notice: 'range', amount, unit });
    }
    expect(fake.seen).toEqual([]);
  });

  test('UX-DR84 the Server\'s refusals become the notice of that Device; a 401 signs out', async () => {
    const fields = { siteId, deviceId: silentNode.id, amount: '8', unit: 'h' };
    const refusals: [number, string, number][] = [
      [403, 'forbidden', 403],
      [404, 'notFound', 404],
      [400, 'range', 400],
      [500, 'unexpected', 502],
    ];
    for (const [status, notice, failureStatus] of refusals) {
      const fake = fakeServer(() => problemResponse(status, 'x'));
      const result = (await silenceWindowAction(locals, formOf(fields), { serverUrl, fetch: fake.fetch })) as { status: number; data: unknown };
      expect(result.status).toBe(failureStatus);
      expect(result.data).toEqual({ action: 'setSilenceWindow', deviceId: silentNode.id, notice, amount: '8', unit: 'h' });
    }
    const unreachable = (await silenceWindowAction(locals, formOf(fields), { serverUrl, fetch: () => Promise.reject(fetchFailed('ECONNREFUSED')) })) as { data: { notice: string } };
    expect(unreachable.data.notice).toBe('unreachable');
    const expired = await redirectOf(() => silenceWindowAction(locals, formOf(fields), { serverUrl, fetch: fakeServer(() => problemResponse(401, 'unauthorized')).fetch }));
    expect(expired.location).toContain('/.oidc/signout');
  });

  test('UX-DR84 a refused range shows the range under the field, with what was entered, in the open form', () => {
    const body = page('Administrator', [silentHub, silentNode], null, 'UTC', lots, { action: 'setSilenceWindow', deviceId: silentNode.id, notice: 'range', amount: '31', unit: 'd' });
    const nodeWindow = windowOf(body, silentNode.id);
    expect(nodeWindow).toMatch(/^<details class="cf-devices__window[^"]*" open/u);
    expect(text(nodeWindow)).toContain('Enter a whole number from 1 min to 30 d.');
    expect(nodeWindow).toMatch(/<input[^>]*name="amount"[^>]*value="31"[^>]*aria-invalid="true"/u);
    expect(nodeWindow).toMatch(/<option value="d" selected[^>]*>d<\/option>/u);
    expect(row(body, silentNode.id)).not.toContain('cf-inline-notice');
    // The other row is as it was.
    expect(text(windowOf(body, silentHub.id))).not.toContain('Enter a whole number');
    expect(windowOf(body, silentHub.id)).not.toMatch(/^<details[^>]* open/u);
  });

  test('UX-DR84 a 403 after the Role changed shows "You can\'t change this on ‹Site›" beside that Device only', () => {
    const failure = { action: 'setSilenceWindow', deviceId: silentHub.id, notice: 'forbidden', amount: '10', unit: 'min' };
    const body = page('Administrator', [silentHub, silentNode], null, 'UTC', lots, failure);
    expect(row(body, silentHub.id)).toContain('id="cf-hub-3f2a9c0d1e4b5a67-notice"');
    expect(text(row(body, silentHub.id))).toContain("You can't change this on Home. Ask an Owner or Administrator.");
    expect(text(row(body, silentNode.id))).not.toContain("You can't change this");
    const gone = page('Owner', [silentHub, silentNode], null, 'UTC', lots, { action: 'setSilenceWindow', deviceId: silentNode.id, notice: 'notFound' });
    expect(text(row(gone, silentNode.id))).toContain('That Device is gone. Reload the page.');
  });
});
