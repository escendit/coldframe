/**
 * View models of the Alert rows (UX-DR25, UX-DR26). The Alerts, their order and the open count are
 * the Server's (AD-14); the only client logic is which of the four variants a row is, its copy and
 * where it leads. A row carries no value and no Threshold: the Alert has none.
 */
import type { Alert, components } from '@coldframe/api-client';
import { deviceRowId } from '$lib/devices';
import { t, type MessageKey } from '$lib/i18n';
import { formatNumber, formatSilence, formatWhen, shortDeviceId } from '$lib/i18n/format';

export type { Alert } from '@coldframe/api-client';

/** A Health Alert of `AlertList.healthAlerts` (Story 7.2): about a Device, with a Lot only when a Node was on one. */
export type HealthAlert = components['schemas']['HealthAlert'];

/** Set when the Server could not list the Alerts. No row is shown then. */
export type AlertsNotice = 'unreachable' | 'certificate';

export function alertsNoticeOf(notice: AlertsNotice | null): { readonly message: MessageKey; readonly tryAgain: boolean } | null {
  switch (notice) {
    case 'unreachable':
      return { message: 'alerts.unreachable', tryAgain: true };
    case 'certificate':
      return { message: 'notice.certificate', tryAgain: false };
    default:
      return null;
  }
}

/** How a row is drawn. Only `needsWater` is orange. */
export type AlertVariant = 'needsWater' | 'threshold' | 'health' | 'closed';

export type AlertIcon = 'rain-drop' | 'arrow--up' | 'arrow--down' | 'help' | 'battery--low' | 'tools';

export interface AlertsContext {
  /** The clock "today" is told against. */
  readonly now: Date;
  readonly locale: string;
  readonly timeZone: string;
}

export interface AlertRow {
  readonly id: string;
  readonly variant: AlertVariant;
  readonly icon: AlertIcon;
  /** Sentence case; uppercase comes from style. */
  readonly eyebrow: string;
  readonly title: string;
  /** The one label a screen reader speaks for the whole row: the condition and when it started. */
  readonly spoken: string;
  /** Lot detail for a Threshold or uncalibrated Alert; Devices, at the Device's row, for a silent Device, a low battery or an unknown kind. */
  readonly href: string;
  /** Hatched rows put their text on a solid plate. */
  readonly hatched: boolean;
}

export interface AlertGroups {
  readonly threshold: readonly AlertRow[];
  readonly health: readonly AlertRow[];
  readonly closed: readonly AlertRow[];
}

const quantityKeys: Readonly<Record<string, MessageKey>> = {
  soil_moisture: 'alerts.quantity.soil_moisture',
  air_temperature: 'alerts.quantity.air_temperature',
  relative_humidity: 'alerts.quantity.relative_humidity',
  gas_resistance: 'alerts.quantity.gas_resistance',
};

/** What the row says, whether the Alert is open or closed. */
interface Condition {
  readonly group: 'threshold' | 'health';
  /** Whether an open Alert of this condition is the needs-water row. */
  readonly needsWater: boolean;
  readonly icon: AlertIcon;
  readonly eyebrow: string;
  readonly title: string;
  readonly href: string;
}

function thresholdCondition(alert: Alert, side: 'low' | 'high'): Condition {
  const lotName = alert.lotName;
  const soil = alert.quantity === 'soil_moisture';
  const needsWater = soil && side === 'low';
  const quantityKey = quantityKeys[alert.quantity];
  const quantity = quantityKey === undefined ? alert.quantity : t(quantityKey);
  let title: string;
  if (soil) {
    title = t(side === 'low' ? 'alerts.title.needsWater' : 'alerts.title.tooWet', { lotName });
  } else {
    title = t(side === 'low' ? 'alerts.title.tooLow' : 'alerts.title.tooHigh', { lotName, quantity });
  }
  let eyebrow: MessageKey = side === 'low' ? 'alerts.eyebrow.belowLow' : 'alerts.eyebrow.aboveHigh';
  if (needsWater) {
    eyebrow = 'alerts.eyebrow.needsWater';
  }
  return {
    group: 'threshold',
    needsWater,
    icon: needsWater ? 'rain-drop' : side === 'low' ? 'arrow--down' : 'arrow--up',
    eyebrow: t(eyebrow),
    title,
    href: `/garden/${alert.lotId}`,
  };
}

function healthCondition(alert: Alert): Condition {
  const lotName = alert.lotName;
  const base = { group: 'health', needsWater: false, eyebrow: t('alerts.eyebrow.health') } as const;
  switch (alert.kind) {
    case 'silent':
      return { ...base, icon: 'help', title: t('alerts.title.silent', { lotName }), href: '/devices' };
    case 'battery':
      return { ...base, icon: 'battery--low', title: t('alerts.title.battery', { lotName }), href: '/devices' };
    case 'uncalibrated':
      return { ...base, icon: 'tools', title: t('alerts.title.uncalibrated', { lotName }), href: `/garden/${alert.lotId}` };
    default:
      // A kind this client does not know, or a Threshold Alert whose side or quantity it cannot tell: Devices, as on mobile.
      return { ...base, icon: 'help', title: t('alerts.title.other', { lotName }), href: '/devices' };
  }
}

/** Where a Health Alert about a Device leads: that Device's row on Devices (UX-DR26). */
export function deviceRowHref(deviceId: string): string {
  return `/devices#${deviceRowId(deviceId)}`;
}

/**
 * "Node on Lot 'Beans' silent for 6 h", "Hub 3F2A silent for 5 min" or "Node 9C0D silent for 6 h"
 * (Story 7.2). The duration is `now − silentSince`, rounded down, and only an open Alert has one: a
 * closed row, and an Alert opened before the Server recorded its silent-since, end at "silent".
 */
function silentTitle(alert: HealthAlert, context: AlertsContext): string {
  const duration = alert.closedAt === undefined ? formatSilence(alert.silentSince, context.now) : null;
  if (alert.deviceKind === 'hub') {
    const hubId = shortDeviceId(alert.deviceId);
    return duration === null ? t('alerts.title.silentHub', { hubId }) : t('alerts.title.silentHubFor', { hubId, duration });
  }
  if (alert.lotName !== undefined) {
    return duration === null ? t('alerts.title.silent', { lotName: alert.lotName }) : t('alerts.title.silentFor', { lotName: alert.lotName, duration });
  }
  const nodeId = shortDeviceId(alert.deviceId);
  return duration === null ? t('alerts.title.silentNode', { nodeId }) : t('alerts.title.silentNodeFor', { nodeId, duration });
}

/**
 * "Node battery 14 %, not charging — Lot 'Lettuce'" or, for a Node on no Lot, "Node 9C0D battery 14 %, not
 * charging" (Story 7.3). The level is the one the Alert opened with, so only an open Alert states it: a
 * closed row, and an Alert without a level the contract can describe, read "battery low". A `battery`
 * Alert opens only from a report that is not charging, so the kind is the charger status.
 */
function batteryTitle(alert: HealthAlert, context: AlertsContext): string {
  const level = alert.batteryPercent;
  const stated = alert.closedAt === undefined && level !== undefined && Number.isInteger(level) && level >= 0 && level <= 100;
  const percent = stated ? formatNumber(level, context.locale) : null;
  if (alert.lotName !== undefined) {
    return percent === null ? t('alerts.title.battery', { lotName: alert.lotName }) : t('alerts.title.batteryLevel', { percent, lotName: alert.lotName });
  }
  // Only a Node has a battery to report: anything else without a Lot is the neutral row, as on mobile.
  if (alert.deviceKind !== 'node') {
    return neutralTitle(alert);
  }
  const nodeId = shortDeviceId(alert.deviceId);
  return percent === null ? t('alerts.title.batteryNode', { nodeId }) : t('alerts.title.batteryNodeLevel', { nodeId, percent });
}

/** The neutral Health row: a kind this client does not know, or one it cannot word without a Lot. */
function neutralTitle(alert: HealthAlert): string {
  return alert.lotName === undefined ? t('alerts.title.otherNoLot') : t('alerts.title.other', { lotName: alert.lotName });
}

/** What a Health Alert of `AlertList.healthAlerts` says and where it leads. */
function deviceCondition(alert: HealthAlert, context: AlertsContext): Condition {
  const base = { group: 'health', needsWater: false, eyebrow: t('alerts.eyebrow.health') } as const;
  const devices = deviceRowHref(alert.deviceId);
  switch (alert.kind) {
    case 'silent':
      return { ...base, icon: 'help', title: silentTitle(alert, context), href: devices };
    case 'battery':
      return { ...base, icon: 'battery--low', title: batteryTitle(alert, context), href: devices };
    case 'uncalibrated':
      return {
        ...base,
        icon: 'tools',
        title: alert.lotName === undefined ? neutralTitle(alert) : t('alerts.title.uncalibrated', { lotName: alert.lotName }),
        href: alert.lotId === undefined ? devices : `/garden/${alert.lotId}`,
      };
    default:
      return { ...base, icon: 'help', title: neutralTitle(alert), href: devices };
  }
}

function conditionOf(alert: Alert): Condition {
  if (alert.kind === 'threshold' && (alert.side === 'low' || alert.side === 'high') && Object.hasOwn(quantityKeys, alert.quantity)) {
    return thresholdCondition(alert, alert.side);
  }
  return healthCondition(alert);
}

function when(value: string, context: AlertsContext): string {
  return formatWhen(new Date(value), context.now, context.locale, context.timeZone);
}

/** The row of one Alert: its variant, icon, eyebrow, title, spoken label and where it leads. */
export function alertRow(alert: Alert, context: AlertsContext): AlertRow {
  return rowOf(alert, conditionOf(alert), context);
}

/** The row of one Health Alert (Story 7.2): hatched while open, the Closed outline afterwards. */
export function healthAlertRow(alert: HealthAlert, context: AlertsContext): AlertRow {
  return rowOf(alert, deviceCondition(alert, context), context);
}

function rowOf(alert: Pick<Alert, 'id' | 'openedAt' | 'closedAt'>, condition: Condition, context: AlertsContext): AlertRow {
  const since = t('alerts.since', { time: when(alert.openedAt, context) });
  const base = { id: alert.id, icon: condition.icon, title: condition.title, href: condition.href };

  if (alert.closedAt !== undefined) {
    const closed = t('alerts.eyebrow.closed', { time: when(alert.closedAt, context) });
    return {
      ...base,
      variant: 'closed',
      eyebrow: [condition.eyebrow, closed].join(t('list.dot')),
      spoken: [condition.title, since, closed].join(t('list.comma')),
      hatched: false,
    };
  }

  const health = condition.group === 'health';
  return {
    ...base,
    variant: health ? 'health' : condition.needsWater ? 'needsWater' : 'threshold',
    // A Threshold row tells when it started; a Health row only names itself.
    eyebrow: health ? condition.eyebrow : [condition.eyebrow, when(alert.openedAt, context)].join(t('list.dot')),
    spoken: [condition.title, since].join(t('list.comma')),
    hatched: health,
  };
}

/**
 * The page's groups: open Threshold Alerts, open Health Alerts, then Closed. `alerts` is the Server's
 * Threshold list and `healthAlerts` its Health list (Story 7.2); each keeps the Server's order, which is
 * newest first, and neither is re-sorted: Closed holds the closed Threshold Alerts, then the closed Health
 * Alerts. An entry of `alerts` that is no Threshold Alert still renders as a Health row, as before. A
 * missing Health list is no Health Alerts.
 */
export function alertGroups(alerts: readonly Alert[], context: AlertsContext, healthAlerts: readonly HealthAlert[] | undefined = []): AlertGroups {
  const threshold: AlertRow[] = [];
  const health: AlertRow[] = [];
  const closed: AlertRow[] = [];
  const seen = new Set<string>();
  const place = (row: AlertRow): void => {
    // One row per Alert, where it first came.
    if (!seen.has(row.id)) {
      seen.add(row.id);
      (row.variant === 'closed' ? closed : row.variant === 'health' ? health : threshold).push(row);
    }
  };
  for (const alert of alerts) {
    place(alertRow(alert, context));
  }
  for (const alert of healthAlerts) {
    place(healthAlertRow(alert, context));
  }
  return { threshold, health, closed };
}
