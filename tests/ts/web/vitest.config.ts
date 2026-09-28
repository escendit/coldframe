import { fileURLToPath } from 'node:url';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

const lib = fileURLToPath(new URL('../../../apps/ts/web/src/lib', import.meta.url));
// SSR renders of route pages need SvelteKit's `$app/forms`; `enhance` is inert on the server.
const appForms = fileURLToPath(new URL('./stubs/app-forms.ts', import.meta.url));

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    alias: { $lib: lib, '$app/forms': appForms },
  },
  test: {
    environment: 'node',
    include: ['**/*.test.ts'],
  },
});
