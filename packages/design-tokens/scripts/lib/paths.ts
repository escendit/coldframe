import { join, resolve } from 'node:path';

/** `packages/design-tokens`. */
export const packageRoot = resolve(import.meta.dirname, '../..');

/** The repository root. */
export const repoRoot = resolve(packageRoot, '../..');

/** Inputs, relative to the repository root. */
export const inputPaths = {
  tokens: 'packages/design-tokens/tokens/tokens.json',
  contrast: 'packages/design-tokens/tokens/contrast.json',
  theme: 'packages/design-tokens/vendor/escendit-branding/theme.css',
  iconsDir: 'packages/design-tokens/vendor/carbon-icons/svg',
} as const;

const swiftDir = 'packages/swift/design-tokens/Sources/ColdframeDesignTokens/Generated';
const kotlinDir = 'packages/kt/design-tokens/generated/kotlin/com/escendit/coldframe/designtokens';

/** Every generated file, relative to the repository root (icons are listed per icon under `iconsDir`). */
export const outputPaths = {
  tokensCss: 'packages/design-tokens/generated/css/tokens.css',
  fontsCss: 'packages/design-tokens/generated/css/fonts.css',
  tsIndex: 'packages/design-tokens/generated/ts/index.ts',
  iconsDir: 'packages/design-tokens/generated/icons',
  swiftColors: `${swiftDir}/ColorTokens.swift`,
  swiftTypography: `${swiftDir}/Typography.swift`,
  swiftSpacing: `${swiftDir}/Spacing.swift`,
  swiftRadius: `${swiftDir}/Radius.swift`,
  swiftIcons: `${swiftDir}/CarbonIcon.swift`,
  kotlinColors: `${kotlinDir}/ColorTokens.kt`,
  kotlinTypography: `${kotlinDir}/Typography.kt`,
  kotlinSpacing: `${kotlinDir}/Spacing.kt`,
  kotlinRadius: `${kotlinDir}/Radius.kt`,
  kotlinIcons: `${kotlinDir}/CarbonIcon.kt`,
} as const;

/** Folders that hold nothing but generated files; a file there that the generator does not write is stale. */
export const generatedDirs = [
  'packages/design-tokens/generated/css',
  'packages/design-tokens/generated/ts',
  outputPaths.iconsDir,
  swiftDir,
  kotlinDir,
] as const;

export function fromRoot(relativePath: string, root = repoRoot): string {
  return join(root, relativePath);
}
