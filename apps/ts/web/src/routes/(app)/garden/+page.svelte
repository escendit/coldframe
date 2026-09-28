<script lang="ts">
  import FirstRunSteps from '$lib/components/FirstRunSteps.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import SiteSummaryHeader from '$lib/components/SiteSummaryHeader.svelte';
  import { firstRunSteps } from '$lib/first-run';
  import { t } from '$lib/i18n';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The web never starts a BLE flow: the tiles carry no action (UX-DR54).
  const steps = $derived(data.currentSite === null ? null : firstRunSteps(data.currentSite.role, false));
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
{/if}
