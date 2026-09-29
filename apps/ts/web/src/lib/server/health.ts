import type { Handle } from '@sveltejs/kit';
import { isConfigLoaded } from './runtime';

/** Liveness: the process answers HTTP. Same path as the Server's Escendit defaults. */
export const livePath = '/.well-known/healthz/live';

/** Readiness: the configuration validated by `init` has been loaded. */
export const readyPath = '/.well-known/healthz/ready';

function status(healthy: boolean, method: string): Response {
  const body = JSON.stringify({ status: healthy ? 'Healthy' : 'Unhealthy' });
  return new Response(method === 'HEAD' ? null : body, {
    status: healthy ? 200 : 503,
    headers: {
      'content-type': 'application/json',
      'cache-control': 'no-store',
    },
  });
}

/**
 * Answers the kubelet's probes before any other handle, so they need no session, no sign-in and
 * no OIDC discovery. Every other request passes through unchanged.
 */
export function createHealthHandle(isReady: () => boolean): Handle {
  return ({ event, resolve }) => {
    const { method } = event.request;
    if (method === 'GET' || method === 'HEAD') {
      const path = event.url.pathname;
      if (path === livePath) {
        return status(true, method);
      }
      if (path === readyPath) {
        return status(isReady(), method);
      }
    }
    return resolve(event);
  };
}

/** The app's health handle: ready once `init` has loaded the configuration. */
export const healthHandle: Handle = createHealthHandle(isConfigLoaded);
