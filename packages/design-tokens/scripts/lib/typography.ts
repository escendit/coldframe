import type { IOSTextStyle, TypeRoleToken } from './tokens.ts';

/** Roles at or above this base size are capped at 2× on every platform. */
export const CAP_THRESHOLD_PX = 36;
export const MAX_SCALE = 2;

/** Default point sizes of the iOS Dynamic Type text styles (Large, the default setting). */
export const IOS_TEXT_STYLE_SIZES: Record<IOSTextStyle, number> = {
  largeTitle: 34,
  title: 28,
  title2: 22,
  title3: 20,
  headline: 17,
  body: 17,
  callout: 16,
  subheadline: 15,
  footnote: 13,
  caption1: 12,
  caption2: 11,
};

export function isCapped(fontSize: number): boolean {
  return fontSize >= CAP_THRESHOLD_PX;
}

/** px → rem (px / 16); roles of 36 px and more are capped at 2× their base size. */
export function webFontSize(fontSize: number): string {
  const rem = `${String(fontSize / 16)}rem`;
  return isCapped(fontSize) ? `min(${rem}, ${String(fontSize * MAX_SCALE)}px)` : rem;
}

const FONT_STACKS: Record<TypeRoleToken['fontFamily'], string> = {
  Ubuntu: "'Ubuntu', system-ui, sans-serif",
  'Ubuntu Condensed': "'Ubuntu Condensed', 'Ubuntu', system-ui, sans-serif",
  'Ubuntu Mono': "'Ubuntu Mono', ui-monospace, monospace",
};

export function fontStack(family: TypeRoleToken['fontFamily']): string {
  return FONT_STACKS[family];
}
