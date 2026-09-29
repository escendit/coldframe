import { join } from 'node:path';
import { describe, expect, test } from 'vitest';
import { messages } from '$lib/i18n';
import { read, webSrc } from './helpers.ts';

interface Glossary {
  readonly terms: readonly string[];
}

const glossary = JSON.parse(read(join(webSrc, 'lib/i18n/glossary.json'))) as Glossary;

/** Every string of the catalogue, plural forms included, with its key. */
function strings(): [string, string][] {
  const all: [string, string][] = [];
  for (const [key, entry] of Object.entries(messages())) {
    if (typeof entry === 'string') {
      all.push([key, entry]);
    } else {
      for (const [form, value] of Object.entries(entry)) {
        all.push([`${key}.${form}`, value]);
      }
    }
  }
  return all;
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}

describe('voice and glossary', () => {
  test('UX-DR130 no exclamation marks, emoji, "successfully", "OK", "fine" or "all good"', () => {
    for (const [key, value] of strings()) {
      expect(value, key).not.toMatch(/!/u);
      expect(value, key).not.toMatch(/\p{Extended_Pictographic}/u);
      expect(value, key).not.toMatch(/successfully/iu);
      expect(value, key).not.toMatch(/\bOK\b|\bokay\b/iu);
      expect(value, key).not.toMatch(/\bfine\b/iu);
      expect(value, key).not.toMatch(/all good/iu);
    }
  });

  test('UX-DR130 UX-DR124 uppercase comes from style, never from the string', () => {
    for (const [key, value] of strings()) {
      expect(value, key).not.toMatch(/\b\p{Lu}{2,}\b/u);
    }
  });

  test('UX-DR131 the glossary lists every term as a proper noun', () => {
    expect(glossary.terms).toEqual([
      'Site',
      'Lot',
      'Node',
      'Hub',
      'Sensor',
      'Reading',
      'Threshold',
      'Alert',
      'Reminder',
      'Notification Window',
      'Silence Window',
      'Pause',
      'Calibration',
    ]);
  });

  test('UX-DR131 glossary terms are capitalised wherever they appear', () => {
    for (const [key, value] of strings()) {
      for (const term of glossary.terms) {
        const pattern = new RegExp(`\\b${escape(term)}s?\\b`, 'giu');
        for (const match of value.matchAll(pattern)) {
          const found = match[0];
          const expected = term.split(' ').map((word) => word[0]);
          const actual = found.split(' ').map((word) => word[0]);
          expect(actual, `${key}: "${found}" must be written "${term}"`).toEqual(expected);
        }
      }
    }
  });
});
