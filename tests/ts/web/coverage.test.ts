import { describe, expect, test } from 'vitest';
import { e2eTestsRoot, filesUnder, read, unitTestsRoot } from './helpers.ts';

/**
 * The UX-DR ids the stories so far name (Story 1.4, then Story 1.8: 21, 22, 23, 54, 61, 62, 82; Story 3.7: 30, 65, 85;
 * Story 4.7: 12, 17, 19, 24, 77, 79, 80, 97, 98, 99, 106, 108, 112, 128, 129; Story 4.8: 27, 28, 29, 30, 32, 33, 63, 78, 98; Story 5.4: 5, 32, 33, 45, 69, 84, 91; Story 6.2: 14, 25, 26, 64, 82, 98. UX-DR107 is the phone layout and has no web test).
 */
const storyIds = [
  5, 12, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 32, 33, 34, 35, 36, 45, 53, 54, 56, 58, 59, 60, 61, 62, 63, 64, 65, 69, 71, 74, 75, 76, 77, 78, 79, 80, 82, 84, 85, 91, 92, 93, 97, 98, 99, 100, 101, 102, 104, 106, 108,
  111, 112, 113, 114, 124, 125, 126, 127, 128, 129, 130, 131,
].map((id) => `UX-DR${String(id)}`);

/** Names of tests and suites that start with a UX-DR id, with every id they mention. */
function namedIds(): Set<string> {
  const ids = new Set<string>();
  const files = [...filesUnder(unitTestsRoot, ['.test.ts']), ...filesUnder(e2eTestsRoot, ['.spec.ts'])];
  const name = /\b(?:test|it|describe)(?:\.(?:each\([^)]*\)|only|skip|describe|serial))?\(\s*(['"`])(UX-DR\d+[^'"`]*)\1/gu;
  for (const file of files) {
    for (const match of read(file).matchAll(name)) {
      for (const id of (match[2] ?? '').matchAll(/UX-DR\d+/gu)) {
        ids.add(id[0]);
      }
    }
  }
  return ids;
}

describe('UX requirement coverage', () => {
  test.each(storyIds)('%s is named by at least one test', (id) => {
    expect(namedIds().has(id)).toBe(true);
  });
});
