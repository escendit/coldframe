/**
 * View models of Lot detail (UX-DR27 to UX-DR29, UX-DR32, UX-DR33, UX-DR63, UX-DR78, UX-DR98). The
 * status, `statusSince`, the latest Reading per Sensor with its unit, the Node's battery and the
 * daily history are the Server's (AD-14); nothing here converts a unit, computes a status or an
 * aggregate. The only client logic is formatting and durations from Server timestamps.
 */
import type { Lot, LotHistory, NodeStatus, SensorQuantity, SensorReading } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatDay, formatNumber, formatPercent, formatWhen } from '$lib/i18n/format';
import { formatSoil, lotTile, statusOf, type TileContext, type TileIcon, type TileVariant } from '$lib/lot-tiles';

export type { LotHistory, NodeStatus, SensorQuantity, SensorReading } from '@coldframe/api-client';

/** The quantities the Server serves, in the order of the picker. */
export const sensorQuantities = ['soil_moisture', 'air_temperature', 'relative_humidity', 'gas_resistance'] as const satisfies readonly SensorQuantity[];

/** Below this charge a Node's battery shows `battery--low` (UX-DR29). */
export const lowBatteryPercent = 20;

/** The days the History chart shows. */
export const historyDays = 30;

const quantityLabel: Readonly<Record<SensorQuantity, MessageKey>> = {
  soil_moisture: 'lotDetail.quantity.soil_moisture',
  air_temperature: 'lotDetail.quantity.air_temperature',
  relative_humidity: 'lotDetail.quantity.relative_humidity',
  gas_resistance: 'lotDetail.quantity.gas_resistance',
};

export function quantityName(quantity: SensorQuantity): string {
  return t(quantityLabel[quantity]);
}

/** The quantities of the Lot's Sensors that the picker offers; soil moisture alone without Readings. */
export function pickerQuantities(sensors: readonly SensorReading[] | undefined): readonly SensorQuantity[] {
  const present = sensorQuantities.filter((quantity) => sensors?.some((sensor) => sensor.quantity === quantity) === true);
  return present.length === 0 ? ['soil_moisture'] : present;
}

/**
 * A converted value as the Server sent it, formatted: soil `raw N`, temperature and humidity in whole
 * numbers, gas resistance to 3 significant digits (UX-DR28).
 */
export function formatValue(quantity: SensorQuantity, value: number, locale: string): string {
  switch (quantity) {
    case 'soil_moisture':
      return t('lotDetail.value.raw', { value: formatNumber(value, locale) });
    case 'air_temperature':
      return t('lotDetail.value.celsius', { value: formatNumber(value, locale) });
    case 'relative_humidity':
      return t('lotDetail.value.percent', { value: formatNumber(value, locale) });
    case 'gas_resistance':
      return t('lotDetail.value.kiloohm', { value: new Intl.NumberFormat(locale, { maximumSignificantDigits: 3 }).format(value) });
  }
}

export interface SensorCell {
  readonly quantity: SensorQuantity;
  readonly label: string;
  /** The latest Reading; the dash while stale: nothing live is shown then. */
  readonly value: string;
  /** When it was taken. */
  readonly time: string | null;
  /** The Sensor ID, which names the Sensor's Thresholds (Story 5.4). */
  readonly sensorId?: string;
}

/** The Sensor cells, in the Server's order. Stale mode keeps the label and drops the value (UX-DR19). */
export function sensorCells(sensors: readonly SensorReading[], context: TileContext): readonly SensorCell[] {
  return sensors.map((sensor) => ({
    quantity: sensor.quantity,
    label: quantityName(sensor.quantity),
    value: context.staleSince === null ? (sensor.quantity === 'soil_moisture' && sensor.unit === '%' ? t('lotDetail.value.percent', { value: formatNumber(sensor.value, context.locale) }) : formatValue(sensor.quantity, sensor.value, context.locale)) : '—',
    time: context.staleSince === null ? formatWhen(new Date(sensor.measuredAt), context.now, context.locale, context.timeZone) : null,
    ...(sensor.sensorId === undefined ? {} : { sensorId: sensor.sensorId }),
  }));
}

export interface DeviceCell {
  readonly id: 'battery' | 'lastSeen';
  readonly label: string;
  readonly value: string;
  readonly meta: string;
  /** `battery--low` below 20 %. */
  readonly icon: 'battery--low' | null;
}

/** The Device cells of the Node: battery with charging, and last seen (UX-DR29). Missing optionals read as a dash. */
export function deviceCells(node: NodeStatus, context: TileContext): readonly DeviceCell[] {
  const live = context.staleSince === null;
  const battery = node.batteryPercent;
  const charging = node.charging === undefined ? '' : t(node.charging === 'charging' ? 'devices.charging.charging' : 'devices.charging.notCharging');
  const lastSeen = node.lastSeenAt === undefined ? null : new Date(node.lastSeenAt);
  return [
    {
      id: 'battery',
      label: t('lotDetail.node', { id: node.deviceId }),
      value: live && battery !== undefined ? t('lotDetail.value.percent', { value: formatNumber(battery, context.locale) }) : '—',
      meta: live ? charging : '',
      icon: live && battery !== undefined && battery < lowBatteryPercent ? 'battery--low' : null,
    },
    {
      id: 'lastSeen',
      label: t('lotDetail.lastSeen'),
      value: live && lastSeen !== null ? formatWhen(lastSeen, context.now, context.locale, context.timeZone) : '—',
      meta: t('lotDetail.everyFifteen'),
      icon: null,
    },
  ];
}

export interface LotHero {
  readonly name: string;
  readonly variant: TileVariant;
  readonly icon: TileIcon;
  readonly label: string;
  /** "since 05:45"; none while stale. */
  readonly since: string | null;
  /** The big value: `raw 1840`, `~35`, a silence duration, or a dash while paused; none otherwise. */
  readonly value: string | null;
  /** The unit after a percentage. */
  readonly unit: string | null;
  /** "low 30 %", only when the Server sent a low Threshold. */
  readonly low: string | null;
  /** "±5 % · 07:02", only with a percentage. */
  readonly reading: string | null;
  /** Status-specific lines (UX-DR78). */
  readonly notes: readonly string[];
  /** Stale: "as of 07:02". */
  readonly asOf: string | null;
  readonly orange: boolean;
  readonly hatched: boolean;
}

export interface HeroContext extends TileContext {
  /** The caller may change the Site (Admin+): the paused-by-Site hint then says how to resume. */
  readonly admin: boolean;
  /** The Hub the Server names for a Hub-silent Lot, when one is known. */
  readonly hubId: string | null;
}

function notesOf(lot: Lot, context: HeroContext): readonly string[] {
  switch (statusOf(lot)) {
    case 'needsCalibration':
      return [t('lotDetail.noPercent')];
    case 'unknown':
      return [lot.unknownCause === 'hub' ? (context.hubId === null ? t('lotDetail.hubSilentNoId') : t('lotDetail.hubSilent', { hubId: context.hubId })) : t('lotDetail.nodeSilent')];
    case 'paused': {
      const notes: string[] = [];
      const until = lot.pausedUntil === undefined ? null : new Date(lot.pausedUntil);
      const bySite = lot.pausedBy?.includes('site') === true;
      if (until !== null && !Number.isNaN(until.getTime())) {
        notes.push(t('lotDetail.pausedUntil', { date: formatDay(until, context.locale, context.timeZone) }));
      } else if (!bySite) {
        notes.push(t('lotDetail.paused'));
      }
      if (bySite) {
        notes.push(t('lotDetail.pausedWithSite'));
        if (context.admin) {
          notes.push(t('lotDetail.resumeSite'));
        }
      }
      return notes;
    }
    case 'noNode':
      return [t('lotDetail.noNode')];
    default:
      return [];
  }
}

/**
 * The hero. Its value is the latest soil Reading as `raw N` while the Lot needs calibration,
 * otherwise the Server's `moisturePercent` when it sent one (UX-DR27).
 */
export function lotHero(lot: Lot, context: HeroContext): LotHero {
  const tile = lotTile(lot, context);
  const stale = tile.variant === 'stale';
  const status = statusOf(lot);
  const base = { name: lot.name, variant: tile.variant, icon: tile.icon, label: tile.label, orange: !stale && status === 'needsWater', hatched: !stale && tile.hatched };
  if (stale) {
    return { ...base, since: null, value: null, unit: null, low: null, reading: null, notes: [], asOf: tile.foot };
  }
  const since = Number.isNaN(Date.parse(lot.statusSince)) ? null : t('lotDetail.since', { time: formatWhen(new Date(lot.statusSince), context.now, context.locale, context.timeZone) });
  const soil = lot.sensors?.find((sensor) => sensor.quantity === 'soil_moisture');
  let value: string | null = null;
  let unit: string | null = null;
  if (status === 'needsCalibration') {
    value = soil === undefined ? t('lotTile.value.raw') : formatValue('soil_moisture', soil.value, context.locale);
  } else if (status === 'unknown' || status === 'paused') {
    value = tile.value;
  } else if (status !== 'noNode' && lot.moisturePercent !== undefined) {
    value = formatSoil(lot.moisturePercent, context.locale);
    unit = '%';
  }
  const reading = lot.lastReadingAt === undefined ? null : formatWhen(new Date(lot.lastReadingAt), context.now, context.locale, context.timeZone);
  const percent = unit === '%';
  return {
    ...base,
    since,
    value,
    unit,
    low: percent && lot.lowThresholdPercent !== undefined ? t('lotTile.foot.low', { percent: formatPercent(lot.lowThresholdPercent, context.locale) }) : null,
    reading: percent && reading !== null ? t('lotDetail.reading', { time: reading }) : null,
    notes: notesOf(lot, context),
    asOf: null,
  };
}

/** The one label a screen reader speaks for the hero (UX-DR98): the tile's spoken label. */
export function heroSpoken(lot: Lot, context: TileContext): string {
  return lotTile(lot, context).spoken;
}

export interface ChartBar {
  /** 0 to 29: the slot of the day, oldest first. */
  readonly slot: number;
  readonly day: string;
  readonly date: string;
  readonly low: number;
  readonly high: number;
  /** The bar's height as a fraction of the chart, 0 to 1. */
  readonly height: number;
  /** "5 Oct: lowest raw 1840", or "5 Oct: 12 °C to 18 °C". */
  readonly readout: string;
  /** The day's low is under the low Threshold: drawn solid in the below-low token (UX-DR32). */
  readonly belowLow: boolean;
}

/** The Thresholds the chart draws: the Server's low and optional high, in the History's percent. */
export interface ChartThreshold {
  readonly low: number | null;
  readonly high: number | null;
}

/** The band as fractions of the chart height, 0 (bottom) to 1 (top). */
export interface ChartBand {
  readonly low: number;
  readonly high: number | null;
}

export interface HistoryChart {
  readonly quantity: SensorQuantity;
  /** One bar per day with Readings; the days without are gaps, never zero. */
  readonly bars: readonly ChartBar[];
  /** The dates under the first and the last slot. */
  readonly axis: { readonly start: string; readonly end: string };
  /** The text alternative of the whole chart. */
  readonly summary: string;
  /** The bar shown in the readout before anything is picked: the newest day. */
  readonly latest: ChartBar | null;
  /** The Threshold band; only for a soil-moisture History in percent with a low Threshold. */
  readonly band: ChartBand | null;
  /** The low Threshold in percent when the band is drawn. */
  readonly lowPercent: number | null;
  /** "solid bar = below 30 %" when the band is drawn. */
  readonly legend: string | null;
}

function utcDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function dayDate(day: string, locale: string): string {
  return formatDay(new Date(`${day}T12:00:00.000Z`), locale, 'UTC');
}

/**
 * The 30 UTC days ending on the day of `now`, with a bar for each day the Server listed. The
 * Server's `low` is the bar; temperature, humidity and gas also give `high` in the readout.
 */
export function historyChart(history: LotHistory | undefined, quantity: SensorQuantity, now: Date, locale: string, threshold: ChartThreshold | null = null): HistoryChart {
  const end = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  const slots = Array.from({ length: historyDays }, (_unused, index) => utcDay(new Date(end - (historyDays - 1 - index) * 86_400_000)));
  const days = (history?.days ?? []).filter((day) => slots.includes(day.day));
  // The band is drawn only for a soil-moisture History the Server sent in percent (UX-DR5); a raw History has no band.
  const percent = quantity === 'soil_moisture' && history?.unit === '%';
  const lowPercent = percent && threshold?.low !== null && threshold?.low !== undefined ? threshold.low : null;
  const lows = days.map((day) => day.low);
  // A percent chart sits on the fixed 0-100 scale, so the band and the bars share one axis.
  const floor = percent ? 0 : Math.min(0, ...lows);
  const ceiling = percent ? 100 : Math.max(0, ...lows);
  const span = ceiling - floor;
  const bars = days.map((day): ChartBar => {
    const date = dayDate(day.day, locale);
    const belowLow = lowPercent !== null && day.low < lowPercent;
    const value = percent ? t('lotDetail.value.percent', { value: formatNumber(day.low, locale) }) : formatValue(quantity, day.low, locale);
    const readout = belowLow
      ? t('lotDetail.chart.readoutBelowLow', { date, value, percent: formatNumber(lowPercent, locale) })
      : quantity === 'soil_moisture'
        ? t('lotDetail.chart.readoutLow', { date, value })
        : t('lotDetail.chart.readoutRange', { date, low: formatValue(quantity, day.low, locale), high: formatValue(quantity, day.high, locale) });
    return { slot: slots.indexOf(day.day), day: day.day, date, low: day.low, high: day.high, height: span === 0 ? 1 : (day.low - floor) / span, readout, belowLow };
  });
  const lowest = bars.reduce<ChartBar | null>((best, bar) => (best === null || bar.low < best.low ? bar : best), null);
  const name = quantityName(quantity);
  const lowestText = lowest === null ? '' : percent ? t('lotDetail.value.percent', { value: formatNumber(lowest.low, locale) }) : formatValue(quantity, lowest.low, locale);
  let summary =
    lowest === null
      ? t('lotDetail.chart.summaryEmpty', { quantity: name })
      : t('lotDetail.chart.summary', { quantity: name, value: lowestText, date: lowest.date, count: bars.length });
  if (lowPercent !== null && lowest !== null) {
    const below = bars.filter((bar) => bar.belowLow).map((bar) => bar.date);
    const shown = formatNumber(lowPercent, locale);
    summary += ` ${below.length === 0 ? t('lotDetail.chart.summaryNeverBelow', { percent: shown }) : t('lotDetail.chart.summaryBelowLow', { percent: shown, days: below.join(', ') })}`;
  }
  return {
    quantity,
    bars,
    axis: { start: dayDate(slots[0] ?? '', locale), end: dayDate(slots[slots.length - 1] ?? '', locale) },
    summary,
    latest: bars[bars.length - 1] ?? null,
    band: lowPercent === null ? null : { low: lowPercent / 100, high: threshold?.high === null || threshold?.high === undefined ? null : threshold.high / 100 },
    lowPercent,
    legend: lowPercent === null ? null : t('lotDetail.chart.legend', { percent: formatNumber(lowPercent, locale) }),
  };
}
