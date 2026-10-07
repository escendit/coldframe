<script lang="ts">
  import { onMount } from 'svelte';
  import HistoryChart from '$lib/components/HistoryChart.svelte';
  import Button from '$lib/components/Button.svelte';
  import Hatch from '$lib/components/Hatch.svelte';
  import Icon from '$lib/components/Icon.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import SegmentedChoice from '$lib/components/SegmentedChoice.svelte';
  import StaleHeader from '$lib/components/StaleHeader.svelte';
  import { locale, t, type MessageKey } from '$lib/i18n';
  import { deviceCells, heroSpoken, historyChart, lotHero, pickerQuantities, quantityName, sensorCells, type SensorQuantity } from '$lib/lot-detail';
  import { calibratableSensor } from '$lib/calibrate';
  import { hasRole } from '$lib/roles';
  import type { PageProps } from './$types';

  let { data, params }: PageProps = $props();

  const noticeMessage: Readonly<Record<NonNullable<PageProps['data']['notice']>, MessageKey>> = {
    notFound: 'lotDetail.notFound',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
    unavailable: 'lots.unavailable',
  };

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');
  let browserNow: Date | null = $state(null);
  const now = $derived(browserNow ?? new Date(data.loadedAt));
  const staleSince = $derived(data.staleSince === null ? null : new Date(data.staleSince));

  const lot = $derived(data.detail?.lot ?? null);
  const admin = $derived(data.currentSite !== null && hasRole(data.currentSite.role, 'Administrator'));
  const context = $derived({ now, locale, timeZone, staleSince, admin, hubId: data.detail?.hubId ?? null });
  const hero = $derived(lot === null ? null : lotHero(lot, context));
  const spoken = $derived(lot === null ? '' : heroSpoken(lot, context));
  const cells = $derived(lot?.sensors === undefined ? [] : sensorCells(lot.sensors, context));
  const device = $derived(lot?.node === undefined ? [] : deviceCells(lot.node, context));
  const quantities = $derived(pickerQuantities(lot?.sensors));

  let picked: SensorQuantity | null = $state(null);
  const quantity = $derived(picked !== null && quantities.includes(picked) ? picked : (quantities[0] ?? 'soil_moisture'));
  const chart = $derived(historyChart(data.detail?.histories[quantity], quantity, now, locale));
  const options = $derived(quantities.map((value) => ({ value, label: quantityName(value) })));
  function choose(value: SensorQuantity): void {
    picked = value;
  }
  const hasNode = $derived(lot?.node !== undefined);
  /** Calibrate: Owners and Administrators only, and only for a Sensor whose Specification calls for it. */
  const canCalibrate = $derived(admin && lot !== null && calibratableSensor(lot) !== null);

  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
    browserNow = new Date();
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: lot?.name ?? t('garden.title') })}</title>
</svelte:head>

<p class="cf-detail__back"><a href="/garden">{t('lotDetail.back')}</a></p>

{#if data.currentSite !== null}
  {#if staleSince !== null}
    <StaleHeader siteName={data.currentSite.name} {staleSince} {now} {timeZone} />
  {/if}

  {#if data.notice !== null}
    <InlineNotice
      id="cf-lot-notice"
      message={t(noticeMessage[data.notice], { siteName: data.currentSite.name })}
      action={data.notice === 'unreachable' || data.notice === 'unavailable' ? { label: t('notice.tryAgain'), href: `/garden/${params.lotId}`, reload: true } : null}
    />
  {:else if lot !== null && hero !== null}
    <div class="cf-detail" data-lot={lot.id} data-status={lot.status} data-variant={hero.variant}>
      <section class="cf-hero cf-hero--{hero.variant}" class:cf-hero--orange={hero.orange} role="group" aria-label={spoken}>
        {#snippet heroBody()}
          <h1 id="cf-hero-name" class="cf-hero__name">{hero.name}</h1>
          <p class="cf-hero__status">
            <Icon name={hero.icon} size={20} />
            <span class="cf-hero__label">{hero.label}</span>
            {#if hero.since !== null}<span class="cf-hero__since">{hero.since}</span>{/if}
            {#if hero.asOf !== null}<span class="cf-hero__since">{hero.asOf}</span>{/if}
          </p>
          {#if hero.value !== null}
            <p class="cf-hero__value">
              <span>{hero.value}</span>
              {#if hero.unit !== null}<span class="cf-hero__unit">{hero.unit}</span>{/if}
            </p>
          {/if}
          {#if hero.low !== null || hero.reading !== null}
            <p class="cf-hero__meta">
              {#if hero.low !== null}<span>{hero.low}</span>{/if}
              {#if hero.reading !== null}<span>{hero.reading}</span>{/if}
            </p>
          {/if}
          {#each hero.notes as note (note)}
            <p class="cf-hero__note">{note}</p>
          {/each}
        {/snippet}
        {#if hero.hatched}
          <Hatch plate>
            <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
            {@render heroBody()}
          </Hatch>
        {:else}
          <div class="cf-hero__body">
            <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
            {@render heroBody()}
          </div>
        {/if}
      </section>

      {#if !hasNode}
        <InlineNotice id="cf-lot-no-node" message={t('lotDetail.noNodeWeb')} />
      {:else}
        <section class="cf-frame" aria-labelledby="cf-detail-sensors">
          <h2 id="cf-detail-sensors" class="cf-section-title">{t('lotDetail.sensors')}</h2>
          {#if cells.length === 0}
            <p class="cf-detail__empty">{t('lotDetail.noReadings')}</p>
          {:else}
            <ul class="cf-cells cf-cells--sensors">
              {#each cells as cell (cell.quantity)}
                <li class="cf-cell" data-quantity={cell.quantity}>
                  <span class="cf-cell__label">{cell.label}</span>
                  <span class="cf-cell__value">{cell.value}</span>
                  {#if cell.time !== null}<span class="cf-cell__meta">{cell.time}</span>{/if}
                </li>
              {/each}
            </ul>
          {/if}
          {#if canCalibrate}
            <p class="cf-detail__calibrate"><Button label={t('calibrate.action')} variant="secondary" href="/garden/{lot.id}/calibrate" id="cf-detail-calibrate" /></p>
          {/if}
        </section>

        <section aria-labelledby="cf-detail-history">
          <h2 id="cf-detail-history" class="cf-section-title">{t('lotDetail.history')}</h2>
          {#if options.length > 1}
            <SegmentedChoice id="cf-history-picker" label={t('lotDetail.picker')} {options} value={quantity} onchange={choose} />
          {/if}
          <HistoryChart {chart} />
        </section>

        <div class="cf-frame">
        <ul class="cf-cells cf-cells--device" aria-label={t('lotDetail.node', { id: lot.node?.deviceId ?? '' })}>
          {#each device as cell (cell.id)}
            <li class="cf-cell" data-cell={cell.id}>
              <a class="cf-cell__link" href="/devices">
                <span class="cf-cell__label">{cell.label}</span>
                <span class="cf-cell__value">
                  {#if cell.icon !== null}<Icon name={cell.icon} size={20} />{/if}
                  {cell.value}
                </span>
                {#if cell.meta !== ''}<span class="cf-cell__meta">{cell.meta}</span>{/if}
              </a>
            </li>
          {/each}
        </ul>
        </div>
      {/if}
    </div>
  {/if}
{/if}

<style>
  .cf-detail {
    display: grid;
    gap: var(--cf-spacing-6);
    max-width: 48rem;
  }

  .cf-detail__back {
    margin: 0 0 var(--cf-spacing-5);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
  }

  .cf-detail__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
    color: var(--cf-color-primary-text);
  }

  .cf-detail__calibrate {
    margin: var(--cf-spacing-5) 0 0;
  }

  .cf-detail__empty {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-hero {
    display: grid;
    box-sizing: border-box;
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-background);
    color: var(--cf-color-text-primary);
  }

  .cf-hero__body,
  .cf-hero :global(.cf-hatch) {
    display: grid;
    gap: var(--cf-spacing-3);
    padding: var(--cf-spacing-tile-padding-web);
  }

  .cf-hero--orange {
    background: var(--cf-color-status-water-fill);
    color: var(--cf-color-status-water-ink);
  }

  .cf-hero--ok {
    border: 1px solid var(--cf-color-status-ok-border);
  }

  .cf-hero--unknown {
    border: 1px dashed var(--cf-color-status-unknown-border);
  }

  .cf-hero--needsCalibration {
    border: 2px dashed var(--cf-color-status-calibration-border);
  }

  .cf-hero--paused {
    background: var(--cf-color-status-paused-fill);
    border: 2px solid var(--cf-color-status-paused-border);
    color: var(--cf-color-status-paused-ink);
  }

  .cf-hero--noNode {
    border: 1px dotted var(--cf-color-status-no-node-border);
    color: var(--cf-color-status-no-node-ink);
  }

  .cf-hero--stale {
    border: 1px solid var(--cf-color-stale-border);
    color: var(--cf-color-text-secondary);
  }

  .cf-hero__name {
    margin: 0;
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-title-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-title-line-height);
    overflow-wrap: anywhere;
  }

  .cf-hero__status {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-2) var(--cf-spacing-3);
    margin: 0;
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
  }

  .cf-hero__label {
    text-transform: uppercase;
  }

  .cf-hero--needsCalibration .cf-hero__label {
    color: var(--cf-color-status-calibration-ink);
  }

  .cf-hero__since,
  .cf-hero__meta {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
  }

  .cf-hero__value {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: var(--cf-spacing-3);
    margin: 0;
    font-family: var(--cf-type-hero-value-font-family);
    font-size: var(--cf-type-hero-value-font-size);
    font-weight: var(--cf-type-hero-value-font-weight);
    line-height: var(--cf-type-hero-value-line-height);
    overflow-wrap: anywhere;
  }

  .cf-hero__unit {
    font-size: var(--cf-type-title-font-size);
  }

  .cf-hero__meta {
    display: flex;
    flex-wrap: wrap;
    justify-content: space-between;
    gap: var(--cf-spacing-3);
    margin: 0;
  }

  .cf-hero__note {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
  }

  /* 3-up Sensor cells and 2-up Device cells; one column below 400 px of the page (320 px reflow, 200 % zoom). */
  .cf-cells {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: var(--cf-spacing-tile-gap);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .cf-frame {
    container-type: inline-size;
  }

  .cf-cell {
    display: grid;
    align-content: start;
    gap: var(--cf-spacing-2);
    padding: var(--cf-spacing-5);
    background: var(--cf-color-layer-01);
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-cell__link {
    display: grid;
    gap: var(--cf-spacing-2);
    color: inherit;
    text-decoration: none;
  }

  .cf-cell__label,
  .cf-cell__meta {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
    overflow-wrap: anywhere;
  }

  .cf-cell__value {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-3);
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-title-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-title-line-height);
    overflow-wrap: anywhere;
  }

  @container (min-width: 400px) {
    .cf-cells--sensors {
      grid-template-columns: repeat(3, minmax(0, 1fr));
    }

    .cf-cells--device {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }
</style>
