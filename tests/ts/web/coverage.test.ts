import { describe, expect, test } from 'vitest';
import { e2eTestsRoot, filesUnder, read, unitTestsRoot } from './helpers.ts';

/** The UX-DR ids the stories so far name (Story 1.4, then Story 1.8: 21, 22, 23, 54, 61, 62, 82). */
const storyIds = [
  15, 16, 18, 20, 21, 22, 23, 34, 35, 36, 53, 54, 56, 58, 59, 60, 61, 62, 71, 74, 75, 76, 82, 84, 92, 93, 100, 101, 102, 104, 111, 113, 114, 124, 125, 126, 127, 130, 131,
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
