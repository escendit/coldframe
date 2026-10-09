import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { appUrl, unreachableUrl, untrustedUrl } from '../fixtures/ports.ts';
import { resetSites, setMode, shellPages, signIn, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

/** Visits every page of this story (Sign in with each notice, and the shell) and runs `check`. */
async function everyPage(page: Page, theme: 'light' | 'dark', check: (label: string) => Promise<void>): Promise<void> {
  for (const origin of [appUrl, unreachableUrl, untrustedUrl]) {
    await useTheme(page, theme, origin);
  }
  for (const [label, url] of [
    ['sign in', `${appUrl}/signin`],
    ['sign in, unreachable', `${appUrl}/signin?notice=unreachable`],
    ['sign in, certificate', `${appUrl}/signin?notice=certificate`],
    ['sign in, keycloak', `${appUrl}/signin?notice=keycloak`],
    ['sign in, signed out', `${appUrl}/signin?notice=signed-out`],
  ] as const) {
    await page.goto(url);
    await check(`${label} (${theme})`);
  }
  await signIn(page);
  for (const path of shellPages) {
    await page.goto(path);
    if (path === '/sites/new') {
      // The time-zone panel asks the browser once the page is interactive.
      await expect(page.getByText(/^Is your time zone /u)).toBeVisible();
    }
    await check(`${path} (${theme})`);
  }
}

/** Every element the keyboard can reach, in DOM order. */
function focusablesInDomOrder(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const selector = 'a[href], button:not([disabled]), input:not([type="hidden"]):not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])';
    return [...document.querySelectorAll<HTMLElement>(selector)]
      .filter((element) => element.offsetParent !== null && element.closest('dialog:not([open])') === null)
      .map((element) => element.outerHTML.slice(0, 120));
  });
}

interface Stop {
  readonly html: string;
  readonly outline: string;
  readonly shadow: string;
}

/** A native time field is one element with a Tab stop per part (hours, minutes, AM/PM). */
const stopsPerElement = 4;

/**
 * Presses Tab from a freshly loaded page until `count` elements were reached, and records each one and
 * its ring. Tab stops that stay inside the same element (the parts of a native time field) count once.
 */
async function tabThrough(page: Page, count: number): Promise<Stop[]> {
  const stops: Stop[] = [];
  for (let presses = 0; stops.length < count && presses < count * stopsPerElement; presses++) {
    await page.keyboard.press('Tab');
    const stop = await page.evaluate(() => {
      const element = document.activeElement as HTMLElement;
      const style = getComputedStyle(element);
      return {
        html: element.outerHTML.slice(0, 120),
        outline: style.outlineStyle === 'none' ? '' : `${style.outlineWidth} ${style.outlineColor}`,
        shadow: style.boxShadow === 'none' ? '' : style.boxShadow,
        time: element instanceof HTMLInputElement && element.type === 'time',
      };
    });
    if (!(stop.time && stops.at(-1)?.html === stop.html)) {
      stops.push({ html: stop.html, outline: stop.outline, shadow: stop.shadow });
    }
  }
  return stops;
}

test.beforeEach(async () => {
  await setMode('normal');
  await resetSites();
});

test.describe('Accessibility', () => {
  for (const theme of themes) {
    test(`UX-DR16 UX-DR102 UX-DR111 axe finds no serious or critical violations on every page (${theme})`, async ({ page }) => {
      await everyPage(page, theme, async (label) => {
        const results = await new AxeBuilder({ page }).analyze();
        const blocking = results.violations
          .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
          .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`);
        expect(blocking, label).toEqual([]);
      });
    });
  }

  for (const theme of themes) {
    test(`UX-DR102 UX-DR16 tab order follows reading order and every focus ring is visible and not orange (${theme})`, async ({ page }) => {
      await everyPage(page, theme, async (label) => {
        const expected = await focusablesInDomOrder(page);
        expect(expected.length, label).toBeGreaterThan(0);
        const stops = await tabThrough(page, expected.length);
        expect(
          stops.map((stop) => stop.html),
          label,
        ).toEqual(expected);
        for (const stop of stops) {
          expect(stop.outline !== '' || stop.shadow !== '', `${label}: no focus ring on ${stop.html}`).toBe(true);
          expect(`${stop.outline} ${stop.shadow}`, `${label}: orange focus ring on ${stop.html}`).not.toContain('rgb(255, 119, 15)');
        }
      });
    });
  }

  test('UX-DR102 UX-DR113 UX-DR76 Esc closes the Modal and focus returns to its opener; one modal at a time', async ({ page }) => {
    await signIn(page);
    await page.goto('/settings');
    const opener = page.getByRole('button', { name: 'Sign out' });
    await opener.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeVisible();
    await expect(dialog.getByRole('link', { name: 'Sign out' })).toBeVisible();
    await expect(page.locator('dialog[open]')).toHaveCount(1);
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();

    await opener.click();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
    await expect(page).toHaveURL(/\/settings$/u);
  });

  test('UX-DR100 every interactive element is at least 44 × 44 px', async ({ page }) => {
    await everyPage(page, 'light', async (label) => {
      const small = await page.evaluate(() =>
        [...document.querySelectorAll<HTMLElement>('a[href], button, input:not([type="hidden"])')]
          .filter((element) => element.offsetParent !== null)
          .map((element) => ({ html: element.outerHTML.slice(0, 80), box: element.getBoundingClientRect() }))
          .filter(({ box }) => box.width < 44 || box.height < 44)
          .map(({ html, box }) => `${html} ${String(box.width)}×${String(box.height)}`),
      );
      expect(small, label).toEqual([]);
    });
  });

  test('UX-DR101 UX-DR114 nothing transitions, animates or spins; no toasts', async ({ page }) => {
    await everyPage(page, 'dark', async (label) => {
      const moving = await page.evaluate(() =>
        [...document.querySelectorAll('*')]
          .map((element) => ({ element, style: getComputedStyle(element) }))
          .filter(({ style }) => style.animationName !== 'none' || style.transitionDuration.split(',').some((value) => parseFloat(value) > 0))
          .map(({ element }) => element.tagName),
      );
      expect(moving, label).toEqual([]);
      await expect(page.locator('[role="progressbar"], .toast, [class*="spinner"]'), label).toHaveCount(0);
    });
  });

  test('UX-DR126 40 % longer text grows buttons and segments instead of clipping them', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 800 });
    const check = async (label: string) => {
      const clipped = await page.evaluate(() => {
        const problems: string[] = [];
        for (const labelElement of document.querySelectorAll<HTMLElement>('.cf-button__label, .cf-segmented__label')) {
          const text = labelElement.textContent;
          labelElement.textContent = `${text} ${'longer '.repeat(Math.ceil((text.length * 0.4) / 7))}`.trim();
        }
        for (const control of document.querySelectorAll<HTMLElement>('.cf-button, .cf-segmented__segment')) {
          if (control.offsetParent === null) {
            continue;
          }
          const box = control.getBoundingClientRect();
          const inner = control.querySelector<HTMLElement>('.cf-button__label, .cf-segmented__label')?.getBoundingClientRect();
          const overflow = control.scrollWidth > control.clientWidth + 1 || control.scrollHeight > control.clientHeight + 1;
          const outside = inner !== undefined && (inner.right > box.right + 1 || inner.bottom > box.bottom + 1 || inner.left < box.left - 1);
          const offscreen = box.right > document.documentElement.clientWidth + 1;
          if (overflow || outside || offscreen) {
            problems.push(control.outerHTML.slice(0, 80));
          }
        }
        return problems;
      });
      expect(clipped, label).toEqual([]);
    };
    await page.goto('/signin?notice=keycloak');
    await check('sign in');
    await signIn(page);
    await page.goto('/settings/appearance');
    await check('appearance');
    await page.goto('/settings');
    await page.getByRole('button', { name: 'Sign out' }).click();
    await check('settings with modal');
  });

  test('UX-DR58 below 672 px the side nav sits behind the header menu button', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await signIn(page);
    const nav = page.getByRole('navigation', { name: 'Main' });
    const menu = page.getByRole('button', { name: 'Menu', exact: true });
    await expect(nav).toBeHidden();
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await menu.click();
    await expect(menu).toHaveAttribute('aria-expanded', 'true');
    await expect(nav).toBeVisible();
    await nav.getByRole('link', { name: 'Devices', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
    await expect(nav).toBeHidden();

    await page.setViewportSize({ width: 1280, height: 800 });
    await expect(menu).toBeHidden();
    await expect(nav).toBeVisible();
  });

  test('UX-DR104 the polite and assertive regions exist, empty, on every page before anything is announced', async ({ page }) => {
    await page.goto('/signin');
    await expect(signInButton(page)).toBeVisible();
    await expect(page.getByRole('status')).toHaveText('');
    await expect(page.getByRole('alert')).toHaveText('');
    await everyPage(page, 'light', async (label) => {
      await expect(page.getByRole('status'), label).toHaveCount(1);
      await expect(page.getByRole('alert'), label).toHaveCount(1);
    });
  });
});
