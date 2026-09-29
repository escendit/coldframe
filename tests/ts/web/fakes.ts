import type { Cookies, Handle, RequestEvent } from '@sveltejs/kit';

type CookieOptions = Parameters<Cookies['set']>[2];

export interface CookieWrite {
  readonly name: string;
  readonly value: string;
  readonly options: Readonly<Partial<CookieOptions>>;
}

/** A cookie jar recording what the handle writes. */
export class FakeCookies implements Cookies {
  readonly jar = new Map<string, string>();
  readonly writes: CookieWrite[] = [];

  constructor(initial: Readonly<Record<string, string>> = {}) {
    for (const [name, value] of Object.entries(initial)) {
      this.jar.set(name, value);
    }
  }

  get(name: string): string | undefined {
    return this.jar.get(name);
  }

  getAll(): { name: string; value: string }[] {
    return [...this.jar].map(([name, value]) => ({ name, value }));
  }

  set(name: string, value: string, options: CookieOptions): void {
    this.jar.set(name, value);
    this.writes.push({ name, value, options });
  }

  delete(name: string, options: CookieOptions): void {
    this.jar.delete(name);
    this.writes.push({ name, value: '', options: { ...options, maxAge: 0 } });
  }

  serialize(name: string, value: string): string {
    return `${name}=${value}`;
  }
}

export function fakeEvent(path: string, cookies: Readonly<Record<string, string>> = {}): RequestEvent & { cookies: FakeCookies } {
  const url = new URL(path, 'http://localhost:5173');
  return {
    url,
    cookies: new FakeCookies(cookies),
    locals: {},
    request: new Request(url),
  } as unknown as RequestEvent & { cookies: FakeCookies };
}

export type Resolve = Parameters<Handle>[0]['resolve'];

/** A resolve that renders a page and records the event it saw. */
export function pageResolve(): Resolve & { calls: RequestEvent[] } {
  const calls: RequestEvent[] = [];
  const resolve = ((event: RequestEvent) => {
    calls.push(event);
    return Promise.resolve(new Response('page', { status: 200 }));
  }) as Resolve & { calls: RequestEvent[] };
  resolve.calls = calls;
  return resolve;
}

/** Thrown by fetch in Node when a connection fails; the real reason sits in `cause`. */
export function fetchFailed(code: string): TypeError {
  const cause = Object.assign(new Error(code), { code });
  return new TypeError('fetch failed', { cause });
}

/** One request the fake Coldframe Server received. */
export interface SeenRequest {
  readonly method: string;
  readonly path: string;
  readonly authorization: string | null;
  readonly key: string | null;
  readonly body: string;
}

/** A fake Coldframe Server: records every request and answers from `answer`. */
export function fakeServer(answer: (request: Request) => Response | Promise<Response>): { fetch: (request: Request) => Promise<Response>; seen: SeenRequest[] } {
  const seen: SeenRequest[] = [];
  return {
    seen,
    fetch: async (request) => {
      seen.push({
        method: request.method,
        path: new URL(request.url).pathname,
        authorization: request.headers.get('authorization'),
        key: request.headers.get('idempotency-key'),
        body: request.method === 'GET' || request.method === 'DELETE' ? '' : await request.clone().text(),
      });
      return answer(request);
    },
  };
}

export function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': status < 400 ? 'application/json' : 'application/problem+json' } });
}

export function problemResponse(status: number, slug: string): Response {
  return jsonResponse(status, { type: `urn:coldframe:problem:${slug}`, title: slug, status });
}
