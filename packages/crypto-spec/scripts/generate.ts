/**
 * Writes the Rust, C# and Kotlin constants and vectors.json from crypto-spec.json. The reference
 * implementation must reproduce the RFC anchors first. With --check it writes nothing and exits 1
 * when a committed output is stale.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

import { fromRoot, repoRoot } from './lib/paths.ts';
import { findStale, loadInputs, renderOutputs } from './lib/render.ts';

const check = process.argv.includes('--check');
const outputs = renderOutputs(loadInputs());
const stale = findStale(repoRoot, outputs);

if (check) {
  if (stale.length > 0) {
    console.error('Generated crypto-spec outputs are stale:');
    for (const path of stale) {
      console.error(`  ${path}`);
    }
    console.error('Run `pnpm --filter @coldframe/crypto-spec run generate` and commit the result.');
    process.exit(1);
  }
  console.log(`All ${String(outputs.size)} generated crypto-spec outputs are fresh; the RFC anchors reproduce.`);
} else {
  for (const path of stale) {
    const file = fromRoot(path);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, outputs.get(path) ?? '');
    console.log(`wrote ${path}`);
  }
  console.log(`${String(stale.length)} of ${String(outputs.size)} outputs changed.`);
}
