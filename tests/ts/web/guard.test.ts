import { isRedirect } from '@sveltejs/kit';
import { describe, expect, test } from 'vitest';
import { guardShell, signinState } from '$lib/server/guard';
import { displayUserOf } from '$lib/server/user';

function redirectOf(action: () => unknown): string {
  try {
    action();
  } catch (error) {
    if (isRedirect(error)) {
      return error.location;
    }
    throw error;
  }
  throw new Error('Expected a redirect.');
}

const identity = {
  authenticated: true,
  accessTokenRaw: 'secret-access-token',
  refreshTokenRaw: 'secret-refresh-token',
  idTokenRaw: 'secret-id-token',
  idToken: { name: 'Simon Novak', given_name: 'Simon', family_name: 'Novak', email: 'simon@example.org', sub: 'abc' },
};

describe('app shell guard', () => {
  test('UX-DR60 signed out → Sign in, with the return path when it is not Garden', () => {
    expect(redirectOf(() => guardShell({ session: { identity: null } }, new URL('http://x/garden')))).toBe('/signin');
    expect(redirectOf(() => guardShell({}, new URL('http://x/alerts')))).toBe('/signin?returnTo=%2Falerts');
  });

  test('UX-DR93 session ended → Sign in with the signed-out notice', () => {
    expect(redirectOf(() => guardShell({ session: { identity: null }, sessionEnded: true }, new URL('http://x/devices')))).toBe(
      '/signin?notice=signed-out&returnTo=%2Fdevices',
    );
  });

  test('AD-14 returns display fields only, never a token or other claim', () => {
    const data = guardShell({ session: { identity } }, new URL('http://x/garden'));
    expect(data).toEqual({ user: { displayName: 'Simon Novak', initials: 'SN' } });
    const serialised = JSON.stringify(data);
    for (const secret of ['secret-access-token', 'secret-refresh-token', 'secret-id-token', 'simon@example.org', 'abc']) {
      expect(serialised).not.toContain(secret);
    }
  });

  test('display name falls back through the claims', () => {
    expect(displayUserOf({ preferred_username: 'simon' })).toEqual({ displayName: 'simon', initials: 's' });
    expect(displayUserOf({ given_name: 'Ana', family_name: 'Silva' })).toEqual({ displayName: 'Ana Silva', initials: 'AS' });
    expect(displayUserOf({ name: 'Ana Maria Silva' })).toEqual({ displayName: 'Ana Maria Silva', initials: 'AS' });
    expect(displayUserOf(null)).toEqual({ displayName: '', initials: '' });
    expect(displayUserOf({ email: 'alice.smith@example.com' })).toEqual({ displayName: 'alice.smith@example.com', initials: 'as' });
    expect(displayUserOf({ email: 'bob@example.com' })).toEqual({ displayName: 'bob@example.com', initials: 'b' });
  });
});

describe('Sign-in state', () => {
  test('UX-DR59 signed out with no notice shows only the card', () => {
    expect(signinState({}, new URL('http://x/signin'))).toEqual({ notice: null, returnTo: null });
  });

  test.each(['unreachable', 'certificate', 'keycloak', 'signed-out'] as const)('UX-DR92 shows the %s notice from the query', (notice) => {
    expect(signinState({}, new URL(`http://x/signin?notice=${notice}`)).notice).toBe(notice);
  });

  test('UX-DR92 ignores unknown notices and unsafe return paths', () => {
    expect(signinState({}, new URL('http://x/signin?notice=<script>&returnTo=https://evil.example'))).toEqual({ notice: null, returnTo: null });
  });

  test('UX-DR93 a session that ended shows the signed-out notice', () => {
    expect(signinState({ sessionEnded: true }, new URL('http://x/signin')).notice).toBe('signed-out');
  });

  test('already signed in → straight to the return path', () => {
    expect(redirectOf(() => signinState({ session: { identity } }, new URL('http://x/signin?returnTo=/alerts')))).toBe('/alerts');
    expect(redirectOf(() => signinState({ session: { identity } }, new URL('http://x/signin')))).toBe('/garden');
  });
});

describe('app shell guard dependencies', () => {
  test('UX-DR93 reads the URL even when signed in, so it reruns on every navigation', () => {
    const read: string[] = [];
    const url = new Proxy(new URL('http://x/garden'), {
      get(target, property: string) {
        read.push(property);
        const value: unknown = Reflect.get(target, property);
        return typeof value === 'function' ? (value as (...args: unknown[]) => unknown).bind(target) : value;
      },
    });
    guardShell({ session: { identity } }, url);
    expect(read).toContain('pathname');
  });

  test('UX-DR60 the root path returns to Garden', () => {
    expect(redirectOf(() => guardShell({}, new URL('http://x/')))).toBe('/signin');
  });
});
