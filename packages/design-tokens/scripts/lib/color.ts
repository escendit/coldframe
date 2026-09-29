export interface Rgba {
  r: number;
  g: number;
  b: number;
  /** 0–255 */
  a: number;
}

/** Parses `#RRGGBB` or `#RRGGBBAA`. */
export function parseHex(hex: string): Rgba {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(hex);
  if (!match?.[1]) {
    throw new Error(`not a #RRGGBB or #RRGGBBAA colour: ${hex}`);
  }
  const rgb = match[1];
  return {
    r: parseInt(rgb.slice(0, 2), 16),
    g: parseInt(rgb.slice(2, 4), 16),
    b: parseInt(rgb.slice(4, 6), 16),
    a: match[2] ? parseInt(match[2], 16) : 255,
  };
}

function hex2(value: number): string {
  return value.toString(16).toUpperCase().padStart(2, '0');
}

/** Swift: `0xRRGGBBAA` as a `UInt32`, grouped `0xRRGG_BBAA` as swift-format requires. */
export function toSwiftColor(hex: string): string {
  const { r, g, b, a } = parseHex(hex);
  return `0x${hex2(r)}${hex2(g)}_${hex2(b)}${hex2(a)}`;
}

/** Kotlin: Android `0xAARRGGBB` as a `Long`. */
export function toKotlinColor(hex: string): string {
  const { r, g, b, a } = parseHex(hex);
  return `0x${hex2(a)}${hex2(r)}${hex2(g)}${hex2(b)}L`;
}

/** `#RRGGBB` or, when not opaque, `#RRGGBBAA`, upper case. */
export function formatHex({ r, g, b, a }: Rgba): string {
  return `#${hex2(r)}${hex2(g)}${hex2(b)}${a === 255 ? '' : hex2(a)}`;
}
