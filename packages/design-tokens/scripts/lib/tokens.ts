import { readFileSync } from 'node:fs';

import { fromRoot, inputPaths, repoRoot } from './paths.ts';

export type Theme = 'light' | 'dark';

/** A CSS custom property of the vendored Escendit theme, for both themes or per theme. */
export type DsMapping = string | Partial<Record<Theme, string>>;

export interface ColorToken {
  /** `#RRGGBB` or `#RRGGBBAA`, as written in DESIGN.md. */
  light: string;
  dark: string;
  ds?: DsMapping;
}

export type IOSTextStyle =
  | 'largeTitle'
  | 'title'
  | 'title2'
  | 'title3'
  | 'headline'
  | 'body'
  | 'callout'
  | 'subheadline'
  | 'footnote'
  | 'caption1'
  | 'caption2';

export interface TypeRoleToken {
  fontFamily: 'Ubuntu' | 'Ubuntu Condensed' | 'Ubuntu Mono';
  /** Base size in px (web), pt (iOS) and sp (Android). */
  fontSize: number;
  fontWeight: 300 | 400;
  /** Unitless multiple of the font size. */
  lineHeight: number;
  letterSpacingEm: number;
  /** Uppercase is applied by style, never baked into strings. */
  uppercase: boolean;
  iosTextStyle: IOSTextStyle;
}

export interface TokensFile {
  colors: Record<string, ColorToken>;
  typography: Record<string, TypeRoleToken>;
  /** px */
  spacing: Record<string, number>;
  /** px */
  rounded: Record<string, number>;
}

export type ContrastKind = 'text' | 'non-text';

export interface ContrastPair {
  label: string;
  foreground: string;
  background: string;
  kind: ContrastKind;
  /** The ratios DESIGN.md › Colors publishes. */
  published: Record<Theme, number>;
}

export const THEMES: readonly Theme[] = ['light', 'dark'];

export function loadTokens(root = repoRoot): TokensFile {
  return JSON.parse(readFileSync(fromRoot(inputPaths.tokens, root), 'utf8')) as TokensFile;
}

export function loadContrastPairs(root = repoRoot): ContrastPair[] {
  const file = JSON.parse(readFileSync(fromRoot(inputPaths.contrast, root), 'utf8')) as {
    pairs: ContrastPair[];
  };
  return file.pairs;
}
