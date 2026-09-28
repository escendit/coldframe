<script lang="ts">
  import { t } from '$lib/i18n';
  import Icon from './Icon.svelte';

  interface Props {
    id: string;
    label: string;
    value?: string;
    type?: 'text' | 'password' | 'email';
    helper?: string;
    /** The reason the value is invalid; replaces the helper and marks the field invalid. */
    invalid?: string | null;
    name?: string;
    autocomplete?: HTMLInputElement['autocomplete'];
  }

  let { id, label, value = $bindable(''), type = 'text', helper, invalid = null, name, autocomplete }: Props = $props();

  let revealed = $state(false);

  const isInvalid = $derived(invalid !== null && invalid !== '');
  const describedBy = $derived(isInvalid ? `${id}-reason` : helper !== undefined ? `${id}-helper` : undefined);
  const inputType = $derived(type === 'password' && revealed ? 'text' : type);
</script>

<div class="cf-text-input" class:cf-text-input--invalid={isInvalid}>
  <label class="cf-text-input__label" for={id}>{label}</label>
  <div class="cf-text-input__control">
    <input
      {id}
      {name}
      {autocomplete}
      type={inputType}
      class="cf-text-input__field"
      aria-invalid={isInvalid ? 'true' : undefined}
      aria-describedby={describedBy}
      bind:value
    />
    {#if isInvalid}
      <span class="cf-text-input__error-icon"><Icon name="error--filled" /></span>
    {/if}
    {#if type === 'password'}
      <button
        type="button"
        class="cf-text-input__reveal"
        aria-label={revealed ? t('field.hidePassword') : t('field.showPassword')}
        aria-pressed={revealed ? 'true' : 'false'}
        onclick={() => (revealed = !revealed)}
      >
        <Icon name="view" />
      </button>
    {/if}
  </div>
  {#if isInvalid}
    <p id="{id}-reason" class="cf-text-input__reason">{invalid}</p>
  {:else if helper !== undefined}
    <p id="{id}-helper" class="cf-text-input__helper">{helper}</p>
  {/if}
</div>

<style>
  .cf-text-input {
    display: grid;
    gap: var(--cf-spacing-3);
  }

  .cf-text-input__label {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-text-input__control {
    display: flex;
    align-items: center;
    min-height: var(--cf-spacing-field-height);
    background: var(--cf-color-field-01);
    border-bottom: 1px solid var(--cf-color-border-strong);
  }

  .cf-text-input--invalid .cf-text-input__control {
    outline: 2px solid var(--cf-color-support-error);
    outline-offset: -2px;
  }

  .cf-text-input__field {
    flex: 1;
    min-width: 0;
    min-height: 44px;
    padding: 0 var(--cf-spacing-5);
    border: 0;
    border-radius: var(--cf-radius-none);
    background: transparent;
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
  }

  .cf-text-input__error-icon {
    display: inline-flex;
    padding-inline: var(--cf-spacing-3);
    color: var(--cf-color-support-error);
  }

  .cf-text-input__reveal {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 44px;
    min-height: 44px;
    border: 0;
    border-radius: var(--cf-radius-none);
    background: transparent;
    color: var(--cf-color-text-primary);
    cursor: pointer;
  }

  .cf-text-input__helper,
  .cf-text-input__reason {
    margin: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }

  .cf-text-input__reason {
    color: var(--cf-color-support-error-text);
  }
</style>
