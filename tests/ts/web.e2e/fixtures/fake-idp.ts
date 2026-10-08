/**
 * A small OIDC provider for the e2e tests (Node `http` + `jose`). It does what Keycloak does for
 * the web app's flow — discovery, JWKS, authorize (auto-approve form), token with a PKCE S256
 * check and refresh, end-session — and doubles as the Coldframe Server: `/.well-known/healthz`
 * and an in-memory Site and Lot API (`GET`/`POST /sites`, `PATCH /sites/{id}`, the
 * `/sites/{id}/lots` routes) plus the Devices list (`GET /sites/{id}/devices`). Control endpoints
 * switch failure modes, list every token it issued, and reset or seed the Sites, Lots (a Lot can
 * be seeded as holding a Node, or with a whole status as the Server would compute it) and Devices
 * (the list can be seeded to fail). `POST /control/reads` makes the Sites and Lots reads fail
 * after they succeeded: the connection is dropped, as from a Server that cannot be reached.
 */
import { createHash, randomUUID } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { exportJWK, generateKeyPair, SignJWT, type CryptoKey, type JWK } from 'jose';

export type Mode = 'normal' | 'error' | 'access_denied' | 'token-error' | 'short-lived' | 'discovery-error';

export const modes: readonly Mode[] = ['normal', 'error', 'access_denied', 'token-error', 'short-lived', 'discovery-error'];

/** The access and ID token lifetime of the short-lived mode. */
export const shortLivedSeconds = 5;

export const realmPath = '/realms/coldframe';
export const clientId = 'coldframe-web';
export const clientSecret = 'e2e-client-secret';

interface PendingCode {
  readonly redirectUri: string;
  readonly codeChallenge: string;
  readonly nonce: string | null;
  readonly sid: string;
}

/** A Site of the fake Server, as `GET /sites` returns it. */
export interface FakeSite {
  readonly id: string;
  readonly name: string;
  readonly role: 'Owner' | 'Administrator' | 'Member';
}

/** One `POST /sites` the fake Server received. */
export interface FakeSitePost {
  readonly idempotencyKey: string | null;
  readonly body: Record<string, unknown>;
}

/** The six statuses in the Server's order. */
export const lotStatusOrder = ['needsWater', 'needsCalibration', 'unknown', 'ok', 'paused', 'noNode'] as const;

/**
 * A Lot of the fake Server. `claimed` stands in for a Node assigned to it (Epic 4): without a
 * seeded `status` such a Lot is `unknown` by its Node, and any other Lot has no Node. The status
 * fields are returned as seeded; the fake computes nothing, as the real Server is the one that does.
 */
export interface FakeLot {
  readonly id: string;
  readonly siteId: string;
  readonly name: string;
  readonly claimed?: boolean;
  readonly removed?: boolean;
  readonly status?: string;
  readonly statusSince?: string;
  readonly lastReadingAt?: string;
  readonly unknownCause?: 'node' | 'hub';
  readonly pausedBy?: readonly ('device' | 'site')[];
  readonly pausedUntil?: string;
  readonly moisturePercent?: number;
  readonly lowThresholdPercent?: number;
  /** Story 4.8: sent by `GET /sites/{id}/lots/{id}` only, never by the list. */
  readonly node?: FakeNodeStatus;
  readonly sensors?: readonly FakeSensorReading[];
  /** Daily history per quantity, as `GET /sites/{id}/lots/{id}/history` returns it. */
  readonly history?: Readonly<Record<string, readonly FakeHistoryDay[]>>;
  /** Story 5.4: the History unit per quantity when it is not the raw default (soil moisture in `%` once calibrated). */
  readonly historyUnits?: Readonly<Record<string, string>>;
}

/** A Node's health as `Lot.node` carries it. */
export interface FakeNodeStatus {
  readonly deviceId: string;
  readonly batteryPercent?: number;
  readonly charging?: 'charging' | 'notCharging';
  readonly lastSeenAt?: string;
}

/** A converted latest Reading as `Lot.sensors` carries it. */
export interface FakeSensorReading {
  readonly quantity: 'soil_moisture' | 'air_temperature' | 'relative_humidity' | 'gas_resistance';
  readonly value: number;
  readonly unit: 'raw' | '°C' | '%' | 'kΩ';
  readonly measuredAt: string;
  /** Story 5.2: the Sensor ID and whether its Specification calls for Calibration. */
  readonly sensorId?: string;
  readonly calibratable?: boolean;
}

/** A stored Reading a Calibration point can be taken from. */
export interface FakeCalibrationReading {
  readonly readingSeq: number;
  readonly rawValue: number;
  readonly measuredAt: string;
}

/** A Sensor's Calibration state, as `GET /sites/{id}/sensors/{id}/calibration` returns it. */
export interface FakeCalibration {
  readonly sensorId: string;
  readonly siteId: string;
  readonly readings: readonly FakeCalibrationReading[];
  readonly pendingDry?: number;
  readonly dry?: number;
  readonly wet?: number;
  /** Answer the next POST with this status instead (a Node that does not confirm: 503). */
  readonly failNextPost?: number;
}

/** One `POST …/calibration` the fake Server received. */
export interface FakeCalibrationPost {
  readonly sensorId: string;
  readonly body: unknown;
}

/** One UTC day of a Lot's history. */
export interface FakeHistoryDay {
  readonly day: string;
  readonly low: number;
  readonly high: number;
  readonly readingCount: number;
}

/** A Sensor's Thresholds as `GET /sites/{id}/sensors/{id}/thresholds` returns them (Story 5.4). */
export interface FakeThresholds {
  readonly sensorId: string;
  readonly siteId: string;
  readonly unit: '%' | '°C' | 'kΩ' | 'raw';
  readonly low: { readonly kind: 'default' | 'override' | 'cleared'; readonly value?: number };
  readonly high: { readonly kind: 'default' | 'override' | 'cleared'; readonly value?: number };
  readonly proposedLow?: number;
  /** Answer the next PUT with this status instead (a Server that cannot save: 503). */
  readonly failNextPut?: number;
}

/** One `PUT …/thresholds` the fake Server received. */
export interface FakeThresholdsPut {
  readonly sensorId: string;
  readonly body: unknown;
}

/** Which reads of the fake Server fail: none, the Sites and the Lots, or the Lots only. */
export type FailingReads = 'none' | 'all' | 'lots';

export const failingReads: readonly FailingReads[] = ['none', 'all', 'lots'];

/** A Device of the fake Server, as `GET /sites/{id}/devices` lists it; `online` is seeded, as the Server computes it. */
export interface FakeDevice {
  readonly id: string;
  readonly siteId: string;
  readonly kind: 'hub' | 'node';
  readonly lotId?: string;
  readonly lastSeenAt?: string;
  readonly online: boolean;
  /** Story 4.8: a Node's Lot name, battery and charger state from its newest report. */
  readonly lotName?: string;
  readonly batteryPercent?: number;
  readonly charging?: 'charging' | 'notCharging';
}

/** One move or unassign the fake Server accepted (Story 4.9). */
export interface FakeDeviceAction {
  readonly action: 'move' | 'unassign';
  readonly deviceId: string;
  readonly lotId?: string;
}

/** Seeded by default, so every spec that signs in still reaches Garden. */
export const defaultSites: readonly FakeSite[] = [{ id: '0192a000-0000-7000-8000-000000000001', name: 'Home garden', role: 'Owner' }];

export interface FakeIdp {
  readonly issuer: string;
  readonly origin: string;
  close(): Promise<void>;
}

function send(response: ServerResponse, status: number, body: string | object, headers: Record<string, string> = {}): void {
  const text = typeof body === 'string' ? body : JSON.stringify(body);
  response.writeHead(status, {
    'content-type': typeof body === 'string' ? 'text/html; charset=utf-8' : 'application/json',
    'cache-control': 'no-store',
    ...headers,
  });
  response.end(text);
}

function redirectTo(response: ServerResponse, location: string): void {
  response.writeHead(302, { location, 'cache-control': 'no-store' });
  response.end();
}

async function readForm(request: IncomingMessage): Promise<URLSearchParams> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(chunk as Buffer);
  }
  return new URLSearchParams(Buffer.concat(chunks).toString('utf8'));
}

async function readJson(request: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(chunk as Buffer);
  }
  const text = Buffer.concat(chunks).toString('utf8');
  return text === '' ? {} : (JSON.parse(text) as Record<string, unknown>);
}

function s256(verifier: string): string {
  return createHash('sha256').update(verifier).digest('base64url');
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/gu, (character) => `&#${String(character.charCodeAt(0))};`);
}

/** Client authentication: `client_secret_basic` or `client_secret_post`. */
function authenticated(request: IncomingMessage, form: URLSearchParams): boolean {
  const header = request.headers.authorization;
  if (header?.startsWith('Basic ') === true) {
    const [id, secret] = Buffer.from(header.slice(6), 'base64').toString('utf8').split(':').map(decodeURIComponent);
    return id === clientId && secret === clientSecret;
  }
  return form.get('client_id') === clientId && form.get('client_secret') === clientSecret;
}

export async function startFakeIdp(port: number, host = 'localhost'): Promise<FakeIdp> {
  const origin = `http://${host}:${String(port)}`;
  const issuer = `${origin}${realmPath}`;
  const oidc = `${realmPath}/protocol/openid-connect`;
  const { privateKey, publicKey } = await generateKeyPair('RS256');
  const jwk: JWK = { ...(await exportJWK(publicKey)), kid: 'e2e', alg: 'RS256', use: 'sig' };

  let mode: Mode = 'normal';
  const codes = new Map<string, PendingCode>();
  const refreshTokens = new Map<string, string>();
  const issued: string[] = [];
  let sites: FakeSite[] = [...defaultSites];
  let posts: FakeSitePost[] = [];
  const created = new Map<string, FakeSite>();
  let lots: FakeLot[] = [];
  let lotPosts: FakeSitePost[] = [];
  let calibrations: FakeCalibration[] = [];
  let calibrationPosts: FakeCalibrationPost[] = [];
  let thresholds: FakeThresholds[] = [];
  let thresholdPuts: FakeThresholdsPut[] = [];
  const createdLots = new Map<string, FakeLot>();
  let devices: FakeDevice[] = [];
  let deviceActions: FakeDeviceAction[] = [];
  /** Set to answer the Devices list with this status instead (a Server that is down). */
  let devicesStatus: number | null = null;
  /** Reads that drop the connection instead of answering. */
  let failing: FailingReads = 'none';
  /** How many times the Lots of any Site were asked for since the last reset. */
  let lotReads = 0;
  /** The `statusSince` of a Lot seeded without one. */
  const startedAt = new Date().toISOString();

  /** The fake Server accepts only access tokens this provider issued. */
  function bearerOk(request: IncomingMessage): boolean {
    const header = request.headers.authorization;
    return header?.startsWith('Bearer ') === true && issued.includes(header.slice(7));
  }

  function problem(response: ServerResponse, status: number, slug: string): void {
    send(response, status, { type: `urn:coldframe:problem:${slug}`, title: slug, status }, { 'content-type': 'application/problem+json' });
  }

  async function sign(claims: Record<string, unknown>, lifetimeSeconds: number, key: CryptoKey = privateKey): Promise<string> {
    const token = await new SignJWT(claims)
      .setProtectedHeader({ alg: 'RS256', kid: 'e2e', typ: 'JWT' })
      .setIssuer(issuer)
      .setIssuedAt()
      .setExpirationTime(`${String(lifetimeSeconds)}s`)
      .setJti(randomUUID())
      .sign(key);
    issued.push(token);
    return token;
  }

  async function tokens(sid: string, nonce: string | null): Promise<Record<string, unknown>> {
    // short-lived: long enough to land on Garden after sign-in on a slow runner (the web app
    // refreshes on the first request after expiry, with no margin), short enough to wait out.
    const lifetime = mode === 'short-lived' ? shortLivedSeconds : 300;
    const subject = 'b5f1c0de-0000-4000-8000-000000000001';
    const profile = { name: 'Simon Novak', given_name: 'Simon', family_name: 'Novak', preferred_username: 'simon' };
    const accessToken = await sign({ sub: subject, aud: 'account', azp: clientId, sid, typ: 'Bearer', scope: 'openid profile' }, lifetime);
    const idToken = await sign({ sub: subject, aud: clientId, azp: clientId, sid, ...profile, ...(nonce === null ? {} : { nonce }) }, lifetime);
    const refreshToken = await sign({ sub: subject, aud: issuer, azp: clientId, sid, typ: 'Refresh' }, 1800);
    refreshTokens.set(refreshToken, sid);
    return {
      access_token: accessToken,
      id_token: idToken,
      refresh_token: refreshToken,
      token_type: 'Bearer',
      expires_in: lifetime,
      refresh_expires_in: 1800,
      scope: 'openid profile',
      session_state: sid,
    };
  }

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const url = new URL(request.url ?? '/', origin);
    const path = url.pathname;

    // The Coldframe Server's health endpoint (the Server probe).
    if (path === '/.well-known/healthz') {
      send(response, 200, 'Healthy', { 'content-type': 'text/plain' });
      return;
    }

    if (path === '/control/mode' && request.method === 'POST') {
      const body = await readJson(request);
      const requested = body.mode;
      if (typeof requested !== 'string' || !modes.includes(requested as Mode)) {
        send(response, 400, { error: 'unknown mode' });
        return;
      }
      mode = requested as Mode;
      send(response, 200, { mode });
      return;
    }
    if (path === '/control/sites' && request.method === 'POST') {
      const body = await readJson(request);
      sites = Array.isArray(body.sites) ? (body.sites as FakeSite[]) : [...defaultSites];
      lots = Array.isArray(body.lots) ? (body.lots as FakeLot[]) : [];
      devices = Array.isArray(body.devices) ? (body.devices as FakeDevice[]) : [];
      deviceActions = [];
      calibrations = Array.isArray(body.calibrations) ? (body.calibrations as FakeCalibration[]) : [];
      calibrationPosts = [];
      thresholds = Array.isArray(body.thresholds) ? (body.thresholds as FakeThresholds[]) : [];
      thresholdPuts = [];
      devicesStatus = typeof body.devicesStatus === 'number' ? body.devicesStatus : null;
      failing = 'none';
      lotReads = 0;
      posts = [];
      lotPosts = [];
      created.clear();
      createdLots.clear();
      send(response, 200, { sites, lots });
      return;
    }
    if (path === '/control/sites') {
      send(response, 200, { sites, posts, lots, lotPosts, lotReads, devices, deviceActions, calibrations, calibrationPosts, thresholds, thresholdPuts });
      return;
    }
    // Story 5.2: a new stored Reading of a Sensor arrives, and the Lot's Sensors as the Lot detail shows them change.
    if (path === '/control/calibration-reading' && request.method === 'POST') {
      const body = await readJson(request);
      const reading = body.reading as FakeCalibrationReading;
      calibrations = calibrations.map((entry) => (entry.sensorId === body.sensorId ? { ...entry, readings: [reading, ...entry.readings] } : entry));
      send(response, 200, {});
      return;
    }
    if (path === '/control/lot-sensors' && request.method === 'POST') {
      const body = await readJson(request);
      lots = lots.map((lot) => (lot.id === body.lotId ? { ...lot, sensors: body.sensors as FakeSensorReading[] } : lot));
      send(response, 200, {});
      return;
    }
    // Story 5.4: fields of a Lot change, as when its first calibrated Reading is stored.
    if (path === '/control/lot-fields' && request.method === 'POST') {
      const body = await readJson(request);
      lots = lots.map((lot) => (lot.id === body.lotId ? { ...lot, ...(body.fields as Partial<FakeLot>) } : lot));
      send(response, 200, {});
      return;
    }
    if (path === '/control/reads' && request.method === 'POST') {
      const requested = (await readJson(request)).failing;
      if (typeof requested !== 'string' || !failingReads.includes(requested as FailingReads)) {
        send(response, 400, { error: 'unknown reads' });
        return;
      }
      failing = requested as FailingReads;
      send(response, 200, { failing });
      return;
    }

    // The Coldframe Server's Site API (Story 1.6 and 1.8), in memory.
    if (path === '/sites' && (request.method === 'GET' || request.method === 'POST')) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      if (request.method === 'GET') {
        if (failing === 'all') {
          // No answer at all: the web app sees a Server it cannot reach.
          request.socket.destroy();
          return;
        }
        send(response, 200, { sites });
        return;
      }
      const body = await readJson(request);
      const keyHeader = request.headers['idempotency-key'];
      const key = typeof keyHeader === 'string' ? keyHeader : null;
      posts.push({ idempotencyKey: key, body });
      if (key === null || key === '') {
        problem(response, 400, 'idempotency-key-missing');
        return;
      }
      const name = typeof body.name === 'string' ? body.name.trim() : '';
      if (name === '' || name.length > 100) {
        problem(response, 400, 'validation');
        return;
      }
      const existing = created.get(key);
      if (existing !== undefined) {
        send(response, existing.name === name ? 201 : 422, existing.name === name ? existing : { type: 'urn:coldframe:problem:idempotency-key-reused', title: 'reused', status: 422 });
        return;
      }
      const site: FakeSite = { id: randomUUID(), name, role: 'Owner' };
      created.set(key, site);
      sites = [...sites, site];
      send(response, 201, site, { location: `/sites/${site.id}` });
      return;
    }

    // Story 4.9: move or unassign a Node (Administrator), in memory. The fake holds the Lot's occupancy the
    // way the Server's Lot grain does: a Lot another Node holds is 409, and a Lot a Node leaves is free.
    const nodeMatch = /^\/sites\/([^/]+)\/devices\/([^/]+)\/(move|unassign)$/u.exec(path);
    if (nodeMatch !== null && request.method === 'POST') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(nodeMatch[1] ?? '');
      const deviceId = decodeURIComponent(nodeMatch[2] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      if (site.role === 'Member') {
        problem(response, 403, 'forbidden');
        return;
      }
      const node = devices.find((candidate) => candidate.id === deviceId && candidate.siteId === siteId && candidate.kind === 'node');
      if (node === undefined) {
        problem(response, 404, 'device-not-found');
        return;
      }
      // A Lot's seeded status is dropped when its occupancy changes: the fake then derives it from `claimed`.
      const without = <T extends object>(value: T, ...keys: string[]): T => Object.fromEntries(Object.entries(value).filter(([key]) => !keys.includes(key))) as T;
      const free = (lotId: string | undefined): void => {
        lots = lots.map((lot) => (lot.id === lotId ? { ...without(lot, 'status'), claimed: false } : lot));
      };
      let moved: FakeDevice = without(node, 'lotId', 'lotName');
      let lotId: string | undefined;
      if (nodeMatch[3] === 'move') {
        const requested = (await readJson(request)).lotId;
        const target = lots.find((lot) => lot.id === requested && lot.siteId === siteId && lot.removed !== true);
        if (target === undefined) {
          problem(response, 404, 'lot-not-found');
          return;
        }
        if (node.lotId !== target.id) {
          const holds = target.status !== undefined ? target.status !== 'noNode' : target.claimed === true;
          if (holds) {
            problem(response, 409, 'lot-claimed');
            return;
          }
          lots = lots.map((lot) => (lot.id === target.id ? { ...without(lot, 'status'), claimed: true } : lot));
          free(node.lotId);
        }
        lotId = target.id;
        moved = { ...node, lotId: target.id, lotName: target.name };
      } else {
        free(node.lotId);
      }
      deviceActions.push({ action: nodeMatch[3] as 'move' | 'unassign', deviceId, ...(lotId === undefined ? {} : { lotId }) });
      devices = devices.map((device) => (device.id === node.id ? moved : device));
      send(response, 200, {
        id: moved.id,
        kind: 'node',
        siteId,
        ...(moved.lotId === undefined ? {} : { lotId: moved.lotId }),
      });
      return;
    }

    // Story 3.7: the Devices list (Member), by Device ID.
    const devicesMatch = /^\/sites\/([^/]+)\/devices$/u.exec(path);
    if (devicesMatch !== null && request.method === 'GET') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(devicesMatch[1] ?? '');
      if (!sites.some((candidate) => candidate.id === siteId)) {
        problem(response, 404, 'site-not-found');
        return;
      }
      if (devicesStatus !== null) {
        problem(response, devicesStatus, 'unavailable');
        return;
      }
      const listed = devices
        .filter((device) => device.siteId === siteId)
        // The Server's order: Hubs by Device ID, then Nodes by Lot name (unassigned last), then Device ID.
        .toSorted((a, b) => {
          const byId = a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
          if (a.kind !== b.kind) {
            return a.kind === 'hub' ? -1 : 1;
          }
          if (a.kind === 'hub' || a.lotName === b.lotName) {
            return byId;
          }
          if (a.lotName === undefined || b.lotName === undefined) {
            return a.lotName === undefined ? 1 : -1;
          }
          return a.lotName < b.lotName ? -1 : 1;
        })
        .map((device) => ({
          id: device.id,
          kind: device.kind,
          ...(device.lotId === undefined ? {} : { lotId: device.lotId }),
          ...(device.lastSeenAt === undefined ? {} : { lastSeenAt: device.lastSeenAt }),
          online: device.online,
          ...(device.lotName === undefined ? {} : { lotName: device.lotName }),
          ...(device.batteryPercent === undefined ? {} : { batteryPercent: device.batteryPercent }),
          ...(device.charging === undefined ? {} : { charging: device.charging }),
        }));
      send(response, 200, { devices: listed });
      return;
    }

    // Story 4.8: a Lot's daily history of one quantity (Member), the default window as seeded.
    const historyMatch = /^\/sites\/([^/]+)\/lots\/([^/]+)\/history$/u.exec(path);
    if (historyMatch !== null && request.method === 'GET') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(historyMatch[1] ?? '');
      const lot = lots.find((candidate) => candidate.id === decodeURIComponent(historyMatch[2] ?? '') && candidate.siteId === siteId);
      const quantity = url.searchParams.get('quantity') ?? '';
      const units: Record<string, string> = { soil_moisture: 'raw', air_temperature: '°C', relative_humidity: '%', gas_resistance: 'kΩ' };
      if (!sites.some((candidate) => candidate.id === siteId)) {
        problem(response, 404, 'site-not-found');
      } else if (lot === undefined) {
        problem(response, 404, 'lot-not-found');
      } else if (units[quantity] === undefined) {
        problem(response, 400, 'validation');
      } else {
        if (failing !== 'none') {
          request.socket.destroy();
          return;
        }
        send(response, 200, { quantity, unit: lot.historyUnits?.[quantity] ?? units[quantity], days: lot.history?.[quantity] ?? [] });
      }
      return;
    }

    // Story 5.2: a Sensor's Calibration state and recent Readings, and saving a point (Administrator).
    const calibrationMatch = /^\/sites\/([^/]+)\/sensors\/([^/]+)\/calibration$/u.exec(path);
    if (calibrationMatch !== null) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(calibrationMatch[1] ?? '');
      const sensorId = decodeURIComponent(calibrationMatch[2] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      if (site.role === 'Member') {
        problem(response, 403, 'forbidden');
        return;
      }
      const entry = calibrations.find((candidate) => candidate.sensorId === sensorId && candidate.siteId === siteId);
      if (entry === undefined) {
        problem(response, 404, 'sensor-not-found');
        return;
      }
      const stateOf = (value: FakeCalibration): Record<string, unknown> => ({
        calibrated: value.dry !== undefined && value.wet !== undefined,
        ...(value.dry === undefined ? {} : { dry: { rawValue: value.dry } }),
        ...(value.wet === undefined ? {} : { wet: { rawValue: value.wet } }),
        ...(value.pendingDry === undefined ? {} : { pendingDry: { rawValue: value.pendingDry } }),
      });
      if (request.method === 'GET') {
        send(response, 200, { ...stateOf(entry), readings: entry.readings });
        return;
      }
      const body = await readJson(request);
      calibrationPosts = [...calibrationPosts, { sensorId, body }];
      const point = (body.dry ?? body.wet) as { readingSeq?: number } | undefined;
      const chosen = entry.readings.find((reading) => reading.readingSeq === point?.readingSeq);
      if (point === undefined || chosen === undefined) {
        problem(response, 400, 'validation');
        return;
      }
      let next: FakeCalibration;
      if (body.dry === undefined) {
        const dry = entry.pendingDry ?? entry.dry;
        if (dry === undefined || Math.abs(dry - chosen.rawValue) < 16) {
          problem(response, 400, 'validation');
          return;
        }
        next = { ...entry, dry, wet: chosen.rawValue, pendingDry: undefined };
      } else {
        next = { ...entry, pendingDry: chosen.rawValue };
      }
      const failure = entry.failNextPost;
      calibrations = calibrations.map((candidate) => (candidate === entry ? { ...next, failNextPost: undefined } : candidate));
      if (failure !== undefined) {
        problem(response, failure, 'calibration-not-delivered');
        return;
      }
      send(response, 200, stateOf(next));
      return;
    }

    // Story 5.4: a Sensor's Thresholds (Member reads) and setting them (Administrator).
    const thresholdsMatch = /^\/sites\/([^/]+)\/sensors\/([^/]+)\/thresholds$/u.exec(path);
    if (thresholdsMatch !== null) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(thresholdsMatch[1] ?? '');
      const sensorId = decodeURIComponent(thresholdsMatch[2] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      const entry = thresholds.find((candidate) => candidate.sensorId === sensorId && candidate.siteId === siteId);
      const viewOf = (value: FakeThresholds): Record<string, unknown> => ({
        unit: value.unit,
        low: value.low,
        high: value.high,
        ...(value.proposedLow === undefined || value.low.value !== undefined ? {} : { proposedLow: value.proposedLow }),
      });
      if (request.method === 'GET') {
        if (entry === undefined) {
          problem(response, 404, 'sensor-not-found');
          return;
        }
        send(response, 200, viewOf(entry));
        return;
      }
      if (site.role === 'Member') {
        problem(response, 403, 'forbidden');
        return;
      }
      if (entry === undefined) {
        problem(response, 404, 'sensor-not-found');
        return;
      }
      const body = await readJson(request);
      thresholdPuts = [...thresholdPuts, { sensorId, body }];
      if (entry.failNextPut !== undefined) {
        const failure = entry.failNextPut;
        thresholds = thresholds.map((candidate) => (candidate === entry ? { ...candidate, failNextPut: undefined } : candidate));
        problem(response, failure, 'unavailable');
        return;
      }
      const sideOf = (sent: unknown, kept: FakeThresholds['low']): FakeThresholds['low'] => {
        const side = sent as { kind?: string; value?: number } | undefined;
        if (side === undefined) {
          return kept;
        }
        return side.kind === 'override' ? { kind: 'override', value: side.value } : { kind: side.kind as 'default' | 'cleared' };
      };
      const request_ = body as { low?: unknown; high?: unknown };
      const next: FakeThresholds = { ...entry, low: sideOf(request_.low, entry.low), high: sideOf(request_.high, entry.high) };
      if (next.high.value !== undefined && (next.low.value === undefined || next.low.value >= next.high.value)) {
        problem(response, 400, 'validation');
        return;
      }
      thresholds = thresholds.map((candidate) => (candidate === entry ? next : candidate));
      // The Lot of that Sensor shows the new low, as the Server's Lot detail does.
      lots = lots.map((lot) =>
        lot.sensors?.some((sensor) => sensor.sensorId === sensorId) === true && lot.moisturePercent !== undefined
          ? { ...lot, ...(next.low.value === undefined ? {} : { lowThresholdPercent: next.low.value }) }
          : lot,
      );
      send(response, 200, viewOf(next));
      return;
    }

    // Story 1.9: rename a Site (Owner) and the Lot routes, in memory.
    const siteMatch = /^\/sites\/([^/]+)(\/lots(?:\/([^/]+))?)?$/u.exec(path);
    if (siteMatch !== null) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(siteMatch[1] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      const roleRank = { Member: 1, Administrator: 2, Owner: 3 } as const;
      const allowed = (minimum: keyof typeof roleRank): boolean => roleRank[site.role] >= roleRank[minimum];
      const statusOf = (lot: FakeLot): string => lot.status ?? (lot.claimed === true ? 'unknown' : 'noNode');
      const lotView = (lot: FakeLot, detail = false): Record<string, unknown> => ({
        id: lot.id,
        name: lot.name,
        status: statusOf(lot),
        statusSince: lot.statusSince ?? startedAt,
        ...(lot.lastReadingAt === undefined ? {} : { lastReadingAt: lot.lastReadingAt }),
        ...(lot.unknownCause !== undefined ? { unknownCause: lot.unknownCause } : statusOf(lot) === 'unknown' ? { unknownCause: 'node' } : {}),
        ...(lot.pausedBy === undefined ? {} : { pausedBy: lot.pausedBy }),
        ...(lot.pausedUntil === undefined ? {} : { pausedUntil: lot.pausedUntil }),
        ...(lot.moisturePercent === undefined ? {} : { moisturePercent: lot.moisturePercent }),
        ...(lot.lowThresholdPercent === undefined ? {} : { lowThresholdPercent: lot.lowThresholdPercent }),
        ...(detail && lot.node !== undefined ? { node: lot.node, sensors: lot.sensors ?? [] } : {}),
        ...(lot.removed === true ? { removed: true } : {}),
      });
      /** The Server's order: by status, then creation. A status outside the contract sorts last. */
      const rank = (lot: FakeLot): number => {
        const index = (lotStatusOrder as readonly string[]).indexOf(statusOf(lot));
        return index === -1 ? lotStatusOrder.length : index;
      };
      const nameOf = (body: Record<string, unknown>): string | null => {
        const name = typeof body.name === 'string' ? body.name.trim() : '';
        return name === '' || name.length > 100 ? null : name;
      };

      if (siteMatch[2] === undefined) {
        if (request.method !== 'PATCH') {
          send(response, 405, { error: 'method' });
          return;
        }
        if (!allowed('Owner')) {
          problem(response, 403, 'forbidden');
          return;
        }
        const name = nameOf(await readJson(request));
        if (name === null) {
          problem(response, 400, 'validation');
          return;
        }
        const renamed: FakeSite = { ...site, name };
        sites = sites.map((candidate) => (candidate.id === siteId ? renamed : candidate));
        send(response, 200, renamed);
        return;
      }

      const lotId = siteMatch[3] === undefined ? null : decodeURIComponent(siteMatch[3]);
      if (lotId === null) {
        if (request.method === 'GET') {
          lotReads++;
          if (failing !== 'none') {
            request.socket.destroy();
            return;
          }
          const live = lots.filter((lot) => lot.siteId === siteId && lot.removed !== true);
          const ordered = live.map((lot, index) => ({ lot, index })).toSorted((a, b) => rank(a.lot) - rank(b.lot) || a.index - b.index);
          send(response, 200, { lots: ordered.map(({ lot }) => lotView(lot)) });
          return;
        }
        if (request.method !== 'POST') {
          send(response, 405, { error: 'method' });
          return;
        }
        if (!allowed('Administrator')) {
          problem(response, 403, 'forbidden');
          return;
        }
        const body = await readJson(request);
        const keyHeader = request.headers['idempotency-key'];
        const key = typeof keyHeader === 'string' ? keyHeader : null;
        lotPosts.push({ idempotencyKey: key, body });
        if (key === null || key === '') {
          problem(response, 400, 'idempotency-key-missing');
          return;
        }
        const name = nameOf(body);
        if (name === null) {
          problem(response, 400, 'validation');
          return;
        }
        const existing = createdLots.get(key);
        if (existing !== undefined) {
          if (existing.name === name) {
            send(response, 201, lotView(existing));
          } else {
            problem(response, 422, 'idempotency-key-reused');
          }
          return;
        }
        const lot: FakeLot = { id: randomUUID(), siteId, name };
        createdLots.set(key, lot);
        lots = [...lots, lot];
        send(response, 201, lotView(lot), { location: `/sites/${siteId}/lots/${lot.id}` });
        return;
      }

      const lot = lots.find((candidate) => candidate.id === lotId && candidate.siteId === siteId);
      if (request.method === 'GET') {
        if (lot === undefined) {
          problem(response, 404, 'lot-not-found');
        } else {
          lotReads++;
          if (failing !== 'none') {
            request.socket.destroy();
            return;
          }
          send(response, 200, lotView(lot, true));
        }
        return;
      }
      if (request.method !== 'PATCH' && request.method !== 'DELETE') {
        send(response, 405, { error: 'method' });
        return;
      }
      if (!allowed('Administrator')) {
        problem(response, 403, 'forbidden');
        return;
      }
      if (request.method === 'DELETE') {
        if (lot === undefined) {
          problem(response, 404, 'lot-not-found');
        } else if (lot.claimed === true) {
          problem(response, 409, 'lot-claimed');
        } else {
          lots = lots.map((candidate) => (candidate.id === lot.id ? { ...candidate, removed: true } : candidate));
          send(response, 204, '');
        }
        return;
      }
      if (lot === undefined || lot.removed === true) {
        problem(response, 404, 'lot-not-found');
        return;
      }
      const name = nameOf(await readJson(request));
      if (name === null) {
        problem(response, 400, 'validation');
        return;
      }
      const renamed: FakeLot = { ...lot, name };
      lots = lots.map((candidate) => (candidate.id === lot.id ? renamed : candidate));
      send(response, 200, lotView(renamed));
      return;
    }

    if (path === '/control/tokens') {
      send(response, 200, { tokens: issued });
      return;
    }

    if (path === `${realmPath}/.well-known/openid-configuration`) {
      if (mode === 'discovery-error') {
        send(response, 500, { error: 'unavailable' });
        return;
      }
      send(response, 200, {
        issuer,
        authorization_endpoint: `${origin}${oidc}/auth`,
        token_endpoint: `${origin}${oidc}/token`,
        end_session_endpoint: `${origin}${oidc}/logout`,
        jwks_uri: `${origin}${oidc}/certs`,
        response_types_supported: ['code'],
        grant_types_supported: ['authorization_code', 'refresh_token'],
        subject_types_supported: ['public'],
        id_token_signing_alg_values_supported: ['RS256'],
        code_challenge_methods_supported: ['S256'],
        token_endpoint_auth_methods_supported: ['client_secret_basic', 'client_secret_post'],
        authorization_response_iss_parameter_supported: true,
        scopes_supported: ['openid', 'profile'],
      });
      return;
    }

    if (path === `${oidc}/certs`) {
      send(response, 200, { keys: [jwk] });
      return;
    }

    if (path === `${oidc}/auth` && request.method === 'GET') {
      const redirectUri = url.searchParams.get('redirect_uri') ?? '';
      const state = url.searchParams.get('state') ?? '';
      const challenge = url.searchParams.get('code_challenge');
      if (
        url.searchParams.get('client_id') !== clientId ||
        url.searchParams.get('response_type') !== 'code' ||
        url.searchParams.get('code_challenge_method') !== 'S256' ||
        challenge === null ||
        redirectUri === ''
      ) {
        send(response, 400, '<p>invalid authorization request</p>');
        return;
      }
      if (mode === 'error' || mode === 'access_denied') {
        const back = new URL(redirectUri);
        back.searchParams.set('error', mode === 'error' ? 'server_error' : 'access_denied');
        back.searchParams.set('state', state);
        back.searchParams.set('iss', issuer);
        redirectTo(response, back.href);
        return;
      }
      // Auto-approve: a form posted by script, as a real sign-in page would post its form.
      const fields = { redirect_uri: redirectUri, state, code_challenge: challenge, nonce: url.searchParams.get('nonce') ?? '' };
      const inputs = Object.entries(fields)
        .map(([name, value]) => `<input type="hidden" name="${name}" value="${escapeHtml(value)}">`)
        .join('');
      send(
        response,
        200,
        `<!doctype html><html lang="en"><title>Fake IdP</title><form method="post" action="${oidc}/auth/approve">${inputs}<button>Continue</button></form><script>document.forms[0].submit()</script></html>`,
      );
      return;
    }

    if (path === `${oidc}/auth/approve` && request.method === 'POST') {
      const form = await readForm(request);
      const code = randomUUID();
      const redirectUri = form.get('redirect_uri') ?? '';
      const nonce = form.get('nonce');
      codes.set(code, {
        redirectUri,
        codeChallenge: form.get('code_challenge') ?? '',
        nonce: nonce === '' ? null : nonce,
        sid: randomUUID(),
      });
      const back = new URL(redirectUri);
      back.searchParams.set('code', code);
      back.searchParams.set('state', form.get('state') ?? '');
      back.searchParams.set('session_state', randomUUID());
      back.searchParams.set('iss', issuer);
      redirectTo(response, back.href);
      return;
    }

    if (path === `${oidc}/token` && request.method === 'POST') {
      const form = await readForm(request);
      if (!authenticated(request, form)) {
        send(response, 401, { error: 'invalid_client' });
        return;
      }
      const grant = form.get('grant_type');
      if (grant === 'authorization_code') {
        const code = form.get('code') ?? '';
        const pending = codes.get(code);
        codes.delete(code);
        if (mode === 'token-error' || pending === undefined) {
          send(response, 400, { error: 'invalid_grant', error_description: 'Code not valid' });
          return;
        }
        const verifier = form.get('code_verifier') ?? '';
        if (form.get('redirect_uri') !== pending.redirectUri || s256(verifier) !== pending.codeChallenge) {
          send(response, 400, { error: 'invalid_grant', error_description: 'PKCE verification failed' });
          return;
        }
        send(response, 200, await tokens(pending.sid, pending.nonce));
        return;
      }
      if (grant === 'refresh_token') {
        const refreshToken = form.get('refresh_token') ?? '';
        const sid = refreshTokens.get(refreshToken);
        if (mode === 'short-lived' || sid === undefined) {
          send(response, 400, { error: 'invalid_grant', error_description: 'Token is not active' });
          return;
        }
        refreshTokens.delete(refreshToken);
        send(response, 200, await tokens(sid, null));
        return;
      }
      send(response, 400, { error: 'unsupported_grant_type' });
      return;
    }

    if (path === `${oidc}/logout`) {
      const back = url.searchParams.get('post_logout_redirect_uri');
      if (back === null) {
        send(response, 200, '<p>signed out</p>');
        return;
      }
      const target = new URL(back);
      const state = url.searchParams.get('state');
      if (state !== null) {
        target.searchParams.set('state', state);
      }
      redirectTo(response, target.href);
      return;
    }

    send(response, 404, { error: 'not found' });
  }

  const server: Server = createServer((request, response) => {
    handle(request, response).catch((error: unknown) => {
      send(response, 500, { error: String(error) });
    });
  });
  await new Promise<void>((resolve) => server.listen(port, resolve));

  return {
    issuer,
    origin,
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => {
          resolve();
        });
      }),
  };
}
