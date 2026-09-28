import { describe, expect, test } from 'vitest';
import { formatDuration, formatNumber, formatPercent, formatTime, formatWhen } from '$lib/i18n/format';

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

describe('locale formatting', () => {
  test('UX-DR127 durations: "min" under 1 h, "h" under 24 h, "d" from 24 h', () => {
    expect(formatDuration(0)).toBe('0 min');
    expect(formatDuration(12 * minute)).toBe('12 min');
    expect(formatDuration(59 * minute + 59_000)).toBe('59 min');
    expect(formatDuration(hour)).toBe('1 h');
    expect(formatDuration(6 * hour + 30 * minute)).toBe('6 h');
    expect(formatDuration(23 * hour + 59 * minute)).toBe('23 h');
    expect(formatDuration(day)).toBe('1 d');
    expect(formatDuration(3 * day + 5 * hour)).toBe('3 d');
  });

  test('UX-DR127 the stale header uses "h min"', () => {
    expect(formatDuration(2 * hour + 12 * minute, { withMinutes: true })).toBe('2 h 12 min');
    expect(formatDuration(2 * hour, { withMinutes: true })).toBe('2 h');
    expect(formatDuration(30 * minute, { withMinutes: true })).toBe('30 min');
  });

  test('UX-DR127 today shows the clock time in the locale 12/24 h format', () => {
    const now = new Date('2026-09-28T15:00:00Z');
    const reading = new Date('2026-09-28T07:02:00Z');
    expect(formatWhen(reading, now, 'en-GB', 'UTC')).toBe('07:02');
    expect(formatWhen(reading, now, 'en-US', 'UTC')).toBe('7:02 AM');
    expect(formatTime(reading, 'de-CH', 'Europe/Zurich')).toBe('09:02');
  });

  test('UX-DR127 earlier than today shows the weekday; older than 7 days the date', () => {
    const now = new Date('2026-09-28T08:00:00Z'); // Monday
    expect(formatWhen(new Date('2026-09-27T22:00:00Z'), now, 'en-GB', 'UTC')).toBe('Sun');
    expect(formatWhen(new Date('2026-09-22T10:00:00Z'), now, 'en-GB', 'UTC')).toBe('Tue');
    expect(formatWhen(new Date('2026-09-01T10:00:00Z'), now, 'en-GB', 'UTC')).toBe('1 Sept');
    expect(formatWhen(new Date('2026-09-01T10:00:00Z'), now, 'en-US', 'UTC')).toBe('Sep 1');
  });

  test('UX-DR127 a later calendar day than now (clock skew) shows the date, never a weekday', () => {
    const now = new Date('2026-09-28T08:00:00Z');
    expect(formatWhen(new Date('2026-09-29T08:00:00Z'), now, 'en-GB', 'UTC')).toBe('29 Sept');
    expect(formatWhen(new Date('2026-10-03T08:00:00Z'), now, 'en-GB', 'UTC')).toBe('3 Oct');
  });

  test('UX-DR127 the day boundary follows the Site time zone', () => {
    const now = new Date('2026-09-28T06:00:00Z'); // 08:00 in Zurich
    const reading = new Date('2026-09-27T22:30:00Z'); // 00:30 in Zurich, 22:30 the day before in UTC
    expect(formatWhen(reading, now, 'en-GB', 'Europe/Zurich')).toBe('00:30');
    expect(formatWhen(reading, now, 'en-GB', 'UTC')).toBe('Sun');
  });

  test('UX-DR127 numbers and percent spacing follow the locale', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,235');
    expect(formatNumber(1234.5, 'de-CH', 1)).toMatch(/^1[’']234\.5$/u);
    expect(formatPercent(20, 'en')).toBe('20%');
    expect(formatPercent(20, 'de')).toMatch(/^20\s%$/u);
  });
});
