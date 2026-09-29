import { describe, expect, test } from 'vitest';
import { parse } from 'svelte/compiler';
import { filesUnder, read, rel, webSrc } from './helpers.ts';

/** Attributes whose literal value would be copy a user reads or hears. */
const copyAttributes = new Set(['aria-label', 'title', 'alt', 'placeholder', 'label', 'aria-description', 'aria-roledescription']);

const letters = /\p{L}/u;

interface Node {
  readonly type?: string;
  readonly [key: string]: unknown;
}

interface Attribute extends Node {
  readonly name?: string;
  readonly value?: unknown;
}

function isNode(value: unknown): value is Node {
  return typeof value === 'object' && value !== null;
}

/** Finds letter-bearing text and literal copy attributes in a parsed template. */
function findCopy(node: unknown, found: string[]): void {
  if (Array.isArray(node)) {
    for (const child of node) {
      findCopy(child, found);
    }
    return;
  }
  if (!isNode(node)) {
    return;
  }
  if (node.type === 'Text' && typeof node.data === 'string' && letters.test(node.data)) {
    found.push(`text "${node.data.trim()}"`);
  }
  const attributes = node.attributes;
  if (Array.isArray(attributes)) {
    for (const attribute of attributes as Attribute[]) {
      if (attribute.type !== 'Attribute' || attribute.name === undefined || !copyAttributes.has(attribute.name)) {
        continue;
      }
      const parts = Array.isArray(attribute.value) ? (attribute.value as Node[]) : [];
      for (const part of parts) {
        if (part.type === 'Text' && typeof part.data === 'string' && letters.test(part.data)) {
          found.push(`${attribute.name}="${part.data}"`);
        }
      }
    }
  }
  for (const [key, value] of Object.entries(node)) {
    if (key !== 'attributes' && typeof value === 'object') {
      findCopy(value, found);
    }
  }
}

const svelteFiles = filesUnder(webSrc, ['.svelte']);

describe('no hard-coded copy', () => {
  test('UX-DR124 the web app has Svelte files to check', () => {
    expect(svelteFiles.length).toBeGreaterThan(10);
  });

  test.each(svelteFiles.map((file) => [rel(file), file]))('UX-DR124 %s takes every string from the catalogue', (_name, file) => {
    const ast = parse(read(file), { modern: true });
    const found: string[] = [];
    findCopy(ast.fragment, found);
    expect(found).toEqual([]);
  });

  test('UX-DR124 the checker catches hard-coded text and attributes', () => {
    const ast = parse('<button aria-label="Close">Save</button><img alt="{x}" /><p>{t("a")} · 12</p>', { modern: true });
    const found: string[] = [];
    findCopy(ast.fragment, found);
    expect(found).toEqual(['aria-label="Close"', 'text "Save"']);
  });
});
