import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { outputPaths, repoRoot } from '../../../packages/crypto-spec/scripts/lib/paths.ts';
import { findStale, loadInputs, renderOutputs } from '../../../packages/crypto-spec/scripts/lib/render.ts';

const GENERATED_HEADER = /generated.*do not edit/i;

describe('generated crypto-spec outputs', () => {
  const inputs = loadInputs();
  const outputs = renderOutputs(inputs);

  it('are fresh: committed files equal a regeneration', () => {
    expect(findStale(repoRoot, outputs)).toEqual([]);
  });

  it.each([...outputs.keys()])('%s carries a "generated, do not edit" header', (path) => {
    const head = (outputs.get(path) ?? '').split('\n').slice(0, 3).join('\n');
    expect(head).toMatch(GENERATED_HEADER);
  });

  it('cover Rust, C#, Kotlin and the vectors', () => {
    expect([...outputs.keys()].sort()).toEqual(Object.values(outputPaths).sort());
  });

  it('are named stale when crypto-spec.json changes without regenerating', () => {
    const changed = structuredClone(inputs);
    changed.spec.keyHierarchy.purposeKeys.labels.seal = 'seal/v2';
    const stale = findStale(repoRoot, renderOutputs(changed));
    expect(stale).toEqual(expect.arrayContaining([outputPaths.rust, outputPaths.csharp, outputPaths.kotlin, outputPaths.vectors]));
  });

  it('are named stale when a generated file is edited or missing', () => {
    const root = mkdtempSync(join(tmpdir(), 'crypto-spec-'));
    try {
      for (const [path, content] of outputs) {
        mkdirSync(dirname(join(root, path)), { recursive: true });
        writeFileSync(join(root, path), content);
      }
      expect(findStale(root, outputs)).toEqual([]);
      writeFileSync(join(root, outputPaths.csharp), `${outputs.get(outputPaths.csharp) ?? ''}// edited\n`);
      rmSync(join(root, outputPaths.kotlin));
      expect(findStale(root, outputs).sort()).toEqual([outputPaths.csharp, outputPaths.kotlin].sort());
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it('are rendered deterministically', () => {
    expect(renderOutputs(loadInputs())).toEqual(outputs);
  });
});
