import { render } from 'svelte/server';
import { describe, expect, test } from 'vitest';
import GardenPage from '../../../apps/ts/web/src/routes/(app)/garden/+page.svelte';
import FirstRunSteps from '$lib/components/FirstRunSteps.svelte';
import SiteMenu from '$lib/components/SiteMenu.svelte';
import SiteSummaryHeader from '$lib/components/SiteSummaryHeader.svelte';
import SiteTabs from '$lib/components/SiteTabs.svelte';
import TimeZonePanel from '$lib/components/TimeZonePanel.svelte';
import { firstRunSteps } from '$lib/first-run';
import { siteMenuItems } from '$lib/site-menu';
import type { Site } from '$lib/sites';

const home: Site = { id: 'a', name: 'Home', role: 'Owner' };
const allotment: Site = { id: 'b', name: 'Allotment', role: 'Member' };

function text(html: string): string {
  return html
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<[^>]*>/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
}

function gardenPage(site: Site | null): string {
  const data = { user: { displayName: 'Simon', initials: 'S' }, theme: 'system', sites: site === null ? [] : [site], currentSite: site, sitesNotice: null, sitesStale: null, lots: [], lotsNotice: null, staleSince: null, loadedAt: '2026-10-06T07:17:00.000Z', timeZone: 'UTC' };
  return render(GardenPage, { props: { data, params: {} } as never }).body;
}

describe('Site summary header', () => {
  test('UX-DR21 the Site name, the headline exposed as a heading, and the subline', () => {
    const { body } = render(SiteSummaryHeader, { props: { siteName: 'Home', headline: 'No Readings yet', subline: "Nothing is measuring, so there's no status to show." } });
    expect(body).toContain('>Home<');
    expect(body).toMatch(/<h2[^>]*>No Readings yet<\/h2>/u);
    expect(text(body)).toContain("Nothing is measuring, so there's no status to show.");
  });
});

describe('Empty Garden', () => {
  test('UX-DR62 UX-DR82 UX-DR21 the empty Site shows its header, No Readings yet and never looks fine', () => {
    const body = gardenPage(home);
    expect(body).toMatch(/<h1[^>]*>Garden<\/h1>/u);
    expect(body).toMatch(/<h2[^>]*>No Readings yet<\/h2>/u);
    expect(text(body)).toContain("Nothing is measuring, so there's no status to show.");
    expect(body).toContain('>Home<');
    expect(text(body)).not.toMatch(/\bOK\b|all good|fine/iu);
  });

  test('UX-DR54 four step tiles, Add a Hub next and solid, the others later and dashed, with no actions on the web', () => {
    const body = gardenPage(home);
    const tiles = [...body.matchAll(/<li[^>]*data-step="([^"]+)"[^>]*data-state="([^"]+)"/gu)].map((match) => [match[1], match[2]]);
    expect(tiles).toEqual([
      ['addHub', 'next'],
      ['addNode', 'later'],
      ['calibrate', 'later'],
      ['setThreshold', 'later'],
    ]);
    expect(text(body)).toMatch(/Step 1 Add a Hub Next Step 2 Add a Node Later Step 3 Calibrate Later Step 4 Set a low Threshold Later/u);
    const steps = body.slice(body.indexOf('cf-first-run'), body.indexOf('</ol>'));
    expect(steps).not.toMatch(/<button|<a /u);
  });

  test('UX-DR54 the web shows the mobile-app notice to Owners and Administrators', () => {
    expect(text(gardenPage(home))).toContain('Adding a Hub or Node needs the Coldframe mobile app.');
    expect(text(gardenPage({ ...home, role: 'Administrator' }))).toContain('Adding a Hub or Node needs the Coldframe mobile app.');
  });

  test('UX-DR54 Members see the tiles without actions and the read-only notice', () => {
    const body = gardenPage(allotment);
    expect(text(body)).toContain('Only Owners and Administrators can add Devices.');
    expect(body).toContain('data-step="addHub"');
  });

  test('UX-DR54 the step tiles are styled from the tokens: next solid primary with ink-on-bright, later 1 px dashed border-strong', () => {
    const { body } = render(FirstRunSteps, { props: { tiles: firstRunSteps('Owner').tiles, actionable: false } });
    expect(body).toContain('cf-first-run__tile--next');
    expect(body).toContain('cf-first-run__tile--later');
  });

  test('UX-DR54 an actionable next step is a button (mobile only; never passed on the web)', () => {
    const { body } = render(FirstRunSteps, { props: { tiles: firstRunSteps('Owner', true).tiles, actionable: true } });
    expect(body.match(/<button/gu)).toHaveLength(1);
  });
});

describe('Site switcher tabs', () => {
  test('UX-DR23 one tab per Site in the Server order, Role from the Server, New Site last', () => {
    const { body } = render(SiteTabs, { props: { sites: [allotment, home], currentSite: home, currentPath: '/alerts' } });
    const tabs = [...body.matchAll(/<a[^>]*role="tab"[^>]*>([\s\S]*?)<\/a>/gu)].map((match) => text(match[1] ?? ''));
    expect(tabs).toEqual(['Allotment · Member', 'Home · Owner', 'New Site']);
    expect(body).toContain('href="/alerts?site=b"');
    expect(body.match(/aria-selected="true"/gu)).toHaveLength(1);
    expect(body).toMatch(/cf-site-tabs__tab--current[^>]*aria-selected="true"[^>]*href="\/alerts\?site=a"/u);
  });

  test('UX-DR23 from Create Site, picking a Site opens its Garden and New Site is the selected tab', () => {
    const { body } = render(SiteTabs, { props: { sites: [home], currentSite: home, currentPath: '/sites/new' } });
    expect(body).toContain('href="/garden?site=a"');
    expect(body).toMatch(/aria-selected="true"[^>]*href="\/sites\/new"/u);
  });
});

describe('Site menu', () => {
  test('UX-DR22 an overflow trigger naming the Site; closed until opened', () => {
    const { body } = render(SiteMenu, { props: { siteName: 'Home', items: siteMenuItems('Owner') } });
    expect(body).toMatch(/<button[^>]*aria-label="Site menu for Home"/u);
    expect(body).toContain('aria-haspopup="menu"');
    expect(body).toContain('aria-expanded="false"');
    expect(body).toContain('data-icon="overflow-menu--vertical"');
    expect(body).not.toContain('role="menu"');
  });

  test('UX-DR22 UX-DR74 the rendered menu holds only Site settings, which opens Site settings; Pause is hidden until it exists', () => {
    for (const role of ['Owner', 'Administrator', 'Member'] as const) {
      expect(siteMenuItems(role).map((item) => [item.label, item.href])).toEqual([['siteMenu.settings', '/settings/site']]);
    }
  });
});

describe('Time-zone confirm panel', () => {
  const zones = ['Europe/London', 'Europe/Zurich', 'America/New_York'];

  test('UX-DR61 proposes the detected zone with Confirm and Change, and sends nothing until confirmed', () => {
    const { body } = render(TimeZonePanel, { props: { detected: 'Europe/Zurich', chosen: null, zones } });
    expect(text(body)).toContain('Is your time zone Europe/Zurich?');
    expect(body).toMatch(/>Confirm</u);
    expect(body).toMatch(/>Change</u);
    expect(body).toMatch(/<input type="hidden" name="timeZone" value=""/u);
    expect(body).toContain('cf-time-zone');
  });

  test('UX-DR61 a zone this browser chose wins over detection and is never overwritten', () => {
    const { body } = render(TimeZonePanel, { props: { detected: 'America/New_York', chosen: 'Europe/Zurich', zones } });
    expect(text(body)).toContain('Your time zone is Europe/Zurich.');
    expect(text(body)).not.toContain('America/New_York');
    expect(body).not.toMatch(/>Confirm</u);
    expect(body).toMatch(/<input type="hidden" name="timeZone" value="Europe\/Zurich"/u);
  });

  test('UX-DR61 before the browser answered, nothing is proposed; without a zone, Change is offered', () => {
    expect(text(render(TimeZonePanel, { props: { detected: undefined, chosen: null, zones } }).body)).not.toMatch(/Is your time zone/u);
    const none = render(TimeZonePanel, { props: { detected: null, chosen: null, zones } }).body;
    expect(text(none)).toContain('Your time zone could not be detected.');
    expect(none).toMatch(/>Change</u);
  });
});
