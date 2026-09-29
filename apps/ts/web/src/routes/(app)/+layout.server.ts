import { loadShell } from '$lib/server/shell';
import type { LayoutServerLoad } from './$types';

/**
 * Guards the shell and loads the caller's Sites. It reads `url`, so it runs again on every
 * navigation and notices a session that ended (UX-DR93). Returns display fields and Sites only;
 * never a token or claim (AD-14). No Membership → Create Site.
 */
export const load: LayoutServerLoad = ({ locals, url, cookies }) => loadShell(locals, url, cookies);
