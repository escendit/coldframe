import type { ReadModelChanged } from '@coldframe/api-client';

const overview = '/garden';

/** The Lot of Lot detail or one of its sub-pages, with whether the path is the detail itself. */
function openLot(pathname: string): { readonly lotId: string; readonly detail: boolean } | null {
  const parts = pathname.split('/').filter((part) => part !== '');
  const lotId = parts[1];
  if (parts[0] !== 'garden' || lotId === undefined) {
    return null;
  }
  return { lotId, detail: parts.length === 2 };
}

function isOverview(pathname: string): boolean {
  return pathname.replace(/\/+$/u, '') === overview;
}

/**
 * Whether the page at `pathname` shows what a `readmodel.changed` hint names (Story 6.6), so that
 * it reads again. A hint for anything else, or for another Lot, is dropped.
 *
 * | resource | pages |
 * | --- | --- |
 * | `sites` | every app page |
 * | `lots` | the overview; Lot detail and its sub-pages of that Lot |
 * | `readings` | the overview; Lot detail of that Lot |
 * | `alerts` | Alerts; the overview |
 * | `devices` | Devices; the overview (a Device that turns silent or recovers changes the Hub notice and the tiles) |
 */
export function showsResource(pathname: string, hint: Pick<ReadModelChanged, 'resource' | 'id'>): boolean {
  const lot = openLot(pathname);
  switch (hint.resource) {
    case 'sites':
      return true;
    case 'lots':
      return isOverview(pathname) || lot?.lotId === hint.id;
    case 'readings':
      return isOverview(pathname) || (lot !== null && lot.detail && lot.lotId === hint.id);
    case 'alerts':
      return isOverview(pathname) || pathname.replace(/\/+$/u, '') === '/alerts';
    case 'devices':
      return isOverview(pathname) || pathname.replace(/\/+$/u, '') === '/devices';
    default:
      return false;
  }
}
