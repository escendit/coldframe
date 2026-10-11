import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { liveHubPath, liveMessages, type LiveMessageName, type LiveMessagePayloads, type NotificationDelivered, type ReadModelChanged } from '@coldframe/api-client';
import { generateLive, liveContractPath, livePath } from '@coldframe/api-client/generate';
import { describe, expect, test } from 'vitest';

type Json = null | boolean | number | string | Json[] | JsonObject;
interface JsonObject {
  [key: string]: Json;
}
interface Schema {
  readonly $ref?: string;
  readonly type?: string;
  readonly enum?: readonly Json[];
  readonly pattern?: string;
  readonly minLength?: number;
  readonly minimum?: number;
  readonly properties?: Readonly<Record<string, Schema>>;
  readonly required?: readonly string[];
  readonly additionalProperties?: boolean;
}

interface Contract {
  readonly channels: Record<string, { readonly address: string }>;
  readonly components: {
    readonly messages: Record<string, { readonly name: string; readonly payload: { readonly $ref: string } }>;
    readonly schemas: Record<string, Schema>;
  };
}

interface Fixture {
  readonly target: string;
  readonly message: Json;
}

const contract = JSON.parse(readFileSync(liveContractPath, 'utf8')) as Contract;
const fixturesFolder = join(dirname(liveContractPath), 'fixtures');
const fixtureNames = readdirSync(fixturesFolder).filter((name) => name.startsWith('live.') && name.endsWith('.json')).sort();

function fixture(name: string): Fixture {
  return JSON.parse(readFileSync(join(fixturesFolder, name), 'utf8')) as Fixture;
}

function resolve(schema: Schema): Schema {
  const reference = schema.$ref;
  if (reference === undefined) {
    return schema;
  }
  const resolved = contract.components.schemas[reference.slice(reference.lastIndexOf('/') + 1)];
  if (resolved === undefined) {
    throw new Error(`The contract has no schema ${reference}.`);
  }
  return resolve(resolved);
}

/**
 * Why `value` does not fit `schema`, as a list of paths; empty when it fits. It knows the JSON Schema
 * keywords the hub's messages use: type, required, properties, additionalProperties, enum, pattern,
 * minLength and minimum.
 */
function problems(value: Json, declared: Schema, path = '$'): string[] {
  const schema = resolve(declared);
  const found: string[] = [];
  const type = schema.type;
  const actual = value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
  if (type === 'integer' ? !(typeof value === 'number' && Number.isSafeInteger(value)) : type !== undefined && type !== actual) {
    return [`${path}: expected ${type ?? ''}, found ${actual}`];
  }
  const allowed = schema.enum;
  if (allowed !== undefined && !allowed.includes(value)) {
    found.push(`${path}: ${JSON.stringify(value)} is not one of ${JSON.stringify(allowed)}`);
  }
  if (typeof value === 'string') {
    const pattern = schema.pattern;
    if (pattern !== undefined && !new RegExp(pattern, 'u').test(value)) {
      found.push(`${path}: does not match ${pattern}`);
    }
    const minLength = schema.minLength;
    if (minLength !== undefined && value.length < minLength) {
      found.push(`${path}: shorter than ${String(minLength)}`);
    }
  }
  if (typeof value === 'number') {
    const minimum = schema.minimum;
    if (minimum !== undefined && value < minimum) {
      found.push(`${path}: below ${String(minimum)}`);
    }
  }
  if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
    const properties = schema.properties ?? {};
    for (const name of schema.required ?? []) {
      if (!(name in value)) {
        found.push(`${path}.${name}: required`);
      }
    }
    for (const [name, member] of Object.entries(value)) {
      const property = properties[name];
      if (property !== undefined) {
        found.push(...problems(member, property, `${path}.${name}`));
      } else if (schema.additionalProperties === false) {
        found.push(`${path}.${name}: not in the contract`);
      }
    }
  }
  return found;
}

function payloadSchemaOf(target: string): Schema {
  const message = Object.values(contract.components.messages).find((candidate) => candidate.name === target);
  if (message === undefined) {
    throw new Error(`The contract has no message named ${target}.`);
  }
  return { $ref: message.payload.$ref };
}

describe('the SignalR hub messages of packages/asyncapi (Story 6.6)', () => {
  test('AD-10 src/live.ts is what openapi-typescript generates from the current AsyncAPI contract', async () => {
    const committed = readFileSync(livePath, 'utf8');
    expect(committed, 'src/live.ts is stale: run pnpm --filter @coldframe/api-client generate').toBe(await generateLive());
  });

  test('the hub path and the two message names are the contract\'s, and there is no third message', () => {
    expect(liveHubPath).toBe(contract.channels.liveHub?.address);
    expect(liveMessages).toEqual({ notificationDelivered: 'notification.delivered', readModelChanged: 'readmodel.changed' });
  });

  test('there is one fixture per notification kind and one hint', () => {
    expect(fixtureNames).toEqual([
      'live.notification.alert.json',
      'live.notification.reminder.json',
      'live.notification.summary.json',
      'live.readmodel.changed.json',
    ]);
  });

  test.each(fixtureNames)('%s fits the schema of its message', (name) => {
    const { target, message } = fixture(name);
    expect(Object.values(liveMessages)).toContain(target);
    expect(problems(message, payloadSchemaOf(target))).toEqual([]);
  });

  test('a fixture that does not fit its message is found out', () => {
    const hint = fixture('live.readmodel.changed.json').message as JsonObject;
    const schema = payloadSchemaOf(liveMessages.readModelChanged);
    expect(problems({ ...hint, lotName: 'Tomatoes' }, schema)).toEqual(['$.lotName: not in the contract']);
    expect(problems({ ...hint, resource: 'members' }, schema)).toHaveLength(1);
    expect(problems({ ...hint, version: 0 }, schema)).toEqual(['$.version: below 1']);
    expect(problems({ resource: 'lots', id: 'a' }, schema)).toEqual(['$.version: required']);
    const untitled = Object.fromEntries(Object.entries(fixture('live.notification.alert.json').message as JsonObject).filter(([name]) => name !== 'title'));
    expect(problems(untitled, payloadSchemaOf(liveMessages.notificationDelivered))).toEqual(['$.title: required']);
  });

  test('the generated types name every field of the fixtures', () => {
    // Compile-time: a field renamed in the contract fails `pnpm -r typecheck` here.
    const alert = fixture('live.notification.alert.json').message as unknown as NotificationDelivered;
    const read: Required<NotificationDelivered> = {
      kind: alert.kind,
      siteId: alert.siteId,
      siteName: alert.siteName,
      lotId: alert.lotId ?? '',
      alertId: alert.alertId ?? '',
      collapseId: alert.collapseId,
      title: alert.title,
      body: alert.body,
    };
    expect(read).toEqual(alert);
    const hint = fixture('live.readmodel.changed.json').message as unknown as ReadModelChanged;
    const exact: { [Key in keyof ReadModelChanged]-?: ReadModelChanged[Key] } = { resource: hint.resource, id: hint.id, version: hint.version };
    expect(exact).toEqual(hint);
    const names: LiveMessageName[] = ['notification.delivered', 'readmodel.changed'];
    const payloads: { [Name in LiveMessageName]: LiveMessagePayloads[Name] } = { 'notification.delivered': alert, 'readmodel.changed': hint };
    expect(Object.keys(payloads)).toEqual(names);
  });
});
