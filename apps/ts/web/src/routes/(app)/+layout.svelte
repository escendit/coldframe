<script lang="ts">
  import { onMount, type Snippet } from 'svelte';
  import { afterNavigate, goto, invalidateAll } from '$app/navigation';
  import { page } from '$app/state';
  import AppShell from '$lib/components/AppShell.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import PauseModal from '$lib/components/PauseModal.svelte';
  import ResumeControl from '$lib/components/ResumeControl.svelte';
  import { t } from '$lib/i18n';
  import { mountLive, type Live } from '$lib/live';
  import { webApp } from '$lib/overview-reach.svelte';
  import { pauseAccessOf, pauseNoticeOf, type PauseFailure } from '$lib/pause';
  import type { SiteMenuAction } from '$lib/site-menu';
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
    // The notice of a Resume that did not happen belongs to the page it was tried from.
    siteResumeFailure = null;
  });

  /**
   * Pause and Resume of the Site, from the Site menu of any page (UX-DR22, UX-DR70): Pause opens the
   * Pause modal with Whole Site, Resume is sent at once. Both post to the overview's form actions, so the
   * browser never calls the Server, and a success reads the open page again.
   */
  const sitePauseSite = $derived(data.currentSite !== null && pauseAccessOf(data.currentSite.role) ? data.currentSite : null);
  let sitePauseOpen = $state(false);
  /** The Resume form of the shell: it has no button of its own, the menu item sends it. */
  let siteResume: { submit: () => void } | undefined = $state();
  let siteResumeFailure: PauseFailure | null = $state(null);
  const siteResumeNotice = $derived(siteResumeFailure === null || sitePauseSite === null ? null : pauseNoticeOf(siteResumeFailure, sitePauseSite.name));

  function siteMenu(action: SiteMenuAction): void {
    if (action === 'pause') {
      sitePauseOpen = true;
    } else if (action === 'resume') {
      siteResume?.submit();
    }
  }

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

<AppShell user={data.user} currentPath={page.url.pathname} sites={data.sites} currentSite={data.currentSite} stale={stale} onsitemenu={siteMenu}>
  {#if sitesNotice !== null}
    <InlineNotice id="cf-sites-notice" message={sitesNotice.message} action={sitesNotice.action} />
  {:else}
    {#if siteResumeNotice !== null}
      <!-- A Resume from the Site menu that did not happen leaves its notice above the page; Try again sends the same Resume. -->
      <form
        class="cf-site-resume-notice"
        onsubmit={(event) => {
          event.preventDefault();
          siteResume?.submit();
        }}
      >
        <InlineNotice id="cf-site-resume-notice" message={siteResumeNotice.message} action={siteResumeNotice.tryAgain ? { label: t('notice.tryAgain') } : null} />
      </form>
    {/if}
    {@render children()}
  {/if}
</AppShell>

{#if sitePauseSite !== null && sitesNotice === null}
  <!-- Outside the page's main content, for Administrators and Owners only: a Member has neither the menu items nor these. -->
  <ResumeControl
    bind:this={siteResume}
    bind:failure={siteResumeFailure}
    showNotice={false}
    id="cf-site-resume"
    action="/garden?/resume"
    fields={{ siteId: sitePauseSite.id, scope: 'site' }}
    label={t('siteMenu.resume', { name: sitePauseSite.name })}
    name={sitePauseSite.name}
    siteName={sitePauseSite.name}
    hidden
  />
  <PauseModal id="cf-site-pause" bind:open={sitePauseOpen} siteId={sitePauseSite.id} siteName={sitePauseSite.name} device={null} action="/garden?/pause" />
{/if}

<style>
  .cf-site-resume-notice {
    margin: 0 0 var(--cf-spacing-5);
  }
</style>
