import en from './en.json';

type Catalogue = typeof en;

/** Every key of the message catalogue. */
export type MessageKey = keyof Catalogue;

/** Keys whose entry is a CLDR plural set (`{ one, other }`). */
export type PluralKey = { [K in MessageKey]: Catalogue[K] extends string ? never : K }[MessageKey];

export type MessageParams = Readonly<Record<string, string | number>>;

export type PluralForms = Readonly<Partial<Record<Intl.LDMLPluralRule, string>>> & { readonly other: string };

export const locale = 'en';

const catalogue: Readonly<Record<string, string | PluralForms>> = en;
const pluralRules = new Intl.PluralRules(locale);
const numberFormat = new Intl.NumberFormat(locale);

function interpolate(template: string, params: MessageParams | undefined): string {
  if (params === undefined) {
    return template;
  }
  return template.replace(/\{(\w+)\}/gu, (match, name: string) => {
    const value = params[name];
    if (value === undefined) {
      return match;
    }
    return typeof value === 'number' ? numberFormat.format(value) : value;
  });
}

/**
 * Looks up a message. Plural entries need a numeric `count` and pick their form with
 * `Intl.PluralRules`; `{name}` placeholders take `params`, numbers formatted for the locale.
 */
export function t(key: MessageKey, params?: MessageParams): string {
  const entry = catalogue[key];
  if (entry === undefined) {
    throw new Error(`Unknown message key ${key}.`);
  }
  if (typeof entry === 'string') {
    return interpolate(entry, params);
  }
  const count = params?.count;
  if (typeof count !== 'number') {
    throw new Error(`Message ${key} is a plural and needs a numeric count.`);
  }
  const form = entry[pluralRules.select(count)] ?? entry.other;
  return interpolate(form, params);
}

/** True when the key exists in the catalogue. */
export function hasMessage(key: string): key is MessageKey {
  return Object.hasOwn(catalogue, key);
}

/** All catalogue entries, for tests and tooling. */
export function messages(): Readonly<Record<string, string | PluralForms>> {
  return catalogue;
}
