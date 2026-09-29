/** Stands in for SvelteKit's `$app/forms` in SSR unit renders, where actions never run. */
export function enhance(): { destroy: () => void } {
  return { destroy: () => undefined };
}
