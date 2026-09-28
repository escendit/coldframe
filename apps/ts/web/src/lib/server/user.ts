import type { DisplayUser } from '$lib/user';

function claim(claims: Readonly<Record<string, unknown>>, name: string): string | undefined {
  const value = claims[name];
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : undefined;
}

function firstLetter(word: string): string {
  const [letter] = Array.from(word);
  return letter ?? '';
}

/** Builds display fields from ID token claims; never passes a token or any other claim on. */
export function displayUserOf(claims: Readonly<Record<string, unknown>> | null | undefined): DisplayUser {
  const source = claims ?? {};
  const given = claim(source, 'given_name');
  const family = claim(source, 'family_name');
  const name = claim(source, 'name');
  const username = claim(source, 'preferred_username') ?? claim(source, 'email');
  const fullName = [given, family].filter((part) => part !== undefined).join(' ');
  const displayName = name ?? (fullName !== '' ? fullName : (username ?? ''));

  let initials: string;
  if (given !== undefined && family !== undefined) {
    initials = firstLetter(given) + firstLetter(family);
  } else {
    // For an email, only the local part carries the name.
    const [localPart = ''] = displayName.split('@');
    const words = localPart.split(/[\s._-]+/u).filter((word) => word !== '');
    const first = words[0] ?? '';
    const last = words.length > 1 ? (words[words.length - 1] ?? '') : '';
    initials = firstLetter(first) + firstLetter(last);
  }
  return { displayName, initials };
}
