import js from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import svelte from 'eslint-plugin-svelte';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default defineConfig(
  globalIgnores([
    '**/node_modules/',
    '**/dist/',
    '**/build/',
    '**/bin/',
    '**/obj/',
    '**/target/',
    '**/.svelte-kit/',
    '**/test-results/',
    '**/playwright-report/',
  ]),
  js.configs.recommended,
  tseslint.configs.strictTypeChecked,
  tseslint.configs.stylisticTypeChecked,
  svelte.configs.recommended,
  {
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
        extraFileExtensions: ['.svelte'],
      },
    },
  },
  {
    files: ['**/*.svelte', '**/*.svelte.ts'],
    languageOptions: {
      globals: { ...globals.browser },
      parserOptions: {
        parser: tseslint.parser,
      },
    },
    rules: {
      // The web app has no base path, and its components stay free of $app imports so they
      // render in unit tests; links are plain same-origin paths.
      'svelte/no-navigation-without-resolve': 'off',
    },
  },
  {
    files: ['**/*.{js,cjs,mjs}'],
    extends: [tseslint.configs.disableTypeChecked],
  },
  {
    files: ['**/*.cjs'],
    languageOptions: { sourceType: 'commonjs' },
  },
);
