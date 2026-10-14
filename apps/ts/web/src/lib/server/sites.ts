/**
 * The web app's only calls to the Coldframe Server's Site API. Server-only: the browser never
 * calls the Server (AD-14). Each call carries the session's access token, which
 * `@escendit/sveltekit-auth-keycloak` keeps fresh, and turns every answer into a value.
 */
import { createColdframeClient, type Site } from '@coldframe/api-client';
import { classifyFailure } from './failures';
import { getConfig } from './runtime';

/**
 * `unavailable` is a 503 (Keycloak down, retry with the same key); `forbidden` a 403 (the Role is
 * missing or too low, e.g. it changed since the page loaded); `notFound` a 404 (the Site or Lot is
 * gone); `lotClaimed` a 409 (a Node holds the Lot); `unexpected` any other answer. Two answers are told apart by
 * their problem type (Story 9.2): `lastOwner` is the 409 `last-owner` (the Site would have no Owner),
 * `memberNotFound` the 404 `member-not-found` (the person is no member of the Site).
 */
export type SitesError =
  | 'validation'
  | 'unavailable'
  | 'unexpected'
  | 'keyReused'
  | 'unreachable'
  | 'certificate'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'lotClaimed'
  | 'lastOwner'
  | 'memberNotFound';

export type SitesResult<T> = { readonly ok: T } | { readonly error: SitesError };

type Locals = Pick<App.Locals, 'session'>;

export interface SitesDependencies {
  readonly serverUrl?: URL;
  readonly fetch?: (request: Request) => Promise<Response>;
}

/** Upper bound for each Server call, so a page never hangs on an unreachable Server. */
export const serverTimeoutMs = 10_000;

/** The session's access token, or null when signed out. It never leaves the server modules. */
export function accessTokenOf(locals: Locals): string | null {
  const token = locals.session?.identity?.accessTokenRaw;
  return typeof token === 'string' && token !== '' ? token : null;
}

const problemPrefix = 'urn:coldframe:problem:';

/** The slug of a Problem Details `type`, e.g. `last-owner`, or null when the body has none. */
function problemSlugOf(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || !('type' in body)) {
    return null;
  }
  const { type } = body;
  return typeof type === 'string' && type.startsWith(problemPrefix) ? type.slice(problemPrefix.length) : null;
}

function statusError(status: number, slug: string | null = null): SitesError {
  if (status === 409 && slug === 'last-owner') {
    return 'lastOwner';
  }
  if (status === 404 && slug === 'member-not-found') {
    return 'memberNotFound';
  }
  switch (status) {
    case 400:
      return 'validation';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'lotClaimed';
    case 422:
      return 'keyReused';
    case 503:
      return 'unavailable';
    default:
      return 'unexpected';
  }
}

/**
 * Runs one Server call with the session's token and turns the answer into a value. A 204 is `ok`
 * with no body. Shared by the Site and Lot calls.
 */
export async function call<T>(
  locals: Locals,
  dependencies: SitesDependencies,
  run: (client: ReturnType<typeof createColdframeClient>) => Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<SitesResult<T>> {
  const accessToken = accessTokenOf(locals);
  if (accessToken === null) {
    return { error: 'unauthorized' };
  }
  const fetcher = dependencies.fetch ?? ((request: Request) => fetch(request, { signal: AbortSignal.timeout(serverTimeoutMs) }));
  const client = createColdframeClient({ baseUrl: dependencies.serverUrl ?? getConfig().serverUrl, accessToken, fetch: fetcher });
  try {
    const { data, error, response } = await run(client);
    if (response.ok && data !== undefined) {
      return { ok: data };
    }
    // 204, and 202 for an invitation (Story 9.1): accepted, with no body.
    if (response.status === 204 || response.status === 202) {
      return { ok: undefined as T };
    }
    return { error: statusError(response.status, problemSlugOf(error)) };
  } catch (error) {
    return { error: classifyFailure(error, 'server') === 'certificate' ? 'certificate' : 'unreachable' };
  }
}

/** The caller's Sites with their Role, in the Server's order (never re-sorted here). */
export async function listSites(locals: Locals, dependencies: SitesDependencies = {}): Promise<SitesResult<readonly Site[]>> {
  const result = await call(locals, dependencies, (client) => client.GET('/sites'));
  return 'ok' in result ? { ok: result.ok.sites } : result;
}

/** Creates a Site with the caller as Owner. The body is `{name}` only; the time zone is the User's and goes to their own settings. */
export function createSite(locals: Locals, name: string, idempotencyKey: string, dependencies: SitesDependencies = {}): Promise<SitesResult<Site>> {
  return call(locals, dependencies, (client) =>
    client.POST('/sites', {
      params: { header: { 'Idempotency-Key': idempotencyKey } },
      body: { name },
    }),
  );
}

/** Renames a Site (Owner). The Server sets the Keycloak Organization name first (AD-3). */
export function renameSite(locals: Locals, siteId: string, name: string, dependencies: SitesDependencies = {}): Promise<SitesResult<Site>> {
  return call(locals, dependencies, (client) => client.PATCH('/sites/{siteId}', { params: { path: { siteId } }, body: { name } }));
}
