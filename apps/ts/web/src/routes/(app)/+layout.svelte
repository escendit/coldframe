<script lang="ts">
  import { onMount, type Snippet } from 'svelte';
  import { afterNavigate, goto, invalidateAll } from '$app/navigation';
  import { page } from '$app/state';
  import AppShell from '$lib/components/AppShell.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import { t } from '$lib/i18n';
  import { mountLive, type Live } from '$lib/live';
  import { webApp } from '$lib/overview-reach.svelte';
  import { sitesNoticeOf } from '$lib/sites';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps & { children: Snippet } = $props();

  // Stale mode of the Site overview: its Lots, or already the Sites, are the last good ones, or
  // this browser cannot reach the web app (UX-DR79).
  const stale = $derived(data.sitesStale !== null || (page.data.staleSince ?? null) !== null || webApp.unreachableSince !== null);

  /**
   * Live updates (Story 6.6): one hub connection per tab. A hint for what the open page shows reads
   * the page again, as its own refetch on focus does; a delivered notification is shown when the User
   * turned browser notifications on here. A lost connection shows nothing.
   */
  let live: Live | undefined;

  onMount(() => {
    live = mountLive({
      path: () => page.url.pathname,
      refetch: () => invalidateAll(),
      open: (route) => void goto(route, { invalidateAll: true }),
    });
    return () => {
      live?.stop();
    };
  });

  // Signed out, the hub answers 401 and nothing is tried again until the User navigates.
  afterNavigate(() => {
    live?.resume();
  });

  const sitesNotice = $derived.by(() => {
    const notice = sitesNoticeOf(data.sitesNotice);
    if (notice === null) {
      return null;
    }
    return {
      message: t(notice.message),
      action: notice.tryAgain ? { label: t('notice.tryAgain'), href: page.url.pathname, reload: true } : null,
    };
  });
</script>

<AppShell user={data.user} currentPath={page.url.pathname} sites={data.sites} currentSite={data.currentSite} stale={stale}>
  {#if sitesNotice !== null}
    <InlineNotice id="cf-sites-notice" message={sitesNotice.message} action={sitesNotice.action} />
  {:else}
    {@render children()}
  {/if}
</AppShell>
