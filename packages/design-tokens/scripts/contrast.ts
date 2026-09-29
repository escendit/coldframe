/**
 * Prints the recomputed contrast table for both themes and exits 1 when a text pair is below
 * 4.5:1 or a non-text pair below 3:1.
 */
import { evaluateContrast, formatContrastTable } from './lib/contrast.ts';
import { loadContrastPairs, loadTokens } from './lib/tokens.ts';

const results = evaluateContrast(loadTokens(), loadContrastPairs());
console.log(formatContrastTable(results));

const failing = results.filter((result) => !result.passes);
if (failing.length > 0) {
  console.error('');
  for (const result of failing) {
    console.error(
      `Contrast too low: ${result.label} (${result.theme}) is ${result.ratio.toFixed(2)}:1, needs ${result.minimum.toFixed(1)}:1.`,
    );
  }
  process.exit(1);
}
console.log(`\nAll ${String(results.length)} pairs meet WCAG 2.2 AA contrast.`);
