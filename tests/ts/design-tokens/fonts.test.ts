import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import { outputPaths } from '../../../packages/design-tokens/scripts/lib/paths.ts';
import { packageRoot, readRepoFile, repoRoot } from './helpers.ts';

const fontsCss = readRepoFile(outputPaths.fontsCss);
const faces = [...fontsCss.matchAll(/@font-face\s*\{([^}]*)\}/g)].map((match) => match[1] ?? '');

describe('fonts', () => {
  it('are the four Ubuntu files, vendored with the Ubuntu Font Licence and their source', () => {
    for (const file of [
      'Ubuntu-Light.ttf',
      'Ubuntu-Regular.ttf',
      'UbuntuCondensed-Regular.ttf',
      'UbuntuMono-Regular.ttf',
      'UFL.txt',
      'SOURCE.md',
    ]) {
      expect(existsSync(join(packageRoot, 'fonts', file)), file).toBe(true);
    }
    expect(readFileSync(join(packageRoot, 'fonts/SOURCE.md'), 'utf8')).toContain('google/fonts');
  });

  it('have one @font-face each, weights 300 and 400 only', () => {
    expect(faces).toHaveLength(4);
    const declared = faces.map((face) => {
      const family = /font-family:\s*'([^']+)'/.exec(face)?.[1];
      const weight = /font-weight:\s*(\d+)/.exec(face)?.[1];
      return `${String(family)} ${String(weight)}`;
    });
    expect(declared.sort()).toEqual(['Ubuntu 300', 'Ubuntu 400', 'Ubuntu Condensed 400', 'Ubuntu Mono 400']);
  });

  it.each(faces)('point at a vendored file by relative URL', (face) => {
    const url = /url\('([^']+)'\)/.exec(face)?.[1] ?? '';
    expect(url).toMatch(/^\.\.\/\.\.\/fonts\/[\w-]+\.ttf$/);
    expect(existsSync(resolve(repoRoot, dirname(outputPaths.fontsCss), url))).toBe(true);
  });

  it('are never fetched from the network', () => {
    for (const path of [outputPaths.fontsCss, outputPaths.tokensCss]) {
      const css = readRepoFile(path);
      expect(css).not.toMatch(/https?:|fonts\.googleapis|@import/);
    }
  });
});
