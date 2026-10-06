<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import AppShell from '$lib/components/AppShell.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import { t } from '$lib/i18n';
  import { webApp } from '$lib/overview-reach.svelte';
  import { sitesNoticeOf } from '$lib/sites';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps & { children: Snippet } = $props();

  // Stale mode of the Site overview: its Lots, or already the Sites, are the last good ones, or
  // this browser cannot reach the web app (UX-DR79).
  const stale = $derived(data.sitesStale !== null || (page.data.staleSince ?? null) !== null || webApp.unreachableSince !== null);

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
