/** Where a signed-in user lands when no safe return path is known. */
export const defaultReturnPath = '/garden';

const placeholderOrigin = 'http://return-path.invalid';

/**
 * Accepts only a same-origin absolute path (`/alerts?x=1`), never a scheme, host, protocol-
 * relative `//host`, backslash trick or an OIDC or sign-in route. Anything else → `/garden`.
 */
export function safeReturnPath(candidate: string | null | undefined, fallback: string = defaultReturnPath): string {
  if (candidate === null || candidate === undefined || candidate === '') {
    return fallback;
  }
  if (!candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
    return fallback;
  }
  // eslint-disable-next-line no-control-regex -- control characters are exactly what is rejected
  if (/[\u0000-\u001F\u007F]/u.test(candidate)) {
    return fallback;
  }
  let resolved: URL;
  try {
    resolved = new URL(candidate, placeholderOrigin);
  } catch {
    return fallback;
  }
  if (resolved.origin !== placeholderOrigin) {
    return fallback;
  }
  const path = resolved.pathname;
  if (path.startsWith('/.oidc') || path === '/signin' || path.startsWith('/signin/')) {
    return fallback;
  }
  return `${path}${resolved.search}${resolved.hash}`;
}
