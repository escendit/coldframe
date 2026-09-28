import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { outputPaths, repoRoot } from '../../../packages/crypto-spec/scripts/lib/paths.ts';
import { pascalName, screamingName } from '../../../packages/crypto-spec/scripts/lib/render.ts';
import { constantsOf, loadSpec } from '../../../packages/crypto-spec/scripts/lib/spec.ts';

const read = (path: string) => readFileSync(join(repoRoot, path), 'utf8');

describe('generated constants', () => {
  const constants = constantsOf(loadSpec()).flatMap((group) => group.constants);

  it('have unique names', () => {
    expect(new Set(constants.map((constant) => constant.name)).size).toBe(constants.length);
  });

  it.each(constants.map((constant) => [constant.name, constant] as const))('%s is emitted in every language', (_, constant) => {
    const value = typeof constant.value === 'string' ? JSON.stringify(constant.value) : String(constant.value);
    expect(read(outputPaths.rust)).toContain(`pub const ${screamingName(constant.name)}:`);
    expect(read(outputPaths.rust)).toContain(`= ${value}`);
    expect(read(outputPaths.csharp)).toContain(` ${pascalName(constant.name)} = `);
    expect(read(outputPaths.kotlin)).toContain(`public const val ${screamingName(constant.name)}:`);
  });

  it('carry the normative labels of the spec', () => {
    const byName = new Map(constants.map((constant) => [constant.name, constant.value]));
    expect(byName.get('deviceKeyLabel')).toBe('coldframe/device/v1');
    expect(byName.get('sealLabel')).toBe('seal/v1');
    expect(byName.get('ackLabel')).toBe('ack/v1');
    expect(byName.get('hubAuthLabel')).toBe('hub-auth/v1');
    expect(byName.get('deviceIdLabel')).toBe('device-id/v1');
    expect(byName.get('enrolmentInfo')).toBe('coldframe/enrolment/v1');
    expect(byName.get('setupLabel')).toBe('coldframe/setup/v1');
    expect(byName.get('frameAadLength')).toBe(17);
    expect(byName.get('replayWindow')).toBe(64);
    expect(byName.get('heartbeatMaxSkewMs')).toBe(300000);
  });
});
