/**
 * The SignalR client of the hub (Story 6.6). It connects to the web app's own origin, over
 * Server-Sent Events or long polling; the web app's server attaches the access token. The hub
 * has no method a client can call, so this only listens.
 */
import { liveHubPath, liveMessages } from '@coldframe/api-client';
import { isDelivered, isHint, type Hub, type HubHandlers } from './live';

type SignalR = typeof import('@microsoft/signalr');

/** Opens one connection. The SignalR client is loaded only in the browser, when the connection starts. */
export function signalrHub(handlers: HubHandlers): Hub {
  let connection: import('@microsoft/signalr').HubConnection | null = null;
  let closed: (() => void) | null = null;
  let stopped = false;
  let lastStatus = 0;

  /** Remembers the status of the last answer: negotiate reports a 401 only as text. */
  function watching(signalr: SignalR): import('@microsoft/signalr').HttpClient {
    return new (class extends signalr.DefaultHttpClient {
      override async send(request: import('@microsoft/signalr').HttpRequest): Promise<import('@microsoft/signalr').HttpResponse> {
        try {
          const response = await super.send(request);
          lastStatus = response.statusCode;
          return response;
        } catch (error) {
          lastStatus = error instanceof signalr.HttpError ? error.statusCode : 0;
          throw error;
        }
      }
    })(signalr.NullLogger.instance);
  }

  return {
    async start() {
      const signalr = await import('@microsoft/signalr');
      if (stopped) {
        return;
      }
      const built = new signalr.HubConnectionBuilder()
        .withUrl(liveHubPath, {
          transport: signalr.HttpTransportType.ServerSentEvents | signalr.HttpTransportType.LongPolling,
          httpClient: watching(signalr),
        })
        .configureLogging(signalr.LogLevel.None)
        .build();
      built.on(liveMessages.notificationDelivered, (message: unknown) => {
        if (isDelivered(message)) {
          handlers.onNotification(message);
        }
      });
      built.on(liveMessages.readModelChanged, (hint: unknown) => {
        if (isHint(hint)) {
          handlers.onHint(hint);
        }
      });
      built.onclose(() => {
        if (!stopped) {
          closed?.();
        }
      });
      connection = built;
      await built.start();
    },
    async stop() {
      stopped = true;
      await connection?.stop();
    },
    onClose(listener) {
      closed = listener;
    },
    wasRefused() {
      return lastStatus === 401;
    },
  };
}
