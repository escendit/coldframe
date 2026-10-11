import { showDelivered } from '$lib/browser-notifications';
import { signalrHub } from './hub';
import { createRefetcher, startLive, type Live } from './live';
import { showsResource } from './rule';

export type { Live } from './live';

export interface MountLiveOptions {
  /** The path of the open page, read when a hint arrives. */
  readonly path: () => string;
  /** Reads the open page again over REST. */
  readonly refetch: () => Promise<void>;
  /** Opens the route a clicked notification leads to. */
  readonly open: (route: string) => void;
}

/**
 * The live updates of one tab (Story 6.6): one hub connection, a refetch of the open page for a
 * hint it shows and after a reconnect, and a browser notification for each delivery when the User
 * turned them on in this browser.
 */
export function mountLive(options: MountLiveOptions): Live {
  const refetcher = createRefetcher(options.refetch);
  return startLive({
    open: signalrHub,
    onHint: (hint) => {
      if (showsResource(options.path(), hint)) {
        refetcher.request();
      }
    },
    onNotification: (message) => {
      showDelivered(message, options.open);
    },
    onReconnected: () => {
      refetcher.request();
    },
  });
}
