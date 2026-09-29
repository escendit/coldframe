import { classifyFailure, type Failure } from './failures';

export type Fetch = (input: URL, init?: RequestInit) => Promise<Response>;

/** Upper bound for each probe, so SIGN IN never hangs on an unreachable host. */
export const probeTimeoutMs = 5_000;

export type ProbeResult = { readonly ok: true } | { readonly ok: false; readonly failure: Failure };

const ok: ProbeResult = { ok: true };

/** The Server answers `/.well-known/healthz`; any HTTP response means it is reachable. */
export function serverHealthUrl(serverUrl: URL): URL {
  return new URL('/.well-known/healthz', serverUrl);
}

/** The issuer's discovery document, relative to the realm path. */
export function discoveryUrl(issuer: URL): URL {
  const base = issuer.href.endsWith('/') ? issuer.href : `${issuer.href}/`;
  return new URL('.well-known/openid-configuration', base);
}

/** Checks the Server answers at all. Refused, DNS, timeout → unreachable; TLS → certificate. */
export async function probeServer(serverUrl: URL, fetcher: Fetch = fetch, timeoutMs = probeTimeoutMs): Promise<ProbeResult> {
  try {
    const response = await fetcher(serverHealthUrl(serverUrl), {
      method: 'GET',
      redirect: 'manual',
      signal: AbortSignal.timeout(timeoutMs),
    });
    await response.body?.cancel();
    return ok;
  } catch (error) {
    return { ok: false, failure: classifyFailure(error, 'server') };
  }
}

/** Checks the issuer serves its discovery document. Any failure but TLS → Keycloak error. */
export async function probeIssuer(issuer: URL, fetcher: Fetch = fetch, timeoutMs = probeTimeoutMs): Promise<ProbeResult> {
  try {
    const response = await fetcher(discoveryUrl(issuer), {
      method: 'GET',
      headers: { accept: 'application/json' },
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!response.ok) {
      await response.body?.cancel();
      return { ok: false, failure: 'keycloak' };
    }
    const document: unknown = await response.json();
    if (typeof document !== 'object' || document === null || !('authorization_endpoint' in document)) {
      return { ok: false, failure: 'keycloak' };
    }
    return ok;
  } catch (error) {
    return { ok: false, failure: classifyFailure(error, 'issuer') };
  }
}

/** Server first, then the issuer; the first failure decides the notice. */
export async function probeSignIn(serverUrl: URL, issuer: URL, fetcher: Fetch = fetch, timeoutMs = probeTimeoutMs): Promise<ProbeResult> {
  const server = await probeServer(serverUrl, fetcher, timeoutMs);
  if (!server.ok) {
    return server;
  }
  return probeIssuer(issuer, fetcher, timeoutMs);
}

/**
 * After a failed code exchange (the package swallows the cause), checks the issuer again to tell
 * an untrusted certificate from any other Keycloak failure.
 */
export async function diagnoseCallback(issuer: URL, fetcher: Fetch = fetch, timeoutMs = probeTimeoutMs): Promise<Failure> {
  const probe = await probeIssuer(issuer, fetcher, timeoutMs);
  return !probe.ok && probe.failure === 'certificate' ? 'certificate' : 'keycloak';
}
