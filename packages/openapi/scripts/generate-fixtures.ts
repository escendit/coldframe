/**
 * Writes the golden Hub JSON fixtures (AD-10, AD-24) under fixtures/hub from coldframe.openapi.json.
 * The Hub's no_std JSON structs are hand-written; tests/rs/uplink decodes every response fixture and
 * encodes the request fixtures' values with them, so the structs cannot drift from the contract.
 *
 * - heartbeat-request-minimal.json, heartbeat-request-full.json: the `examples` of the
 *   deviceHeartbeat request schema with the fewest and the most properties.
 * - heartbeat-response-fraction.json, heartbeat-response-no-fraction.json: the `examples` of its 200
 *   response schema whose serverTime has, and has not, a fraction of a second.
 * - heartbeat-response-extra-field.json: the first response example plus a property the schema does
 *   not define, which the Hub must ignore (additive changes within a major, AD-10).
 * - schemas.json: both schemas' properties and required keys.
 *
 * With --check it writes nothing and exits 1 when a committed fixture is stale.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

const packageRoot = resolve(import.meta.dirname, '..');
const contractPath = join(packageRoot, 'coldframe.openapi.json');
const fixtureDir = 'fixtures/hub';

type Json = null | boolean | number | string | Json[] | { [key: string]: Json };
type JsonObject = Record<string, Json>;

function isObject(value: Json | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function object(value: Json | undefined, where: string): JsonObject {
  if (!isObject(value)) {
    throw new Error(`${where} is not an object`);
  }
  return value;
}

function array(value: Json | undefined, where: string): Json[] {
  if (!Array.isArray(value)) {
    throw new Error(`${where} is not an array`);
  }
  return value;
}

/** Follows a local `$ref` such as `#/components/schemas/HeartbeatRequest`. */
function resolveRef(contract: JsonObject, value: Json | undefined, where: string): JsonObject {
  const node = object(value, where);
  const ref = node.$ref;
  if (typeof ref !== 'string') {
    return node;
  }
  if (!ref.startsWith('#/')) {
    throw new Error(`${where}: only local references are supported, got ${ref}`);
  }
  let target: Json = contract;
  for (const part of ref.slice(2).split('/')) {
    target = object(target, ref)[part] ?? null;
  }
  return object(target, ref);
}

interface Schema {
  properties: string[];
  required: string[];
  examples: JsonObject[];
}

function schemaOf(node: JsonObject, where: string): Schema {
  const properties = Object.keys(object(node.properties, `${where}.properties`)).sort();
  const required = array(node.required, `${where}.required`).map((key) => {
    if (typeof key !== 'string' || !properties.includes(key)) {
      throw new Error(`${where}: required key ${JSON.stringify(key)} is not a property`);
    }
    return key;
  });
  const examples = array(node.examples, `${where}.examples`).map((example, index) =>
    object(example, `${where}.examples[${String(index)}]`),
  );
  for (const [index, example] of examples.entries()) {
    for (const key of Object.keys(example)) {
      if (!properties.includes(key)) {
        throw new Error(`${where}.examples[${String(index)}] has ${key}, which is not a property`);
      }
    }
    for (const key of required) {
      if (!(key in example)) {
        throw new Error(`${where}.examples[${String(index)}] lacks the required ${key}`);
      }
    }
  }
  return { properties, required: [...required].sort(), examples };
}

function heartbeatSchemas(contract: JsonObject): { request: Schema; response: Schema } {
  const paths = object(contract.paths, 'paths');
  const operation = object(object(paths['/device/heartbeat'], 'paths./device/heartbeat').post, 'deviceHeartbeat');
  if (operation.operationId !== 'deviceHeartbeat') {
    throw new Error('POST /device/heartbeat is not deviceHeartbeat');
  }
  const requestBody = object(operation.requestBody, 'deviceHeartbeat.requestBody');
  const requestJson = object(object(requestBody.content, 'requestBody.content')['application/json'], 'request JSON');
  const request = resolveRef(contract, requestJson.schema, 'request schema');
  const ok = object(object(operation.responses, 'deviceHeartbeat.responses')['200'], 'deviceHeartbeat 200');
  const responseJson = object(object(ok.content, '200.content')['application/json'], 'response JSON');
  const response = resolveRef(contract, responseJson.schema, 'response schema');
  return {
    request: schemaOf(request, 'HeartbeatRequest'),
    response: schemaOf(response, 'HeartbeatResponse'),
  };
}

function pick<T>(items: T[], score: (item: T) => number, best: 'min' | 'max', what: string): T {
  if (items.length === 0) {
    throw new Error(`no example for ${what}`);
  }
  const sorted = [...items].sort((a, b) => score(a) - score(b));
  const chosen = best === 'min' ? sorted[0] : sorted[sorted.length - 1];
  if (chosen === undefined) {
    throw new Error(`no example for ${what}`);
  }
  return chosen;
}

function serverTimeOf(example: JsonObject): string {
  const time = example.serverTime;
  if (typeof time !== 'string') {
    throw new Error('a response example has no string serverTime');
  }
  return time;
}

function render(value: Json): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export function renderFixtures(contract: JsonObject): Map<string, string> {
  const { request, response } = heartbeatSchemas(contract);
  const hasFraction = (example: JsonObject): boolean => serverTimeOf(example).includes('.');
  const withFraction = response.examples.filter(hasFraction);
  const withoutFraction = response.examples.filter((example) => !hasFraction(example));
  const first = response.examples[0];
  if (first === undefined) {
    throw new Error('HeartbeatResponse has no example');
  }
  const extraKey = 'coldframeFixtureUnknownField';
  if (response.properties.includes(extraKey)) {
    throw new Error(`${extraKey} became a real property; pick another name`);
  }
  const files = new Map<string, string>();
  const put = (name: string, value: Json): void => {
    files.set(`${fixtureDir}/${name}`, render(value));
  };
  put('heartbeat-request-minimal.json', pick(request.examples, (e) => Object.keys(e).length, 'min', 'a minimal request'));
  put('heartbeat-request-full.json', pick(request.examples, (e) => Object.keys(e).length, 'max', 'a full request'));
  put('heartbeat-response-fraction.json', pick(withFraction, () => 0, 'min', 'a response with a fraction'));
  put('heartbeat-response-no-fraction.json', pick(withoutFraction, () => 0, 'min', 'a response without a fraction'));
  put('heartbeat-response-extra-field.json', { ...first, [extraKey]: { nested: [1, 'two', true, null] } });
  put('schemas.json', {
    HeartbeatRequest: { properties: request.properties, required: request.required },
    HeartbeatResponse: { properties: response.properties, required: response.required },
  });
  return files;
}

const contract = object(JSON.parse(readFileSync(contractPath, 'utf8')) as Json, 'the contract');
const outputs = renderFixtures(contract);
const stale = [...outputs].filter(([path, content]) => {
  const file = join(packageRoot, path);
  return !existsSync(file) || readFileSync(file, 'utf8') !== content;
});

if (process.argv.includes('--check')) {
  if (stale.length > 0) {
    console.error('Golden Hub fixtures are stale:');
    for (const [path] of stale) {
      console.error(`  packages/openapi/${path}`);
    }
    console.error('Run `pnpm --filter @coldframe/openapi run generate` and commit the result.');
    process.exit(1);
  }
  console.log(`All ${String(outputs.size)} golden Hub fixtures are fresh.`);
} else {
  for (const [path, content] of stale) {
    const file = join(packageRoot, path);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, content);
    console.log(`wrote packages/openapi/${path}`);
  }
  console.log(`${String(stale.length)} of ${String(outputs.size)} fixtures changed.`);
}
