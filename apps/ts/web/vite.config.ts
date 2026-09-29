import { fileURLToPath } from 'node:url';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

// fonts.css references the Ubuntu fonts relative to the design-tokens package, outside this app.
const designTokens = fileURLToPath(new URL('../../../packages/design-tokens', import.meta.url));

export default defineConfig({
  plugins: [sveltekit()],
  server: {
    fs: { allow: [designTokens] },
  },
  build: {
    rolldownOptions: {
      // @escendit/sveltekit-session's RedisSessionStore imports Bun's client lazily; the app
      // uses the in-memory store, so that import is never reached under Node.
      external: ['bun'],
    },
  },
});
