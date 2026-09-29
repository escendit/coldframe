import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { repoRoot } from '../../../packages/crypto-spec/scripts/lib/paths.ts';
import { aeadOpen, unhex } from '../../../packages/crypto-spec/scripts/lib/reference.ts';
import { loadSpec } from '../../../packages/crypto-spec/scripts/lib/spec.ts';
import { ANCHORS, verifyAnchors } from '../../../packages/crypto-spec/scripts/lib/vectors.ts';

interface Frame {
  key: string;
  nonce: string;
  aad: string;
  plaintext: string;
  ciphertext: string;
}

const vectors = JSON.parse(readFileSync(join(repoRoot, 'packages/crypto-spec/vectors.json'), 'utf8')) as {
  anchors: unknown;
  frames: Frame[];
  [category: string]: unknown;
};

describe('vectors.json', () => {
  it('holds every category', () => {
    for (const category of ['keyHierarchy', 'frames', 'replay', 'enrolment', 'setup', 'heartbeat', 'anchors']) {
      expect(vectors[category], category).toBeDefined();
    }
  });

  it('carries the RFC anchors verbatim', () => {
    expect(vectors.anchors).toEqual(ANCHORS);
  });

  it('is built by a reference implementation that reproduces the RFC anchors', () => {
    expect(() => {
      verifyAnchors(loadSpec());
    }).not.toThrow();
  });

  it('opens its own frames on Node crypto and refuses a flipped bit', () => {
    for (const frame of vectors.frames) {
      const sealed = unhex(frame.ciphertext);
      expect(aeadOpen(unhex(frame.key), unhex(frame.nonce), unhex(frame.aad), sealed).toString('hex')).toBe(frame.plaintext);
      sealed[0] = (sealed[0] ?? 0) ^ 1;
      expect(() => aeadOpen(unhex(frame.key), unhex(frame.nonce), unhex(frame.aad), sealed)).toThrow();
    }
  });
});
