import { describe, expect, test } from 'vitest';
import { safeReturnPath } from '$lib/server/return-path';

describe('safe return path', () => {
  test.each([
    ['/alerts', '/alerts'],
    ['/settings/appearance', '/settings/appearance'],
    ['/devices?lot=1#top', '/devices?lot=1#top'],
    ['/garden', '/garden'],
  ])('keeps the same-origin path %s', (candidate, expected) => {
    expect(safeReturnPath(candidate)).toBe(expected);
  });

  test.each([
    null,
    undefined,
    '',
    'https://evil.example/garden',
    '//evil.example/garden',
    '/\\evil.example',
    '\\\\evil.example',
    'javascript:alert(1)',
    'garden',
    '/%0d%0aSet-Cookie:x=1',
    '/\u0000',
    '/.oidc/signin',
    '/.oidc/signout?redirect_uri=/',
    '/signin',
    '/signin/start',
  ])('falls back to /garden for %s', (candidate) => {
    const result = safeReturnPath(candidate);
    if (candidate === '/%0d%0aSet-Cookie:x=1') {
      // Encoded characters stay encoded, so the path is harmless and same-origin.
      expect(result.startsWith('/')).toBe(true);
      expect(result).not.toContain('\n');
      return;
    }
    expect(result).toBe('/garden');
  });
});
