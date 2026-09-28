import type { Theme } from '$lib/theme';

/**
 * The identity `@escendit/sveltekit-auth-keycloak` keeps in the server-side session. Read only
 * by server modules; the raw tokens never leave them.
 */
interface SessionIdentity {
  readonly authenticated: boolean;
  readonly accessTokenExpiresAt?: string | Date | null;
  readonly idToken?: Readonly<Record<string, unknown>> | null;
  readonly [key: string]: unknown;
}

declare global {
  namespace App {
    interface Locals {
      /** Set by the package's session middleware. `identity` is null when signed out. */
      session?: {
        identity: SessionIdentity | null;
        created?: string | null;
      };
      sessionId?: string;
      /** Package internals (session store, OIDC configuration); opaque to app code. */
      store?: unknown;
      config?: unknown;
      /** The request carried the "had a session" marker but no identity (UX-DR93). */
      sessionEnded?: boolean;
      /** Theme of this browser, from the `cf_theme` cookie. */
      theme?: Theme;
    }
  }
}

export {};
