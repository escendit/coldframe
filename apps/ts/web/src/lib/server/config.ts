/**
 * Private configuration of the web app, read from the environment once at startup.
 * Only server modules import this file; nothing here reaches the browser.
 */
export interface WebConfig {
  /** Base URL of the Coldframe Server, probed before every sign-in. */
  readonly serverUrl: URL;
  /** Keycloak realm issuer, e.g. `https://id.example.org/realms/coldframe`. */
  readonly issuer: URL;
  readonly clientId: string;
  readonly clientSecret: string;
  /** Allows a plain-HTTP issuer; only for a local Keycloak. */
  readonly allowInsecureHttp: boolean;
  /** Marks the session cookie `Secure`. */
  readonly sessionCookieSecure: boolean;
}

export type Env = Readonly<Record<string, string | undefined>>;

type RequiredVariable = 'COLDFRAME_SERVER_URL' | 'KEYCLOAK_ISSUER' | 'KEYCLOAK_CLIENT_ID' | 'KEYCLOAK_CLIENT_SECRET';

function readRequired(env: Env, name: RequiredVariable): string {
  const value = env[name]?.trim();
  if (value === undefined || value === '') {
    throw new Error(`Missing required environment variable ${name}.`);
  }
  return value;
}

function readUrl(env: Env, name: RequiredVariable): URL {
  const value = readRequired(env, name);
  try {
    return new URL(value);
  } catch {
    throw new Error(`Environment variable ${name} is not an absolute URL: ${value}`);
  }
}

function readBoolean(env: Env, name: string, fallback: boolean): boolean {
  const value = env[name]?.trim().toLowerCase();
  if (value === undefined || value === '') {
    return fallback;
  }
  if (value === 'true' || value === '1') {
    return true;
  }
  if (value === 'false' || value === '0') {
    return false;
  }
  throw new Error(`Environment variable ${name} must be true or false, not ${value}.`);
}

/** Reads and validates the configuration. Throws naming the first missing or invalid variable. */
export function loadConfig(env: Env): WebConfig {
  return {
    serverUrl: readUrl(env, 'COLDFRAME_SERVER_URL'),
    issuer: readUrl(env, 'KEYCLOAK_ISSUER'),
    clientId: readRequired(env, 'KEYCLOAK_CLIENT_ID'),
    clientSecret: readRequired(env, 'KEYCLOAK_CLIENT_SECRET'),
    allowInsecureHttp: readBoolean(env, 'KEYCLOAK_ALLOW_INSECURE_HTTP', false),
    sessionCookieSecure: readBoolean(env, 'SESSION_COOKIE_SECURE', true),
  };
}
