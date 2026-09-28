import { describe, expect, test } from 'vitest';
import { loadConfig } from '$lib/server/config';

const complete = {
  COLDFRAME_SERVER_URL: 'https://coldframe.example.org',
  KEYCLOAK_ISSUER: 'https://id.example.org/realms/coldframe',
  KEYCLOAK_CLIENT_ID: 'coldframe-web',
  KEYCLOAK_CLIENT_SECRET: 'secret',
};

describe('configuration', () => {
  test('reads the required variables with safe defaults', () => {
    const config = loadConfig(complete);
    expect(config.serverUrl.href).toBe('https://coldframe.example.org/');
    expect(config.issuer.href).toBe('https://id.example.org/realms/coldframe');
    expect(config.clientId).toBe('coldframe-web');
    expect(config.allowInsecureHttp).toBe(false);
    expect(config.sessionCookieSecure).toBe(true);
  });

  test.each(Object.keys(complete))('fails naming %s when it is missing', (name) => {
    const env: Record<string, string> = { ...complete };
    env[name] = '';
    expect(() => loadConfig(env)).toThrow(name);
  });

  test('parses the optional switches', () => {
    const config = loadConfig({ ...complete, KEYCLOAK_ALLOW_INSECURE_HTTP: 'true', SESSION_COOKIE_SECURE: 'false' });
    expect(config.allowInsecureHttp).toBe(true);
    expect(config.sessionCookieSecure).toBe(false);
    expect(() => loadConfig({ ...complete, SESSION_COOKIE_SECURE: 'maybe' })).toThrow('SESSION_COOKIE_SECURE');
    expect(() => loadConfig({ ...complete, KEYCLOAK_ISSUER: 'not a url' })).toThrow('KEYCLOAK_ISSUER');
  });
});
