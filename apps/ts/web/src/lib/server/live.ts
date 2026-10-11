/**
 * The web app's side of the SignalR hub (Story 6.6). The browser talks only to the web origin
 * (AD-14): `/live` and `/live/negotiate` are forwarded to the Server's hub with the session's
 * access token as `Authorization: Bearer`, and nothing else of the session. The hub speaks
 * Server-Sent Events and long polling, which are ordinary HTTP, so no WebSocket upgrade is needed.
 */
import { liveHubPath } from '@coldframe/api-client';
import { getConfig } from './runtime';
import { accessTokenOf, serverTimeoutMs } from './sites';

type Locals = Pick<App.Locals, 'session'>;

export interface LiveDependencies {
  readonly serverUrl?: URL;
  readonly fetch?: (request: Request) => Promise<Response>;
  /** The bound of every request but the stream; `serverTimeoutMs` unless a test says otherwise. */
  readonly timeoutMs?: number;
}

/** The hub's own paths below `/live` and the methods each takes: negotiate, then receive, send and close. */
const hubRoutes: Readonly<Record<string, readonly string[]>> = {
  '': ['GET', 'POST', 'DELETE'],
  negotiate: ['POST'],
};

/** The connection token SignalR hands out on negotiate, and the protocol version. Never an access token. */
const hubQuery = ['id', 'negotiateVersion'] as const;

/** `accept` picks Server-Sent Events over long polling on the Server. */
const requestHeaders = ['accept', 'content-type'] as const;

/** What a client sends is a handshake, a ping or a close: a few bytes. */
const maxBodyBytes = 64 * 1024;

function empty(status: number, headers: Record<string, string> = {}): Response {
  return new Response(null, { status, headers: { 'cache-control': 'no-store', ...headers } });
}

/** The upstream body, passed on chunk by chunk; cancelling it (the browser left) ends the upstream request. */
function relay(body: ReadableStream<Uint8Array>, gone: AbortController): ReadableStream<Uint8Array> {
  const reader = body.getReader();
  return new ReadableStream<Uint8Array>({
    async pull(controller) {
      try {
        const { done, value } = await reader.read();
        if (done) {
          controller.close();
        } else {
          controller.enqueue(value);
        }
      } catch (error) {
        controller.error(error);
      }
    },
    async cancel(reason) {
      gone.abort();
      await reader.cancel(reason).catch(() => undefined);
    },
  });
}

/**
 * Forwards one hub request. Without a session it answers 401 and the Server is never asked. The
 * Server's status and body are passed through, so its 401 stays a 401; the event stream has no
 * time limit and ends when the browser leaves.
 */
export async function proxyLive(locals: Locals, request: Request, url: URL, path: string, dependencies: LiveDependencies = {}): Promise<Response> {
  const accessToken = accessTokenOf(locals);
  if (accessToken === null) {
    return empty(401);
  }
  const methods = Object.hasOwn(hubRoutes, path) ? hubRoutes[path] : undefined;
  if (methods === undefined) {
    return empty(404);
  }
  const method = request.method;
  if (!methods.includes(method)) {
    return empty(405, { allow: methods.join(', ') });
  }

  const base = (dependencies.serverUrl ?? getConfig().serverUrl).href.replace(/\/+$/u, '');
  const target = new URL(`${base}${liveHubPath}${path === '' ? '' : `/${path}`}`);
  for (const name of hubQuery) {
    const value = url.searchParams.get(name);
    if (value !== null) {
      target.searchParams.set(name, value);
    }
  }

  const headers = new Headers({ authorization: `Bearer ${accessToken}` });
  for (const name of requestHeaders) {
    const value = request.headers.get(name);
    if (value !== null) {
      headers.set(name, value);
    }
  }

  let body: ArrayBuffer | undefined;
  if (method === 'POST') {
    body = await request.arrayBuffer();
    if (body.byteLength > maxBodyBytes) {
      return empty(413);
    }
  }

  const gone = new AbortController();
  if (request.signal.aborted) {
    gone.abort();
  } else {
    request.signal.addEventListener(
      'abort',
      () => {
        gone.abort();
      },
      { once: true },
    );
  }
  // Receiving waits for as long as the connection lives; everything else is bounded like any Server call.
  const signal = method === 'GET' ? gone.signal : AbortSignal.any([gone.signal, AbortSignal.timeout(dependencies.timeoutMs ?? serverTimeoutMs)]);
  const send = dependencies.fetch ?? ((upstream: Request) => fetch(upstream));

  let answer: Response;
  try {
    answer = await send(new Request(target, { method, headers, signal, redirect: 'manual', ...(body === undefined ? {} : { body }) }));
  } catch {
    return empty(502);
  }

  const passed: Record<string, string> = { 'cache-control': 'no-store' };
  const contentType = answer.headers.get('content-type');
  if (contentType !== null) {
    passed['content-type'] = contentType;
    if (contentType.startsWith('text/event-stream')) {
      // A reverse proxy in front of the web app must hand the events on as they come.
      passed['x-accel-buffering'] = 'no';
    }
  }
  const stream = answer.status === 204 || answer.status === 304 ? null : answer.body;
  return new Response(stream === null ? null : relay(stream, gone), { status: answer.status, headers: passed });
}
