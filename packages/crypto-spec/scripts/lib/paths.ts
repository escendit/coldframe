import { join, resolve } from 'node:path';

/** `packages/crypto-spec`. */
export const packageRoot = resolve(import.meta.dirname, '../..');

/** The repository root. */
export const repoRoot = resolve(packageRoot, '../..');

/** The normative spec, relative to the repository root. */
export const specPath = 'packages/crypto-spec/crypto-spec.json';

/** Every generated file, relative to the repository root. */
export const outputPaths = {
  vectors: 'packages/crypto-spec/vectors.json',
  rust: 'packages/rs/crypto/src/spec.rs',
  csharp: 'packages/cs/crypto/Generated/CryptoSpec.g.cs',
  kotlin: 'packages/kt/core/generated/kotlin/com/escendit/coldframe/core/crypto/CryptoSpec.kt',
} as const;

export function fromRoot(relativePath: string, root = repoRoot): string {
  return join(root, relativePath);
}
