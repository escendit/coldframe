<script lang="ts">
  import { invalidateAll } from '$app/navigation';
  import { alertGroups, alertsNoticeOf } from '$lib/alerts';
  import AlertRows from '$lib/components/AlertRows.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import { onFocusDecision, webAppProbePath } from '$lib/garden';
  import { locale, t } from '$lib/i18n';
  import { onMount } from 'svelte';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  const notice = $derived(alertsNoticeOf(data.alertsNotice));

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');

  // Open Threshold Alerts, open Health Alerts, then Closed (UX-DR64); each in the Server's order.
  const groups = $derived(alertGroups(data.alerts, { now: new Date(data.loadedAt), locale, timeZone }));
  const noneOpen = $derived(groups.threshold.length === 0 && groups.health.length === 0);

  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;

    // Refetch on focus (UX-DR112): the page loads again when its tab is looked at, and never on a timer.
    let refreshing = false;
    const look = async (): Promise<void> => {
      // When the web app itself is out of reach, loading again would replace the page with an error.
      if ((await onFocusDecision(() => fetch(webAppProbePath, { cache: 'no-store' }))) === 'reload') {
        await invalidateAll();
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
    };
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('alerts.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('alerts.title')}</h1>

{#if data.currentSite !== null}
  <div class="cf-alerts">
    {#if notice !== null}
      <!-- A failed load shows no rows: nothing is kept from an earlier answer. -->
      <InlineNotice id="cf-alerts-notice" message={t(notice.message)} action={notice.tryAgain ? { label: t('notice.tryAgain'), href: '/alerts', reload: true } : null} />
    {:else}
      {#if noneOpen}
        <p class="cf-alerts__empty">{t('alerts.empty')}</p>
      {/if}
      {#if groups.threshold.length > 0}
        <section aria-labelledby="cf-alerts-threshold">
          <h2 id="cf-alerts-threshold" class="cf-section-title">{t('alerts.group.threshold')}</h2>
          <AlertRows rows={groups.threshold} labelledBy="cf-alerts-threshold" />
        </section>
      {/if}
      {#if groups.health.length > 0}
        <section aria-labelledby="cf-alerts-health">
          <h2 id="cf-alerts-health" class="cf-section-title">{t('alerts.group.health')}</h2>
          <AlertRows rows={groups.health} labelledBy="cf-alerts-health" />
        </section>
      {/if}
      {#if groups.closed.length > 0}
        <section aria-labelledby="cf-alerts-closed">
          <h2 id="cf-alerts-closed" class="cf-section-title">{t('alerts.group.closed')}</h2>
          <AlertRows rows={groups.closed} labelledBy="cf-alerts-closed" />
        </section>
      {/if}
    {/if}
  </div>
{/if}

<style>
  .cf-alerts {
    display: grid;
    gap: var(--cf-spacing-6);
    max-width: 40rem;
  }

  .cf-alerts__empty {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }
</style>
