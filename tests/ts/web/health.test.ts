import type { RequestEvent } from '@sveltejs/kit';
import { describe, expect, test } from 'vitest';
import { createHealthHandle, healthHandle, livePath, readyPath } from '$lib/server/health';
import { setConfig } from '$lib/server/runtime';
import { loadConfig } from '$lib/server/config';
import { fakeEvent, type Resolve } from './fakes.ts';

function passThrough(): Resolve & { calls: RequestEvent[] } {
  const calls: RequestEvent[] = [];
  const resolve = ((event: RequestEvent) => {
    calls.push(event);
    return new Response('page', { status: 200 });
  }) as Resolve & { calls: RequestEvent[] };
  resolve.calls = calls;
  return resolve;
}

function eventFor(path: string, method = 'GET'): RequestEvent {
  const event = fakeEvent(path);
  return { ...event, request: new Request(event.url, { method }) };
}

describe('health endpoints', () => {
  test('live answers 200 Healthy without touching the rest of the chain', async () => {
    const resolve = passThrough();
    const response = await createHealthHandle(() => false)({ event: eventFor(livePath), resolve });
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ status: 'Healthy' });
    expect(resolve.calls).toHaveLength(0);
  });

  test('ready answers 503 Unhealthy until the configuration is loaded, then 200', async () => {
    let loaded = false;
    const handle = createHealthHandle(() => loaded);

    const before = await handle({ event: eventFor(readyPath), resolve: passThrough() });
    expect(before.status).toBe(503);
    expect(await before.json()).toEqual({ status: 'Unhealthy' });

    loaded = true;
    const after = await handle({ event: eventFor(readyPath), resolve: passThrough() });
    expect(after.status).toBe(200);
    expect(await after.json()).toEqual({ status: 'Healthy' });
  });

  test('responses are JSON and never cached', async () => {
    for (const path of [livePath, readyPath]) {
      const response = await createHealthHandle(() => true)({ event: eventFor(path), resolve: passThrough() });
      expect(response.headers.get('content-type')).toBe('application/json');
      expect(response.headers.get('cache-control')).toBe('no-store');
    }
  });

  test('HEAD answers the status without a body', async () => {
    const response = await createHealthHandle(() => true)({ event: eventFor(livePath, 'HEAD'), resolve: passThrough() });
    expect(response.status).toBe(200);
    expect(await response.text()).toBe('');
  });

  test('every other request passes through', async () => {
    const handle = createHealthHandle(() => true);
    for (const [path, method] of [
      ['/', 'GET'],
      ['/garden', 'GET'],
      ['/.well-known/healthz', 'GET'],
      ['/.well-known/healthz/startup', 'GET'],
      [`${livePath}/x`, 'GET'],
      [livePath, 'POST'],
    ] as const) {
      const resolve = passThrough();
      const response = await handle({ event: eventFor(path, method), resolve });
      expect(await response.text()).toBe('page');
      expect(resolve.calls).toHaveLength(1);
    }
  });

  test('the app handle reports ready once init has stored the configuration', async () => {
    setConfig(
      loadConfig({
        COLDFRAME_SERVER_URL: 'https://coldframe.example.org',
        KEYCLOAK_ISSUER: 'https://id.example.org/realms/coldframe',
        KEYCLOAK_CLIENT_ID: 'coldframe-web',
        KEYCLOAK_CLIENT_SECRET: 'secret',
      }),
    );
    const response = await healthHandle({ event: eventFor(readyPath), resolve: passThrough() });
    expect(response.status).toBe(200);
  });
});
