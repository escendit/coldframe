/**
 * The Site overview's headline and counts (UX-DR129), and its stale-mode announcements (UX-DR106).
 * Statuses are the Server's (AD-14): this only counts them.
 */
import type { Lot } from '@coldframe/api-client';
import { t } from '$lib/i18n';
import { formatDay } from '$lib/i18n/format';
import { statusOf, type TileStatus } from '$lib/lot-tiles';

export interface SiteHeadline {
  /** The one-sentence Site state, exposed as a heading. */
  readonly headline: string;
  /** The counts, or the detail line of "No Readings yet"; null when there is nothing to count. */
  readonly subline: string | null;
  /** The whole Site is paused: the headline takes the paused ink. */
  readonly paused: boolean;
}

function count(lots: readonly Lot[]): Readonly<Record<TileStatus, number>> {
  const counts: Record<TileStatus, number> = { needsWater: 0, needsCalibration: 0, unknown: 0, ok: 0, paused: 0, noNode: 0 };
  for (const lot of lots) {
    counts[statusOf(lot)]++;
  }
  return counts;
}

/** Non-zero counts in the Server's order, without needs water (the headline says that). */
function countsLine(counts: Readonly<Record<TileStatus, number>>): string | null {
  const parts = [
    counts.needsCalibration > 0 ? t('garden.count.needsCalibration', { count: counts.needsCalibration }) : null,
    counts.unknown > 0 ? t('garden.count.unknown', { count: counts.unknown }) : null,
    counts.ok > 0 ? t('garden.count.ok', { count: counts.ok }) : null,
    counts.paused > 0 ? t('garden.count.paused', { count: counts.paused }) : null,
    counts.noNode > 0 ? t('garden.count.noNode', { count: counts.noNode }) : null,
  ].filter((part) => part !== null);
  return parts.length === 0 ? null : parts.join(t('list.dot'));
}

/** The headline and counts of a Site, by the first rule that matches. */
export function siteHeadline(lots: readonly Lot[], locale: string, timeZone: string): SiteHeadline {
  const counts = count(lots);
  const withNode = lots.filter((lot) => statusOf(lot) !== 'noNode');
  if (withNode.length === 0) {
    return { headline: t('garden.noReadings'), subline: t('garden.noReadingsDetail'), paused: false };
  }
  const subline = countsLine(counts);
  if (counts.needsWater > 0) {
    const first = lots.find((lot) => statusOf(lot) === 'needsWater');
    const headline = counts.needsWater === 1 && first !== undefined ? t('garden.headline.lotNeedsWater', { lotName: first.name }) : t('count.lotsNeedWater', { count: counts.needsWater });
    return { headline, subline, paused: false };
  }
  const unread = counts.unknown + counts.needsCalibration;
  if (unread > 0) {
    return { headline: t('garden.headline.cantRead', { count: unread }), subline, paused: false };
  }
  if (withNode.every((lot) => statusOf(lot) === 'paused' && lot.pausedBy?.includes('site') === true)) {
    const ends = new Set(withNode.map((lot) => lot.pausedUntil ?? ''));
    const [end = ''] = ends;
    const until = ends.size === 1 && end !== '' ? new Date(end) : null;
    const headline = until === null || Number.isNaN(until.getTime()) ? t('garden.headline.paused') : t('garden.headline.pausedUntil', { date: formatDay(until, locale, timeZone) });
    return { headline, subline, paused: true };
  }
  return { headline: t('garden.headline.nothing'), subline, paused: false };
}

export type OverviewMode = 'live' | 'stale';

/**
 * What to announce when the overview goes from `previous` to `current`: entering stale mode and
 * leaving it, once each. A refetch that changes nothing announces nothing, and neither does the
 * first live view (`previous` is null before this browser has shown the overview).
 */
export function staleTransition(previous: OverviewMode | null, current: OverviewMode): 'entered' | 'left' | null {
  if (previous === current) {
    return null;
  }
  if (current === 'stale') {
    return 'entered';
  }
  return previous === 'stale' ? 'left' : null;
}

/** A small static file of the web app itself: asking for it reads nothing from the Server. */
export const webAppProbePath = '/_app/version.json';

/**
 * What a look at the tab does (UX-DR112). The browser first asks the web app itself, once more
 * after a network error. Any answer means it can be reached: the page loads again. Two network
 * errors mean this browser cannot reach the web app (a laptop away from home): loading again
 * would replace the overview with an error page, so the shown Lots stay and go stale instead.
 */
export async function onFocusDecision(probe: () => Promise<unknown>): Promise<'reload' | 'unreachable'> {
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      await probe();
      return 'reload';
    } catch {
      // A network error; an HTTP answer of any status resolves.
    }
  }
  return 'unreachable';
}

/**
 * "As of" when the web app cannot be reached: the time of the last successful load. Data that
 * was already stale keeps the time it is from.
 */
export function lastGoodLoad(data: { readonly staleSince: string | null; readonly loadedAt: string }): string {
  return data.staleSince ?? data.loadedAt;
}
