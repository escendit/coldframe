<script lang="ts">
  import { t, type MessageKey } from '$lib/i18n';
  import type { FirstRunStep, StepState, StepTile } from '$lib/first-run';
  import Icon from './Icon.svelte';

  interface Props {
    tiles: readonly StepTile[];
    /** The next step starts its flow. The web never starts a BLE flow, so it always passes false. */
    actionable: boolean;
    onstep?: (step: FirstRunStep) => void;
  }

  let { tiles, actionable, onstep }: Props = $props();

  const stateLabel: Readonly<Record<StepState, MessageKey>> = { next: 'garden.stepNext', later: 'garden.stepLater', done: 'garden.stepDone' };
</script>

<!-- First-run step tiles (UX-DR54): 2 × 2, the next step solid orange, later steps dashed. -->
<ol class="cf-first-run" aria-label={t('garden.steps')}>
  {#each tiles as tile (tile.step)}
    <li class="cf-first-run__tile cf-first-run__tile--{tile.state}" data-step={tile.step} data-state={tile.state}>
      {#if actionable && tile.state === 'next'}
        <button type="button" class="cf-first-run__action" onclick={() => onstep?.(tile.step)}>
          <span class="cf-first-run__number">{t('garden.step', { number: tile.number })}</span>
          <span class="cf-first-run__label">{t(tile.label)}</span>
          <span class="cf-first-run__state">{t(stateLabel[tile.state])}</span>
        </button>
      {:else}
        <span class="cf-first-run__number">{t('garden.step', { number: tile.number })}</span>
        <span class="cf-first-run__label">
          {#if tile.state === 'done'}
            <Icon name="checkmark" />
          {/if}
          {t(tile.label)}
        </span>
        <span class="cf-first-run__state">{t(stateLabel[tile.state])}</span>
      {/if}
    </li>
  {/each}
</ol>

<style>
  .cf-first-run {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--cf-spacing-tile-gap);
    margin: 0 0 var(--cf-spacing-6);
    padding: 0;
    list-style: none;
  }

  @media (max-width: 399.98px) {
    .cf-first-run {
      grid-template-columns: minmax(0, 1fr);
    }
  }

  .cf-first-run__tile {
    display: grid;
    align-content: space-between;
    gap: var(--cf-spacing-3);
    min-height: 8rem;
    padding: var(--cf-spacing-tile-padding-web);
    border: 1px solid transparent;
    border-radius: var(--cf-radius-none);
    overflow-wrap: anywhere;
  }

  .cf-first-run__tile--next {
    background: var(--cf-color-primary);
    border-color: var(--cf-color-primary);
    color: var(--cf-color-ink-on-bright);
  }

  .cf-first-run__tile--later,
  .cf-first-run__tile--done {
    background: transparent;
    border: 1px dashed var(--cf-color-border-strong);
    color: var(--cf-color-text-primary);
  }

  .cf-first-run__action {
    display: grid;
    gap: var(--cf-spacing-3);
    padding: 0;
    border: 0;
    border-radius: var(--cf-radius-none);
    background: transparent;
    color: inherit;
    font: inherit;
    text-align: start;
    cursor: pointer;
  }

  .cf-first-run__number,
  .cf-first-run__state {
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
    text-transform: uppercase;
  }

  .cf-first-run__label {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-3);
    font-family: var(--cf-type-section-font-family);
    font-size: var(--cf-type-section-font-size);
    line-height: var(--cf-type-section-line-height);
  }
</style>
