<script lang="ts">
  import { t } from '$lib/i18n';
  import Button from './Button.svelte';
  import Icon from './Icon.svelte';
  import TextInput from './TextInput.svelte';

  interface Props {
    /** The browser's zone; `undefined` until the page has asked the browser, `null` when it has none. */
    detected: string | null | undefined;
    /** The zone this browser already confirmed or picked. It wins over detection, always. */
    chosen: string | null;
    /** IANA zone IDs offered by Change. */
    zones: readonly string[];
    /** Form field carrying the confirmed zone; empty until the user confirms or picks one. */
    name?: string;
    id?: string;
  }

  let { detected, chosen, zones, name = 'timeZone', id = 'cf-time-zone' }: Props = $props();

  let picked: string | null = $state(null);
  let changing = $state(false);
  let filter = $state('');

  /** A choice (this visit's, then this browser's) is never overwritten by detection. */
  const zone = $derived(picked ?? chosen ?? detected ?? null);
  const confirmed = $derived(picked !== null || chosen !== null);
  const matches = $derived.by(() => {
    const needle = filter.trim().toLowerCase().replace(/\s+/gu, '_');
    return needle === '' ? zones : zones.filter((candidate) => candidate.toLowerCase().includes(needle));
  });

  function confirm(): void {
    if (zone !== null) {
      picked = zone;
    }
  }

  function pick(candidate: string): void {
    picked = candidate;
    changing = false;
    filter = '';
  }
</script>

<!-- Time-zone confirm panel (UX-DR61): propose the browser's zone; Confirm or Change. -->
<fieldset class="cf-time-zone" aria-describedby="{id}-helper">
  <legend class="cf-time-zone__legend">{t('timeZone.legend')}</legend>
  <input type="hidden" {name} value={confirmed && zone !== null ? zone : ''} />
  {#if changing}
    <TextInput id="{id}-filter" label={t('timeZone.filter')} helper={t('timeZone.filterHelper')} bind:value={filter} autocomplete="off" />
    {#if matches.length === 0}
      <p class="cf-time-zone__text">{t('timeZone.none')}</p>
    {:else}
      <ul class="cf-time-zone__list" aria-label={t('timeZone.list')}>
        {#each matches as candidate (candidate)}
          {@const current = candidate === zone}
          <li>
            <button type="button" class="cf-time-zone__option" aria-pressed={current ? 'true' : 'false'} onclick={() => {
                pick(candidate);
              }}>
              {#if current}
                <Icon name="checkmark" />
              {/if}
              <span>{candidate}</span>
            </button>
          </li>
        {/each}
      </ul>
    {/if}
  {:else if detected !== undefined || chosen !== null}
    {#if confirmed && zone !== null}
      <p class="cf-time-zone__text">{t('timeZone.chosen', { zone })}</p>
    {:else if zone !== null}
      <p class="cf-time-zone__text">{t('timeZone.question', { zone })}</p>
    {:else}
      <p class="cf-time-zone__text">{t('timeZone.unknown')}</p>
    {/if}
    <div class="cf-time-zone__actions">
      {#if !confirmed && zone !== null}
        <Button variant="secondary" label={t('timeZone.confirm')} onclick={confirm} />
      {/if}
      <Button variant="ghost" label={t('timeZone.change')} onclick={() => (changing = true)} />
    </div>
  {/if}
  <p id="{id}-helper" class="cf-time-zone__helper">{t('timeZone.helper')}</p>
</fieldset>

<style>
  .cf-time-zone {
    display: grid;
    gap: var(--cf-spacing-4);
    min-width: 0;
    margin: 0;
    padding: var(--cf-spacing-5);
    border: 1px dashed var(--cf-color-support-warning);
    border-radius: var(--cf-radius-none);
    background: transparent;
  }

  .cf-time-zone__legend {
    padding: 0 var(--cf-spacing-2);
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-time-zone__text {
    margin: 0;
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-time-zone__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-3);
  }

  .cf-time-zone__list {
    max-height: 20rem;
    margin: 0;
    padding: 0;
    overflow-y: auto;
    list-style: none;
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-time-zone__option {
    display: flex;
    align-items: center;
    gap: var(--cf-spacing-3);
    width: 100%;
    min-height: 44px;
    padding: var(--cf-spacing-3) var(--cf-spacing-5);
    border: 0;
    border-bottom: 1px solid var(--cf-color-border-subtle);
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-background);
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    text-align: start;
    overflow-wrap: anywhere;
    cursor: pointer;
  }

  .cf-time-zone__option:hover {
    background: var(--cf-color-layer-01);
  }

  /* Rows abut, so the focus ring is drawn inset. */
  .cf-time-zone__option:focus-visible {
    outline: none;
    box-shadow:
      inset 0 0 0 var(--cf-spacing-focus-ring) var(--cf-color-focus),
      inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-color-focus-gap);
  }

  .cf-time-zone__helper {
    margin: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }
</style>
