import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { repoRoot } from '../../../packages/crypto-spec/scripts/lib/paths.ts';
import { aeadOpen, sensorId, unhex, uuidV5 } from '../../../packages/crypto-spec/scripts/lib/reference.ts';
import { loadSpec } from '../../../packages/crypto-spec/scripts/lib/spec.ts';
import { ANCHORS, verifyAnchors } from '../../../packages/crypto-spec/scripts/lib/vectors.ts';

interface Frame {
  key: string;
  nonce: string;
  aad: string;
  plaintext: string;
  ciphertext: string;
}

interface SensorIdVector {
  deviceId: string;
  slot: number;
  quantity: string;
  namespace: string;
  uuidName: string;
  sensorId: string;
}

const vectors = JSON.parse(readFileSync(join(repoRoot, 'packages/crypto-spec/vectors.json'), 'utf8')) as {
  anchors: unknown;
  frames: Frame[];
  sensorId: SensorIdVector[];
  [category: string]: unknown;
};

describe('vectors.json', () => {
  it('holds every category', () => {
    for (const category of ['keyHierarchy', 'frames', 'replay', 'enrolment', 'setup', 'heartbeat', 'sensorId', 'anchors']) {
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

  it('derives its Sensor IDs as UUIDv5 of the name in the spec namespace', () => {
    const spec = loadSpec();
    expect(uuidV5(spec.sensorId.urlNamespace, spec.sensorId.namespaceName)).toBe(spec.sensorId.namespace);
    expect(vectors.sensorId.length).toBeGreaterThan(0);
    for (const vector of vectors.sensorId) {
      expect(vector.namespace).toBe(spec.sensorId.namespace);
      expect(vector.uuidName).toBe(`${vector.deviceId}:${String(vector.slot)}:${vector.quantity}`);
      expect(uuidV5(vector.namespace, vector.uuidName)).toBe(vector.sensorId);
      expect(sensorId(spec, unhex(vector.deviceId), vector.slot, vector.quantity)).toBe(vector.sensorId);
      expect(vector.sensorId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    }
  });
});
