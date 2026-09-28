import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { icons, iconNames } from '@coldframe/design-tokens';
import {
  formatPathData,
  ICON_NAMES,
  normaliseSvg,
  parsePathData,
} from '../../../packages/design-tokens/scripts/lib/icons.ts';
import { swiftCaseName, kotlinConstName, memberName } from '../../../packages/design-tokens/scripts/lib/naming.ts';
import { outputPaths } from '../../../packages/design-tokens/scripts/lib/paths.ts';
import { packageRoot, readRepoFile } from './helpers.ts';

/** UX-DR13, in the order the epic lists them. */
const uxDr13 = [
  'rain-drop',
  'checkmark--outline',
  'checkmark',
  'help',
  'tools',
  'pause--outline',
  'add',
  'cloud--offline',
  'overflow-menu--vertical',
  'chevron--down',
  'arrow--up',
  'arrow--down',
  'battery--low',
  'error--filled',
  'view',
  'in-progress',
  'time',
  'grid',
  'notification',
  'box',
  'settings',
];

const COLOUR = /fill|stroke|color|style|opacity|#[0-9a-f]{3,8}\b|rgb\(/i;

describe('Carbon icons', () => {
  it('are exactly the UX-DR13 list', () => {
    expect([...ICON_NAMES]).toEqual(uxDr13);
    expect([...iconNames]).toEqual(uxDr13);
    expect(Object.keys(icons)).toEqual(uxDr13);
  });

  it('are vendored with their licence and source', () => {
    const source = readFileSync(join(packageRoot, 'vendor/carbon-icons/SOURCE.md'), 'utf8');
    expect(source).toContain('@carbon/icons');
    expect(source).toContain('11.89.0');
    expect(source).toContain('Apache-2.0');
    expect(existsSync(join(packageRoot, 'vendor/carbon-icons/LICENSE'))).toBe(true);
  });

  describe.each(uxDr13)('%s', (name) => {
    const pathData = icons[name as keyof typeof icons];

    it('is vendored from svg/32', () => {
      expect(existsSync(join(packageRoot, `vendor/carbon-icons/svg/${name}.svg`))).toBe(true);
    });

    it('parses into drawable M/L/C/Z commands inside the 32×32 viewport', () => {
      const commands = parsePathData(pathData);
      expect(commands.length).toBeGreaterThan(1);
      expect(commands[0]?.type).toBe('M');
      expect(commands.some((command) => command.type === 'L' || command.type === 'C')).toBe(true);
      for (const command of commands) {
        expect(['M', 'L', 'C', 'Z']).toContain(command.type);
        for (const value of command.values) {
          expect(value).toBeGreaterThanOrEqual(0);
          expect(value).toBeLessThanOrEqual(32);
        }
      }
      expect(formatPathData(commands)).toBe(pathData);
    });

    it('is a web SVG with currentColor on the root and nowhere else', () => {
      const svg = readRepoFile(`${outputPaths.iconsDir}/${name}.svg`);
      const root = /<svg\b[^>]*>/.exec(svg)?.[0] ?? '';
      expect(root).toContain('viewBox="0 0 32 32"');
      expect(root).toContain('fill="currentColor"');
      expect(svg.match(/currentColor/g)).toHaveLength(1);
      const body = svg.slice(svg.indexOf(root) + root.length);
      expect(body).not.toMatch(COLOUR);
      expect(body).toContain(`<path d="${pathData}"/>`);
    });

    it('is in Swift and Kotlin as colourless path data', () => {
      const swift = readRepoFile(outputPaths.swiftIcons);
      const kotlin = readRepoFile(outputPaths.kotlinIcons);
      expect(swift).toContain(`case ${swiftCaseName(name)} = "${name}"`);
      expect(kotlin).toContain(`${kotlinConstName(memberName(name))}(`);
      expect(kotlin).toContain(`"${name}"`);
    });
  });

  it('keep Swift and Kotlin free of colour', () => {
    for (const path of [outputPaths.swiftIcons, outputPaths.kotlinIcons]) {
      expect(readRepoFile(path)).not.toMatch(/\bfill\b|stroke|#[0-9a-f]{6}\b|colou?r/i);
    }
  });
});

describe('the SVG normaliser', () => {
  const wrap = (body: string) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">${body}</svg>`;

  it('turns H, V, arcs, circles and transforms into absolute M/L/C/Z', () => {
    const commands = normaliseSvg('probe', wrap('<path d="M4 4h2v2H4z"/><circle cx="16" cy="16" r="2"/>'));
    expect(commands.map((command) => command.type).join('')).toMatch(/^MLLLZMC+Z$/);
    const rotated = normaliseSvg('probe', wrap('<path d="M5 15H13V17H5z" transform="rotate(90 9 16)"/>'));
    expect(formatPathData(rotated)).toBe('M 10 12 L 10 20 L 8 20 L 8 12 Z');
  });

  it('turns rect, ellipse and polygon into paths and Q into C', () => {
    expect(formatPathData(normaliseSvg('probe', wrap('<rect x="1" y="2" width="3" height="4"/>')))).toBe(
      'M 1 2 L 4 2 L 4 6 L 1 6 Z',
    );
    expect(formatPathData(normaliseSvg('probe', wrap('<polygon points="1,1 3,1 2,3"/>')))).toBe(
      'M 1 1 L 3 1 L 2 3 Z',
    );
    expect(normaliseSvg('probe', wrap('<ellipse cx="16" cy="16" rx="4" ry="2"/>'))[1]?.type).toBe('C');
    expect(formatPathData(normaliseSvg('probe', wrap('<path d="M0 0Q3 3 6 0"/>')))).toBe('M 0 0 C 2 2 4 2 6 0');
  });

  it('skips inner paths and transparent rectangles', () => {
    const commands = normaliseSvg(
      'probe',
      wrap(
        '<path fill="none" d="M1 1H2V2z" data-icon-path="inner-path"/>' +
          '<rect width="32" height="32" fill="none"/><path d="M4 4L5 5Z"/>',
      ),
    );
    expect(formatPathData(commands)).toBe('M 4 4 L 5 5 Z');
  });

  it.each([
    ['<line x1="0" y1="0" x2="4" y2="4"/>', /probe.*<line>/],
    ['<g><path d="M0 0L1 1Z"/></g>', /probe.*<g>/],
    ['<path d="M0 0L1 1Z" fill="#ff0000"/>', /probe.*fill.*<path>/],
    ['<path d="M0 0L1 1Z" stroke="red"/>', /probe.*stroke.*<path>/],
    ['<rect width="4" height="4" rx="1"/>', /probe.*rx.*<rect>/],
  ])('rejects %s, naming the icon and element', (body, message) => {
    expect(() => normaliseSvg('probe', wrap(body))).toThrow(message);
  });

  it('rejects a viewBox other than 0 0 32 32', () => {
    expect(() => normaliseSvg('probe', '<svg viewBox="0 0 16 16"><path d="M0 0L1 1Z"/></svg>')).toThrow(
      /probe.*viewBox/,
    );
  });
});
