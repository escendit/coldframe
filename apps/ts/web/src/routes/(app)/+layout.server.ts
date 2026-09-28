import { guardShell } from '$lib/server/guard';
import type { LayoutServerLoad } from './$types';

/**
 * Guards the shell. It reads `url`, so it runs again on every navigation and notices a session
 * that ended (UX-DR93). Returns display fields only; never a token or claim (AD-14).
 */
export const load: LayoutServerLoad = ({ locals, url }) => guardShell(locals, url);
