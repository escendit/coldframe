/** Roles on a Site, ordered Owner > Administrator > Member. Always from the Server, never token claims. */
export type Role = 'Owner' | 'Administrator' | 'Member';

const rank: Readonly<Record<Role, number>> = { Member: 1, Administrator: 2, Owner: 3 };

/** True when `role` is at least `minimum`; no Role grants nothing. */
export function hasRole(role: Role | null | undefined, minimum: Role): boolean {
  return role !== null && role !== undefined && rank[role] >= rank[minimum];
}
