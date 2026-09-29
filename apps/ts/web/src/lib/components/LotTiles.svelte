<script lang="ts">
  import { t } from '$lib/i18n';
  import type { Lot } from '$lib/lots';
  import Icon from './Icon.svelte';

  interface Props {
    /** In the Server's order (status, then creation); never re-sorted here (UX-DR20). */
    lots: readonly Lot[];
  }

  let { lots }: Props = $props();
</script>

<!--
  Lot tiles (UX-DR18, UX-DR20). Only the no-Node variant exists in Story 1.9; any other status
  shows the Lot name alone until its variant arrives. Tiles are not tappable yet: Lot detail and
  Add a Node come later. Each tile is one accessibility element.
-->
<div class="cf-lot-grid-frame">
  <ul class="cf-lot-grid" aria-label={t('garden.lots')}>
    {#each lots as lot (lot.id)}
      <li class="cf-lot-grid__cell" data-lot={lot.id} data-status={lot.status}>
        {#if lot.status === 'noNode'}
          <div class="cf-lot-tile cf-lot-tile--no-node" role="img" aria-label={t('lotTile.noNodeLabel', { lotName: lot.name })}>
            <div class="cf-lot-tile__top">
              <span class="cf-lot-tile__name">{lot.name}</span>
              <span class="cf-lot-tile__status"><Icon name="add" />{t('lotTile.noNode')}</span>
            </div>
            <div class="cf-lot-tile__bottom">
              <span class="cf-lot-tile__value">+</span>
              <span class="cf-lot-tile__foot">{t('lotTile.addNode')}</span>
            </div>
          </div>
        {:else}
          <div class="cf-lot-tile cf-lot-tile--other" role="img" aria-label={lot.name}>
            <div class="cf-lot-tile__top">
              <span class="cf-lot-tile__name">{lot.name}</span>
            </div>
          </div>
        {/if}
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
    display: grid;
    align-content: space-between;
    gap: var(--cf-spacing-5);
    box-sizing: border-box;
    aspect-ratio: 1 / 0.82;
    padding: var(--cf-spacing-tile-padding-web);
    border-radius: var(--cf-radius-none);
    overflow-wrap: anywhere;
  }

  .cf-lot-tile--no-node {
    background: transparent;
    border: 1px dotted var(--cf-color-status-no-node-border);
    color: var(--cf-color-status-no-node-ink);
  }

  .cf-lot-tile--other {
    background: transparent;
    border: 1px solid var(--cf-color-border-subtle);
    color: var(--cf-color-text-primary);
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
</style>
