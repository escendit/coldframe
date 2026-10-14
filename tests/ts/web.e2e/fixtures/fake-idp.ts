/**
 * A small OIDC provider for the e2e tests (Node `http` + `jose`). It does what Keycloak does for
 * the web app's flow — discovery, JWKS, authorize (auto-approve form), token with a PKCE S256
 * check and refresh, end-session — and doubles as the Coldframe Server: `/.well-known/healthz`
 * and an in-memory Site and Lot API (`GET`/`POST /sites`, `PATCH /sites/{id}`, the
 * `/sites/{id}/lots` routes) plus the Devices list (`GET /sites/{id}/devices`) and the notification
 * settings (`/me/notification-settings`, `/sites/{id}/notification-settings`, `/sites/{id}/reminder-cadence`). Control endpoints
 * switch failure modes, list every token it issued, and reset or seed the Sites, Lots (a Lot can
 * be seeded as holding a Node, or with a whole status as the Server would compute it) and Devices
 * (the list can be seeded to fail). `POST /control/reads` makes the Sites and Lots reads fail
 * after they succeeded: the connection is dropped, as from a Server that cannot be reached.
 * Story 8.2: the four pause endpoints (`PUT`/`DELETE /sites/{id}/pause` and `…/devices/{id}/pause`);
 * a Site carries `paused`, a Device its `pausedBy`, and a Lot whose Node is paused reads `paused`, as the
 * Server would compute it. `POST /control/pause` makes the next pause write fail.
 */
import { createHash, randomUUID } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { exportJWK, generateKeyPair, SignJWT, type CryptoKey, type JWK } from 'jose';

export type Mode = 'normal' | 'error' | 'access_denied' | 'token-error' | 'short-lived' | 'short-refresh' | 'discovery-error';

export const modes: readonly Mode[] = ['normal', 'error', 'access_denied', 'token-error', 'short-lived', 'short-refresh', 'discovery-error'];

/**
 * The access and ID token lifetime of the short-lived modes: `short-lived` also rejects the refresh (the
 * session ends), `short-refresh` accepts it (the session goes on with a new access token).
 */
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
  /** Story 8.2: the Site's Pause, seeded or set through `PUT /sites/{id}/pause`; the end is a UTC instant. */
  readonly paused?: boolean;
  readonly pausedUntil?: string;
}

/** One pause write the fake Server received (Story 8.2), with the status it answered. */
export interface FakePauseCall {
  readonly method: 'PUT' | 'DELETE';
  readonly path: string;
  readonly body: Record<string, unknown>;
  readonly status: number;
}

/** What the next pause write does instead of pausing: answer this status, or `drop` the connection. */
export type FakePauseFailure = number | 'drop';

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
  /** Story 7.2: the silent Hub behind the Lot, and when the silent Device was last heard; returned as seeded. */
  readonly silentHubId?: string;
  readonly silentSince?: string;
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

/** The caller's own notification settings, as `GET /me/notification-settings` returns them (Story 6.3). */
export interface FakeNotificationSettings {
  readonly window: { readonly from: string; readonly to: string };
  readonly timeZone?: string;
  readonly timeZoneConfirmed: boolean;
}

export type FakeReminderCadence = 'daily' | 'every2Days';

/** The caller's own settings for one Site; a Site without an entry is not muted and uses the Site setting. */
export interface FakeSiteNotificationSettings {
  readonly siteId: string;
  readonly muted: boolean;
  readonly reminderCadence?: FakeReminderCadence;
}

/** A Site's Reminder cadence; a Site without an entry is daily. */
export interface FakeSiteCadence {
  readonly siteId: string;
  readonly cadence: FakeReminderCadence;
}

/** One write to a notification-settings route the fake Server received. */
export interface FakeNotificationWrite {
  readonly method: string;
  readonly path: string;
  readonly body: unknown;
}

/** The status the next write to each resource answers instead of saving; then it answers again. */
export interface FakeNotificationFailures {
  readonly mine?: number;
  readonly site?: number;
  readonly cadence?: number;
}

/** What `POST /control/notifications` seeds; a field left out goes back to its default. */
export interface FakeNotificationsSeed {
  readonly settings?: FakeNotificationSettings;
  readonly siteSettings?: readonly FakeSiteNotificationSettings[];
  readonly cadences?: readonly FakeSiteCadence[];
  readonly failNext?: FakeNotificationFailures;
}

/** What `GET /control/notifications` answers. */
export interface FakeNotificationsState {
  readonly settings: FakeNotificationSettings;
  readonly siteSettings: readonly FakeSiteNotificationSettings[];
  readonly cadences: readonly FakeSiteCadence[];
  readonly writes: readonly FakeNotificationWrite[];
}

const defaultNotificationSettings: FakeNotificationSettings = { window: { from: '07:00', to: '22:00' }, timeZoneConfirmed: false };

const wallClock = /^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/u;

function isCadence(value: unknown): value is FakeReminderCadence {
  return value === 'daily' || value === 'every2Days';
}

/** True for an IANA zone this runtime knows, as the Server checks the zones it is sent. */
function knownZone(value: unknown): value is string {
  if (typeof value !== 'string' || value === '' || value.length > 64) {
    return false;
  }
  try {
    new Intl.DateTimeFormat('en', { timeZone: value });
    return true;
  } catch {
    return false;
  }
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
  /**
   * Story 7.2: seeded while the Device has an open silent Alert. A Hub with it is also one of the Site's
   * `silentHubs` on the Lots list. The fake opens and closes nothing by itself.
   */
  readonly silentSince?: string;
  /** The Device's own Silence Window in seconds; without it the list gives the default of its kind. */
  readonly silenceWindowSeconds?: number;
  /**
   * Story 8.2: the Device's own Pause, seeded or set through `PUT …/devices/{id}/pause`, with its end as a
   * UTC instant. The list's `pausedBy` adds `site` while the Site is paused.
   */
  readonly paused?: boolean;
  readonly pausedUntil?: string;
}

/** The Silence Window a Device has without its own (Story 7.2). */
export const defaultSilenceWindowSeconds = { node: 21_600, hub: 300 } as const;

/** One `PUT …/silence-window` the fake Server accepted. */
export interface FakeSilenceWindowPut {
  readonly deviceId: string;
  readonly body: Record<string, unknown>;
}

/** A Health Alert of the fake Server, as `AlertList.healthAlerts` lists it (Story 7.2). */
export interface FakeHealthAlert {
  readonly id: string;
  readonly siteId: string;
  /** `silent`, or any other Health kind: the contract's enum is extensible. */
  readonly kind: string;
  readonly deviceId: string;
  readonly deviceKind: 'hub' | 'node';
  readonly lotId?: string;
  readonly lotName?: string;
  readonly silentSince?: string;
  /** The level a `battery` Alert opened with (Story 7.3). */
  readonly batteryPercent?: number;
  readonly openedAt: string;
  readonly closedAt?: string;
  readonly reason?: string;
}

/** An Alert of the fake Server, as `GET /sites/{id}/alerts` lists it (Story 6.2). */
export interface FakeAlert {
  readonly id: string;
  readonly siteId: string;
  /** `threshold`, or a Health kind; any string, as the contract's enum is extensible. */
  readonly kind: string;
  readonly side?: 'low' | 'high';
  readonly quantity: string;
  readonly lotId: string;
  readonly lotName: string;
  readonly deviceId: string;
  readonly openedAt: string;
  readonly closedAt?: string;
  readonly reason?: string;
}

/** One move or unassign the fake Server accepted (Story 4.9). */
export interface FakeDeviceAction {
  readonly action: 'move' | 'unassign';
  readonly deviceId: string;
  readonly lotId?: string;
}

/** Seeded by default, so every spec that signs in still reaches Garden. */
/** One request the fake hub received (Story 6.6): what the web app's server forwarded, never a secret itself. */
export interface FakeLiveRequest {
  readonly method: string;
  readonly path: string;
  /** The names of the query parameters. */
  readonly query: readonly string[];
  /** Whether it carried an access token this provider issued, as `Authorization: Bearer`. */
  readonly bearer: boolean;
  /**
   * Which token that was, by its `jti`, and whether it had expired when the request arrived; `null`
   * without a readable token. Enough to tell two tokens apart, and no token itself.
   */
  readonly token: { readonly id: string; readonly expired: boolean } | null;
  /** The names of every header but the ones Node's fetch adds itself. */
  readonly headers: readonly string[];
}

/** What `GET /control/live` answers. */
export interface FakeLiveState {
  /** Connections whose event stream is open and whose handshake is done. */
  readonly connections: number;
  readonly requests: readonly FakeLiveRequest[];
}

/** What `POST /control/live` takes: a hub message for every open connection, and switches of the hub. */
export interface FakeLiveControl {
  readonly send?: { readonly target: string; readonly argument: unknown };
  /** Ends every event stream, as when the Server closes a connection whose token expired. */
  readonly drop?: boolean;
  /** Makes the hub answer 401, as for a session that is gone. */
  readonly refuse?: boolean;
  /** Forgets the recorded requests and stops refusing. */
  readonly reset?: boolean;
}

/** SignalR's JSON hub protocol ends every message with this record separator. */
const recordSeparator = '\u001e';

/** Headers Node's fetch adds to every request; the rest is what the web app chose to forward. */
const fetchHeaders = new Set(['host', 'connection', 'accept-language', 'sec-fetch-mode', 'user-agent', 'accept-encoding', 'content-length', 'transfer-encoding']);

interface LiveConnection {
  stream: ServerResponse | null;
  ready: boolean;
  ping: ReturnType<typeof setInterval> | null;
}

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
  let alerts: FakeAlert[] = [];
  let healthAlerts: FakeHealthAlert[] = [];
  let silenceWindowPuts: FakeSilenceWindowPut[] = [];
  /** Story 8.2: every pause write received, and what the next one does instead of pausing. */
  let pauseCalls: FakePauseCall[] = [];
  let pauseFailNext: FakePauseFailure | null = null;
  /** While set, every pause write waits here before it is answered: a Server that is slow to answer. */
  let pauseHold: { readonly held: Promise<void>; readonly release: () => void } | null = null;
  const releasePauses = (): void => {
    pauseHold?.release();
    pauseHold = null;
  };
  /** Set to answer the Alerts list with this status instead (a Server that is down). */
  let alertsStatus: number | null = null;
  /** How many times the Alerts of any Site were asked for since the last reset. */
  let alertReads = 0;
  /** Story 6.3: the one user's notification settings, per-Site settings and the Sites' cadences. */
  let notificationSettings: FakeNotificationSettings = defaultNotificationSettings;
  let siteNotificationSettings: FakeSiteNotificationSettings[] = [];
  let siteCadences: FakeSiteCadence[] = [];
  let notificationWrites: FakeNotificationWrite[] = [];
  let notificationFailures: FakeNotificationFailures = {};
  /** Reads that drop the connection instead of answering. */
  let failing: FailingReads = 'none';
  /** How many times the Lots of any Site were asked for since the last reset. */
  let lotReads = 0;
  /** Story 6.6: the fake SignalR hub's connections by connection token, and what it was asked. */
  const liveConnections = new Map<string, LiveConnection>();
  let liveRequests: FakeLiveRequest[] = [];
  let liveRefuse = false;

  function liveWrite(connection: LiveConnection, message: object): boolean {
    if (connection.stream === null || connection.stream.writableEnded) {
      return false;
    }
    connection.stream.write(`data: ${JSON.stringify(message)}${recordSeparator}\r\n\r\n`);
    return true;
  }

  function liveEnd(token: string): void {
    const connection = liveConnections.get(token);
    if (connection === undefined) {
      return;
    }
    if (connection.ping !== null) {
      clearInterval(connection.ping);
    }
    connection.stream?.end();
    liveConnections.delete(token);
  }

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

  /** The `jti` and expiry of the bearer token of a request, read from its payload. */
  function bearerToken(request: IncomingMessage): { id: string; expired: boolean } | null {
    const payload = request.headers.authorization?.split(' ')[1]?.split('.')[1];
    if (payload === undefined) {
      return null;
    }
    try {
      const claims = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as { jti?: unknown; exp?: unknown };
      return typeof claims.jti === 'string' && typeof claims.exp === 'number' ? { id: claims.jti, expired: claims.exp * 1000 <= Date.now() } : null;
    } catch {
      return null;
    }
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
    const lifetime = mode === 'short-lived' || mode === 'short-refresh' ? shortLivedSeconds : 300;
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

  /**
   * Story 8.2: a Device's Pause as the Server tells it. The sources are the Device's own Pause and its
   * Site's, `device` before `site`; the end is the later one, or none while any source has no end. Null
   * when nothing pauses it.
   */
  function pauseOf(device: Pick<FakeDevice, 'paused' | 'pausedUntil'> | undefined, site: FakeSite | undefined): { pausedBy: ('device' | 'site')[]; pausedUntil?: string } | null {
    const sources: { source: 'device' | 'site'; until: string | undefined }[] = [];
    if (device?.paused === true) {
      sources.push({ source: 'device', until: device.pausedUntil });
    }
    if (site?.paused === true) {
      sources.push({ source: 'site', until: site.pausedUntil });
    }
    if (sources.length === 0) {
      return null;
    }
    const ends = sources.map((entry) => entry.until);
    const latest = ends.every((end) => end !== undefined) ? ends.toSorted().at(-1) : undefined;
    return { pausedBy: sources.map((entry) => entry.source), ...(latest === undefined ? {} : { pausedUntil: latest }) };
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
      alerts = [];
      healthAlerts = [];
      silenceWindowPuts = [];
      pauseCalls = [];
      pauseFailNext = null;
      releasePauses();
      alertsStatus = null;
      alertReads = 0;
      notificationSettings = defaultNotificationSettings;
      siteNotificationSettings = [];
      siteCadences = [];
      notificationWrites = [];
      notificationFailures = {};
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
      send(response, 200, { sites, posts, lots, lotPosts, lotReads, devices, deviceActions, calibrations, calibrationPosts, thresholds, thresholdPuts, silenceWindowPuts });
      return;
    }
    // Story 8.2: what the next pause write does instead of pausing (`failNext`), and whether pause writes wait
    // before they are answered (`hold`); `POST /control/sites` clears both and the calls.
    if (path === '/control/pause' && request.method === 'POST') {
      const body = await readJson(request);
      if ('failNext' in body) {
        const requested = body.failNext;
        pauseFailNext = typeof requested === 'number' || requested === 'drop' ? requested : null;
      }
      if (body.hold === true && pauseHold === null) {
        let release = (): void => undefined;
        const held = new Promise<void>((resolve) => {
          release = resolve;
        });
        pauseHold = { held, release };
      } else if (body.hold === false) {
        releasePauses();
      }
      send(response, 200, { failNext: pauseFailNext, hold: pauseHold !== null });
      return;
    }
    if (path === '/control/pause') {
      send(response, 200, { calls: pauseCalls });
      return;
    }
    // Story 6.2: seeds the Alerts, or makes their list answer a status; `POST /control/sites` clears both.
    if (path === '/control/alerts' && request.method === 'POST') {
      const body = await readJson(request);
      alerts = Array.isArray(body.alerts) ? (body.alerts as FakeAlert[]) : [];
      healthAlerts = Array.isArray(body.healthAlerts) ? (body.healthAlerts as FakeHealthAlert[]) : [];
      alertsStatus = typeof body.alertsStatus === 'number' ? body.alertsStatus : null;
      alertReads = 0;
      send(response, 200, { alerts });
      return;
    }
    if (path === '/control/alerts') {
      send(response, 200, { alerts, alertReads });
      return;
    }
    // Story 6.3: seeds the notification settings and the next failure of a write; `POST /control/sites` resets them.
    if (path === '/control/notifications' && request.method === 'POST') {
      const body = (await readJson(request)) as FakeNotificationsSeed;
      notificationSettings = body.settings ?? defaultNotificationSettings;
      siteNotificationSettings = [...(body.siteSettings ?? [])];
      siteCadences = [...(body.cadences ?? [])];
      notificationFailures = body.failNext ?? {};
      notificationWrites = [];
      send(response, 200, {});
      return;
    }
    if (path === '/control/notifications') {
      const state: FakeNotificationsState = { settings: notificationSettings, siteSettings: siteNotificationSettings, cadences: siteCadences, writes: notificationWrites };
      send(response, 200, state);
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
        // Story 8.2: `paused` always, `pausedUntil` only while paused with an end.
        send(response, 200, {
          sites: sites.map((site) => ({
            id: site.id,
            name: site.name,
            role: site.role,
            paused: site.paused === true,
            ...(site.paused === true && site.pausedUntil !== undefined ? { pausedUntil: site.pausedUntil } : {}),
          })),
        });
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

    // Story 8.2: Pause and Resume of a Site or of one Device (Administrator). `until` is a calendar date that must lie
    // ahead; the fake ends the Pause at 00:00 UTC of it, where the Server uses the caller's time zone.
    const pauseMatch = /^\/sites\/([^/]+)(?:\/devices\/([^/]+))?\/pause$/u.exec(path);
    if (pauseMatch !== null && (request.method === 'PUT' || request.method === 'DELETE')) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const method = request.method;
      const siteId = decodeURIComponent(pauseMatch[1] ?? '');
      const deviceId = pauseMatch[2] === undefined ? null : decodeURIComponent(pauseMatch[2]);
      const body = method === 'PUT' ? await readJson(request) : {};
      if (pauseHold !== null) {
        await pauseHold.held;
      }
      const refuse = (status: number, slug: string): void => {
        pauseCalls.push({ method, path, body, status });
        problem(response, status, slug);
      };
      if (pauseFailNext !== null) {
        const failure = pauseFailNext;
        pauseFailNext = null;
        if (failure === 'drop') {
          // No answer at all: the web app sees a Server it cannot reach.
          pauseCalls.push({ method, path, body, status: 0 });
          request.socket.destroy();
          return;
        }
        refuse(failure, failure === 403 ? 'forbidden' : 'unavailable');
        return;
      }
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        refuse(404, 'site-not-found');
        return;
      }
      if (site.role === 'Member') {
        refuse(403, 'forbidden');
        return;
      }
      const device = deviceId === null ? null : devices.find((candidate) => candidate.id === deviceId && candidate.siteId === siteId);
      if (device === undefined) {
        refuse(404, 'device-not-found');
        return;
      }
      const until = body.until;
      if (until !== undefined && (typeof until !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(until) || until <= new Date().toISOString().slice(0, 10))) {
        refuse(400, 'validation');
        return;
      }
      const pause = method === 'PUT' ? { paused: true, ...(typeof until === 'string' ? { pausedUntil: `${until}T00:00:00.000Z` } : {}) } : {};
      pauseCalls.push({ method, path, body, status: 200 });
      if (device === null) {
        const changed: FakeSite = { id: site.id, name: site.name, role: site.role, ...pause };
        sites = sites.map((candidate) => (candidate === site ? changed : candidate));
        send(response, 200, { paused: changed.paused === true, ...(changed.pausedUntil === undefined ? {} : { pausedUntil: changed.pausedUntil }) });
        return;
      }
      const rest = Object.fromEntries(Object.entries(device).filter(([key]) => key !== 'paused' && key !== 'pausedUntil')) as unknown as FakeDevice;
      const changed: FakeDevice = { ...rest, ...pause };
      devices = devices.map((candidate) => (candidate === device ? changed : candidate));
      send(response, 200, pauseOf(changed, sites.find((candidate) => candidate.id === siteId)) ?? { pausedBy: [] });
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

    // Story 7.2: a Device's Silence Window (Administrator). 60 to 2592000 whole seconds, or no value for the default of its kind.
    const windowMatch = /^\/sites\/([^/]+)\/devices\/([^/]+)\/silence-window$/u.exec(path);
    if (windowMatch !== null && request.method === 'PUT') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(windowMatch[1] ?? '');
      const deviceId = decodeURIComponent(windowMatch[2] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      if (site.role === 'Member') {
        problem(response, 403, 'forbidden');
        return;
      }
      if (!/^[0-9a-f]{16}$/u.test(deviceId)) {
        problem(response, 400, 'validation');
        return;
      }
      const device = devices.find((candidate) => candidate.id === deviceId && candidate.siteId === siteId);
      if (device === undefined) {
        problem(response, 404, 'device-not-found');
        return;
      }
      const body = await readJson(request);
      const requested = body.windowSeconds;
      if (requested !== undefined && (typeof requested !== 'number' || !Number.isInteger(requested) || requested < 60 || requested > 2_592_000)) {
        problem(response, 400, 'validation');
        return;
      }
      silenceWindowPuts.push({ deviceId, body });
      const rest = Object.fromEntries(Object.entries(device).filter(([key]) => key !== 'silenceWindowSeconds')) as unknown as FakeDevice;
      const changed: FakeDevice = requested === undefined ? rest : { ...rest, silenceWindowSeconds: requested };
      devices = devices.map((candidate) => (candidate === device ? changed : candidate));
      send(response, 200, { windowSeconds: requested ?? defaultSilenceWindowSeconds[device.kind], overridden: requested !== undefined });
      return;
    }

    // Story 6.2: the Alerts list (Member): open newest first, then closed newest first, in pages. Story 7.2: `alerts` are the
    // Threshold Alerts as seeded; the Health Alerts come beside them, not paged, with the first page only.
    const alertsMatch = /^\/sites\/([^/]+)\/alerts$/u.exec(path);
    if (alertsMatch !== null && request.method === 'GET') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(alertsMatch[1] ?? '');
      if (!sites.some((candidate) => candidate.id === siteId)) {
        problem(response, 404, 'site-not-found');
        return;
      }
      if (alertsStatus !== null) {
        problem(response, alertsStatus, 'unavailable');
        return;
      }
      const limit = Number(url.searchParams.get('limit') ?? '50');
      const start = Number(url.searchParams.get('cursor') ?? '0');
      if (!Number.isInteger(limit) || limit < 1 || limit > 200 || !Number.isInteger(start) || start < 0) {
        problem(response, 400, 'validation');
        return;
      }
      alertReads += 1;
      const newest = (a: string, b: string): number => (a < b ? 1 : a > b ? -1 : 0);
      const ofSite = alerts.filter((alert) => alert.siteId === siteId);
      const open = ofSite.filter((alert) => alert.closedAt === undefined).toSorted((a, b) => newest(a.openedAt, b.openedAt));
      const closed = ofSite.filter((alert) => alert.closedAt !== undefined).toSorted((a, b) => newest(a.closedAt ?? '', b.closedAt ?? ''));
      const ordered = [...open, ...closed];
      const listed = ordered.slice(start, start + limit).map((alert) => ({
        id: alert.id,
        kind: alert.kind,
        ...(alert.side === undefined ? {} : { side: alert.side }),
        quantity: alert.quantity,
        lotId: alert.lotId,
        lotName: alert.lotName,
        deviceId: alert.deviceId,
        openedAt: alert.openedAt,
        ...(alert.closedAt === undefined ? {} : { closedAt: alert.closedAt }),
        ...(alert.reason === undefined ? {} : { reason: alert.reason }),
      }));
      const healthOfSite = healthAlerts.filter((alert) => alert.siteId === siteId);
      const healthOpen = healthOfSite.filter((alert) => alert.closedAt === undefined).toSorted((a, b) => newest(a.openedAt, b.openedAt));
      const healthClosed = healthOfSite.filter((alert) => alert.closedAt !== undefined).toSorted((a, b) => newest(a.closedAt ?? '', b.closedAt ?? ''));
      const health = [...healthOpen, ...healthClosed].map((alert) => Object.fromEntries(Object.entries(alert).filter(([key]) => key !== 'siteId')));
      send(response, 200, {
        alerts: listed,
        // Absent on a page read with `cursor`.
        ...(url.searchParams.has('cursor') ? {} : { healthAlerts: health }),
        openCount: open.length + healthOpen.length,
        ...(start + limit < ordered.length ? { nextCursor: String(start + limit) } : {}),
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
      const listedSite = sites.find((candidate) => candidate.id === siteId);
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
          ...(device.silentSince === undefined ? {} : { silentSince: device.silentSince }),
          silenceWindowSeconds: device.silenceWindowSeconds ?? defaultSilenceWindowSeconds[device.kind],
          silenceWindowOverridden: device.silenceWindowSeconds !== undefined,
          // Story 8.2: both members only while the Device is paused.
          ...(pauseOf(device, listedSite) ?? {}),
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

    // Story 6.3: the caller's own Notification Window and time zone. The first path that names no Site.
    if (path === '/me/notification-settings') {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      if (request.method === 'GET') {
        send(response, 200, notificationSettings);
        return;
      }
      const body = await readJson(request);
      notificationWrites = [...notificationWrites, { method: 'PATCH', path, body }];
      if (notificationFailures.mine !== undefined) {
        const status = notificationFailures.mine;
        notificationFailures = { ...notificationFailures, mine: undefined };
        problem(response, status, 'unavailable');
        return;
      }
      const window = body.window as { from?: unknown; to?: unknown } | undefined;
      let next = notificationSettings;
      if (window !== undefined) {
        const from = window.from;
        const to = window.to ?? '22:00';
        if (typeof from !== 'string' || typeof to !== 'string' || !wallClock.test(from) || !wallClock.test(to) || from >= to) {
          problem(response, 400, 'validation');
          return;
        }
        next = { ...next, window: { from, to } };
      }
      if (body.timeZone !== undefined) {
        if (!knownZone(body.timeZone)) {
          problem(response, 400, 'validation');
          return;
        }
        // The User's own choice always wins.
        next = { ...next, timeZone: body.timeZone, timeZoneConfirmed: true };
      }
      if (body.detectedTimeZone !== undefined) {
        if (!knownZone(body.detectedTimeZone)) {
          problem(response, 400, 'validation');
          return;
        }
        // Kept only while the User has chosen none.
        next = next.timeZoneConfirmed ? next : { ...next, timeZone: body.detectedTimeZone };
      }
      if (window === undefined && body.timeZone === undefined && body.detectedTimeZone === undefined) {
        problem(response, 400, 'validation');
        return;
      }
      notificationSettings = next;
      send(response, 200, notificationSettings);
      return;
    }

    // Story 6.3: the caller's mute and cadence for a Site (Member), and the Site's cadence (Member reads, Administrator sets).
    const notificationsMatch = /^\/sites\/([^/]+)\/(notification-settings|reminder-cadence)$/u.exec(path);
    if (notificationsMatch !== null) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      const siteId = decodeURIComponent(notificationsMatch[1] ?? '');
      const site = sites.find((candidate) => candidate.id === siteId);
      if (site === undefined) {
        problem(response, 404, 'site-not-found');
        return;
      }
      const cadenceOf = (): FakeReminderCadence => siteCadences.find((entry) => entry.siteId === siteId)?.cadence ?? 'daily';
      if (notificationsMatch[2] === 'reminder-cadence') {
        if (request.method === 'GET') {
          send(response, 200, { cadence: cadenceOf() });
          return;
        }
        if (site.role === 'Member') {
          problem(response, 403, 'forbidden');
          return;
        }
        const body = await readJson(request);
        notificationWrites = [...notificationWrites, { method: 'PUT', path, body }];
        if (!isCadence(body.cadence)) {
          problem(response, 400, 'validation');
          return;
        }
        const failure = notificationFailures.cadence;
        notificationFailures = { ...notificationFailures, cadence: undefined };
        if (failure !== undefined && failure !== 503) {
          problem(response, failure, 'unavailable');
          return;
        }
        // The Site keeps the cadence even when a member was not reached (503): repeating the request repairs it.
        siteCadences = [...siteCadences.filter((entry) => entry.siteId !== siteId), { siteId, cadence: body.cadence }];
        if (failure === 503) {
          problem(response, 503, 'reminder-cadence-not-delivered');
          return;
        }
        send(response, 200, { cadence: body.cadence });
        return;
      }
      const viewOf = (): Record<string, unknown> => {
        const own = siteNotificationSettings.find((entry) => entry.siteId === siteId);
        return { muted: own?.muted ?? false, ...(own?.reminderCadence === undefined ? {} : { reminderCadence: own.reminderCadence }), siteReminderCadence: cadenceOf() };
      };
      if (request.method === 'GET') {
        send(response, 200, viewOf());
        return;
      }
      const body = await readJson(request);
      notificationWrites = [...notificationWrites, { method: 'PUT', path, body }];
      if (notificationFailures.site !== undefined) {
        const status = notificationFailures.site;
        notificationFailures = { ...notificationFailures, site: undefined };
        problem(response, status, 'unavailable');
        return;
      }
      if (typeof body.muted !== 'boolean' || (body.reminderCadence !== undefined && !isCadence(body.reminderCadence))) {
        problem(response, 400, 'validation');
        return;
      }
      // Both values are replaced: a missing cadence means "use the Site setting".
      siteNotificationSettings = [
        ...siteNotificationSettings.filter((entry) => entry.siteId !== siteId),
        { siteId, muted: body.muted, ...(isCadence(body.reminderCadence) ? { reminderCadence: body.reminderCadence } : {}) },
      ];
      send(response, 200, viewOf());
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
      const seededStatus = (lot: FakeLot): string => lot.status ?? (lot.claimed === true ? 'unknown' : 'noNode');
      /**
       * Story 8.2: the Pause of the Lot's Node, set through the pause endpoints or seeded on the Site or the
       * Device (not on the Lot: a Lot seeded as paused is returned as seeded). A Lot without a Node has none.
       */
      const lotPause = (lot: FakeLot): ReturnType<typeof pauseOf> => {
        if (seededStatus(lot) === 'noNode') {
          return null;
        }
        const node = devices.find((device) => device.siteId === siteId && device.kind === 'node' && (device.lotId === lot.id || device.id === lot.node?.deviceId));
        return pauseOf(node, site);
      };
      const statusOf = (lot: FakeLot): string => (lotPause(lot) === null ? seededStatus(lot) : 'paused');
      const lotView = (lot: FakeLot, detail = false): Record<string, unknown> => ({
        id: lot.id,
        name: lot.name,
        status: statusOf(lot),
        statusSince: lot.statusSince ?? startedAt,
        ...(lot.lastReadingAt === undefined ? {} : { lastReadingAt: lot.lastReadingAt }),
        ...(lot.unknownCause !== undefined ? { unknownCause: lot.unknownCause } : statusOf(lot) === 'unknown' ? { unknownCause: 'node' } : {}),
        ...(lot.silentHubId === undefined ? {} : { silentHubId: lot.silentHubId }),
        ...(lot.silentSince === undefined ? {} : { silentSince: lot.silentSince }),
        ...(lotPause(lot) ?? { ...(lot.pausedBy === undefined ? {} : { pausedBy: lot.pausedBy }), ...(lot.pausedUntil === undefined ? {} : { pausedUntil: lot.pausedUntil }) }),
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
          // Story 7.2: every Hub of the Site seeded as silent, by Device ID, whether or not a Lot is behind it.
          const silentHubs = devices
            .filter((device) => device.siteId === siteId && device.kind === 'hub' && device.silentSince !== undefined)
            .toSorted((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
            .map((device) => ({ deviceId: device.id, silentSince: device.silentSince }));
          send(response, 200, { lots: ordered.map(({ lot }) => lotView(lot)), ...(silentHubs.length === 0 ? {} : { silentHubs }) });
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

    // Story 6.6: drives the fake hub. See `FakeLiveControl`.
    if (path === '/control/live' && request.method === 'POST') {
      const body = (await readJson(request)) as FakeLiveControl;
      let delivered = 0;
      if (body.reset === true) {
        liveRequests = [];
        liveRefuse = false;
      }
      if (body.refuse !== undefined) {
        liveRefuse = body.refuse;
      }
      if (body.send !== undefined) {
        for (const connection of liveConnections.values()) {
          if (connection.ready && liveWrite(connection, { type: 1, target: body.send.target, arguments: [body.send.argument] })) {
            delivered += 1;
          }
        }
      }
      if (body.drop === true) {
        for (const token of [...liveConnections.keys()]) {
          liveEnd(token);
        }
      }
      send(response, 200, { delivered });
      return;
    }
    if (path === '/control/live') {
      const state: FakeLiveState = { connections: [...liveConnections.values()].filter((connection) => connection.ready && connection.stream !== null).length, requests: liveRequests };
      send(response, 200, state);
      return;
    }

    // The Server's SignalR hub (Story 6.6), as far as the web app uses it: negotiate, a Server-Sent Events
    // stream, and the client's handshake and pings by POST. It offers no other transport.
    if (path === '/live/negotiate' || path === '/live') {
      liveRequests.push({
        method: request.method ?? '',
        path,
        query: [...url.searchParams.keys()],
        bearer: bearerOk(request),
        token: bearerToken(request),
        headers: Object.keys(request.headers).filter((name) => !fetchHeaders.has(name)).sort(),
      });
      // Like the Server, the hub refuses a token that has expired.
      if (liveRefuse || !bearerOk(request) || bearerToken(request)?.expired !== false) {
        problem(response, 401, 'unauthorized');
        return;
      }
      if (path === '/live/negotiate') {
        if (request.method !== 'POST') {
          send(response, 405, { error: 'method not allowed' });
          return;
        }
        const token = randomUUID();
        liveConnections.set(token, { stream: null, ready: false, ping: null });
        send(response, 200, {
          negotiateVersion: 1,
          connectionId: randomUUID(),
          connectionToken: token,
          availableTransports: [{ transport: 'ServerSentEvents', transferFormats: ['Text'] }],
        });
        return;
      }
      const token = url.searchParams.get('id') ?? '';
      const connection = liveConnections.get(token);
      if (connection === undefined) {
        send(response, 404, { error: 'no such connection' });
        return;
      }
      if (request.method === 'GET') {
        response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache, no-store', 'x-accel-buffering': 'no' });
        response.write(':\r\n\r\n');
        connection.stream = response;
        // Keep-alive pings, well inside the client's 30 s server timeout.
        connection.ping = setInterval(() => {
          liveWrite(connection, { type: 6 });
        }, 10_000);
        request.on('close', () => {
          liveEnd(token);
        });
        return;
      }
      if (request.method === 'POST') {
        const chunks: Buffer[] = [];
        for await (const chunk of request) {
          chunks.push(chunk as Buffer);
        }
        for (const text of Buffer.concat(chunks).toString('utf8').split(recordSeparator)) {
          if (text === '') {
            continue;
          }
          const message = JSON.parse(text) as { protocol?: string; type?: number };
          if (message.protocol !== undefined) {
            // The handshake: an empty object accepts it.
            liveWrite(connection, {});
            connection.ready = true;
          } else if (message.type === 7) {
            liveEnd(token);
          }
        }
        send(response, 200, '', { 'content-type': 'text/plain' });
        return;
      }
      if (request.method === 'DELETE') {
        liveEnd(token);
        send(response, 202, '', { 'content-type': 'text/plain' });
        return;
      }
      send(response, 405, { error: 'method not allowed' });
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
