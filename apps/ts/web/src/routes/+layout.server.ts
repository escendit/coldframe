import type { LayoutServerLoad } from './$types';

/** The theme of this browser, for the Appearance switcher. No identity data here. */
export const load: LayoutServerLoad = ({ locals }) => ({ theme: locals.theme ?? 'system' });
