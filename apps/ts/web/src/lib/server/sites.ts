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
 * gone); `lotClaimed` a 409 (a Node holds the Lot); `unexpected` any other answer.
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
  | 'lotClaimed';

export type SitesResult<T> = { readonly ok: T } | { readonly error: SitesError };

type Locals = Pick<App.Locals, 'session'>;

export interface SitesDependencies {
  readonly serverUrl?: URL;
  readonly fetch?: (request: Request) => Promise<Response>;
}

/** Upper bound for each Server call, so a page never hangs on an unreachable Server. */
export const serverTimeoutMs = 10_000;

function accessTokenOf(locals: Locals): string | null {
  const token = locals.session?.identity?.accessTokenRaw;
  return typeof token === 'string' && token !== '' ? token : null;
}

function statusError(status: number): SitesError {
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
  run: (client: ReturnType<typeof createColdframeClient>) => Promise<{ data?: T; response: Response }>,
): Promise<SitesResult<T>> {
  const accessToken = accessTokenOf(locals);
  if (accessToken === null) {
    return { error: 'unauthorized' };
  }
  const fetcher = dependencies.fetch ?? ((request: Request) => fetch(request, { signal: AbortSignal.timeout(serverTimeoutMs) }));
  const client = createColdframeClient({ baseUrl: dependencies.serverUrl ?? getConfig().serverUrl, accessToken, fetch: fetcher });
  try {
    const { data, response } = await run(client);
    if (response.ok && data !== undefined) {
      return { ok: data };
    }
    if (response.status === 204) {
      return { ok: undefined as T };
    }
    return { error: statusError(response.status) };
  } catch (error) {
    return { error: classifyFailure(error, 'server') === 'certificate' ? 'certificate' : 'unreachable' };
  }
}

/** The caller's Sites with their Role, in the Server's order (never re-sorted here). */
export async function listSites(locals: Locals, dependencies: SitesDependencies = {}): Promise<SitesResult<readonly Site[]>> {
  const result = await call(locals, dependencies, (client) => client.GET('/sites'));
  return 'ok' in result ? { ok: result.ok.sites } : result;
}

/** Creates a Site with the caller as Owner. The body is `{name}` only; the time zone stays here. */
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
