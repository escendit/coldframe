/**
 * Writes every generated output from tokens/tokens.json and the vendored Carbon icons.
 * With --check it writes nothing and exits 1 when a committed output is stale.
 */
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

import { fromRoot, repoRoot } from './lib/paths.ts';
import { findStale, loadInputs, renderOutputs } from './lib/render.ts';

const check = process.argv.includes('--check');
const outputs = renderOutputs(loadInputs());
const stale = findStale(repoRoot, outputs);

if (check) {
  if (stale.length > 0) {
    console.error('Generated design-token outputs are stale:');
    for (const path of stale) {
      console.error(`  ${path}`);
    }
    console.error('Run `pnpm --filter @coldframe/design-tokens run generate` and commit the result.');
    process.exit(1);
  }
  console.log(`All ${String(outputs.size)} generated design-token outputs are fresh.`);
} else {
  for (const path of stale) {
    const content = outputs.get(path);
    const file = fromRoot(path);
    if (content === undefined) {
      rmSync(file);
      console.log(`removed ${path}`);
    } else {
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, content);
      console.log(`wrote ${path}`);
    }
  }
  console.log(`${String(stale.length)} of ${String(outputs.size)} outputs changed.`);
}
