/**
 * Web announcement regions (UX-DR104): `role="status"` (polite) and `role="alert"` (assertive)
 * exist empty in the root layout before anything is announced; this fills them.
 */
export const announcements = $state({ polite: '', assertive: '' });

const refillDelayMs = 100;

/** Announces a message; clearing first makes a repeated message speak again. */
export function announce(message: string, priority: 'polite' | 'assertive' = 'polite'): void {
  announcements[priority] = '';
  setTimeout(() => {
    announcements[priority] = message;
  }, refillDelayMs);
}
