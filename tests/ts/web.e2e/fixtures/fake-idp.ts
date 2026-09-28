/**
 * A small OIDC provider for the e2e tests (Node `http` + `jose`). It does what Keycloak does for
 * the web app's flow — discovery, JWKS, authorize (auto-approve form), token with a PKCE S256
 * check and refresh, end-session — and doubles as the Coldframe Server: `/.well-known/healthz`
 * and an in-memory Site and Lot API (`GET`/`POST /sites`, `PATCH /sites/{id}`, the
 * `/sites/{id}/lots` routes). Control endpoints switch failure modes, list every token it issued,
 * and reset or seed the Sites and Lots (a Lot can be seeded as holding a Node).
 */
import { createHash, randomUUID } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { exportJWK, generateKeyPair, SignJWT, type CryptoKey, type JWK } from 'jose';

export type Mode = 'normal' | 'error' | 'access_denied' | 'token-error' | 'short-lived' | 'discovery-error';

export const modes: readonly Mode[] = ['normal', 'error', 'access_denied', 'token-error', 'short-lived', 'discovery-error'];

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

/** A Lot of the fake Server. `claimed` stands in for a Node assigned to it (Epic 4). */
export interface FakeLot {
  readonly id: string;
  readonly siteId: string;
  readonly name: string;
  readonly claimed?: boolean;
  readonly removed?: boolean;
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
  const createdLots = new Map<string, FakeLot>();

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
    const lifetime = mode === 'short-lived' ? 1 : 300;
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
      posts = [];
      lotPosts = [];
      created.clear();
      createdLots.clear();
      send(response, 200, { sites, lots });
      return;
    }
    if (path === '/control/sites') {
      send(response, 200, { sites, posts, lots, lotPosts });
      return;
    }

    // The Coldframe Server's Site API (Story 1.6 and 1.8), in memory.
    if (path === '/sites' && (request.method === 'GET' || request.method === 'POST')) {
      if (!bearerOk(request)) {
        problem(response, 401, 'unauthorized');
        return;
      }
      if (request.method === 'GET') {
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
      const rank = { Member: 1, Administrator: 2, Owner: 3 } as const;
      const allowed = (minimum: keyof typeof rank): boolean => rank[site.role] >= rank[minimum];
      const lotView = (lot: FakeLot): Record<string, unknown> => ({
        id: lot.id,
        name: lot.name,
        status: lot.claimed === true ? 'unknown' : 'noNode',
        ...(lot.removed === true ? { removed: true } : {}),
      });
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
          // The Server's order: status (unknown before noNode), then creation.
          const live = lots.filter((lot) => lot.siteId === siteId && lot.removed !== true);
          const ordered = [...live.filter((lot) => lot.claimed === true), ...live.filter((lot) => lot.claimed !== true)];
          send(response, 200, { lots: ordered.map(lotView) });
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
          send(response, 200, lotView(lot));
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
