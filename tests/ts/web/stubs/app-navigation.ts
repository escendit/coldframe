/** Stands in for SvelteKit's `$app/navigation` in SSR unit renders, where nothing navigates. */
export function invalidateAll(): Promise<void> {
  return Promise.resolve();
}
