import { describe, expect, test } from 'vitest';
import { classifyFailure, isCertificateError, tlsErrorCodes } from '$lib/server/failures';
import { fetchFailed } from './fakes.ts';

const certificateCodes = [
  'DEPTH_ZERO_SELF_SIGNED_CERT',
  'SELF_SIGNED_CERT_IN_CHAIN',
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE',
  'UNABLE_TO_GET_ISSUER_CERT',
  'UNABLE_TO_GET_ISSUER_CERT_LOCALLY',
  'CERT_HAS_EXPIRED',
  'CERT_NOT_YET_VALID',
  'CERT_UNTRUSTED',
  'ERR_TLS_CERT_ALTNAME_INVALID',
];

describe('failure classifier', () => {
  test.each(certificateCodes)('UX-DR92 %s is a certificate failure at every stage', (code) => {
    expect(tlsErrorCodes.has(code)).toBe(true);
    for (const stage of ['server', 'issuer', 'oidc'] as const) {
      expect(classifyFailure(fetchFailed(code), stage)).toBe('certificate');
    }
  });

  test('UX-DR92 walks nested causes to find the TLS code', () => {
    const inner = Object.assign(new Error('tls'), { code: 'CERT_HAS_EXPIRED' });
    const wrapped = new Error('discovery failed', { cause: new TypeError('fetch failed', { cause: inner }) });
    expect(isCertificateError(wrapped)).toBe(true);
    expect(classifyFailure(wrapped, 'oidc')).toBe('certificate');
  });

  test.each(['ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN', 'ETIMEDOUT', 'ECONNRESET'])(
    'UX-DR92 %s on the Server probe is unreachable, on the issuer a Keycloak error',
    (code) => {
      expect(classifyFailure(fetchFailed(code), 'server')).toBe('unreachable');
      expect(classifyFailure(fetchFailed(code), 'issuer')).toBe('keycloak');
      expect(classifyFailure(fetchFailed(code), 'oidc')).toBe('keycloak');
    },
  );

  test('UX-DR92 a timeout on the Server probe is unreachable', () => {
    const timeout = new DOMException('The operation was aborted due to timeout', 'TimeoutError');
    expect(classifyFailure(timeout, 'server')).toBe('unreachable');
  });

  test('tolerates non-error values and cause cycles', () => {
    expect(classifyFailure('boom', 'server')).toBe('unreachable');
    expect(classifyFailure(null, 'oidc')).toBe('keycloak');
    const cyclic: { cause?: unknown } = {};
    cyclic.cause = cyclic;
    expect(isCertificateError(cyclic)).toBe(false);
  });
});
