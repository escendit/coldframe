/** `text-primary` → `textPrimary`, `layer-01` → `layer01`, `checkmark--outline` → `checkmarkOutline`. */
export function memberName(name: string): string {
  return name
    .split(/-+/)
    .filter((part) => part.length > 0)
    .map((part, index) => (index === 0 ? part : part.charAt(0).toUpperCase() + part.slice(1)))
    .join('');
}

/** `textPrimary` → `TEXT_PRIMARY`, `step1` → `STEP_1`. */
export function kotlinConstName(camel: string): string {
  return camel
    .replace(/([a-z])([A-Z0-9])/g, '$1_$2')
    .replace(/([0-9])([A-Za-z])/g, '$1_$2')
    .toUpperCase();
}

/** Spacing keys `'1'`…`'13'` become `step1`…`step13`; named keys become camel case. */
export function spacingMemberName(name: string): string {
  return /^\d+$/.test(name) ? `step${name}` : memberName(name);
}

/** Radius keys `none` and `DEFAULT` become `none` and `default`. */
export function radiusMemberName(name: string): string {
  return memberName(name.toLowerCase());
}

export function swiftCaseName(iconName: string): string {
  return memberName(iconName);
}

export function cssColorVar(name: string): string {
  return `--cf-color-${name}`;
}

export function cssSpacingVar(name: string): string {
  return `--cf-spacing-${name}`;
}

export function cssRadiusVar(name: string): string {
  return `--cf-radius-${name.toLowerCase()}`;
}
