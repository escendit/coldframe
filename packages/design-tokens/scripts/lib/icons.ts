import { readFileSync } from 'node:fs';

import svgpath from 'svgpath';

import { fromRoot, inputPaths, repoRoot } from './paths.ts';

/** UX-DR13: the Carbon icons Coldframe uses, taken from `@carbon/icons` `svg/32/`. */
export const ICON_NAMES = [
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
] as const;

export type IconName = (typeof ICON_NAMES)[number];

export const VIEWPORT_SIZE = 32;

/** One absolute drawing command; `values` are x/y pairs (M, L: 2, C: 6, Z: 0). */
export interface PathCommand {
  type: 'M' | 'L' | 'C' | 'Z';
  values: number[];
}

const ARITY: Record<PathCommand['type'], number> = { M: 2, L: 2, C: 6, Z: 0 };

/** Geometry attributes per element; anything else fails generation. */
const GEOMETRY: Record<string, readonly string[]> = {
  path: ['d'],
  circle: ['cx', 'cy', 'r'],
  ellipse: ['cx', 'cy', 'rx', 'ry'],
  rect: ['x', 'y', 'width', 'height', 'rx', 'ry'],
  polygon: ['points'],
};

/** Attributes that never change geometry or colour. */
const NEUTRAL = ['transform', 'id', 'data-name'];

interface Element {
  name: string;
  attributes: Map<string, string>;
}

function fail(icon: string, message: string): never {
  throw new Error(`icon ${icon}: ${message}`);
}

function parseAttributes(icon: string, source: string): Map<string, string> {
  const attributes = new Map<string, string>();
  const rest = source.replace(/\s*([\w:-]+)\s*=\s*"([^"]*)"/g, (_match, name: string, value: string) => {
    attributes.set(name, value);
    return '';
  });
  if (rest.trim() !== '') {
    fail(icon, `cannot read attributes "${rest.trim()}"`);
  }
  return attributes;
}

/** Splits a Carbon SVG into its root and its flat list of child elements. */
function parseSvg(icon: string, svg: string): { root: Element; children: Element[] } {
  const tag = /\s*<(\/?)([A-Za-z][\w:-]*)((?:\s+[\w:-]+\s*=\s*"[^"]*")*)\s*(\/?)>/y;
  const elements: { element: Element; closing: boolean; selfClosing: boolean }[] = [];
  let index = 0;
  const source = svg.replace(/^\s*<\?xml[^>]*\?>/, '');
  while (index < source.length) {
    if (source.slice(index).trim() === '') break;
    tag.lastIndex = index;
    const match = tag.exec(source);
    if (!match) {
      fail(icon, `unexpected content "${source.slice(index, index + 20).trim()}"`);
    }
    const [whole, closing, name = '', attributes = '', selfClosing] = match;
    elements.push({
      element: { name, attributes: parseAttributes(icon, attributes) },
      closing: closing === '/',
      selfClosing: selfClosing === '/',
    });
    index += whole.length;
  }
  const first = elements[0];
  const last = elements[elements.length - 1];
  if (first?.element.name !== 'svg' || first.closing || last?.element.name !== 'svg' || !last.closing) {
    fail(icon, 'expected one <svg> root');
  }
  const children = elements.slice(1, -1);
  for (const child of children) {
    if (!child.selfClosing || child.closing) {
      fail(icon, `unsupported element <${child.element.name}> (only flat, self-closing shapes are supported)`);
    }
  }
  return { root: first.element, children: children.map((child) => child.element) };
}

function isSkipped(element: Element): boolean {
  if (element.attributes.get('data-icon-path') === 'inner-path') {
    return true;
  }
  const fill = element.attributes.get('fill');
  const style = element.attributes.get('style');
  const transparent = fill === 'none' || (style !== undefined && /^\s*fill\s*:\s*none\s*;?\s*$/.test(style));
  return element.name === 'rect' && transparent;
}

function numberAttribute(icon: string, element: Element, name: string, fallback?: number): number {
  const raw = element.attributes.get(name);
  if (raw === undefined) {
    if (fallback !== undefined) return fallback;
    fail(icon, `missing "${name}" on <${element.name}>`);
  }
  const value = Number(raw);
  if (!Number.isFinite(value)) {
    fail(icon, `"${name}" on <${element.name}> is not a number`);
  }
  return value;
}

/** The element's outline as SVG path data. */
function toPathData(icon: string, element: Element): string {
  const n = (name: string, fallback?: number) => numberAttribute(icon, element, name, fallback);
  switch (element.name) {
    case 'path':
      return element.attributes.get('d') ?? fail(icon, 'missing "d" on <path>');
    case 'circle':
    case 'ellipse': {
      const cx = n('cx', 0);
      const cy = n('cy', 0);
      const rx = element.name === 'circle' ? n('r') : n('rx');
      const ry = element.name === 'circle' ? rx : n('ry');
      return `M${String(cx - rx)} ${String(cy)}A${String(rx)} ${String(ry)} 0 1 0 ${String(cx + rx)} ${String(cy)}A${String(rx)} ${String(ry)} 0 1 0 ${String(cx - rx)} ${String(cy)}Z`;
    }
    case 'rect': {
      if (n('rx', 0) !== 0 || n('ry', 0) !== 0) {
        fail(icon, 'unsupported attribute "rx"/"ry" on <rect> (rounded rectangles)');
      }
      const x = n('x', 0);
      const y = n('y', 0);
      const right = x + n('width');
      const bottom = y + n('height');
      return `M${String(x)} ${String(y)}H${String(right)}V${String(bottom)}H${String(x)}Z`;
    }
    case 'polygon': {
      const values = (element.attributes.get('points') ?? '').trim().split(/[\s,]+/).map(Number);
      if (values.length < 4 || values.length % 2 !== 0 || values.some((value) => !Number.isFinite(value))) {
        fail(icon, 'cannot read "points" on <polygon>');
      }
      const points: string[] = [];
      for (let index = 0; index < values.length; index += 2) {
        points.push(`${String(values[index])} ${String(values[index + 1])}`);
      }
      return `M${points.join('L')}Z`;
    }
    default:
      return fail(icon, `unsupported element <${element.name}>`);
  }
}

/** Absolute M/L/C/Z: arcs become cubics, H/V become L, S/Q/T become C, the transform is applied. */
function toCommands(icon: string, pathData: string, transform: string | undefined): PathCommand[] {
  let path = svgpath(pathData);
  if (transform) {
    path = path.transform(transform);
  }
  const commands: PathCommand[] = [];
  path
    .abs()
    .unarc()
    .unshort()
    .iterate((segment, _index, x, y) => {
      const [type, ...args] = segment;
      switch (type) {
        case 'M':
        case 'L':
        case 'C':
          commands.push({ type, values: args });
          break;
        case 'H':
          commands.push({ type: 'L', values: [args[0] ?? x, y] });
          break;
        case 'V':
          commands.push({ type: 'L', values: [x, args[0] ?? y] });
          break;
        case 'Q': {
          const [qx = 0, qy = 0, ex = 0, ey = 0] = args;
          commands.push({
            type: 'C',
            values: [x + (2 / 3) * (qx - x), y + (2 / 3) * (qy - y), ex + (2 / 3) * (qx - ex), ey + (2 / 3) * (qy - ey), ex, ey],
          });
          break;
        }
        case 'Z':
        case 'z':
          commands.push({ type: 'Z', values: [] });
          break;
        default:
          fail(icon, `cannot normalise path command "${type}"`);
      }
    });
  return commands.map((command) => ({ type: command.type, values: command.values.map(round) }));
}

function round(value: number): number {
  const rounded = Math.round(value * 1000) / 1000;
  return Object.is(rounded, -0) ? 0 : rounded;
}

/**
 * Normalises one Carbon SVG into a single absolute path of M/L/C/Z commands, geometry only.
 * Inner paths and transparent rectangles are skipped; anything else it cannot convert fails.
 */
export function normaliseSvg(icon: string, svg: string): PathCommand[] {
  const { root, children } = parseSvg(icon, svg);
  for (const name of root.attributes.keys()) {
    if (name !== 'xmlns' && name !== 'viewBox' && name !== 'id') {
      fail(icon, `unsupported attribute "${name}" on <svg>`);
    }
  }
  if (root.attributes.get('viewBox') !== `0 0 ${String(VIEWPORT_SIZE)} ${String(VIEWPORT_SIZE)}`) {
    fail(icon, `viewBox must be "0 0 32 32", found "${String(root.attributes.get('viewBox'))}"`);
  }
  const commands: PathCommand[] = [];
  for (const element of children) {
    if (isSkipped(element)) continue;
    const allowed = GEOMETRY[element.name];
    if (!allowed) {
      fail(icon, `unsupported element <${element.name}>`);
    }
    for (const name of element.attributes.keys()) {
      if (!allowed.includes(name) && !NEUTRAL.includes(name)) {
        fail(icon, `unsupported attribute "${name}" on <${element.name}>`);
      }
    }
    commands.push(...toCommands(icon, toPathData(icon, element), element.attributes.get('transform')));
  }
  if (commands.length === 0) {
    fail(icon, 'no drawable geometry');
  }
  return commands;
}

/** `M 16 24 L 16 22 C … Z`: single spaces between command letters and numbers. */
export function formatPathData(commands: readonly PathCommand[]): string {
  return commands.map((command) => [command.type, ...command.values.map(String)].join(' ')).join(' ');
}

/** The inverse of `formatPathData`; the same grammar the Swift and Kotlin parsers accept. */
export function parsePathData(data: string): PathCommand[] {
  const tokens = data.trim().split(/\s+/);
  const commands: PathCommand[] = [];
  let index = 0;
  while (index < tokens.length) {
    const type = tokens[index++];
    if (type !== 'M' && type !== 'L' && type !== 'C' && type !== 'Z') {
      throw new Error(`unexpected path token "${String(type)}"`);
    }
    const values = tokens.slice(index, index + ARITY[type]).map(Number);
    if (values.length !== ARITY[type] || values.some((value) => !Number.isFinite(value))) {
      throw new Error(`command ${type} needs ${String(ARITY[type])} numbers`);
    }
    index += ARITY[type];
    commands.push({ type, values });
  }
  return commands;
}

/** Path data of every UX-DR13 icon, read from the vendored SVGs. */
export function loadIcons(root = repoRoot): Record<IconName, string> {
  const entries = ICON_NAMES.map((name) => {
    const svg = readFileSync(fromRoot(`${inputPaths.iconsDir}/${name}.svg`, root), 'utf8');
    return [name, formatPathData(normaliseSvg(name, svg))] as const;
  });
  return Object.fromEntries(entries) as Record<IconName, string>;
}
