/**
 * The last good Sites and Lots, for stale mode on the Site overview (UX-DR79). The browser never
 * calls the Server (AD-14), so what it last answered is kept here, in the web app's process:
 * keyed by user and Site, bounded, and lost on a restart (the unreachable notice shows then).
 */
import type { SitesDependencies, SitesError, SitesResult } from './sites';

/** A value the Server returned, with the time of that successful read (ISO-8601). */
export interface LastGood<T> {
  readonly value: T;
  readonly fetchedAt: string;
}

export interface LastGoodStore {
  readonly size: number;
  /** The kept value; reading it makes it the most recently used. */
  get<T>(key: string): LastGood<T> | undefined;
  set(key: string, value: unknown, fetchedAt: string): void;
  delete(key: string): void;
}

/** Entries kept at most: one per user for the Sites, one per user and Site for the Lots. */
export const lastGoodLimit = 500;

/** A bounded store that forgets the least recently used entry first. */
export function createLastGoodStore(limit = lastGoodLimit): LastGoodStore {
  const entries = new Map<string, LastGood<unknown>>();
  return {
    get size() {
      return entries.size;
    },
    get<T>(key: string) {
      const entry = entries.get(key);
      if (entry !== undefined) {
        entries.delete(key);
        entries.set(key, entry);
      }
      return entry as LastGood<T> | undefined;
    },
    set(key, value, fetchedAt) {
      entries.delete(key);
      entries.set(key, { value, fetchedAt });
      while (entries.size > limit) {
        const [oldest] = entries.keys();
        if (oldest === undefined) {
          break;
        }
        entries.delete(oldest);
      }
    },
    delete(key) {
      entries.delete(key);
    },
  };
}

/** The store of this process. */
const processStore = createLastGoodStore();

export interface LastGoodDependencies extends SitesDependencies {
  /** The clock a successful read is timed with; injectable for tests. */
  readonly now?: () => Date;
  /** The store to keep answers in; the process-wide one by default. */
  readonly lastGood?: LastGoodStore;
}

type Locals = Pick<App.Locals, 'session'>;

/** The user answers are kept for: the subject of the ID token. Without one nothing is kept. */
export function userKeyOf(locals: Locals): string | null {
  const subject = locals.session?.identity?.idToken?.sub;
  return typeof subject === 'string' && subject !== '' ? subject : null;
}

export function sitesKey(user: string): string {
  return JSON.stringify([user, 'sites']);
}

export function lotsKey(user: string, siteId: string): string {
  return JSON.stringify([user, 'lots', siteId]);
}

/**
 * Stale mode is about transport only: the Server did not answer (`unreachable`) or answered that
 * it cannot serve (`unavailable`, `unexpected`: a 5xx or an answer outside the contract). A 401,
 * 403 or 404 is an answer, and an untrusted certificate keeps its own notice (AD-13).
 */
export function isTransportFailure(error: SitesError): boolean {
  return error === 'unreachable' || error === 'unavailable' || error === 'unexpected';
}

export type ReadOutcome<T> = { readonly ok: T; readonly fetchedAt: string; readonly stale: boolean } | { readonly error: SitesError };

/**
 * One refresh of a kept value. A success is kept with its time. A transport failure is retried
 * once; when the retry fails too, the last good value is served as stale, or the failure when
 * nothing is kept. Any other refusal is returned as it is and drops what was kept, so nobody
 * keeps seeing a Site they may no longer read.
 */
export async function readThrough<T>(key: string | null, read: () => Promise<SitesResult<T>>, dependencies: LastGoodDependencies = {}): Promise<ReadOutcome<T>> {
  const store = dependencies.lastGood ?? processStore;
  let result = await read();
  if ('error' in result && isTransportFailure(result.error)) {
    result = await read();
  }
  if ('ok' in result) {
    const fetchedAt = (dependencies.now?.() ?? new Date()).toISOString();
    if (key !== null) {
      store.set(key, result.ok, fetchedAt);
    }
    return { ok: result.ok, fetchedAt, stale: false };
  }
  if (key === null) {
    return result;
  }
  if (!isTransportFailure(result.error)) {
    if (result.error !== 'certificate') {
      store.delete(key);
    }
    return result;
  }
  const kept = store.get<T>(key);
  return kept === undefined ? result : { ok: kept.value, fetchedAt: kept.fetchedAt, stale: true };
}

/** Keeps a value read outside `readThrough` (the shell's Sites on pages without stale mode). */
export function remember(key: string | null, value: unknown, dependencies: LastGoodDependencies = {}): void {
  if (key !== null) {
    (dependencies.lastGood ?? processStore).set(key, value, (dependencies.now?.() ?? new Date()).toISOString());
  }
}

/** The kept value, without calling the Server. */
export function recall<T>(key: string | null, dependencies: LastGoodDependencies = {}): LastGood<T> | undefined {
  return key === null ? undefined : (dependencies.lastGood ?? processStore).get<T>(key);
}

/** Drops a kept value: the Server refused the read, so it is no longer the caller's to see. */
export function forget(key: string | null, dependencies: LastGoodDependencies = {}): void {
  if (key !== null) {
    (dependencies.lastGood ?? processStore).delete(key);
  }
}
