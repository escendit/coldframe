import { signinState } from '$lib/server/guard';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = ({ locals, url }) => signinState(locals, url);
