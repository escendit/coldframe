import type { Handle } from '@sveltejs/kit';
import { parseTheme, themeAttribute, themeCookieName } from '$lib/theme';

/** Placeholder in `src/app.html`: `<html lang="en" %cf.theme%>`. */
export const themePlaceholder = '%cf.theme%';

/** Renders the stored theme into `<html>` on the server, so the first paint has no flash. */
export const themeHandle: Handle = async ({ event, resolve }) => {
  const theme = parseTheme(event.cookies.get(themeCookieName));
  event.locals.theme = theme;
  return resolve(event, {
    transformPageChunk: ({ html }) => html.replace(themePlaceholder, themeAttribute(theme)),
  });
};
