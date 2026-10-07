<script lang="ts">
  import { locale, t } from '$lib/i18n';
  import { lotTile, type LotTile, type TileVariant } from '$lib/lot-tiles';
  import type { Lot } from '$lib/lots';
  import Hatch from './Hatch.svelte';
  import Icon from './Icon.svelte';

  interface Props {
    /** In the Server's order (status, then creation); never re-sorted here (UX-DR20). */
    lots: readonly Lot[];
    /** The clock durations and "today" are told against. */
    now: Date;
    timeZone: string;
    /** Stale mode: when the Lots were last read; every tile is then drawn stale (UX-DR19). */
    staleSince?: Date | null;
  }

  let { lots, now, timeZone, staleSince = null }: Props = $props();

  const tiles = $derived(lots.map((lot) => lotTile(lot, { now, locale, timeZone, staleSince })));

  const variantClass: Readonly<Record<TileVariant, string>> = {
    needsWater: 'needs-water',
    ok: 'ok',
    unknown: 'unknown',
    needsCalibration: 'needs-calibration',
    paused: 'paused',
    noNode: 'no-node',
    stale: 'stale',
  };
</script>

{#snippet content(tile: LotTile)}
  <div class="cf-lot-tile__top">
    <span class="cf-lot-tile__name">{tile.name}</span>
    <span class="cf-lot-tile__status"><Icon name={tile.icon} /><span class="cf-lot-tile__label">{tile.label}</span></span>
  </div>
  <div class="cf-lot-tile__bottom">
    {#if tile.value !== null}
      <span class="cf-lot-tile__value">{tile.value}</span>
    {/if}
    {#if tile.foot !== null}
      <span class="cf-lot-tile__foot">{tile.foot}</span>
    {/if}
  </div>
{/snippet}

<!--
  Lot tiles (UX-DR17 to UX-DR20): the six status variants and the stale one, each told apart by
  shape and icon before colour. The models come from `lotTile`; the status is the Server's. Tiles
  open Lot detail (UX-DR63): each tile is one link, whatever its status, the no-Node tile included (it has no BLE action on the web). Each tile is one accessibility element.
-->
<div class="cf-lot-grid-frame">
  <ul class="cf-lot-grid" aria-label={t('garden.lots')}>
    {#each tiles as tile (tile.id)}
      <li class="cf-lot-grid__cell" data-lot={tile.id} data-status={tile.status} data-variant={tile.variant}>
        <a class="cf-lot-tile cf-lot-tile--{variantClass[tile.variant]}" href="/garden/{tile.id}" aria-label={tile.spoken}>
          {#if tile.level !== null}
            <span class="cf-lot-tile__level" style:--cf-lot-level="{tile.level}%"></span>
          {/if}
          {#if tile.low !== null}
            <span class="cf-lot-tile__low" style:--cf-lot-low="{tile.low}%"></span>
          {/if}
          {#if tile.hatched}
            <Hatch plate>
              <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
              {@render content(tile)}
            </Hatch>
          {:else}
            <div class="cf-lot-tile__body">
              <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
              {@render content(tile)}
            </div>
          {/if}
        </a>
      </li>
    {/each}
  </ul>
</div>

<style>
  /* Columns follow the grid's own width: 1 below 400 px (200 % zoom, 320 px reflow), then 2, 3, 4. */
  .cf-lot-grid-frame {
    container-type: inline-size;
  }

  .cf-lot-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: var(--cf-spacing-tile-gap);
    margin: 0 0 var(--cf-spacing-6);
    padding: 0;
    list-style: none;
  }

  @container (min-width: 400px) {
    .cf-lot-grid {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }

  @container (min-width: 672px) {
    .cf-lot-grid {
      grid-template-columns: repeat(3, minmax(0, 1fr));
    }
  }

  @container (min-width: 1056px) {
    .cf-lot-grid {
      grid-template-columns: repeat(4, minmax(0, 1fr));
    }
  }

  .cf-lot-grid__cell {
    display: grid;
  }

  /* At least 1 : 0.82, growing in height with its content; never clipped. */
  .cf-lot-tile {
    position: relative;
    isolation: isolate;
    display: grid;
    box-sizing: border-box;
    aspect-ratio: 1 / 0.82;
    border-radius: var(--cf-radius-none);
    overflow-wrap: anywhere;
    color: inherit;
    text-decoration: none;
  }

  /* The text of a tile: the name and status at the top, the value and foot line at the bottom. In one column the value follows the status label directly and the tile grows downward. */
  .cf-lot-tile__body,
  .cf-lot-tile :global(.cf-hatch) {
    display: grid;
    align-content: start;
    gap: var(--cf-spacing-5);
    padding: var(--cf-spacing-tile-padding-web);
  }

  @container (min-width: 400px) {
    .cf-lot-tile__body,
    .cf-lot-tile :global(.cf-hatch) {
      align-content: space-between;
    }
  }

  .cf-lot-tile--needs-water {
    background: var(--cf-color-status-water-fill);
    border: 0;
    color: var(--cf-color-status-water-ink);
  }

  .cf-lot-tile--ok {
    background: var(--cf-color-status-ok-fill);
    border: 1px solid var(--cf-color-status-ok-border);
    color: var(--cf-color-text-primary);
  }

  .cf-lot-tile--unknown {
    border: 1px dashed var(--cf-color-status-unknown-border);
    color: var(--cf-color-text-primary);
  }

  .cf-lot-tile--needs-calibration {
    border: 2px dashed var(--cf-color-status-calibration-border);
    color: var(--cf-color-text-primary);
  }

  .cf-lot-tile--paused {
    background: var(--cf-color-status-paused-fill);
    border: 2px solid var(--cf-color-status-paused-border);
    color: var(--cf-color-status-paused-ink);
  }

  .cf-lot-tile--no-node {
    background: transparent;
    border: 1px dotted var(--cf-color-status-no-node-border);
    color: var(--cf-color-status-no-node-ink);
  }

  /* Stale: every fill, hatch and colour is gone; only the outline and when the data is from. */
  .cf-lot-tile--stale {
    background: transparent;
    border: 1px solid var(--cf-color-stale-border);
    color: var(--cf-color-text-secondary);
  }

  /* The soil level rises from the bottom edge to the Reading's percentage, under a 2 px edge. */
  .cf-lot-tile__level {
    position: absolute;
    inset-inline: 0;
    inset-block-end: 0;
    z-index: -1;
    block-size: var(--cf-lot-level);
    box-sizing: border-box;
    border-block-start: 2px solid var(--cf-color-status-level-edge);
    background: var(--cf-color-status-ok-level);
  }

  .cf-lot-tile--needs-water .cf-lot-tile__level {
    border-block-start-color: var(--cf-color-status-water-ink);
    background: var(--cf-color-status-water-level);
  }

  /* A 12 px tick on the right edge at the height of the low Threshold. */
  .cf-lot-tile__low {
    position: absolute;
    inset-inline-end: 0;
    inset-block-end: var(--cf-lot-low);
    inline-size: 12px;
    border-block-start: 2px solid var(--cf-color-status-low-marker);
  }

  .cf-lot-tile--needs-water .cf-lot-tile__low {
    border-block-start-color: var(--cf-color-status-water-ink);
  }

  .cf-lot-tile__top,
  .cf-lot-tile__bottom {
    display: grid;
    gap: var(--cf-spacing-2);
  }

  .cf-lot-tile__name {
    font-family: var(--cf-type-tile-name-font-family);
    font-size: var(--cf-type-tile-name-font-size);
    font-weight: var(--cf-type-tile-name-font-weight);
    line-height: var(--cf-type-tile-name-line-height);
  }

  .cf-lot-tile__status {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
    text-transform: uppercase;
  }

  .cf-lot-tile--needs-calibration .cf-lot-tile__status {
    color: var(--cf-color-status-calibration-ink);
  }

  .cf-lot-tile__value {
    font-family: var(--cf-type-tile-value-web-font-family);
    font-size: var(--cf-type-tile-value-web-font-size);
    font-weight: var(--cf-type-tile-value-web-font-weight);
    line-height: var(--cf-type-tile-value-web-line-height);
  }

  .cf-lot-tile__foot {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
  }

  .cf-lot-tile--ok .cf-lot-tile__foot,
  .cf-lot-tile--unknown .cf-lot-tile__foot,
  .cf-lot-tile--needs-calibration .cf-lot-tile__foot {
    color: var(--cf-color-text-secondary);
  }

  .cf-lot-tile--stale .cf-lot-tile__foot {
    color: var(--cf-color-stale-ink);
  }
</style>
