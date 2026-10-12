/**
 * Web announcement regions (UX-DR104): `role="status"` (polite) and `role="alert"` (assertive)
 * exist empty in the root layout before anything is announced; this fills them.
 */
export const announcements = $state({ polite: '', assertive: '' });

const refillDelayMs = 100;

const pending: Record<'polite' | 'assertive', string[]> = { polite: [], assertive: [] };

/**
 * Announces a message; clearing first makes a repeated message speak again. Messages announced
 * before the region is filled are said together, in order, so one does not replace the other
 * ("Live again." and a Hub-silent notice of the same load).
 */
export function announce(message: string, priority: 'polite' | 'assertive' = 'polite'): void {
  announcements[priority] = '';
  const waiting = pending[priority];
  waiting.push(message);
  if (waiting.length > 1) {
    return;
  }
  setTimeout(() => {
    announcements[priority] = waiting.join(' ');
    waiting.length = 0;
  }, refillDelayMs);
}
