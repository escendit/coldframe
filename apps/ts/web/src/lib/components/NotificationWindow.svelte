<script lang="ts">
  import { untrack } from 'svelte';
  import { t } from '$lib/i18n';
  import { windowBar, windowOf, type NotificationWindow } from '$lib/notifications';
  import Hatch from './Hatch.svelte';
  import TextInput from './TextInput.svelte';

  interface Props {
    /** The window in force, as the Server holds it. */
    window: NotificationWindow;
    /** What the fields start with; the window in force unless a submit kept other values. */
    from?: string;
    to?: string;
    /** The reason a field is invalid; replaces its helper. */
    fromInvalid?: string | null;
    toInvalid?: string | null;
    id?: string;
  }

  let { window, from, to, fromInvalid = null, toInvalid = null, id = 'cf-window' }: Props = $props();

  // The props seed the fields; afterwards the control owns what is typed.
  let start = $state(untrack(() => from ?? window.from));
  let end = $state(untrack(() => to ?? window.to));

  /** The bar and the range preview what is typed while it is a window, else the window in force. */
  const shown = $derived.by(() => {
    const typed = windowOf(start, end);
    return typeof typed === 'string' ? window : typed;
  });
  const bar = $derived(windowBar(shown));
</script>

<!--
  Notification Window control (UX-DR47): two time fields, then the 24 h bar (the interactive fill inside
  the window, hatch outside) and the range in large type. The bar is decorative and hidden from screen
  readers; the fields and the range say everything it shows.
-->
<div class="cf-window">
  <div class="cf-window__fields">
    <TextInput id="{id}-from" name="from" type="time" label={t('notifications.windowFrom')} bind:value={start} invalid={fromInvalid} />
    <TextInput id="{id}-to" name="to" type="time" label={t('notifications.windowTo')} helper={t('notifications.windowToHelper')} bind:value={end} invalid={toInvalid} />
  </div>
  <div class="cf-window__bar" aria-hidden="true">
    <Hatch>
      <span class="cf-window__inside" style:margin-inline-start="{bar.start}%" style:inline-size="{bar.length}%"></span>
    </Hatch>
    <div class="cf-window__axis">
      {#each ['00', '06', '12', '18', '24'] as hour (hour)}
        <span>{hour}</span>
      {/each}
    </div>
  </div>
  <p class="cf-window__range">{t('notifications.windowRange', { from: shown.from, to: shown.to })}</p>
  <p class="cf-window__helper">{t('notifications.windowHelper', { from: shown.from })}</p>
</div>

<style>
  .cf-window {
    display: grid;
    gap: var(--cf-spacing-4);
    min-width: 0;
  }

  .cf-window__fields {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
    gap: var(--cf-spacing-5);
  }

  .cf-window__bar {
    display: grid;
    gap: var(--cf-spacing-2);
  }

  .cf-window__inside {
    display: block;
    block-size: var(--cf-spacing-7);
    background: var(--cf-color-primary);
  }

  .cf-window__axis {
    display: flex;
    justify-content: space-between;
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-window__range {
    margin: 0;
    font-family: var(--cf-type-headline-font-family);
    font-size: var(--cf-type-headline-font-size);
    font-weight: var(--cf-type-headline-font-weight);
    line-height: var(--cf-type-headline-line-height);
    overflow-wrap: anywhere;
  }

  .cf-window__helper {
    margin: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }
</style>
