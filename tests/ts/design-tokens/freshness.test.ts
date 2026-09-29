import { cpSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { findStale, loadInputs, renderOutputs } from '../../../packages/design-tokens/scripts/lib/render.ts';
import { generatedDirs, outputPaths } from '../../../packages/design-tokens/scripts/lib/paths.ts';
import { repoRoot } from './helpers.ts';

const GENERATED_HEADER = /generated.*do not edit/i;

describe('generated outputs', () => {
  const inputs = loadInputs();
  const outputs = renderOutputs(inputs);

  it('are fresh: committed files equal a regeneration', () => {
    expect(findStale(repoRoot, outputs)).toEqual([]);
  });

  it.each([...outputs.keys()])('%s carries a "generated, do not edit" header', (path) => {
    const head = (outputs.get(path) ?? '').split('\n').slice(0, 3).join('\n');
    expect(head).toMatch(GENERATED_HEADER);
  });

  it('cover every platform', () => {
    const { iconsDir, ...files } = outputPaths;
    for (const path of Object.values(files)) {
      expect([...outputs.keys()]).toContain(path);
    }
    expect([...outputs.keys()].filter((path) => path.startsWith(`${iconsDir}/`))).toHaveLength(21);
  });

  it('are named stale when tokens.json changes without regenerating', () => {
    const changed = structuredClone(inputs);
    const background = changed.tokens.colors.background;
    if (!background) throw new Error('background missing');
    background.light = '#FFFFFF';
    const stale = findStale(repoRoot, renderOutputs(changed));
    expect(stale).toContain(outputPaths.tokensCss);
    expect(stale).toContain(outputPaths.tsIndex);
    expect(stale).toContain(outputPaths.swiftColors);
    expect(stale).toContain(outputPaths.kotlinColors);
  });

  it('are named stale when a generated file is edited', () => {
    const edited = new Map(outputs);
    edited.set(outputPaths.swiftSpacing, `${outputs.get(outputPaths.swiftSpacing) ?? ''}// edited\n`);
    expect(findStale(repoRoot, edited)).toEqual([outputPaths.swiftSpacing]);
  });

  it('are named stale when a generated folder holds a file the generator no longer writes', () => {
    const tmpRoot = mkdtempSync(join(tmpdir(), 'design-tokens-'));
    try {
      for (const dir of generatedDirs) {
        cpSync(join(repoRoot, dir), join(tmpRoot, dir), { recursive: true });
      }
      const stray = `${outputPaths.iconsDir}/old.svg`;
      writeFileSync(join(tmpRoot, stray), '<svg/>');
      expect(findStale(tmpRoot, outputs)).toEqual([stray]);
    } finally {
      rmSync(tmpRoot, { recursive: true, force: true });
    }
  });

  it('are rendered deterministically', () => {
    expect(renderOutputs(loadInputs())).toEqual(outputs);
  });
});
