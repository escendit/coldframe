import { readFileSync } from 'node:fs';
import { createColdframeClient, type Lot, type Site } from '@coldframe/api-client';
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

  test('the Lot operations use the contract paths and methods; removeLot answers 204 without a body', async () => {
    const site = '00000000-0000-4000-8000-00000000000a';
    const lot: Lot = { id: '0192a000-0000-7000-8000-000000000011', name: 'Tomatoes', status: 'noNode' };
    const answers = [json(200, { lots: [lot] }), json(201, lot), json(200, { ...lot, name: 'Beans' }), new Response(null, { status: 204 }), json(200, { id: site, name: 'Home garden', role: 'Owner' })];
    const { fetch, seen } = recording(() => answers.shift() ?? json(500, {}));
    const client = createColdframeClient({ baseUrl: 'https://server.example', accessToken: 't', fetch });
    const path = { siteId: site };
    expect((await client.GET('/sites/{siteId}/lots', { params: { path } })).data?.lots).toEqual([lot]);
    expect((await client.POST('/sites/{siteId}/lots', { params: { path, header: { 'Idempotency-Key': 'k' } }, body: { name: 'Tomatoes' } })).data).toEqual(lot);
    expect((await client.PATCH('/sites/{siteId}/lots/{lotId}', { params: { path: { ...path, lotId: lot.id } }, body: { name: 'Beans' } })).data?.name).toBe('Beans');
    const removed = await client.DELETE('/sites/{siteId}/lots/{lotId}', { params: { path: { ...path, lotId: lot.id } } });
    expect(removed.response.status).toBe(204);
    expect((await client.PATCH('/sites/{siteId}', { params: { path }, body: { name: 'Home garden' } })).data?.name).toBe('Home garden');
    expect(seen.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      `GET /sites/${site}/lots`,
      `POST /sites/${site}/lots`,
      `PATCH /sites/${site}/lots/${lot.id}`,
      `DELETE /sites/${site}/lots/${lot.id}`,
      `PATCH /sites/${site}`,
    ]);
    expect(seen[1]?.headers.get('idempotency-key')).toBe('k');
  });
});
