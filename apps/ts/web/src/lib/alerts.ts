/**
 * View models of the Alert rows (UX-DR25, UX-DR26). The Alerts, their order and the open count are
 * the Server's (AD-14); the only client logic is which of the four variants a row is, its copy and
 * where it leads. A row carries no value and no Threshold: the Alert has none.
 */
import type { Alert } from '@coldframe/api-client';
import { t, type MessageKey } from '$lib/i18n';
import { formatWhen } from '$lib/i18n/format';

export type { Alert } from '@coldframe/api-client';

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
  /** Lot detail for a Threshold or uncalibrated Alert, Devices for a silent Node, a low battery or an unknown kind. */
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
  const condition = conditionOf(alert);
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
 * The page's groups: open Threshold Alerts, open Health Alerts (every other kind), then Closed.
 * Each keeps the Server's order, which is newest first.
 */
export function alertGroups(alerts: readonly Alert[], context: AlertsContext): AlertGroups {
  const threshold: AlertRow[] = [];
  const health: AlertRow[] = [];
  const closed: AlertRow[] = [];
  for (const alert of alerts) {
    const row = alertRow(alert, context);
    (row.variant === 'closed' ? closed : row.variant === 'health' ? health : threshold).push(row);
  }
  return { threshold, health, closed };
}
