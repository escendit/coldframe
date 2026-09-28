import { fileURLToPath } from 'node:url';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

const lib = fileURLToPath(new URL('../../../apps/ts/web/src/lib', import.meta.url));

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    alias: { $lib: lib },
  },
  test: {
    environment: 'node',
    include: ['**/*.test.ts'],
  },
});
