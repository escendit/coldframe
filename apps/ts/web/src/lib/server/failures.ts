/** What went wrong on the way to Keycloak, in the terms of the UX-DR92 notices. */
export type Failure = 'unreachable' | 'certificate' | 'keycloak';

/** Where the failure happened: the Server probe, the issuer probe, or the OIDC flow itself. */
export type FailureStage = 'server' | 'issuer' | 'oidc';

/** Node/OpenSSL codes for a certificate that cannot be verified. Never retried insecurely. */
export const tlsErrorCodes: ReadonlySet<string> = new Set([
  'DEPTH_ZERO_SELF_SIGNED_CERT',
  'SELF_SIGNED_CERT_IN_CHAIN',
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE',
  'UNABLE_TO_GET_ISSUER_CERT',
  'UNABLE_TO_GET_ISSUER_CERT_LOCALLY',
  'UNABLE_TO_DECRYPT_CERT_SIGNATURE',
  'UNABLE_TO_DECODE_ISSUER_PUBLIC_KEY',
  'CERT_SIGNATURE_FAILURE',
  'CERT_NOT_YET_VALID',
  'CERT_HAS_EXPIRED',
  'CERT_UNTRUSTED',
  'CERT_REJECTED',
  'CERT_REVOKED',
  'INVALID_CA',
  'PATH_LENGTH_EXCEEDED',
  'INVALID_PURPOSE',
  'HOSTNAME_MISMATCH',
  'ERR_TLS_CERT_ALTNAME_INVALID',
  'ERR_TLS_CERT_ALTNAME_FORMAT',
]);

const maxCauseDepth = 10;

function codeOf(value: unknown): string | undefined {
  if (typeof value === 'object' && value !== null && 'code' in value) {
    return typeof value.code === 'string' ? value.code : undefined;
  }
  return undefined;
}

function causeOf(value: unknown): unknown {
  if (typeof value === 'object' && value !== null && 'cause' in value) {
    return value.cause;
  }
  return undefined;
}

/** True when the error, or any error in its `cause` chain, is a certificate verification failure. */
export function isCertificateError(error: unknown): boolean {
  let current = error;
  for (let depth = 0; depth < maxCauseDepth && current !== undefined && current !== null; depth++) {
    const code = codeOf(current);
    if (code !== undefined && tlsErrorCodes.has(code)) {
      return true;
    }
    current = causeOf(current);
  }
  return false;
}

/**
 * Maps an error to the notice it shows. A certificate failure wins everywhere; otherwise a
 * Server probe that got no response means the Server is unreachable, and anything that went
 * wrong with the issuer or the OIDC flow is a Keycloak error.
 */
export function classifyFailure(error: unknown, stage: FailureStage): Failure {
  if (isCertificateError(error)) {
    return 'certificate';
  }
  return stage === 'server' ? 'unreachable' : 'keycloak';
}
