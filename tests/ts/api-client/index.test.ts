import { packageName } from '@coldframe/api-client';
import { describe, expect, it } from 'vitest';

describe('@coldframe/api-client', () => {
  it('exports its package name', () => {
    expect(packageName).toBe('@coldframe/api-client');
  });
});
