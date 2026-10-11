/**
 * The open web app's connection to the hub (Story 6.6), without the transport: when to connect
 * again, and how hints become refetches. It shows nothing: no notice, no indicator. `hub.ts` holds
 * the SignalR client.
 */
import type { NotificationDelivered, ReadModelChanged } from '@coldframe/api-client';

export { liveHubPath, liveMessages } from '@coldframe/api-client';
export type { NotificationDelivered, ReadModelChanged, ReadModelResource } from '@coldframe/api-client';

export interface HubHandlers {
  readonly onHint: (hint: ReadModelChanged) => void;
  readonly onNotification: (message: NotificationDelivered) => void;
}

/** One connection to the hub. It is started once and never reused. */
export interface Hub {
  start(): Promise<void>;
  stop(): Promise<void>;
  /** Called when a started connection ends for any reason but `stop`. */
  onClose(listener: () => void): void;
  /** Whether the web app answered the last request with 401: the session is gone. */
  wasRefused(): boolean;
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null;
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value !== '';
}

/** Whether a received argument is a `readmodel.changed` hint of the contract. */
export function isHint(value: unknown): value is ReadModelChanged {
  return isRecord(value) && isText(value.resource) && isText(value.id) && typeof value.version === 'number';
}

/** Whether a received argument is a `notification.delivered` message of the contract. */
export function isDelivered(value: unknown): value is NotificationDelivered {
  return isRecord(value) && isText(value.kind) && isText(value.siteId) && isText(value.collapseId) && isText(value.title);
}

export interface Refetcher {
  /** Reads the open page again. While a read runs, any number of requests cause one more read. */
  request(): void;
}

/** Coalesces refetches. A read that fails is quiet: the page keeps its data and its own stale handling. */
export function createRefetcher(refetch: () => Promise<void>): Refetcher {
  let running = false;
  let again = false;
  const run = (): void => {
    running = true;
    void refetch()
      .catch(() => undefined)
      .finally(() => {
        running = false;
        if (again) {
          again = false;
          run();
        }
      });
  };
  return {
    request() {
      if (running) {
        again = true;
      } else {
        run();
      }
    },
  };
}

/** Waits before the next try: at once after a connection that held, then slower, then every 30 s. */
export const reconnectDelaysMs: readonly number[] = [0, 2000, 5000, 10_000, 30_000];

/**
 * How long a connection must stay open to count as one that held. One that the Server closes sooner
 * (it could not read the Memberships, or the token was at its end) counts as a failed try, so the
 * back-off goes on instead of starting over.
 */
export const heldAfterMs = 5000;

export interface LiveOptions extends HubHandlers {
  readonly open: (handlers: HubHandlers) => Hub;
  /** A connection was opened after the first try failed or an earlier one was lost: hints may have been missed. */
  readonly onReconnected: () => void;
  readonly schedule?: (run: () => void, ms: number) => () => void;
  readonly delays?: readonly number[];
  readonly now?: () => number;
  readonly heldAfter?: number;
}

export interface Live {
  /** A navigation happened: a connection that stopped at a 401 is tried once more. */
  resume(): void;
  stop(): void;
}

function later(run: () => void, ms: number): () => void {
  const timer = setTimeout(run, ms);
  return () => {
    clearTimeout(timer);
  };
}

/**
 * Keeps one connection open. A failed or lost connection is opened again with back-off, quietly;
 * a 401 means signed out, and nothing is tried again until `resume`.
 */
export function startLive(options: LiveOptions): Live {
  const schedule = options.schedule ?? later;
  const delays = options.delays ?? reconnectDelaysMs;
  const now = options.now ?? Date.now;
  const heldAfter = options.heldAfter ?? heldAfterMs;
  let state: 'connecting' | 'connected' | 'waiting' | 'refused' | 'stopped' = 'connecting';
  let hub: Hub | null = null;
  let cancelWait: (() => void) | null = null;
  let failures = 0;
  let first = true;

  const retry = (): void => {
    state = 'waiting';
    const delay = delays[Math.min(failures, delays.length - 1)] ?? 0;
    failures += 1;
    cancelWait = schedule(() => {
      cancelWait = null;
      if (state === 'waiting') {
        connect();
      }
    }, delay);
  };

  function connect(): void {
    state = 'connecting';
    const opened = options.open({ onHint: options.onHint, onNotification: options.onNotification });
    hub = opened;
    let connectedAt = 0;
    opened.onClose(() => {
      if (hub === opened && state === 'connected') {
        if (now() - connectedAt >= heldAfter) {
          failures = 0;
        }
        retry();
      }
    });
    const wasFirst = first;
    first = false;
    opened.start().then(
      () => {
        if (hub !== opened || state !== 'connecting') {
          void opened.stop().catch(() => undefined);
          return;
        }
        state = 'connected';
        connectedAt = now();
        if (!wasFirst) {
          options.onReconnected();
        }
      },
      () => {
        if (hub !== opened || state !== 'connecting') {
          return;
        }
        if (opened.wasRefused()) {
          state = 'refused';
        } else {
          retry();
        }
      },
    );
  }

  connect();

  return {
    resume() {
      if (state === 'refused') {
        failures = 0;
        connect();
      }
    },
    stop() {
      state = 'stopped';
      cancelWait?.();
      cancelWait = null;
      const open = hub;
      hub = null;
      void open?.stop().catch(() => undefined);
    },
  };
}
