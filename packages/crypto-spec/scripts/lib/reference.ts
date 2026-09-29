/**
 * A reference implementation of crypto-spec.json on Node's crypto module. It computes vectors.json and
 * must reproduce the RFC anchors first, so the vectors never rest on this code alone: Rust, C# and
 * Kotlin each reproduce them independently.
 */
import {
  createCipheriv,
  createDecipheriv,
  createHash,
  createHmac,
  createPrivateKey,
  createPublicKey,
  diffieHellman,
} from 'node:crypto';

import type { CryptoSpec } from './spec.ts';

export const hex = (bytes: Uint8Array): string => Buffer.from(bytes).toString('hex');
export const unhex = (text: string): Buffer => Buffer.from(text, 'hex');
const utf8 = (text: string): Buffer => Buffer.from(text, 'utf8');

export function u64be(value: bigint): Buffer {
  const out = Buffer.alloc(8);
  out.writeBigUInt64BE(value);
  return out;
}

function i2osp(value: number, length: number): Buffer {
  const out = Buffer.alloc(length);
  out.writeUIntBE(value, 0, length);
  return out;
}

export const sha256 = (data: Uint8Array): Buffer => createHash('sha256').update(data).digest();
export const hmacSha256 = (key: Uint8Array, data: Uint8Array): Buffer => createHmac('sha256', key).update(data).digest();

export function hkdfExtract(salt: Uint8Array, ikm: Uint8Array): Buffer {
  return hmacSha256(salt.length === 0 ? Buffer.alloc(32) : salt, ikm);
}

export function hkdfExpand(prk: Uint8Array, info: Uint8Array, length: number): Buffer {
  const blocks: Buffer[] = [];
  let previous: Buffer = Buffer.alloc(0);
  for (let counter = 1; Buffer.concat(blocks).length < length; counter++) {
    previous = hmacSha256(prk, Buffer.concat([previous, info, Buffer.from([counter])]));
    blocks.push(previous);
  }
  return Buffer.concat(blocks).subarray(0, length);
}

export const hkdf = (salt: Uint8Array, ikm: Uint8Array, info: Uint8Array, length: number): Buffer =>
  hkdfExpand(hkdfExtract(salt, ikm), info, length);

// ---------- X25519 ----------

const PKCS8_X25519 = unhex('302e020100300506032b656e04220420');
const SPKI_X25519 = unhex('302a300506032b656e032100');

function privateKey(raw: Uint8Array) {
  return createPrivateKey({ key: Buffer.concat([PKCS8_X25519, raw]), format: 'der', type: 'pkcs8' });
}

function publicKey(raw: Uint8Array) {
  return createPublicKey({ key: Buffer.concat([SPKI_X25519, raw]), format: 'der', type: 'spki' });
}

export function x25519PublicKey(privateRaw: Uint8Array): Buffer {
  const der = createPublicKey(privateKey(privateRaw)).export({ format: 'der', type: 'spki' });
  return der.subarray(SPKI_X25519.length);
}

export function x25519(privateRaw: Uint8Array, publicRaw: Uint8Array): Buffer {
  return diffieHellman({ privateKey: privateKey(privateRaw), publicKey: publicKey(publicRaw) });
}

// ---------- ChaCha20-Poly1305 ----------

export function aeadSeal(key: Uint8Array, nonce: Uint8Array, aad: Uint8Array, plaintext: Uint8Array): Buffer {
  const cipher = createCipheriv('chacha20-poly1305', key, nonce, { authTagLength: 16 });
  cipher.setAAD(aad, { plaintextLength: plaintext.length });
  return Buffer.concat([cipher.update(plaintext), cipher.final(), cipher.getAuthTag()]);
}

export function aeadOpen(key: Uint8Array, nonce: Uint8Array, aad: Uint8Array, sealed: Uint8Array): Buffer {
  const decipher = createDecipheriv('chacha20-poly1305', key, nonce, { authTagLength: 16 });
  decipher.setAAD(aad, { plaintextLength: sealed.length - 16 });
  decipher.setAuthTag(sealed.subarray(sealed.length - 16));
  return Buffer.concat([decipher.update(sealed.subarray(0, sealed.length - 16)), decipher.final()]);
}

// ---------- Key hierarchy ----------

export interface DeviceKeys {
  deviceKey: Buffer;
  sealKey: Buffer;
  ackKey: Buffer;
  hubAuthKey: Buffer;
  deviceId: Buffer;
}

export function deriveDeviceKeys(spec: CryptoSpec, rootKey: Uint8Array): DeviceKeys {
  const { deviceKey: dk, purposeKeys: pk, deviceId: id } = spec.keyHierarchy;
  const deviceKey = hmacSha256(rootKey, utf8(dk.label));
  const purpose = (label: string, length: number) => hkdf(Buffer.alloc(0), deviceKey, utf8(label), length);
  return {
    deviceKey,
    sealKey: purpose(pk.labels.seal, pk.length),
    ackKey: purpose(pk.labels.ack, pk.length),
    hubAuthKey: purpose(pk.labels.hubAuth, pk.length),
    deviceId: purpose(id.label, id.length),
  };
}

// ---------- Frames ----------

export function frameNonce(spec: CryptoSpec, deviceId: Uint8Array, counter: bigint): Buffer {
  return Buffer.concat([deviceId.subarray(0, spec.frame.nonceDeviceIdPrefixLength), u64be(counter)]);
}

export function frameAad(spec: CryptoSpec, deviceId: Uint8Array, counter: bigint): Buffer {
  return Buffer.concat([Buffer.from([spec.protocolMajor]), deviceId, u64be(counter)]);
}

// ---------- HPKE (RFC 9180 base mode, DHKEM(X25519)/HKDF-SHA256/ChaCha20Poly1305) ----------

export interface HpkeSealed {
  skE: Buffer;
  enc: Buffer;
  sharedSecret: Buffer;
  key: Buffer;
  baseNonce: Buffer;
  ciphertext: Buffer;
}

export function hpkeSealBase(
  spec: CryptoSpec,
  pkR: Uint8Array,
  ikmE: Uint8Array,
  info: Uint8Array,
  aad: Uint8Array,
  plaintext: Uint8Array,
): HpkeSealed {
  const { hpke } = spec.enrolment;
  const version = utf8(hpke.versionLabel);
  const kemSuite = Buffer.concat([utf8('KEM'), i2osp(hpke.kemId, 2)]);
  const hpkeSuite = Buffer.concat([utf8('HPKE'), i2osp(hpke.kemId, 2), i2osp(hpke.kdfId, 2), i2osp(hpke.aeadId, 2)]);
  const labeledExtract = (suite: Buffer, salt: Uint8Array, label: string, ikm: Uint8Array) =>
    hkdfExtract(salt, Buffer.concat([version, suite, utf8(label), ikm]));
  const labeledExpand = (suite: Buffer, prk: Uint8Array, label: string, labelInfo: Uint8Array, length: number) =>
    hkdfExpand(prk, Buffer.concat([i2osp(length, 2), version, suite, utf8(label), labelInfo]), length);
  const empty = Buffer.alloc(0);
  const n = spec.enrolment.x25519KeyLength;

  const dkpPrk = labeledExtract(kemSuite, empty, hpke.labels.dkpPrk, ikmE);
  const skE = labeledExpand(kemSuite, dkpPrk, hpke.labels.sk, empty, n);
  const enc = x25519PublicKey(skE);
  const dh = x25519(skE, pkR);
  const kemContext = Buffer.concat([enc, pkR]);
  const eaePrk = labeledExtract(kemSuite, empty, hpke.labels.eaePrk, dh);
  const sharedSecret = labeledExpand(kemSuite, eaePrk, hpke.labels.sharedSecret, kemContext, n);

  const pskIdHash = labeledExtract(hpkeSuite, empty, hpke.labels.pskIdHash, empty);
  const infoHash = labeledExtract(hpkeSuite, empty, hpke.labels.infoHash, info);
  const context = Buffer.concat([Buffer.from([hpke.mode]), pskIdHash, infoHash]);
  const secret = labeledExtract(hpkeSuite, sharedSecret, hpke.labels.secret, empty);
  const key = labeledExpand(hpkeSuite, secret, hpke.labels.key, context, spec.aead.keyLength);
  const baseNonce = labeledExpand(hpkeSuite, secret, hpke.labels.baseNonce, context, spec.aead.nonceLength);
  return { skE, enc, sharedSecret, key, baseNonce, ciphertext: aeadSeal(key, baseNonce, aad, plaintext) };
}

export const fingerprint = (publicRaw: Uint8Array): string => hex(sha256(publicRaw));

// ---------- Setup session ----------

export interface SetupKeys {
  sharedSecret: Buffer;
  appToHubKey: Buffer;
  hubToAppKey: Buffer;
}

export function setupKeys(
  spec: CryptoSpec,
  ownPrivate: Uint8Array,
  peerPublic: Uint8Array,
  appPublic: Uint8Array,
  hubPublic: Uint8Array,
  popCode: string,
): SetupKeys {
  const { setup } = spec;
  const sharedSecret = x25519(ownPrivate, peerPublic);
  const okm = hkdf(
    utf8(popCode.toUpperCase()),
    sharedSecret,
    Buffer.concat([utf8(setup.label), appPublic, hubPublic]),
    setup.okmLength,
  );
  return {
    sharedSecret,
    appToHubKey: okm.subarray(setup.appToHubKeyOffset, setup.appToHubKeyOffset + setup.keyLength),
    hubToAppKey: okm.subarray(setup.hubToAppKeyOffset, setup.hubToAppKeyOffset + setup.keyLength),
  };
}

export const setupNonce = (spec: CryptoSpec, counter: bigint): Buffer =>
  Buffer.concat([Buffer.alloc(spec.setup.noncePrefixLength), u64be(counter)]);

export const setupAad = (spec: CryptoSpec): Buffer => Buffer.from([spec.protocolMajor]);

// ---------- Heartbeat ----------

export function heartbeatCanonical(
  spec: CryptoSpec,
  method: string,
  path: string,
  body: Uint8Array,
  timestampMs: bigint,
  nonce: Uint8Array,
): string {
  return [method, path, hex(sha256(body)), timestampMs.toString(), hex(nonce)].join(spec.heartbeat.separator);
}
