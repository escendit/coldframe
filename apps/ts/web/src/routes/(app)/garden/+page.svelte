<script lang="ts" module>
  import type { OverviewMode } from '$lib/garden';

  /** What this browser last showed, kept across navigations so a change is announced once. */
  let shown: OverviewMode | null = null;
  /** The silent Hubs whose notice this browser last showed: a notice is announced when it appears, not while it stays. */
  let shownHubs: ReadonlySet<string> = new Set();
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
  import { appearedHubNotices, hubNotices, lastGoodLoad, onFocusDecision, siteHeadline, sitePauseNotes, staleTransition, webAppProbePath } from '$lib/garden';
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
  /** The paused-Site line and the Member's notice follow the Site the Server sent (UX-DR83, UX-DR84). */
  const pauseNotes = $derived(data.currentSite === null ? null : sitePauseNotes(data.currentSite));

  $effect(() => {
    // Every load carries a new `loadedAt`, which runs this again.
    if (data.loadedAt !== '') {
      browserNow = new Date();
    }
  });

  /**
   * The Hub-silent notices (UX-DR81), from the Server's list of silent Hubs. Stale mode shows none:
   * nothing there is live, and "silent for 12 min" would be.
   */
  const silentHubNotices = $derived(staleSince === null && lotsNotice === null ? hubNotices(data.silentHubs, now) : []);

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

  // A Hub-silent notice is announced politely once each time it appears (UX-DR81): not while it stays over a refetch.
  $effect(() => {
    const appeared = appearedHubNotices(shownHubs, silentHubNotices);
    shownHubs = new Set(silentHubNotices.map((notice) => notice.deviceId));
    if (appeared.length > 0) {
      announce(appeared.map((notice) => notice.message).join(' '));
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
    <SiteSummaryHeader siteName={data.currentSite.name} headline={summary.headline} subline={summary.subline} paused={summary.paused} line={pauseNotes?.line ?? null} />
    {#if pauseNotes !== null && pauseNotes.memberNotice !== null}
      <!-- A Member sees the paused Site and no Resume (hidden, never disabled): the one notice says who can. -->
      <div class="cf-garden__paused">
        <InlineNotice id="cf-site-paused-notice" message={pauseNotes.memberNotice} />
      </div>
    {/if}
  {/if}
  <FirstRunSteps tiles={steps.tiles} actionable={steps.actionable} />
  <InlineNotice id="cf-garden-notice" message={steps.memberNotice ? t('garden.memberNotice') : t('garden.webNotice')} />
  <!-- The Lot grid sits below the first-run content (Design Notes): both conditions hold. -->
  <div class="cf-garden__lots">
    {#if silentHubNotices.length > 0}
      <!-- A silent Hub (UX-DR81): said above the tiles, also when no Lot is behind it. It has no action and cannot be dismissed. -->
      <div class="cf-garden__hubs">
        {#each silentHubNotices as hubNotice (hubNotice.deviceId)}
          <InlineNotice id="cf-hub-silent-{hubNotice.deviceId}" message={hubNotice.message} />
        {/each}
      </div>
    {/if}
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

  .cf-garden__paused {
    margin-block-end: var(--cf-spacing-6);
  }

  .cf-garden__hubs {
    display: grid;
    gap: var(--cf-spacing-3);
    margin-block-end: var(--cf-spacing-5);
  }
</style>
