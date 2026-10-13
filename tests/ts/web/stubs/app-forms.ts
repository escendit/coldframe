/** Stands in for SvelteKit's `$app/forms` in SSR unit renders, where actions never run. */
export function enhance(): { destroy: () => void } {
  return { destroy: () => undefined };
}

/** No action answers in a server render, so there is no result to apply. */
export function applyAction(): Promise<void> {
  return Promise.resolve();
}
