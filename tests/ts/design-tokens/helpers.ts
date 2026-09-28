import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { parse } from 'yaml';

import { packageRoot, repoRoot } from '../../../packages/design-tokens/scripts/lib/paths.ts';

export { packageRoot, repoRoot };

export const designMdPath = join(
  repoRoot,
  '_bmad-output/planning-artifacts/ux-designs/ux-coldframe-2026-09-27/DESIGN.md',
);

export interface DesignMdTypography {
  fontFamily: string;
  fontSize: string;
  fontWeight: string;
  lineHeight: string;
  letterSpacing?: string;
  note?: string;
}

export interface DesignMdFrontmatter {
  colors: Record<string, string>;
  typography: Record<string, DesignMdTypography>;
  spacing: Record<string, string>;
  rounded: Record<string, string>;
}

/** The YAML frontmatter of DESIGN.md, the spec the tokens are checked against. */
export function readDesignMd(): DesignMdFrontmatter {
  const text = readFileSync(designMdPath, 'utf8');
  const match = /^---\n([\s\S]*?)\n---\n/.exec(text);
  if (!match?.[1]) {
    throw new Error('DESIGN.md has no frontmatter');
  }
  return parse(match[1]) as DesignMdFrontmatter;
}

export function readRepoFile(relativePath: string): string {
  return readFileSync(join(repoRoot, relativePath), 'utf8');
}

/** `"36px"` → `36`. */
export function px(value: string): number {
  const match = /^(-?\d+(?:\.\d+)?)px$/.exec(value);
  if (!match?.[1]) {
    throw new Error(`not a px value: ${value}`);
  }
  return Number(match[1]);
}
