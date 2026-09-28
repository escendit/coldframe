import { readFileSync } from 'node:fs';
import { createColdframeClient, type Site } from '@coldframe/api-client';
import { generateSchema, schemaPath } from '@coldframe/api-client/generate';
import { describe, expect, test } from 'vitest';

interface Seen {
  readonly method: string;
  readonly url: string;
  readonly headers: Headers;
  readonly body: string;
}

function recording(response: () => Response): { fetch: (request: Request) => Promise<Response>; seen: Seen[] } {
  const seen: Seen[] = [];
  return {
    seen,
    fetch: async (request: Request) => {
      seen.push({ method: request.method, url: request.url, headers: request.headers, body: await request.text() });
      return response();
    },
  };
}

function json(status: number, body: unknown, type = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': type } });
}

describe('@coldframe/api-client', () => {
  test('AD-10 src/schema.ts is what openapi-typescript generates from the current contract', async () => {
    const committed = readFileSync(schemaPath, 'utf8');
    expect(committed, 'src/schema.ts is stale: run pnpm --filter @coldframe/api-client generate').toBe(await generateSchema());
  });

  test('listSites sends the bearer token and returns the Server order', async () => {
    const sites: Site[] = [
      { id: '00000000-0000-4000-8000-00000000000b', name: 'B', role: 'Member' },
      { id: '00000000-0000-4000-8000-00000000000a', name: 'A', role: 'Owner' },
    ];
    const { fetch, seen } = recording(() => json(200, { sites }));
    const client = createColdframeClient({ baseUrl: 'https://server.example/', accessToken: 'token-1', fetch });
    const { data } = await client.GET('/sites');
    expect(data?.sites).toEqual(sites);
    expect(seen).toHaveLength(1);
    expect(seen[0]?.method).toBe('GET');
    expect(seen[0]?.url).toBe('https://server.example/sites');
    expect(seen[0]?.headers.get('authorization')).toBe('Bearer token-1');
  });

  test('createSite sends Authorization: Bearer, the Idempotency-Key and only {name}', async () => {
    const { fetch, seen } = recording(() => json(201, { id: '00000000-0000-4000-8000-00000000000a', name: 'Home', role: 'Owner' }));
    const client = createColdframeClient({ baseUrl: new URL('https://server.example'), accessToken: 'token-2', fetch });
    const { data, response } = await client.POST('/sites', {
      params: { header: { 'Idempotency-Key': 'key-1' } },
      body: { name: 'Home' },
    });
    expect(response.status).toBe(201);
    expect(data?.role).toBe('Owner');
    expect(seen[0]?.method).toBe('POST');
    expect(seen[0]?.headers.get('authorization')).toBe('Bearer token-2');
    expect(seen[0]?.headers.get('idempotency-key')).toBe('key-1');
    expect(JSON.parse(seen[0]?.body ?? '')).toEqual({ name: 'Home' });
  });

  test('a Problem Details answer arrives as the typed error', async () => {
    const { fetch } = recording(() =>
      json(422, { type: 'urn:coldframe:problem:idempotency-key-reused', title: 'Reused', status: 422 }, 'application/problem+json'),
    );
    const client = createColdframeClient({ baseUrl: 'https://server.example', accessToken: 't', fetch });
    const { error, response } = await client.POST('/sites', { params: { header: { 'Idempotency-Key': 'k' } }, body: { name: 'x' } });
    expect(response.status).toBe(422);
    expect(error?.type).toBe('urn:coldframe:problem:idempotency-key-reused');
  });
});
