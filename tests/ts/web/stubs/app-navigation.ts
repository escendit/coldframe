/** Stands in for SvelteKit's `$app/navigation` in SSR unit renders, where nothing navigates. */
export function invalidateAll(): Promise<void> {
  return Promise.resolve();
}

/** Nothing navigates in a server render, so the callback never runs. */
export function afterNavigate(): void {
  // Inert on the server.
}

export function goto(): Promise<void> {
  return Promise.resolve();
}
