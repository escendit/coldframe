import { t } from './index';

const minuteMs = 60_000;
const hourMs = 60 * minuteMs;
const dayMs = 24 * hourMs;

/**
 * A duration by the Voice rules (UX-DR127): "min" under 1 h, "h" under 24 h, "d" from 24 h.
 * `withMinutes` gives "2 h 12 min" for the stale header.
 */
export function formatDuration(milliseconds: number, options: { withMinutes?: boolean } = {}): string {
  const ms = Math.max(0, milliseconds);
  if (ms < hourMs) {
    return t('duration.minutes', { count: Math.floor(ms / minuteMs) });
  }
  if (ms < dayMs) {
    const hours = Math.floor(ms / hourMs);
    const minutes = Math.floor((ms % hourMs) / minuteMs);
    if (options.withMinutes === true && minutes > 0) {
      return t('duration.hoursMinutes', { hours, minutes });
    }
    return t('duration.hours', { count: hours });
  }
  return t('duration.days', { count: Math.floor(ms / dayMs) });
}

/** A duration in words for screen readers: "12 minutes", "6 hours", "2 days". */
export function formatDurationSpoken(milliseconds: number): string {
  const ms = Math.max(0, milliseconds);
  if (ms < hourMs) {
    return t('duration.spoken.minutes', { count: Math.floor(ms / minuteMs) });
  }
  if (ms < dayMs) {
    return t('duration.spoken.hours', { count: Math.floor(ms / hourMs) });
  }
  return t('duration.spoken.days', { count: Math.floor(ms / dayMs) });
}

function calendarDay(date: Date, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
}

function daysBetween(earlier: string, later: string): number {
  return Math.round((Date.parse(later) - Date.parse(earlier)) / dayMs);
}

/**
 * A point in time by the Voice rules: today → clock time (12/24 h per locale); earlier than
 * today → weekday; older than 7 days → date.
 */
export function formatWhen(date: Date, now: Date, locale: string, timeZone: string): string {
  const day = calendarDay(date, timeZone);
  const today = calendarDay(now, timeZone);
  if (day === today) {
    return formatTime(date, locale, timeZone);
  }
  const days = daysBetween(day, today);
  if (days >= 1 && days <= 7) {
    return new Intl.DateTimeFormat(locale, { timeZone, weekday: 'short' }).format(date);
  }
  return new Intl.DateTimeFormat(locale, { timeZone, day: 'numeric', month: 'short' }).format(date);
}

/** Clock time in the locale's 12 or 24 hour format. */
export function formatTime(date: Date, locale: string, timeZone: string): string {
  return new Intl.DateTimeFormat(locale, { timeZone, timeStyle: 'short' }).format(date);
}

/** A number in the locale's format. */
export function formatNumber(value: number, locale: string, maximumFractionDigits = 0): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits }).format(value);
}

/** A percentage (0–100) with the locale's spacing, e.g. "20%" in en, "20 %" in de. */
export function formatPercent(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(value / 100);
}

/** A calendar day without its year: "1 Nov", or "1 November" with the month in full. */
export function formatDay(date: Date, locale: string, timeZone: string, month: 'short' | 'long' = 'short'): string {
  return new Intl.DateTimeFormat(locale, { timeZone, day: 'numeric', month }).format(date);
}
