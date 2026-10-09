<script lang="ts">
  import type { AlertRow, AlertVariant } from '$lib/alerts';
  import Hatch from './Hatch.svelte';
  import Icon from './Icon.svelte';

  interface Props {
    /** In the Server's order (newest first); never re-sorted here. */
    rows: readonly AlertRow[];
    /** The id of the heading that names this group. */
    labelledBy: string;
  }

  let { rows, labelledBy }: Props = $props();

  const variantClass: Readonly<Record<AlertVariant, string>> = {
    needsWater: 'needs-water',
    threshold: 'threshold',
    health: 'health',
    closed: 'closed',
  };
</script>

{#snippet content(row: AlertRow)}
  <span class="cf-alert-row__eyebrow"><Icon name={row.icon} /><span>{row.eyebrow}</span></span>
  <span class="cf-alert-row__title">{row.title}</span>
{/snippet}

<!--
  Alert rows (UX-DR25, UX-DR26): the four variants, told apart by shape and icon before colour. The
  models come from `alertRow`. A row is one link with one spoken label, and has no other action.
-->
<ul class="cf-alert-rows" aria-labelledby={labelledBy}>
  {#each rows as row (row.id)}
    <li data-alert={row.id} data-variant={row.variant}>
      <a class="cf-alert-row cf-alert-row--{variantClass[row.variant]}" href={row.href} aria-label={row.spoken}>
        {#if row.hatched}
          <Hatch plate>
            <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
            {@render content(row)}
          </Hatch>
        {:else}
          <span class="cf-alert-row__body">
            <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
            {@render content(row)}
          </span>
        {/if}
      </a>
    </li>
  {/each}
</ul>

<style>
  .cf-alert-rows {
    display: grid;
    gap: var(--cf-spacing-3);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .cf-alert-row {
    display: grid;
    box-sizing: border-box;
    min-height: 44px;
    border-radius: var(--cf-radius-none);
    overflow-wrap: anywhere;
    color: inherit;
    text-decoration: none;
  }

  .cf-alert-row__body,
  .cf-alert-row :global(.cf-hatch) {
    display: grid;
    align-content: start;
    gap: var(--cf-spacing-2);
    padding: var(--cf-spacing-5);
  }

  /* The only orange row: an open low-side soil-moisture Threshold Alert. */
  .cf-alert-row--needs-water {
    background: var(--cf-color-status-water-fill);
    border: 0;
    color: var(--cf-color-status-water-ink);
  }

  .cf-alert-row--threshold {
    background: var(--cf-color-layer-01);
    border: 2px solid var(--cf-color-border-strong);
    color: var(--cf-color-text-primary);
  }

  .cf-alert-row--health {
    border: 1px dashed var(--cf-color-status-unknown-border);
    color: var(--cf-color-text-primary);
  }

  .cf-alert-row--closed {
    background: transparent;
    border: 1px solid var(--cf-color-border-subtle);
    color: var(--cf-color-text-secondary);
  }

  .cf-alert-row__eyebrow {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
    text-transform: uppercase;
  }

  .cf-alert-row__title {
    font-family: var(--cf-type-section-font-family);
    font-size: var(--cf-type-section-font-size);
    font-weight: var(--cf-type-section-font-weight);
    line-height: var(--cf-type-section-line-height);
  }
</style>
