<script lang="ts" generics="T extends string">
  import Icon from './Icon.svelte';

  interface Option {
    readonly value: T;
    readonly label: string;
  }

  interface Props {
    /** Names the group (legend). */
    label: string;
    options: readonly Option[];
    value: T;
    /** Called with the segment picked; never for the selected one. */
    onchange?: (value: T) => void;
    /**
     * With a name each segment is a submit button carrying its value under that name, so the choice
     * applies at once through the form the group sits in.
     */
    name?: string;
    helper?: string;
    id?: string;
  }

  let { label, options, value, onchange, name, helper, id = 'cf-segmented' }: Props = $props();
</script>

<fieldset class="cf-segmented" aria-describedby={helper !== undefined ? `${id}-helper` : undefined}>
  <legend class="cf-segmented__legend">{label}</legend>
  <div class="cf-segmented__segments">
    {#each options as option (option.value)}
      {@const selected = option.value === value}
      <button
        type={name === undefined ? 'button' : 'submit'}
        {name}
        value={name === undefined ? undefined : option.value}
        class="cf-segmented__segment"
        class:cf-segmented__segment--selected={selected}
        aria-pressed={selected ? 'true' : 'false'}
        onclick={(event) => {
          if (selected) {
            // The selected segment is already in force: nothing is submitted again.
            event.preventDefault();
            return;
          }
          onchange?.(option.value);
        }}
      >
        {#if selected}
          <Icon name="checkmark" />
        {/if}
        <span class="cf-segmented__label">{option.label}</span>
      </button>
    {/each}
  </div>
  {#if helper !== undefined}
    <p id="{id}-helper" class="cf-segmented__helper">{helper}</p>
  {/if}
</fieldset>

<style>
  .cf-segmented {
    margin: 0;
    padding: 0;
    border: 0;
    min-width: 0;
  }

  .cf-segmented__legend {
    padding: 0;
    margin-bottom: var(--cf-spacing-3);
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-segmented__segments {
    display: flex;
    flex-wrap: wrap;
    gap: 1px;
    background: var(--cf-color-border-subtle);
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-segmented__segment {
    display: inline-flex;
    flex: 1 1 0;
    align-items: center;
    justify-content: center;
    gap: var(--cf-spacing-3);
    min-width: fit-content;
    min-height: var(--cf-spacing-button-height);
    padding: var(--cf-spacing-4) var(--cf-spacing-5);
    border: 0;
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-layer-01);
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    text-align: center;
    white-space: normal;
    cursor: pointer;
  }

  .cf-segmented__segment--selected {
    background: var(--cf-color-primary);
    color: var(--cf-color-ink-on-bright);
  }

  /* Segments abut, so the focus ring is drawn inset: ring outside, gap inside over the fill. */
  .cf-segmented__segment:focus-visible {
    outline: none;
    box-shadow:
      inset 0 0 0 var(--cf-spacing-focus-ring) var(--cf-color-focus),
      inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-color-focus-gap);
  }

  .cf-segmented__helper {
    margin: var(--cf-spacing-3) 0 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }
</style>
