import { proxyLive } from '$lib/server/live';
import type { RequestHandler } from './$types';

/**
 * `/live` and `/live/negotiate`: the SignalR hub as the browser sees it (AD-14). Every method lands
 * here, and `proxyLive` forwards only the hub's own with the session's access token.
 */
export const fallback: RequestHandler = ({ locals, request, url, params }) => proxyLive(locals, request, url, params.path);
