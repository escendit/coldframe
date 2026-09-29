import { describe, expect, it } from 'vitest';

import { typography } from '@coldframe/design-tokens';
import { loadTokens } from '../../../packages/design-tokens/scripts/lib/tokens.ts';
import { IOS_TEXT_STYLE_SIZES, webFontSize } from '../../../packages/design-tokens/scripts/lib/typography.ts';
import { memberName } from '../../../packages/design-tokens/scripts/lib/naming.ts';
import { outputPaths } from '../../../packages/design-tokens/scripts/lib/paths.ts';
import { readRepoFile } from './helpers.ts';

const tokens = loadTokens();
const roles = Object.entries(tokens.typography);
const css = readRepoFile(outputPaths.tokensCss);
const swift = readRepoFile(outputPaths.swiftTypography);
const kotlin = readRepoFile(outputPaths.kotlinTypography);

/** DESIGN.md notes and Design Notes of story 1.3. */
const expectedTextStyle: Record<string, string> = {
  headline: 'largeTitle',
  title: 'title',
  section: 'title3',
  'hero-value': 'largeTitle',
  'tile-value': 'largeTitle',
  'tile-value-web': 'largeTitle',
  'tile-name': 'body',
  'status-label': 'footnote',
  body: 'subheadline',
  'body-lg': 'callout',
  helper: 'caption1',
  'meta-mono': 'caption1',
  button: 'subheadline',
  'step-counter': 'largeTitle',
};

describe('typography', () => {
  it('uses weights 300 and 400 only', () => {
    expect(new Set(roles.map(([, role]) => role.fontWeight))).toEqual(new Set([300, 400]));
  });

  it('marks status-label and button uppercase, and nothing else', () => {
    expect(roles.filter(([, role]) => role.uppercase).map(([name]) => name)).toEqual(['status-label', 'button']);
  });

  it('writes rem sizes with the 2× cap on roles of 36 px and more', () => {
    expect(webFontSize(14)).toBe('0.875rem');
    expect(webFontSize(13)).toBe('0.8125rem');
    expect(webFontSize(36)).toBe('min(2.25rem, 72px)');
    expect(webFontSize(72)).toBe('min(4.5rem, 144px)');
  });

  describe.each(roles)('%s', (name, role) => {
    const capped = role.fontSize >= 36;

    it('maps to the iOS Dynamic Type style', () => {
      expect(role.iosTextStyle).toBe(expectedTextStyle[name]);
      expect(IOS_TEXT_STYLE_SIZES[role.iosTextStyle]).toBeDefined();
      expect(swift).toContain(`static let ${memberName(name)} = TypeRole(`);
      const block = swift.slice(swift.indexOf(`static let ${memberName(name)} = TypeRole(`));
      const call = block.slice(0, block.indexOf(')\n') + 1);
      expect(call).toContain(`textStyle: .${role.iosTextStyle}`);
      expect(call).toContain(`size: ${String(role.fontSize)}`);
      expect(call).toContain(capped ? `maximumPointSize: ${String(role.fontSize * 2)}` : 'maximumPointSize: nil');
    });

    it('maps to an Android sp size', () => {
      const block = kotlin.slice(kotlin.indexOf(`public val ${memberName(name)}: TypeRole =`));
      const call = block.slice(0, block.indexOf('\n        )') + 1);
      expect(call).toContain(`sizeSp = ${String(role.fontSize)}f`);
      expect(call).toContain(capped ? 'maxFontScale = 2f' : 'maxFontScale = null');
    });

    it('maps to a web rem size in tokens.css and the TS export', () => {
      const size = webFontSize(role.fontSize);
      expect(size).toBe(
        capped ? `min(${String(role.fontSize / 16)}rem, ${String(role.fontSize * 2)}px)` : `${String(role.fontSize / 16)}rem`,
      );
      expect(css).toContain(`--cf-type-${name}-font-size: ${size};`);
      expect(css).toContain(`--cf-type-${name}-font-weight: ${String(role.fontWeight)};`);
      expect(css).toContain(`--cf-type-${name}-line-height: ${String(role.lineHeight)};`);
      expect(css).toContain(`--cf-type-${name}-letter-spacing: ${String(role.letterSpacingEm)}em;`);
      expect(css).toContain(`--cf-type-${name}-text-transform: ${role.uppercase ? 'uppercase' : 'none'};`);
      expect(typography[name as keyof typeof typography].cssFontSize).toBe(size);
    });
  });
});
