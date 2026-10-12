import { describe, expect, test } from 'vitest';
import { createRefetcher, isDelivered, isHint, liveMessages, startLive, type Hub, type HubHandlers, type NotificationDelivered, type ReadModelChanged, type ReadModelResource } from '$lib/live/live';
import { showsResource } from '$lib/live/rule';
import { proxyLive } from '$lib/server/live';
import { fetchFailed, problemResponse } from './fakes.ts';
import { read, repoRoot, webSrc } from './helpers.ts';

const serverUrl = new URL('https://server.example');
const signedIn = { session: { identity: { authenticated: true, accessTokenRaw: 'token-1', refreshTokenRaw: 'refresh-1', idTokenRaw: 'id-1' } } };
const signedOut = { session: { identity: null } };

interface Upstream {
  readonly method: string;
  readonly url: string;
  readonly headers: Record<string, string>;
  readonly body: string;
  readonly signal: AbortSignal;
}

function upstream(answer: (request: Request) => Response | Promise<Response>): { fetch: (request: Request) => Promise<Response>; seen: Upstream[] } {
  const seen: Upstream[] = [];
  return {
    seen,
    fetch: async (request) => {
      seen.push({
        method: request.method,
        url: request.url,
        headers: Object.fromEntries(request.headers),
        body: request.method === 'POST' ? await request.clone().text() : '',
        signal: request.signal,
      });
      return answer(request);
    },
  };
}

function browser(method: string, path: string, init: RequestInit = {}): { request: Request; url: URL } {
  const url = new URL(path, 'http://localhost:5173');
  const headers = new Headers({ cookie: 'oidc_session=secret; cf_site=1', 'x-forwarded-for': '10.0.0.7', ...(init.headers as Record<string, string> | undefined) });
  return { request: new Request(url, { ...init, method, headers }), url };
}

function run(method: string, path: string, hubPath: string, fake: { fetch: (request: Request) => Promise<Response> }, locals: unknown = signedIn, init: RequestInit = {}): Promise<Response> {
  const { request, url } = browser(method, path, init);
  return proxyLive(locals as never, request, url, hubPath, { serverUrl, fetch: fake.fetch });
}

describe('the BFF route of the hub (AD-14)', () => {
  test('negotiate goes to the Server with the session’s access token and nothing else of the session', async () => {
    const fake = upstream(() => Response.json({ connectionId: 'c1', connectionToken: 't1', negotiateVersion: 1, availableTransports: [] }));
    const response = await run('POST', '/live/negotiate?negotiateVersion=1&access_token=leak&other=1', 'negotiate', fake, signedIn, { body: '', headers: { 'content-type': 'text/plain;charset=UTF-8' } });
    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ connectionToken: 't1' });
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(fake.seen).toHaveLength(1);
    const sent = fake.seen[0];
    expect(sent?.method).toBe('POST');
    // Only the hub's own query: no token ever travels in a URL.
    expect(sent?.url).toBe('https://server.example/live/negotiate?negotiateVersion=1');
    expect(sent?.headers.authorization).toBe('Bearer token-1');
    expect(Object.keys(sent?.headers ?? {}).sort()).toEqual(['authorization', 'content-type']);
    expect(JSON.stringify(sent)).not.toMatch(/oidc_session|refresh-1|id-1|10\.0\.0\.7/u);
  });

  test('a Server under a path prefix keeps it', async () => {
    const fake = upstream(() => Response.json({}));
    const { request, url } = browser('POST', '/live/negotiate', { body: '' });
    await proxyLive(signedIn, request, url, 'negotiate', { serverUrl: new URL('https://server.example/api/'), fetch: fake.fetch });
    expect(fake.seen[0]?.url).toBe('https://server.example/api/live/negotiate');
  });

  test.each([signedOut, {}, { session: { identity: { authenticated: true, accessTokenRaw: '' } } }])('without a session it answers 401 and the Server is never asked', async (locals) => {
    const fake = upstream(() => Response.json({}));
    for (const [method, path, hubPath] of [
      ['POST', '/live/negotiate', 'negotiate'],
      ['GET', '/live?id=t1', ''],
      ['POST', '/live?id=t1', ''],
      ['DELETE', '/live?id=t1', ''],
      ['GET', '/live/anything', 'anything'],
    ] as const) {
      const response = await run(method, path, hubPath, fake, locals, method === 'POST' ? { body: '{}' } : {});
      expect(response.status, `${method} ${path}`).toBe(401);
    }
    expect(fake.seen).toEqual([]);
  });

  test('the event stream is passed on as it arrives, with no time limit, and marked as not to be buffered', async () => {
    let push: ReadableStreamDefaultController<Uint8Array> | undefined;
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        push = controller;
      },
    });
    const fake = upstream(() => new Response(body, { headers: { 'content-type': 'text/event-stream', 'set-cookie': 'server=1', 'x-powered-by': 'kestrel' } }));
    const response = await run('GET', '/live?id=t1', '', fake, signedIn, { headers: { accept: 'text/event-stream' } });
    expect(response.status).toBe(200);
    expect(response.headers.get('content-type')).toBe('text/event-stream');
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(response.headers.get('x-accel-buffering')).toBe('no');
    expect(response.headers.get('set-cookie')).toBeNull();
    expect(response.headers.get('x-powered-by')).toBeNull();
    expect(fake.seen[0]?.url).toBe('https://server.example/live?id=t1');
    expect(fake.seen[0]?.headers.accept).toBe('text/event-stream');
    const reader = response.body?.getReader();
    const encoder = new TextEncoder();
    push?.enqueue(encoder.encode('data: {}\u001e\n\n'));
    expect(new TextDecoder().decode((await reader?.read())?.value)).toBe('data: {}\u001e\n\n');
    push?.enqueue(encoder.encode('data: {"type":6}\u001e\n\n'));
    expect(new TextDecoder().decode((await reader?.read())?.value)).toBe('data: {"type":6}\u001e\n\n');
    expect(fake.seen[0]?.signal.aborted).toBe(false);
  });

  test('the stream has no deadline of the proxy’s own, while every other hub request is bounded', async () => {
    const timeoutMs = 20;
    // A Server that never answers a send, and a stream that never ends.
    const hanging = upstream(
      (request) =>
        new Promise<Response>((resolve, reject) => {
          if (request.method === 'GET') {
            resolve(new Response(new ReadableStream<Uint8Array>({}), { headers: { 'content-type': 'text/event-stream' } }));
            return;
          }
          request.signal.addEventListener('abort', () => {
            reject(new DOMException('timed out', 'TimeoutError'));
          });
        }),
    );
    const call = (method: string, init: RequestInit = {}): Promise<Response> => {
      const { request, url } = browser(method, '/live?id=t1', init);
      return proxyLive(signedIn, request, url, '', { serverUrl, fetch: hanging.fetch, timeoutMs });
    };
    const stream = await call('GET');
    const sent = await call('POST', { body: '{"type":6}\u001e' });
    expect(sent.status).toBe(502);
    await new Promise((resolve) => setTimeout(resolve, timeoutMs * 3));
    const [get, post] = hanging.seen;
    expect([get?.method, post?.method]).toEqual(['GET', 'POST']);
    expect(post?.signal.aborted).toBe(true);
    expect(get?.signal.aborted).toBe(false);
    expect(stream.status).toBe(200);
    await stream.body?.cancel();
  });

  test('the upstream request ends when the browser leaves: the stream is cancelled or the request aborted', async () => {
    const endless = (): Response => new Response(new ReadableStream<Uint8Array>({}), { headers: { 'content-type': 'text/event-stream' } });
    const cancelled = upstream(endless);
    const response = await run('GET', '/live?id=t1', '', cancelled);
    await response.body?.cancel();
    expect(cancelled.seen[0]?.signal.aborted).toBe(true);

    const aborted = upstream(endless);
    const leaving = new AbortController();
    await run('GET', '/live?id=t1', '', aborted, signedIn, { signal: leaving.signal });
    expect(aborted.seen[0]?.signal.aborted).toBe(false);
    leaving.abort();
    expect(aborted.seen[0]?.signal.aborted).toBe(true);
  });

  test('what the client sends is forwarded as it is; DELETE ends a long-polling connection', async () => {
    const fake = upstream((request) => new Response(null, { status: request.method === 'DELETE' ? 202 : 200 }));
    const handshake = '{"protocol":"json","version":1}\u001e';
    const sent = await run('POST', '/live?id=t1', '', fake, signedIn, { body: handshake, headers: { 'content-type': 'text/plain;charset=UTF-8' } });
    expect(sent.status).toBe(200);
    expect(fake.seen[0]).toMatchObject({ method: 'POST', url: 'https://server.example/live?id=t1', body: handshake });
    expect((await run('DELETE', '/live?id=t1', '', fake)).status).toBe(202);
    expect(fake.seen[1]).toMatchObject({ method: 'DELETE', url: 'https://server.example/live?id=t1' });
  });

  test('only the hub’s own paths and methods exist', async () => {
    const fake = upstream(() => Response.json({}));
    for (const [method, path, hubPath, status] of [
      ['GET', '/live/negotiate', 'negotiate', 405],
      ['PUT', '/live', '', 405],
      ['PATCH', '/live/negotiate', 'negotiate', 405],
      ['POST', '/live/sites', 'sites', 404],
      ['GET', '/live/../sites', '../sites', 404],
      ['GET', '/live/negotiate/x', 'negotiate/x', 404],
    ] as const) {
      const response = await run(method, path, hubPath, fake);
      expect(response.status, `${method} ${path}`).toBe(status);
    }
    expect((await run('PUT', '/live', '', fake)).headers.get('allow')).toBe('GET, POST, DELETE');
    expect(fake.seen).toEqual([]);
    const route = read(`${webSrc}/routes/(app)/live/[...path]/+server.ts`);
    expect(route).toContain('proxyLive(locals, request, url, params.path)');
    expect(route).toMatch(/export const fallback/u);
    expect(route).not.toMatch(/export const (?:GET|POST|PUT|PATCH|DELETE)\b/u);
  });

  test('the Server’s refusal stays a 401, and a Server that does not answer is a 502 without detail', async () => {
    const refused = upstream(() => problemResponse(401, 'unauthorized'));
    const response = await run('POST', '/live/negotiate', 'negotiate', refused, signedIn, { body: '' });
    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
    const down = upstream(() => Promise.reject(fetchFailed('ECONNREFUSED')));
    const unreachable = await run('POST', '/live/negotiate', 'negotiate', down, signedIn, { body: '' });
    expect(unreachable.status).toBe(502);
    expect(await unreachable.text()).toBe('');
  });

  test('a redirect of the Server is not followed, and a large body is refused', async () => {
    const fake = upstream(() => Response.json({}));
    await run('POST', '/live/negotiate', 'negotiate', fake, signedIn, { body: '' });
    const source = read(`${webSrc}/lib/server/live.ts`);
    expect(source).toContain("redirect: 'manual'");
    const large = await run('POST', '/live?id=t1', '', fake, signedIn, { body: 'x'.repeat(70_000) });
    expect(large.status).toBe(413);
    expect(fake.seen).toHaveLength(1);
  });
});

const tomatoes = '0199c1f0-5a00-7000-8000-0000000000a1';
const beans = '0199c1f0-5a00-7000-8000-0000000000b2';

function hint(resource: ReadModelResource, id = tomatoes): ReadModelChanged {
  return { resource, id, version: 7 };
}

describe('which page a hint is for', () => {
  const pages = {
    overview: '/garden',
    lot: `/garden/${tomatoes}`,
    thresholds: `/garden/${tomatoes}/thresholds`,
    calibrate: `/garden/${tomatoes}/calibrate`,
    alerts: '/alerts',
    devices: '/devices',
    members: '/members',
    settings: '/settings',
    notifications: '/settings/notifications',
    siteSettings: '/settings/site',
  };

  test.each([
    ['sites', Object.keys(pages)],
    ['lots', ['overview', 'lot', 'thresholds', 'calibrate']],
    ['readings', ['overview', 'lot']],
    ['alerts', ['alerts', 'overview']],
    // Story 7.2: a Device that turns silent or recovers changes the overview's Hub notice and tiles.
    ['devices', ['devices', 'overview']],
  ] as const)('%s is shown by %j', (resource, expected) => {
    const shown = Object.entries(pages)
      .filter(([, path]) => showsResource(path, hint(resource)))
      .map(([name]) => name);
    expect(shown.sort()).toEqual([...expected].sort());
  });

  test('a hint for another Lot does not concern Lot detail or its sub-pages, but it does the overview', () => {
    for (const resource of ['lots', 'readings'] as const) {
      expect(showsResource(pages.lot, hint(resource, beans))).toBe(false);
      expect(showsResource(pages.thresholds, hint(resource, beans))).toBe(false);
      expect(showsResource(pages.overview, hint(resource, beans))).toBe(true);
    }
    expect(showsResource(`${pages.lot}/`, hint('readings'))).toBe(true);
  });

  test('a resource this client does not know concerns no page', () => {
    expect(showsResource('/garden', { resource: 'members' as ReadModelResource, id: 'x' })).toBe(false);
  });
});

function deferred(): { promise: Promise<void>; resolve: () => void; reject: (error: Error) => void } {
  let resolve: () => void = () => undefined;
  let reject: (error: Error) => void = () => undefined;
  const promise = new Promise<void>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}

async function settle(): Promise<void> {
  for (let turn = 0; turn < 10; turn += 1) {
    await Promise.resolve();
  }
}

describe('refetch on a hint', () => {
  test('several hints during one refetch cause one more refetch, not one each', async () => {
    const runs: ReturnType<typeof deferred>[] = [];
    const refetcher = createRefetcher(() => {
      const next = deferred();
      runs.push(next);
      return next.promise;
    });
    refetcher.request();
    refetcher.request();
    refetcher.request();
    refetcher.request();
    expect(runs).toHaveLength(1);
    runs[0]?.resolve();
    await settle();
    expect(runs).toHaveLength(2);
    runs[1]?.resolve();
    await settle();
    expect(runs).toHaveLength(2);
    refetcher.request();
    expect(runs).toHaveLength(3);
  });

  test('a refetch that fails is quiet: the page keeps its data and the next hint refetches again', async () => {
    let count = 0;
    const refetcher = createRefetcher(() => {
      count += 1;
      return Promise.reject(new Error('offline'));
    });
    refetcher.request();
    await settle();
    refetcher.request();
    await settle();
    expect(count).toBe(2);
  });
});

class FakeHub implements Hub {
  started = 0;
  stopped = 0;
  refused = false;
  private closed: (() => void) | undefined;
  next: () => Promise<void> = () => Promise.resolve();
  readonly handlers: HubHandlers;

  constructor(handlers: HubHandlers) {
    this.handlers = handlers;
  }

  start(): Promise<void> {
    this.started += 1;
    return this.next();
  }

  stop(): Promise<void> {
    this.stopped += 1;
    return Promise.resolve();
  }

  onClose(listener: () => void): void {
    this.closed = listener;
  }

  wasRefused(): boolean {
    return this.refused;
  }

  drop(): void {
    this.closed?.();
  }
}

function harness(prepare: (hub: FakeHub, index: number) => void = () => undefined) {
  const hubs: FakeHub[] = [];
  const waits: { run: () => void; ms: number; cancelled: boolean }[] = [];
  const events: string[] = [];
  const clock = { now: 0 };
  const live = startLive({
    open: (handlers) => {
      const hub = new FakeHub(handlers);
      prepare(hub, hubs.length);
      hubs.push(hub);
      return hub;
    },
    onHint: (received) => events.push(`hint ${received.resource}`),
    onNotification: (received) => events.push(`notification ${received.title}`),
    onReconnected: () => events.push('reconnected'),
    schedule: (run, ms) => {
      const wait = { run, ms, cancelled: false };
      waits.push(wait);
      return () => {
        wait.cancelled = true;
      };
    },
    delays: [0, 2000, 5000],
    now: () => clock.now,
    heldAfter: 5000,
  });
  return { hubs, waits, events, live, clock };
}

describe('the live connection', () => {
  test('one connection is opened, and its messages reach the handlers', async () => {
    const { hubs, events, waits } = harness();
    await settle();
    expect(hubs).toHaveLength(1);
    expect(hubs[0]?.started).toBe(1);
    hubs[0]?.handlers.onHint(hint('lots'));
    hubs[0]?.handlers.onNotification({ kind: 'alert', siteId: 's', siteName: 'Home', collapseId: 'c', title: 'Tomatoes needs water', body: '' });
    expect(events).toEqual(['hint lots', 'notification Tomatoes needs water']);
    // Connecting for the first time is no reconnect: the page has just loaded.
    expect(waits).toEqual([]);
  });

  test('a lost connection is opened again with back-off, and the open page refetches once', async () => {
    const { hubs, events, waits, clock } = harness((hub, index) => {
      if (index === 1 || index === 2) {
        hub.next = () => Promise.reject(new Error('no answer'));
      }
    });
    await settle();
    hubs[0]?.drop();
    expect(waits.map((wait) => wait.ms)).toEqual([0]);
    waits[0]?.run();
    await settle();
    expect(waits.map((wait) => wait.ms)).toEqual([0, 2000]);
    waits[1]?.run();
    await settle();
    expect(waits.map((wait) => wait.ms)).toEqual([0, 2000, 5000]);
    expect(events).toEqual([]);
    waits[2]?.run();
    await settle();
    expect(hubs).toHaveLength(4);
    expect(events).toEqual(['reconnected']);
    // The back-off starts over after a connection that held.
    clock.now += 5000;
    hubs[3]?.drop();
    expect(waits.at(-1)?.ms).toBe(0);
  });

  test('a connection the Server closes right after it opened counts as a failed try: the back-off goes on', async () => {
    // The Server accepts the handshake and closes at once, every time (it cannot read the Memberships,
    // or the token is at its end).
    const { hubs, events, waits, clock } = harness();
    await settle();
    for (let round = 0; round < 4; round += 1) {
      clock.now += 100;
      hubs[round]?.drop();
      waits[round]?.run();
      await settle();
    }
    expect(waits.map((wait) => wait.ms)).toEqual([0, 2000, 5000, 5000]);
    expect(hubs).toHaveLength(5);
    // One refetch per connection that opened, at the pace of the back-off, never in a tight loop.
    expect(events).toEqual(['reconnected', 'reconnected', 'reconnected', 'reconnected']);
    // Just short of holding still counts as failed; once it has held, the back-off starts over.
    clock.now += 4999;
    hubs[4]?.drop();
    expect(waits.at(-1)?.ms).toBe(5000);
    waits.at(-1)?.run();
    await settle();
    clock.now += 5000;
    hubs[5]?.drop();
    expect(waits.at(-1)?.ms).toBe(0);
  });

  test('a Server that is not there at the start is retried quietly, at the longest delay for as long as it takes', async () => {
    const { hubs, events, waits } = harness((hub, index) => {
      if (index < 5) {
        hub.next = () => Promise.reject(new Error('no answer'));
      }
    });
    await settle();
    for (let attempt = 0; attempt < 5; attempt += 1) {
      waits[attempt]?.run();
      await settle();
    }
    expect(waits.map((wait) => wait.ms)).toEqual([0, 2000, 5000, 5000, 5000]);
    expect(hubs).toHaveLength(6);
    // The page loaded while nothing could tell it of a change, so it refetches once.
    expect(events).toEqual(['reconnected']);
  });

  test('a 401 means signed out: no further retries until the next navigation', async () => {
    const { hubs, waits, live, events } = harness((hub, index) => {
      if (index === 1) {
        hub.refused = true;
        hub.next = () => Promise.reject(new Error('401'));
      }
    });
    await settle();
    hubs[0]?.drop();
    waits[0]?.run();
    await settle();
    expect(hubs).toHaveLength(2);
    expect(waits).toHaveLength(1);
    // A navigation tries once more (the shell signs out when the session is gone).
    live.resume();
    await settle();
    expect(hubs).toHaveLength(3);
    expect(events).toEqual(['reconnected']);
    // While connected or waiting, a navigation opens nothing.
    live.resume();
    await settle();
    expect(hubs).toHaveLength(3);
  });

  test('stopping ends the connection and every pending retry', async () => {
    const { hubs, waits, live } = harness();
    await settle();
    hubs[0]?.drop();
    live.stop();
    expect(waits[0]?.cancelled).toBe(true);
    expect(hubs[0]?.stopped).toBe(1);
    waits[0]?.run();
    live.resume();
    await settle();
    expect(hubs).toHaveLength(1);
  });

  test('a message that is not the contract’s is dropped', () => {
    expect(isHint(hint('lots'))).toBe(true);
    expect(isHint({ resource: 'lots', id: '', version: 1 })).toBe(false);
    expect(isHint({ resource: 'lots', id: 'a' })).toBe(false);
    expect(isHint(null)).toBe(false);
    expect(isDelivered({ kind: 'alert', siteId: 's', siteName: '', collapseId: 'c', title: 'T', body: '' } satisfies NotificationDelivered)).toBe(true);
    expect(isDelivered({ kind: 'alert', siteId: 's', collapseId: 'c' })).toBe(false);
    expect(isDelivered('Tomatoes needs water')).toBe(false);
  });

  test('the hub is reached on the web origin only, over Server-Sent Events and long polling, with the contract’s names', () => {
    const source = read(`${webSrc}/lib/live/hub.ts`);
    expect(source).toContain('.withUrl(liveHubPath, {');
    expect(source).toContain('HttpTransportType.ServerSentEvents | signalr.HttpTransportType.LongPolling');
    expect(source).not.toMatch(/WebSockets|accessTokenFactory|https?:\/\//u);
    expect(source).toContain('liveMessages.notificationDelivered');
    expect(source).toContain('liveMessages.readModelChanged');
    expect(liveMessages).toEqual({ notificationDelivered: 'notification.delivered', readModelChanged: 'readmodel.changed' });
    // No client-to-server call exists.
    for (const file of ['hub.ts', 'live.ts', 'index.ts', 'rule.ts']) {
      expect(read(`${webSrc}/lib/live/${file}`), file).not.toMatch(/(?:connection|built)\.(?:invoke|send|stream)\(/u);
    }
  });

  test('one connection per tab: only the app layout mounts it, refetching through invalidateAll and resuming on navigation', () => {
    const layout = read(`${webSrc}/routes/(app)/+layout.svelte`);
    expect(layout).toMatch(/mountLive\(\{[\s\S]*path: \(\) => page\.url\.pathname,[\s\S]*refetch: \(\) => invalidateAll\(\),/u);
    expect(layout).toMatch(/afterNavigate\(\(\) => \{\s*live\?\.resume\(\);/u);
    expect(layout).toMatch(/return \(\) => \{\s*live\?\.stop\(\);/u);
    const mounts = ['routes', 'lib/components'].flatMap((folder) => read(`${webSrc}/${folder === 'routes' ? 'routes/(app)/+layout.svelte' : 'lib/components/AppShell.svelte'}`).match(/mountLive\(/gu) ?? []);
    expect(mounts).toHaveLength(1);
    // No notice, no indicator and no timer of the page's own.
    expect(layout).not.toMatch(/setInterval|setTimeout|connected|reconnect/iu);
    expect(repoRoot).not.toBe('');
  });
});
