<script lang="ts">
  import FirstRunSteps from '$lib/components/FirstRunSteps.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import LotTiles from '$lib/components/LotTiles.svelte';
  import SiteSummaryHeader from '$lib/components/SiteSummaryHeader.svelte';
  import { firstRunSteps } from '$lib/first-run';
  import { t } from '$lib/i18n';
  import { lotsNoticeOf } from '$lib/lots';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The web never starts a BLE flow: the tiles carry no action (UX-DR54).
  const steps = $derived(data.currentSite === null ? null : firstRunSteps(data.currentSite.role, false));
  const lotsNotice = $derived(lotsNoticeOf(data.lotsNotice));
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('garden.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('garden.title')}</h1>

{#if data.currentSite !== null && steps !== null}
  <!-- Empty Site (UX-DR62, UX-DR82): no Hub or Node yet, so no Readings and no status. -->
  <SiteSummaryHeader siteName={data.currentSite.name} headline={t('garden.noReadings')} subline={t('garden.noReadingsDetail')} />
  <FirstRunSteps tiles={steps.tiles} actionable={steps.actionable} />
  <InlineNotice id="cf-garden-notice" message={steps.memberNotice ? t('garden.memberNotice') : t('garden.webNotice')} />
  <!-- The Lot grid sits below the empty-Site content (Design Notes): both conditions hold. -->
  <div class="cf-garden__lots">
    {#if lotsNotice !== null}
      <InlineNotice
        id="cf-lots-notice"
        message={t(lotsNotice.message)}
        action={lotsNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/garden', reload: true } : null}
      />
    {:else if data.lots.length > 0}
      <LotTiles lots={data.lots} />
    {/if}
  </div>
{/if}

<style>
  .cf-garden__lots {
    margin-block-start: var(--cf-spacing-6);
  }
</style>
