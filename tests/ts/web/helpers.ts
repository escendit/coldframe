import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repoRoot = fileURLToPath(new URL('../../../', import.meta.url));
export const webRoot = join(repoRoot, 'apps/ts/web');
export const webSrc = join(webRoot, 'src');
export const unitTestsRoot = fileURLToPath(new URL('.', import.meta.url));
export const e2eTestsRoot = join(repoRoot, 'tests/ts/web.e2e');

/** Every file under `dir` whose name ends with one of `extensions`, skipping node_modules. */
export function filesUnder(dir: string, extensions: readonly string[]): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name.startsWith('.')) {
      continue;
    }
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...filesUnder(path, extensions));
    } else if (extensions.some((extension) => entry.name.endsWith(extension))) {
      found.push(path);
    }
  }
  return found.sort();
}

export function read(path: string): string {
  return readFileSync(path, 'utf8');
}

export function rel(path: string): string {
  return relative(repoRoot, path);
}
