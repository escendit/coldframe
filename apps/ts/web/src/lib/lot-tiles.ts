/**
 * View models of the Lot tiles (UX-DR18, UX-DR19, UX-DR77). The status, its order, `statusSince`,
 * `unknownCause` and `pausedBy` are the Server's (AD-14); nothing here computes or re-sorts a
 * status. The only client logic is formatting, and durations from Server timestamps and the clock.
 */
import type { Lot } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatDay, formatDuration, formatDurationSpoken, formatNumber, formatPercent, formatWhen } from '$lib/i18n/format';

/** The six statuses the Server computes, in its order. */
export const tileStatuses = ['needsWater', 'needsCalibration', 'unknown', 'ok', 'paused', 'noNode'] as const;

export type TileStatus = (typeof tileStatuses)[number];

/** How a tile is drawn: by its status, or as stale whatever the status was. */
export type TileVariant = TileStatus | 'stale';

export type TileIcon = 'rain-drop' | 'checkmark--outline' | 'help' | 'tools' | 'pause--outline' | 'add' | 'cloud--offline';

export interface TileContext {
  /** The clock durations and "today" are told against. */
  readonly now: Date;
  readonly locale: string;
  readonly timeZone: string;
  /** Stale mode: when the Lots were last read from the Server; null while live. */
  readonly staleSince: Date | null;
}

export interface LotTile {
  readonly id: string;
  readonly name: string;
  /** The Server's status; a status this client does not know is `unknown`. */
  readonly status: TileStatus;
  readonly variant: TileVariant;
  readonly icon: TileIcon;
  /** Sentence case; uppercase comes from style. */
  readonly label: string;
  /** The big value; none on a stale tile or when the Server sent no percentage. */
  readonly value: string | null;
  readonly foot: string | null;
  /** The one label a screen reader speaks for the whole tile. */
  readonly spoken: string;
  /** Height of the soil level in percent of the tile, or null without one. */
  readonly level: number | null;
  /** Height of the low Threshold tick in percent of the tile, or null without one. */
  readonly low: number | null;
  /** Hatched tiles put their text on a solid plate. */
  readonly hatched: boolean;
}

/** The Lot's status as the Server sent it; a string this client does not know renders as `unknown`. */
export function statusOf(lot: Pick<Lot, 'status'>): TileStatus {
  return tileStatuses.find((status) => status === lot.status) ?? 'unknown';
}

/** Soil moisture rounded to the nearest 5 %, within 0 to 100 (UX-DR128). */
export function soilApprox(percent: number): number {
  return Math.min(100, Math.max(0, Math.round(percent / 5) * 5));
}

/** Approximate soil moisture: "~20", or "~20%" with the locale's percent sign. */
export function formatSoil(percent: number, locale: string, withSign = false): string {
  const rounded = soilApprox(percent);
  return t('soil.approx', { value: withSign ? formatPercent(rounded, locale) : formatNumber(rounded, locale) });
}

function instant(value: string | undefined): Date | null {
  if (value === undefined) {
    return null;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function clamp(percent: number | undefined): number | null {
  return percent === undefined ? null : Math.min(100, Math.max(0, percent));
}

function join(separator: 'list.comma' | 'list.dot', parts: readonly (string | null)[]): string | null {
  const present = parts.filter((part) => part !== null);
  return present.length === 0 ? null : present.join(t(separator));
}

type LabelVariant = 'needsWater' | 'ok' | 'unknownNode' | 'unknownHub' | 'needsCalibration' | 'paused' | 'pausedBySite' | 'noNode';

const liveLabel: Readonly<Record<LabelVariant, MessageKey>> = {
  needsWater: 'lotTile.label.needsWater',
  ok: 'lotTile.label.ok',
  unknownNode: 'lotTile.label.unknownNode',
  unknownHub: 'lotTile.label.unknownHub',
  needsCalibration: 'lotTile.label.needsCalibration',
  paused: 'lotTile.label.paused',
  pausedBySite: 'lotTile.label.pausedBySite',
  noNode: 'lotTile.noNode',
};

const wasLabel: Readonly<Record<LabelVariant, MessageKey>> = {
  needsWater: 'lotTile.was.needsWater',
  ok: 'lotTile.was.ok',
  unknownNode: 'lotTile.was.unknownNode',
  unknownHub: 'lotTile.was.unknownHub',
  needsCalibration: 'lotTile.was.needsCalibration',
  paused: 'lotTile.was.paused',
  pausedBySite: 'lotTile.was.pausedBySite',
  noNode: 'lotTile.was.noNode',
};

const spokenWas: Readonly<Record<TileStatus, MessageKey>> = {
  needsWater: 'lotTile.spokenWas.needsWater',
  ok: 'lotTile.spokenWas.ok',
  unknown: 'lotTile.spokenWas.unknown',
  needsCalibration: 'lotTile.spokenWas.needsCalibration',
  paused: 'lotTile.spokenWas.paused',
  noNode: 'lotTile.spokenWas.noNode',
};

const icons: Readonly<Record<TileStatus, TileIcon>> = {
  needsWater: 'rain-drop',
  ok: 'checkmark--outline',
  unknown: 'help',
  needsCalibration: 'tools',
  paused: 'pause--outline',
  noNode: 'add',
};

/** Which label a Lot carries. The variants come from Server fields only: `unknownCause` and `pausedBy`. */
function labelVariant(lot: Lot, status: TileStatus): LabelVariant {
  if (status === 'unknown') {
    return lot.unknownCause === 'hub' ? 'unknownHub' : 'unknownNode';
  }
  if (status === 'paused') {
    return lot.pausedBy?.includes('site') === true ? 'pausedBySite' : 'paused';
  }
  return status;
}

interface Content {
  readonly value: string | null;
  readonly foot: string | null;
  /** The parts of the spoken label after the Lot name. */
  readonly spoken: readonly (string | null)[];
}

function measured(lot: Lot, status: 'needsWater' | 'ok', context: TileContext): Content {
  const { now, locale, timeZone } = context;
  const reading = instant(lot.lastReadingAt);
  const time = reading === null ? null : formatWhen(reading, now, locale, timeZone);
  const percent = lot.moisturePercent;
  const low = lot.lowThresholdPercent;
  return {
    value: percent === undefined ? null : formatSoil(percent, locale),
    foot: join('list.dot', [time, low === undefined ? null : t('lotTile.foot.low', { percent: formatPercent(low, locale) })]),
    spoken: [
      t(status === 'needsWater' ? 'lotTile.spoken.needsWater' : 'lotTile.spoken.ok'),
      percent === undefined ? null : t('lotTile.spoken.percent', { percent: soilApprox(percent) }),
      low === undefined ? null : t('lotTile.spoken.low', { percent: Math.round(low) }),
      // An OK Lot is not read with its Reading time; a Lot that needs water is.
      status === 'needsWater' && time !== null ? t('lotTile.spoken.reading', { time }) : null,
    ],
  };
}

function unknown(lot: Lot, context: TileContext): Content {
  const { now, locale, timeZone } = context;
  const reading = instant(lot.lastReadingAt);
  const byHub = lot.unknownCause === 'hub';
  // A silent Node is silent since its last Reading; a silent Hub, or a Node that never reported, since the status changed.
  const since = (byHub ? null : reading) ?? instant(lot.statusSince) ?? now;
  const silence = now.getTime() - since.getTime();
  const time = reading === null ? null : formatWhen(reading, now, locale, timeZone);
  const percent = lot.moisturePercent;
  let foot: string;
  let last: string;
  if (time === null) {
    foot = t('lotTile.foot.noReadings');
    last = t('lotTile.spoken.noReadings');
  } else if (percent === undefined) {
    foot = t('lotTile.foot.lastReading', { time });
    last = t('lotTile.spoken.lastReading', { time });
  } else {
    foot = t('lotTile.foot.was', { percent: formatSoil(percent, locale, true), time });
    last = t('lotTile.spoken.lastPercent', { percent: soilApprox(percent), time });
  }
  return {
    value: formatDuration(silence),
    foot,
    spoken: [t('lotTile.spoken.unknown'), t(byHub ? 'lotTile.spoken.hubSilent' : 'lotTile.spoken.nodeSilent', { duration: formatDurationSpoken(silence) }), last],
  };
}

function paused(lot: Lot, context: TileContext): Content {
  const { locale, timeZone } = context;
  const until = instant(lot.pausedUntil);
  const bySite = lot.pausedBy?.includes('site') === true;
  let spoken: string;
  if (until === null) {
    spoken = t(bySite ? 'lotTile.spoken.pausedSite' : 'lotTile.spoken.paused');
  } else {
    // The end wins over the source, as on Android and iOS.
    spoken = t('lotTile.spoken.pausedUntil', { date: formatDay(until, locale, timeZone, 'long') });
  }
  return {
    value: '—',
    foot: until === null ? t('lotTile.foot.paused') : t('lotTile.foot.until', { date: formatDay(until, locale, timeZone) }),
    spoken: [spoken],
  };
}

function contentOf(lot: Lot, status: TileStatus, context: TileContext): Content {
  switch (status) {
    case 'needsWater':
    case 'ok':
      return measured(lot, status, context);
    case 'unknown':
      return unknown(lot, context);
    case 'needsCalibration':
      // No percentage before a Calibration, whatever the Server sent.
      return { value: t('lotTile.value.raw'), foot: t('lotTile.foot.uncalibrated'), spoken: [t('lotTile.spoken.needsCalibration'), t('lotTile.spoken.uncalibrated')] };
    case 'paused':
      return paused(lot, context);
    case 'noNode':
      return { value: '+', foot: t('lotTile.addNode'), spoken: [t('lotTile.spoken.noNode'), t('lotTile.addNode')] };
  }
}

/** The tile of one Lot: its variant's shape, icon, label, value, foot and spoken label. */
export function lotTile(lot: Lot, context: TileContext): LotTile {
  const status = statusOf(lot);
  const label = labelVariant(lot, status);
  const base = { id: lot.id, name: lot.name, status };

  if (context.staleSince !== null) {
    // Stale: nothing is drawn or said as live; only what the status was, and as of when.
    const asOf = t('lotTile.asOf', { time: formatWhen(context.staleSince, context.now, context.locale, context.timeZone) });
    return {
      ...base,
      variant: 'stale',
      icon: 'cloud--offline',
      label: t(wasLabel[label]),
      value: null,
      foot: asOf,
      spoken: [lot.name, t(spokenWas[status]), t('lotTile.spoken.notLive'), asOf].join(t('list.comma')),
      level: null,
      low: null,
      hatched: false,
    };
  }

  const content = contentOf(lot, status, context);
  const fills = status === 'needsWater' || status === 'ok';
  return {
    ...base,
    variant: status,
    icon: icons[status],
    label: t(liveLabel[label]),
    value: content.value,
    foot: content.foot,
    spoken: status === 'noNode' ? t('lotTile.noNodeLabel', { lotName: lot.name }) : (join('list.comma', [lot.name, ...content.spoken]) ?? lot.name),
    level: fills ? clamp(lot.moisturePercent) : null,
    low: fills ? clamp(lot.lowThresholdPercent) : null,
    hatched: status === 'unknown' || status === 'needsCalibration',
  };
}
