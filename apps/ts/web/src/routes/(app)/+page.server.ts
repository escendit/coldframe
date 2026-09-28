import { redirect } from '@sveltejs/kit';
import type { PageServerLoad } from './$types';

/** `/` opens the Garden. */
export const load: PageServerLoad = () => {
  redirect(307, '/garden');
};
