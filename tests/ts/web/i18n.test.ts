import { describe, expect, test } from 'vitest';
import { hasMessage, messages, t } from '$lib/i18n';

describe('message catalogue', () => {
  test('UX-DR124 looks up messages and interpolates parameters', () => {
    expect(t('app.name')).toBe('Coldframe');
    expect(t('shell.user', { name: 'Simon Novak' })).toBe('Signed in as Simon Novak');
    expect(t('app.titleSuffix', { page: t('signin.pageTitle') })).toBe('Sign in · Coldframe');
    expect(hasMessage('nav.garden')).toBe(true);
    expect(hasMessage('nav.nothing')).toBe(false);
  });

  test('UX-DR125 counted strings follow CLDR plural rules', () => {
    expect(t('count.lotsNeedWater', { count: 1 })).toBe('1 Lot needs water');
    expect(t('count.lotsNeedWater', { count: 2 })).toBe('2 Lots need water');
    expect(t('count.lotsNeedWater', { count: 0 })).toBe('0 Lots need water');
    expect(t('count.lotsNeedWater', { count: 1200 })).toBe('1,200 Lots need water');
    expect(t('count.openAlerts', { count: 5 })).toBe('Alerts, 5 open');
  });

  test('UX-DR125 every plural entry has the CLDR "other" form and no unknown categories', () => {
    const categories = new Set(['zero', 'one', 'two', 'few', 'many', 'other']);
    for (const [key, entry] of Object.entries(messages())) {
      if (typeof entry === 'string') {
        continue;
      }
      expect(entry.other, key).toBeTypeOf('string');
      for (const category of Object.keys(entry)) {
        expect(categories.has(category), `${key}.${category}`).toBe(true);
      }
    }
  });

  test('UX-DR125 a plural without a count is a programming error', () => {
    expect(() => t('count.lotsNeedWater')).toThrow('count');
  });

  test('UX-DR124 the Sign-in notices are the UX-DR92/93 copy verbatim', () => {
    expect(t('notice.unreachable')).toBe("Can't reach your Coldframe Server. Check that this phone is on your home Wi-Fi.");
    expect(t('notice.certificate')).toBe(
      "Your Server's certificate isn't trusted, so Coldframe won't connect. The Server needs a valid certificate for its domain.",
    );
    expect(t('notice.keycloak')).toBe("Sign-in didn't finish: your Server's sign-in page returned an error. Nothing was changed.");
    expect(t('notice.signedOut')).toBe("You're signed out. Sign in again to see live data.");
  });
});
