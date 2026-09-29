import { describe, expect, it } from 'vitest';

import { loadTokens } from '../../../packages/design-tokens/scripts/lib/tokens.ts';
import {
  cssColorVar,
  cssRadiusVar,
  cssSpacingVar,
  kotlinConstName,
  memberName,
  spacingMemberName,
} from '../../../packages/design-tokens/scripts/lib/naming.ts';
import { toKotlinColor, toSwiftColor } from '../../../packages/design-tokens/scripts/lib/color.ts';
import { outputPaths } from '../../../packages/design-tokens/scripts/lib/paths.ts';
import { px, readDesignMd, readRepoFile } from './helpers.ts';

const design = readDesignMd();
const tokens = loadTokens();

const designColors = Object.keys(design.colors).filter((name) => !name.endsWith('-dark'));

describe('tokens.json matches the DESIGN.md frontmatter', () => {
  it.each(designColors)('colour %s has the same light and dark values', (name) => {
    const token = tokens.colors[name];
    expect(token, `colour ${name} is missing from tokens.json`).toBeDefined();
    const light = design.colors[name];
    const dark = design.colors[`${name}-dark`] ?? light;
    expect(token?.light, `colour ${name} light`).toBe(light);
    expect(token?.dark, `colour ${name} dark`).toBe(dark);
  });

  it('holds no colour that DESIGN.md does not define', () => {
    expect(Object.keys(tokens.colors).sort()).toEqual([...designColors].sort());
  });

  it('never has a -dark twin in DESIGN.md without an unsuffixed token', () => {
    const orphans = Object.keys(design.colors).filter(
      (name) => name.endsWith('-dark') && !(name.slice(0, -'-dark'.length) in design.colors),
    );
    expect(orphans).toEqual([]);
  });

  it.each(Object.keys(design.typography))('typography role %s has the same values', (name) => {
    const spec = design.typography[name];
    const role = tokens.typography[name];
    expect(role, `typography ${name} is missing from tokens.json`).toBeDefined();
    if (!spec || !role) return;
    expect(role.fontFamily, `typography ${name} fontFamily`).toBe(spec.fontFamily);
    expect(role.fontSize, `typography ${name} fontSize`).toBe(px(spec.fontSize));
    expect(role.fontWeight, `typography ${name} fontWeight`).toBe(Number(spec.fontWeight));
    expect(role.lineHeight, `typography ${name} lineHeight`).toBe(Number(spec.lineHeight));
    const letterSpacing = spec.letterSpacing ? Number(spec.letterSpacing.replace(/em$/, '')) : 0;
    expect(role.letterSpacingEm, `typography ${name} letterSpacing`).toBe(letterSpacing);
  });

  it('holds no typography role that DESIGN.md does not define', () => {
    expect(Object.keys(tokens.typography).sort()).toEqual(Object.keys(design.typography).sort());
  });

  it.each(Object.keys(design.spacing))('spacing %s has the same value', (name) => {
    expect(tokens.spacing[name], `spacing ${name}`).toBe(px(design.spacing[name] ?? ''));
  });

  it('holds no spacing token that DESIGN.md does not define', () => {
    expect(Object.keys(tokens.spacing).sort()).toEqual(Object.keys(design.spacing).sort());
  });

  it.each(Object.keys(design.rounded))('rounded %s has the same value, and it is 0', (name) => {
    expect(tokens.rounded[name], `rounded ${name}`).toBe(px(design.rounded[name] ?? ''));
    expect(tokens.rounded[name]).toBe(0);
  });

  it('holds no rounded token that DESIGN.md does not define', () => {
    expect(Object.keys(tokens.rounded).sort()).toEqual(Object.keys(design.rounded).sort());
  });

  it('has no shadow, elevation, blur or gradient tokens', () => {
    const groups = Object.keys(tokens).sort();
    expect(groups).toEqual(['colors', 'rounded', 'spacing', 'typography']);
    const names = [
      ...Object.keys(tokens.colors),
      ...Object.keys(tokens.spacing),
      ...Object.keys(tokens.typography),
    ];
    expect(names.filter((name) => /shadow|elevation|blur|gradient/.test(name))).toEqual([]);
  });
});

describe('every DESIGN.md token reaches every platform output', () => {
  const css = readRepoFile(outputPaths.tokensCss);
  const swiftColors = readRepoFile(outputPaths.swiftColors);
  const kotlinColors = readRepoFile(outputPaths.kotlinColors);
  const swiftSpacing = readRepoFile(outputPaths.swiftSpacing);
  const kotlinSpacing = readRepoFile(outputPaths.kotlinSpacing);
  const swiftRadius = readRepoFile(outputPaths.swiftRadius);
  const kotlinRadius = readRepoFile(outputPaths.kotlinRadius);
  const swiftTypography = readRepoFile(outputPaths.swiftTypography);
  const kotlinTypography = readRepoFile(outputPaths.kotlinTypography);

  it.each(designColors)('colour %s is in CSS, Swift and Kotlin with both values', (name) => {
    const light = design.colors[name] ?? '';
    const dark = design.colors[`${name}-dark`] ?? light;
    const [lightBlock, darkBlock, mediaBlock] = css.split(/^(?=\[data-theme="dark"\]|@media)/m);
    expect(lightBlock).toContain(`${cssColorVar(name)}: ${light};`);
    expect(darkBlock).toContain(`${cssColorVar(name)}: ${dark};`);
    expect(mediaBlock).toContain(`${cssColorVar(name)}: ${dark};`);
    expect(swiftColors).toContain(
      `public static let ${memberName(name)} = ThemedColor(light: ${toSwiftColor(light)}, dark: ${toSwiftColor(dark)})`,
    );
    expect(kotlinColors).toContain(`public val ${memberName(name)}: ThemedColor =`);
    expect(kotlinColors).toContain(
      `ThemedColor(light = ${toKotlinColor(light)}, dark = ${toKotlinColor(dark)})`,
    );
  });

  it.each(Object.keys(design.spacing))('spacing %s is in CSS, Swift and Kotlin', (name) => {
    const value = px(design.spacing[name] ?? '');
    expect(css).toContain(`${cssSpacingVar(name)}: ${String(value)}px;`);
    expect(swiftSpacing).toContain(`public static let ${spacingMemberName(name)}: Double = ${String(value)}`);
    expect(kotlinSpacing).toContain(
      `public const val ${kotlinConstName(spacingMemberName(name))}: Float = ${String(value)}f`,
    );
  });

  it.each(Object.keys(design.rounded))('rounded %s is in CSS, Swift and Kotlin', (name) => {
    expect(css).toContain(`${cssRadiusVar(name)}: 0px;`);
    const swiftName = memberName(name.toLowerCase());
    expect(swiftRadius).toMatch(new RegExp(`public static let \`?${swiftName}\`?: Double = 0\\b`));
    expect(kotlinRadius).toContain(`public const val ${kotlinConstName(swiftName)}: Float = 0f`);
  });

  it.each(Object.keys(design.typography))('typography role %s is in CSS, Swift and Kotlin', (name) => {
    expect(css).toContain(`--cf-type-${name}-font-size:`);
    expect(swiftTypography).toContain(`public static let ${memberName(name)} = TypeRole(`);
    expect(kotlinTypography).toContain(`public val ${memberName(name)}: TypeRole =`);
  });
});
