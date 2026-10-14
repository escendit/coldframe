import { expect, test, type Page } from '@playwright/test';
import { appUrl } from '../fixtures/ports.ts';
import { axeClean, largestText, nothingClipped, resetSites, serverInvitations, setMembers, setMode, signInButton, useTheme } from './helpers.ts';

const themes = ['light', 'dark'] as const;

const homeId = '0192a000-0000-7000-8000-00000000000a';
const home = { id: homeId, name: 'Home', role: 'Owner' } as const;
const simon = { siteId: homeId, userId: 'b5f1c0de-0000-4000-8000-000000000001', name: 'Simon Novak', email: 'simon@example.com', role: 'Owner' } as const;
const ana = { siteId: homeId, userId: 'a0000000-0000-4000-8000-000000000002', name: 'Ana Lima', email: 'ana@example.com', role: 'Member' } as const;

const copy = {
  member: "Member sees everything and gets the same Alerts, but can't change settings.",
  administrator: 'Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.',
  owner: 'Owner can do everything, including inviting people and changing Roles.',
  sent: (email: string) => `Invitation sent to ${email}. They accept from the email, on the garden's Wi-Fi.`,
  alreadyInvited: 'That person is already invited or a member of this Site.',
  notSent: 'The invitation was not sent. Try again.',
  hint: 'Accepted an invitation? It can take a minute to appear.',
} as const;

test.beforeEach(async () => {
  await setMode('normal');
});

test.afterEach(async () => {
  // Other specs expect the default Site, no Members seeded and no invitations.
  await resetSites();
});

/** Signs in and waits for where the shell lands: Garden, or Create Site without a Site. */
async function signInTo(page: Page, lands = /\/garden$/u): Promise<void> {
  await page.goto('/');
  await signInButton(page).click();
  await expect(page).toHaveURL(lands);
}

function memberTiles(page: Page) {
  return page.getByRole('list', { name: 'Members of Home' }).getByRole('listitem');
}

test.describe('Members and the Invite form', () => {
  for (const theme of themes) {
    test(`UX-DR73 UX-DR51 UX-DR52 an Owner sees every Membership and invites Bo as Administrator; the list stays as it was (${theme}, largest text)`, async ({ page }) => {
      await resetSites([home]);
      await setMembers([simon, ana]);
      await useTheme(page, theme, appUrl);
      await page.emulateMedia({ colorScheme: theme });
      await signInTo(page);

      await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Members' }).click();
      await expect(page).toHaveURL(/\/members$/u);
      await largestText(page);

      await expect(memberTiles(page)).toHaveText([/SN\s*Simon Novak\s*You\s*Owner/u, /AL\s*Ana Lima\s*Member/u]);

      const email = page.getByRole('textbox', { name: 'Email' });
      await expect(email).toHaveAttribute('type', 'email');
      const roles = page.getByRole('group', { name: 'Role' });
      await expect(roles.getByRole('button', { name: 'Member' })).toHaveAttribute('aria-pressed', 'true');
      await expect(page.getByText(copy.member)).toBeVisible();
      await roles.getByRole('button', { name: 'Owner' }).click();
      await expect(page.getByText(copy.owner)).toBeVisible();
      await roles.getByRole('button', { name: 'Administrator' }).click();
      await expect(roles.getByRole('button', { name: 'Administrator' })).toHaveAttribute('aria-pressed', 'true');
      await expect(page.getByText(copy.administrator)).toBeVisible();

      await email.fill('bo@example.com');
      await page.getByRole('button', { name: 'Send invite' }).click();
      await expect(page.getByText(copy.sent('bo@example.com'))).toBeVisible();
      await expect(email).toHaveValue('');
      await expect(roles.getByRole('button', { name: 'Member' })).toHaveAttribute('aria-pressed', 'true');
      await expect(memberTiles(page)).toHaveCount(2);

      expect((await serverInvitations()).map(({ body, status }) => ({ body, status }))).toEqual([{ body: { email: 'bo@example.com', role: 'Administrator' }, status: 202 }]);

      await axeClean(page, `members (${theme})`);
      await nothingClipped(page, `members (${theme})`);
      // Submitting leaves the page scrolled; the full-page capture starts at the top.
      await page.evaluate(() => {
        window.scrollTo(0, 0);
      });
      await expect(page).toHaveScreenshot(`members-${theme}.png`, { fullPage: true });
    });
  }

  test('UX-DR52 an invalid email is one line under the field and nothing is sent', async ({ page }) => {
    await resetSites([home]);
    await signInTo(page);
    await page.goto('/members');
    await page.getByRole('textbox', { name: 'Email' }).fill('bo');
    await page.getByRole('button', { name: 'Send invite' }).click();
    await expect(page.getByText('Enter one email address, like ana@example.com.')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Email' })).toHaveAttribute('aria-invalid', 'true');
    expect(await serverInvitations()).toEqual([]);
  });

  test('UX-DR52 someone already invited: the notice says so and the form keeps the address and Role', async ({ page }) => {
    await resetSites([home]);
    await setMembers([simon, ana]);
    await signInTo(page);
    await page.goto('/members');
    const email = page.getByRole('textbox', { name: 'Email' });
    await email.fill('ana@example.com');
    await page.getByRole('group', { name: 'Role' }).getByRole('button', { name: 'Owner' }).click();
    await page.getByRole('button', { name: 'Send invite' }).click();
    await expect(page.getByText(copy.alreadyInvited)).toBeVisible();
    await expect(email).toHaveValue('ana@example.com');
    await expect(page.getByRole('group', { name: 'Role' }).getByRole('button', { name: 'Owner' })).toHaveAttribute('aria-pressed', 'true');
  });

  test('UX-DR52 Keycloak down: the invitation was not sent, and Try again sends the same one', async ({ page }) => {
    await resetSites([home]);
    await setMembers([], 503);
    await signInTo(page);
    await page.goto('/members');
    await page.getByRole('textbox', { name: 'Email' }).fill('bo@example.com');
    await page.getByRole('button', { name: 'Send invite' }).click();
    await expect(page.getByText(copy.notSent)).toBeVisible();
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByText(copy.sent('bo@example.com'))).toBeVisible();
    expect((await serverInvitations()).map(({ status }) => status)).toEqual([503, 202]);
  });
});

test.describe('Members is for Owners only', () => {
  for (const role of ['Administrator', 'Member'] as const) {
    test(`UX-DR76 as ${role}: no Members nav item, no Settings row, and the page goes to Garden`, async ({ page }) => {
      await resetSites([{ ...home, role }]);
      await signInTo(page);
      await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Members' })).toHaveCount(0);
      await page.goto('/settings');
      await expect(page.locator('a[href="/members"]')).toHaveCount(0);
      await page.goto('/members');
      await expect(page).toHaveURL(/\/garden$/u);
      await expect(page.getByRole('button', { name: 'Send invite' })).toHaveCount(0);
    });
  }
});

test.describe('Invitation accepted, Membership not arrived yet', () => {
  test('UX-DR90 with no Site Create Site shows the hint, reads the Memberships again, and opens the Site once it arrives', async ({ page }) => {
    test.setTimeout(45_000);
    await resetSites([]);
    await signInTo(page, /\/sites\/new$/u);
    await expect(page.getByText(copy.hint)).toBeVisible();

    // The invitee accepted on the Phase Two page; the Membership reaches the Server a little later.
    await resetSites([{ id: homeId, name: 'Home', role: 'Member' }]);
    await expect(page).toHaveURL(/\/garden$/u, { timeout: 15_000 });
    await expect(page.getByRole('tab', { name: 'Home · Member' })).toBeVisible();
  });

  test('UX-DR90 from New Site with a Site already there is no hint', async ({ page }) => {
    await resetSites([home]);
    await signInTo(page);
    await page.goto('/sites/new');
    await expect(page.getByRole('heading', { name: 'Create Site' })).toBeVisible();
    await expect(page.getByText(copy.hint)).toHaveCount(0);
  });
});
