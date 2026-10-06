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

/**
 * "OK" is the name of the `ok` Lot status and appears nowhere else: only in keys with an `ok`
 * segment (the tile label, its stale and spoken forms, and the count of OK Lots).
 */
function namesOkStatus(key: string): boolean {
  return /\.ok(?:\.|$)/u.test(key);
}

describe('voice and glossary', () => {
  test('UX-DR130 no exclamation marks, emoji, "successfully", "OK", "fine" or "all good"', () => {
    for (const [key, value] of strings()) {
      expect(value, key).not.toMatch(/!/u);
      expect(value, key).not.toMatch(/\p{Extended_Pictographic}/u);
      expect(value, key).not.toMatch(/successfully/iu);
      expect(value, key).not.toMatch(/\bokay\b/iu);
      if (!namesOkStatus(key)) {
        expect(value, key).not.toMatch(/\bOK\b/iu);
      }
      expect(value, key).not.toMatch(/\bfine\b/iu);
      expect(value, key).not.toMatch(/all good/iu);
    }
  });

  test('UX-DR130 "OK" names the ok status only: its tile label, its stale and spoken forms, and its count', () => {
    const allowed = strings()
      .filter(([, value]) => /\bOK\b/iu.test(value))
      .map(([key]) => key);
    expect(allowed).toEqual(['garden.count.ok', 'lotTile.label.ok', 'lotTile.was.ok', 'lotTile.spoken.ok', 'lotTile.spokenWas.ok']);
    for (const [key, value] of strings().filter(([name]) => namesOkStatus(name))) {
      expect(value, key).toMatch(/\bOK$/u);
    }
  });

  test('UX-DR130 UX-DR124 uppercase comes from style, never from the string', () => {
    for (const [key, value] of strings()) {
      // "OK" is a word written in capitals, not a styled label; it is allowed only where the test above allows it.
      const checked = namesOkStatus(key) ? value.replace(/\bOK\b/gu, '') : value;
      expect(checked, key).not.toMatch(/\b\p{Lu}{2,}\b/u);
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
