<script lang="ts">
  import Icon from '$lib/components/Icon.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import { devicesAccessOf, devicesNoticeOf, hubsOf, lastSeenText, statusOf } from '$lib/devices';
  import { locale, t } from '$lib/i18n';
  import { onMount } from 'svelte';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  const access = $derived(data.currentSite === null ? null : devicesAccessOf(data.currentSite.role));
  const notice = $derived(devicesNoticeOf(data.devicesNotice));
  const hubs = $derived(hubsOf(data.devices));
  const loadedAt = $derived(new Date(data.loadedAt));

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');

  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('devices.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('devices.title')}</h1>

{#if data.currentSite !== null && access !== null}
  <div class="cf-devices">
    {#if notice !== null}
      <!-- A failed load shows no rows, so no Hub stays "Online" from an earlier answer. -->
      <InlineNotice
        id="cf-devices-notice"
        message={t(notice.message)}
        action={notice.tryAgain ? { label: t('notice.tryAgain'), href: '/devices', reload: true } : null}
      />
    {:else}
      {#if hubs.length === 0}
        <p class="cf-devices__empty">{t('devices.empty')}</p>
      {:else}
        <section aria-labelledby="cf-devices-hubs">
          <h2 id="cf-devices-hubs" class="cf-section-title">{t('devices.hubs')}</h2>
          <ul class="cf-devices__list" aria-labelledby="cf-devices-hubs">
            {#each hubs as hub (hub.id)}
              {@const status = statusOf(hub)}
              <li class="cf-devices__row" data-device={hub.id} data-online={hub.online}>
                <span class="cf-devices__id">{hub.id}</span>
                <span class="cf-devices__meta">
                  <span class="cf-devices__status"><Icon name={status.icon} />{t(status.label)}</span>
                  <span class="cf-devices__seen">{lastSeenText(hub, loadedAt, locale, timeZone)}</span>
                </span>
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      {#if access.mobileAppNotice}
        <!-- No BLE on the web (UX-DR85): the notice stands in for the Add actions, which are Admin+ (UX-DR84). -->
        <InlineNotice id="cf-devices-web-notice" message={t('devices.webNotice')} />
      {/if}
    {/if}
  </div>
{/if}

<style>
  .cf-devices {
    display: grid;
    gap: var(--cf-spacing-6);
    max-width: 40rem;
  }

  .cf-devices__empty {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__list {
    margin: 0;
    padding: 0;
    list-style: none;
    border-top: 1px solid var(--cf-color-border-subtle);
  }

  .cf-devices__row {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--cf-spacing-3) var(--cf-spacing-5);
    padding: var(--cf-spacing-5) 0;
    background: var(--cf-color-background);
    border-bottom: 1px solid var(--cf-color-border-subtle);
  }

  .cf-devices__id {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-primary);
    overflow-wrap: anywhere;
  }

  .cf-devices__meta {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-2) var(--cf-spacing-5);
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__status {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
    color: var(--cf-color-text-primary);
  }
</style>
