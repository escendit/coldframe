/**
 * Whether this browser can reach the web app, as the Site overview found on its last look
 * (UX-DR79). `unreachableSince` is the time of the last successful load while it cannot, and null
 * otherwise. Only the overview writes it, and only in the browser; the shell reads it to disable
 * the Site menu as in the Server-side stale mode.
 */
export const webApp = $state<{ unreachableSince: string | null }>({ unreachableSince: null });
