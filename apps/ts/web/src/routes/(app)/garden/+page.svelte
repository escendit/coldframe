<script lang="ts" module>
  import type { OverviewMode } from '$lib/garden';

  /** What this browser last showed, kept across navigations so a change is announced once. */
  let shown: OverviewMode | null = null;
</script>

<script lang="ts">
  import { onMount } from 'svelte';
  import { invalidateAll } from '$app/navigation';
  import { announce } from '$lib/announcer.svelte';
  import FirstRunSteps from '$lib/components/FirstRunSteps.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import LotTiles from '$lib/components/LotTiles.svelte';
  import SiteSummaryHeader from '$lib/components/SiteSummaryHeader.svelte';
  import StaleHeader from '$lib/components/StaleHeader.svelte';
  import { calibrateAccessOf } from '$lib/calibrate';
  import { firstRunSteps } from '$lib/first-run';
  import { lastGoodLoad, onFocusDecision, siteHeadline, staleTransition, webAppProbePath } from '$lib/garden';
  import { locale, t } from '$lib/i18n';
  import { formatWhen } from '$lib/i18n/format';
  import { lotsNoticeOf } from '$lib/lots';
  import { webApp } from '$lib/overview-reach.svelte';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The web never starts a BLE flow: the tiles carry no action (UX-DR54).
  const steps = $derived(data.currentSite === null ? null : firstRunSteps(data.currentSite.role, false));
  const lotsNotice = $derived(lotsNoticeOf(data.lotsNotice));
  /**
   * Stale mode (UX-DR79): the Server could not be reached, or this browser cannot reach the web
   * app, and this is when the data is from.
   */
  const staleSince = $derived.by(() => {
    const since = webApp.unreachableSince ?? data.staleSince;
    return since === null ? null : new Date(since);
  });

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');

  /** Durations are told against this browser's clock, read again with every load. */
  let browserNow: Date | null = $state(null);
  const now = $derived(browserNow ?? new Date(data.loadedAt));

  const summary = $derived(siteHeadline(data.lots, locale, timeZone));

  $effect(() => {
    // Every load carries a new `loadedAt`, which runs this again.
    if (data.loadedAt !== '') {
      browserNow = new Date();
    }
  });

  // Entering and leaving stale mode are announced politely, once (UX-DR106).
  $effect(() => {
    const change = staleTransition(shown, staleSince === null ? 'live' : 'stale');
    shown = staleSince === null ? 'live' : 'stale';
    if (change === 'entered' && staleSince !== null) {
      const zone = data.timeZone ?? new Intl.DateTimeFormat().resolvedOptions().timeZone;
      announce(t('stale.entered', { time: formatWhen(staleSince, new Date(), locale, zone) }));
    } else if (change === 'left') {
      announce(t('stale.left'));
    }
  });

  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;

    // Refetch on focus (UX-DR112): the page loads again when its tab is looked at, and never on a timer.
    let refreshing = false;
    const look = async (): Promise<void> => {
      const decision = await onFocusDecision(() => fetch(webAppProbePath, { cache: 'no-store' }));
      if (decision === 'unreachable') {
        // Loading again would fail and replace the overview: the shown Lots stay, as stale.
        webApp.unreachableSince ??= lastGoodLoad(data);
        browserNow = new Date();
        return;
      }
      try {
        await invalidateAll();
      } finally {
        webApp.unreachableSince = null;
      }
    };
    const refresh = (): void => {
      if (refreshing || document.visibilityState !== 'visible') {
        return;
      }
      refreshing = true;
      void look().finally(() => {
        refreshing = false;
      });
    };
    window.addEventListener('focus', refresh);
    document.addEventListener('visibilitychange', refresh);
    return () => {
      window.removeEventListener('focus', refresh);
      document.removeEventListener('visibilitychange', refresh);
      webApp.unreachableSince = null;
    };
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('garden.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('garden.title')}</h1>

{#if data.currentSite !== null && steps !== null}
  {#if staleSince !== null}
    <!-- Stale mode: the stale header replaces the summary, and nothing below is drawn as live. -->
    <StaleHeader siteName={data.currentSite.name} {staleSince} {now} {timeZone} />
  {:else}
    <!-- The headline and counts come from the Server's statuses; an empty Site says No Readings yet (UX-DR62, UX-DR82). -->
    <SiteSummaryHeader siteName={data.currentSite.name} headline={summary.headline} subline={summary.subline} paused={summary.paused} />
  {/if}
  <FirstRunSteps tiles={steps.tiles} actionable={steps.actionable} />
  <InlineNotice id="cf-garden-notice" message={steps.memberNotice ? t('garden.memberNotice') : t('garden.webNotice')} />
  <!-- The Lot grid sits below the first-run content (Design Notes): both conditions hold. -->
  <div class="cf-garden__lots">
    {#if lotsNotice !== null}
      <InlineNotice
        id="cf-lots-notice"
        message={t(lotsNotice.message)}
        action={lotsNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/garden', reload: true } : null}
      />
    {:else if data.lots.length > 0}
      <LotTiles lots={data.lots} {now} {timeZone} {staleSince} canCalibrate={calibrateAccessOf(data.currentSite.role)} />
    {/if}
  </div>
{/if}

<style>
  .cf-garden__lots {
    margin-block-start: var(--cf-spacing-6);
  }
</style>
