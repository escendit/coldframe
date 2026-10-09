<script lang="ts">
  import { t } from '$lib/i18n';

  interface Props {
    id: string;
    /** Names the switch ("Mute Home garden"). */
    label: string;
    /** Form field: present when on, absent when off, as any checkbox. */
    name?: string;
    checked?: boolean;
    helper?: string;
    /** Called with the new state when the user flips the switch. */
    onchange?: (checked: boolean) => void;
  }

  let { id, label, name, checked = $bindable(false), helper, onchange }: Props = $props();
</script>

<!--
  DS Toggle (UX-DR49): a native checkbox with switch semantics. The input covers the whole row, so the
  row is one target; the track is drawn next to it and the state is also written out, never colour alone.
-->
<div class="cf-toggle-field">
  <label class="cf-toggle">
    <input
      {id}
      {name}
      type="checkbox"
      role="switch"
      class="cf-toggle__input"
      aria-describedby={helper !== undefined ? `${id}-helper` : undefined}
      bind:checked
      onchange={() => onchange?.(checked)}
    />
    <span class="cf-toggle__track" class:cf-toggle__track--on={checked} aria-hidden="true"><span class="cf-toggle__thumb"></span></span>
    <span class="cf-toggle__label">{label}</span>
    <span class="cf-toggle__state" aria-hidden="true">{checked ? t('toggle.on') : t('toggle.off')}</span>
  </label>
  {#if helper !== undefined}
    <p id="{id}-helper" class="cf-toggle__helper">{helper}</p>
  {/if}
</div>

<style>
  .cf-toggle-field {
    display: grid;
    gap: var(--cf-spacing-3);
  }

  .cf-toggle {
    position: relative;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-4);
    min-height: 44px;
    color: var(--cf-color-text-primary);
  }

  .cf-toggle__input {
    position: absolute;
    inset: 0;
    inline-size: 100%;
    block-size: 100%;
    margin: 0;
    opacity: 0;
    cursor: pointer;
  }

  .cf-toggle__track {
    display: flex;
    flex: none;
    align-items: center;
    box-sizing: border-box;
    inline-size: 3rem;
    block-size: 1.5rem;
    padding: 0 var(--cf-spacing-1);
    border: 1px solid var(--cf-color-border-strong);
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-layer-01);
  }

  .cf-toggle__thumb {
    inline-size: 1rem;
    block-size: 1rem;
    background: var(--cf-color-text-primary);
  }

  .cf-toggle__track--on {
    justify-content: flex-end;
    border-color: var(--cf-color-primary);
    background: var(--cf-color-primary);
  }

  .cf-toggle__track--on .cf-toggle__thumb {
    background: var(--cf-color-ink-on-bright);
  }

  /* The input is invisible, so its two-tone focus ring is drawn on the track. */
  .cf-toggle__input:focus-visible + .cf-toggle__track {
    outline: var(--cf-spacing-focus-ring) solid var(--cf-color-focus);
    outline-offset: var(--cf-spacing-focus-offset);
    box-shadow: 0 0 0 var(--cf-spacing-focus-offset) var(--cf-color-focus-gap);
  }

  .cf-toggle__label {
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-toggle__state,
  .cf-toggle__helper {
    margin: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }
</style>
