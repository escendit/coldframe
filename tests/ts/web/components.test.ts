import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { createRawSnippet } from 'svelte';
import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import AppShell from '$lib/components/AppShell.svelte';
import Button from '$lib/components/Button.svelte';
import Icon from '$lib/components/Icon.svelte';
import InlineNotice from '$lib/components/InlineNotice.svelte';
import LiveRegions from '$lib/components/LiveRegions.svelte';
import Modal from '$lib/components/Modal.svelte';
import RoleGate from '$lib/components/RoleGate.svelte';
import SegmentedChoice from '$lib/components/SegmentedChoice.svelte';
import SignInCard from '$lib/components/SignInCard.svelte';
import TextInput from '$lib/components/TextInput.svelte';
import ThemeSwitcher from '$lib/components/ThemeSwitcher.svelte';
import { t } from '$lib/i18n';
import { hasRole } from '$lib/roles';
import { webSrc } from './helpers.ts';

const children = createRawSnippet(() => ({ render: () => '<p>content</p>' }));

/** Counts elements matching a tag and attribute fragment in SSR output. */
function count(html: string, pattern: RegExp): number {
  return [...html.matchAll(new RegExp(pattern.source, 'gu'))].length;
}

describe('Button', () => {
  test('UX-DR34 primary, secondary and ghost variants with the label in place', () => {
    for (const variant of ['primary', 'secondary', 'ghost'] as const) {
      const { body } = render(Button, { props: { label: t('signin.action'), variant } });
      expect(body).toMatch(new RegExp(`<button[^>]*class="[^"]*cf-button--${variant}`, 'u'));
      expect(body).toContain('Sign in');
      expect(body).toContain('type="button"');
    }
  });

  test('UX-DR34 working shows the in-place progress label, never a spinner', () => {
    const { body } = render(Button, { props: { label: t('signin.action'), working: true, workingLabel: t('signin.working'), type: 'submit' } });
    expect(body).toContain('Signing in…');
    expect(body).not.toContain('>Sign in<');
    expect(body).toContain('aria-disabled="true"');
    expect(body).not.toMatch(/spinner|loading|<svg/iu);
  });

  test('UX-DR34 a link button renders an anchor', () => {
    const { body } = render(Button, { props: { label: t('notice.tryAgain'), href: '/signin/start', variant: 'ghost', reload: true } });
    expect(body).toMatch(/<a[^>]*href="\/signin\/start"/u);
    expect(body).toContain('data-sveltekit-reload');
  });
});

describe('TextInput', () => {
  test('UX-DR35 label above, helper below, linked for screen readers', () => {
    const { body } = render(TextInput, { props: { id: 'site', label: 'Site name', helper: 'Shown to Members' } });
    expect(body).toMatch(/<label[^>]*for="site"/u);
    expect(body).toMatch(/<input[^>]*id="site"[^>]*aria-describedby="site-helper"|<input[^>]*aria-describedby="site-helper"[^>]*id="site"/u);
    expect(body).toMatch(/id="site-helper"/u);
    expect(body).not.toContain('aria-invalid');
  });

  test('UX-DR35 invalid reason replaces the helper, with aria-invalid and the error icon', () => {
    const { body } = render(TextInput, { props: { id: 'site', label: 'Site name', helper: 'Shown to Members', invalid: 'Name the Site.' } });
    expect(body).toContain('aria-invalid="true"');
    expect(body).toContain('aria-describedby="site-reason"');
    expect(body).toMatch(/id="site-reason"[^>]*>Name the Site\./u);
    expect(body).not.toContain('Shown to Members');
    expect(body).toContain('data-icon="error--filled"');
  });

  test('UX-DR35 password fields offer a reveal with the view icon', () => {
    const { body } = render(TextInput, { props: { id: 'pw', label: 'Password', type: 'password' } });
    expect(body).toContain('type="password"');
    expect(body).toContain('data-icon="view"');
    expect(body).toContain(`aria-label="${t('field.showPassword')}"`);
    expect(body).toContain('aria-pressed="false"');
  });
});

describe('SegmentedChoice and ThemeSwitcher', () => {
  const options = [
    { value: 'a', label: 'Daily' },
    { value: 'b', label: 'Every 2 days' },
  ] as const;

  test('UX-DR36 selected segment is pressed and carries a leading checkmark', () => {
    const { body } = render(SegmentedChoice, { props: { label: 'Reminder', options, value: 'b', onchange: () => undefined } });
    expect(body).toMatch(/<fieldset/u);
    expect(body).toMatch(/<legend[^>]*>Reminder/u);
    expect(count(body, /aria-pressed="true"/u)).toBe(1);
    expect(count(body, /aria-pressed="false"/u)).toBe(1);
    expect(count(body, /data-icon="checkmark"/u)).toBe(1);
    const pressed = body.slice(body.indexOf('aria-pressed="true"'));
    expect(pressed.indexOf('data-icon="checkmark"')).toBeLessThan(pressed.indexOf('Every 2 days'));
  });

  test('UX-DR53 UX-DR15 Theme switcher offers System, Light and Dark with the current one selected', () => {
    const { body } = render(ThemeSwitcher, { props: { value: 'system' } });
    for (const label of ['System', 'Light', 'Dark']) {
      expect(body).toContain(label);
    }
    expect(body).toMatch(/aria-pressed="true"[^>]*>(?:(?!<\/button>).)*System/su);
    expect(count(body, /aria-pressed="true"/u)).toBe(1);
  });
});

describe('InlineNotice', () => {
  test('UX-DR56 layer-01 notice with one action and no dismiss control', () => {
    const { body } = render(InlineNotice, { props: { message: t('notice.unreachable'), action: { label: t('notice.tryAgain'), href: '/signin/start' } } });
    expect(body).toContain('cf-inline-notice');
    expect(body).toContain('reach your Coldframe Server');
    expect(count(body, /<a /u) + count(body, /<button/u)).toBe(1);
    expect(body).not.toMatch(/close|dismiss/iu);
  });

  test('UX-DR56 UX-DR92 the certificate notice has no action', () => {
    const { body } = render(InlineNotice, { props: { message: t('notice.certificate'), action: null } });
    expect(count(body, /<a /u) + count(body, /<button/u)).toBe(0);
  });
});

describe('Modal and RoleGate', () => {
  test('UX-DR76 UX-DR113 Modal is a native dialog with Cancel and a named result action', () => {
    const { body } = render(Modal, {
      props: { open: false, title: t('settings.signOutQuestion'), actionLabel: t('settings.signOut'), actionHref: '/.oidc/signout' },
    });
    expect(body).toMatch(/<dialog/u);
    expect(body).toContain('aria-labelledby');
    expect(body).toContain(t('modal.cancel'));
    expect(body).toContain(t('settings.signOut'));
  });

  test('UX-DR76 RoleGate hides, never disables, what a Role cannot use', () => {
    for (const [role, visible] of [
      ['Owner', true],
      ['Administrator', true],
      ['Member', false],
      [null, false],
    ] as const) {
      const { body } = render(RoleGate, { props: { role, minimum: 'Administrator', children } });
      expect(body.includes('content'), String(role)).toBe(visible);
      expect(body).not.toContain('disabled');
    }
    expect(hasRole('Owner', 'Owner')).toBe(true);
    expect(hasRole('Administrator', 'Owner')).toBe(false);
    expect(hasRole('Member', 'Member')).toBe(true);
  });
});

describe('App shell', () => {
  const user = { displayName: 'Simon Novak', initials: 'SN' };

  test('UX-DR58 Garden · Alerts · Devices · Members with Settings in the nav footer and aria-current', () => {
    const { body } = render(AppShell, { props: { user, currentPath: '/alerts', children } });
    const labels = ['Garden', 'Alerts', 'Devices', 'Members', 'Settings'];
    const positions = labels.map((label) => body.indexOf(`>${label}<`));
    for (const position of positions) {
      expect(position).toBeGreaterThan(-1);
    }
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
    expect(count(body, /aria-current="page"/u)).toBe(1);
    expect(body).toMatch(/<a[^>]*href="\/alerts"[^>]*aria-current="page"|<a[^>]*aria-current="page"[^>]*href="\/alerts"/u);
    expect(body).toMatch(/<nav[^>]*aria-label="Main"/u);
    expect(body).toContain('SN');
    expect(body).toContain('aria-label="Signed in as Simon Novak"');
  });

  test('UX-DR58 below 672 px the nav sits behind a header menu button', () => {
    const { body } = render(AppShell, { props: { user, currentPath: '/garden', children } });
    expect(body).toMatch(/<button[^>]*aria-expanded="false"[^>]*aria-controls="cf-side-nav"|<button[^>]*aria-controls="cf-side-nav"[^>]*aria-expanded="false"/u);
    expect(body).toContain('id="cf-side-nav"');
  });

  test('UX-DR58 Site tabs, Site menu and Alerts counts are not part of this story', () => {
    const { body } = render(AppShell, { props: { user, currentPath: '/garden', children } });
    expect(body).not.toMatch(/New Site|role="tablist"/u);
  });
});

describe('Sign-in card and live regions', () => {
  test('UX-DR59 the card holds the mark and a single SIGN IN button, nothing else', () => {
    const { body } = render(SignInCard, { props: { notice: null, returnTo: null } });
    expect(body).toMatch(/<h1[^>]*>Coldframe<\/h1>/u);
    expect(count(body, /<button/u)).toBe(1);
    expect(count(body, /<a /u)).toBe(0);
    expect(count(body, /<input(?![^>]*type="hidden")/u)).toBe(0);
    expect(body).toMatch(/<form[^>]*method="GET"[^>]*action="\/signin\/start"|<form[^>]*action="\/signin\/start"[^>]*method="GET"/iu);
    expect(body).toContain('cf-signin-surface');
  });

  test('UX-DR59 UX-DR92 notices sit inside the card with their one action', () => {
    const { body } = render(SignInCard, { props: { notice: 'unreachable', returnTo: '/alerts' } });
    const card = body.slice(body.indexOf('cf-signin-card'));
    expect(card).toContain('cf-inline-notice');
    expect(card).toContain('Try again');
    expect(body).toContain('href="/signin/start?returnTo=%2Falerts"');
    expect(body).toContain('name="returnTo"');
  });

  test('UX-DR92 the certificate notice offers no way to continue', () => {
    const { body } = render(SignInCard, { props: { notice: 'certificate', returnTo: null } });
    expect(body).toContain('certificate');
    expect(body).not.toContain('Try again');
    expect(count(body, /<a /u)).toBe(0);
  });

  test('UX-DR93 the signed-out notice offers Sign in', () => {
    const { body } = render(SignInCard, { props: { notice: 'signed-out', returnTo: null } });
    expect(body).toContain('signed out');
    expect(count(body, /<a /u)).toBe(1);
  });

  test('UX-DR104 polite and assertive regions exist, empty, before anything is announced', () => {
    const { body } = render(LiveRegions);
    expect(body).toMatch(/<div[^>]*role="status"[^>]*>\s*(<!--[^>]*-->)*\s*<\/div>/u);
    expect(body).toMatch(/<div[^>]*role="alert"[^>]*>\s*(<!--[^>]*-->)*\s*<\/div>/u);
  });

  test('icons are inline Carbon SVGs hidden from assistive technology', () => {
    const { body } = render(Icon, { props: { name: 'checkmark' } });
    expect(body).toContain('<svg');
    expect(body).toContain('aria-hidden="true"');
    expect(body).toContain('fill="currentColor"');
    expect(body).not.toContain('Generated by');
  });
});

describe('web platform structure', () => {
  test('UX-DR111 the DS-style Svelte components exist in the app', () => {
    for (const name of [
      'AppShell',
      'AppHeader',
      'SideNav',
      'Button',
      'TextInput',
      'SegmentedChoice',
      'ThemeSwitcher',
      'InlineNotice',
      'Modal',
      'RoleGate',
      'Icon',
      'LiveRegions',
      'SignInCard',
    ]) {
      expect(existsSync(join(webSrc, 'lib/components', `${name}.svelte`)), name).toBe(true);
    }
  });
});
