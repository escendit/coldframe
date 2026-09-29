/** Fixed local ports of the e2e stack. */
export const ports = {
  /** Fake OIDC provider, also the fake Coldframe Server's health endpoint. */
  idp: 4410,
  /** HTTPS stub with a self-signed certificate: the "untrusted Server". */
  tls: 4411,
  /** Nothing listens here: the "unreachable Server". */
  closed: 4412,
  /** Web app whose Server and issuer answer. */
  app: 4401,
  /** Web app whose Server URL points at the closed port. */
  unreachable: 4402,
  /** Web app whose Server URL points at the TLS stub. */
  untrusted: 4403,
} as const;

export const idpOrigin = `http://localhost:${String(ports.idp)}`;
export const appUrl = `http://localhost:${String(ports.app)}`;
export const unreachableUrl = `http://localhost:${String(ports.unreachable)}`;
export const untrustedUrl = `http://localhost:${String(ports.untrusted)}`;
