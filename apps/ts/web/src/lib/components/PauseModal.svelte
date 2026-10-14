<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { applyAction, enhance } from '$app/forms';
  import { invalidateAll } from '$app/navigation';
  import { announce } from '$lib/announcer.svelte';
  import { t } from '$lib/i18n';
  import { shortDeviceId } from '$lib/i18n/format';
  import { pauseAttempts, pauseDateRefused, pauseNoticeOf, pausePrimaryLabel, pauseScopeOptions, pauseScopes, type PauseDevice, type PauseFailure, type PauseScope } from '$lib/pause';
  import Button from './Button.svelte';
  import InlineNotice from './InlineNotice.svelte';
  import { claimModal, releaseModal } from './modal-stack';
  import SegmentedChoice from './SegmentedChoice.svelte';
  import TextInput from './TextInput.svelte';

  interface Props {
    open?: boolean;
    siteId: string;
    siteName: string;
    /** The Device the modal was opened for: This Device is then selected beside Whole Site. Null from the Site menu. */
    device: PauseDevice | null;
    /** The form action that pauses: `?/pause` of the page, or `/garden?/pause` from the shell. */
    action: string;
    /** Why the last save did not happen; the modal stays open and says it. */
    failure?: PauseFailure | null;
    /** The date picked, `YYYY-MM-DD`, or empty: no end. Kept while the modal is open. */
    until?: string;
    id?: string;
  }

  let { open = $bindable(false), siteId, siteName, device, action, failure = $bindable(null), until = $bindable(''), id = 'cf-pause' }: Props = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let picked: PauseScope | null = $state(null);
  let working = $state(false);
  const attempts = pauseAttempts();

  const scopes = $derived(pauseScopes(device));
  /** The scope in force: what was picked, else the first one offered (This Device from a Device). */
  const scope = $derived(picked !== null && scopes.includes(picked) ? picked : (scopes[0] ?? 'site'));
  const notice = $derived(failure === null ? null : pauseNoticeOf(failure, siteName));
  /** The mark of a refused date goes as soon as the date is edited. */
  const dateRefused = $derived(pauseDateRefused(failure, until));

  $effect(() => {
    if (dialog === undefined) {
      return;
    }
    if (open && !dialog.open) {
      // One modal level at a time (UX-DR76): never opened over another modal.
      if (claimModal(dialog)) {
        dialog.showModal();
      } else {
        open = false;
      }
    } else if (!open && dialog.open) {
      dialog.close();
    }
  });

  // Unmounted while open (e.g. browser back): give the modal level back.
  $effect(() => {
    const mounted = dialog;
    return () => {
      if (mounted !== undefined) {
        releaseModal(mounted);
      }
    };
  });

  function choose(value: PauseScope): void {
    picked = value;
  }

  /** Closed by Cancel, Esc or a save: the next opening starts empty. */
  function closed(): void {
    if (dialog !== undefined) {
      releaseModal(dialog);
    }
    // A save still on its way belongs to this opening: its refusal must not show in the next one.
    attempts.abandon();
    working = false;
    open = false;
    picked = null;
    until = '';
    failure = null;
  }

  /**
   * Saves through the form action. A success closes the modal and reads the page again, so every
   * surface shows what the Server now says; a refusal keeps the modal and what was entered.
   */
  const submitting: SubmitFunction = () => {
    const saved = scope;
    const current = attempts.begin();
    working = true;
    return async ({ result }) => {
      if (current()) {
        working = false;
      }
      if (result.type === 'redirect') {
        await applyAction(result);
      } else if (result.type === 'success') {
        const name = saved === 'device' && device !== null ? shortDeviceId(device.id) : siteName;
        // Closed meanwhile and perhaps opened again: the Pause still happened, but that opening is not this save's to close.
        if (current()) {
          open = false;
        }
        announce(t('pause.paused', { name }), 'polite');
        await invalidateAll();
      } else if (!current()) {
        // Cancelled or closed meanwhile: nothing changed, and the abandoned attempt says nothing.
      } else if (result.type === 'failure' && typeof result.data?.notice === 'string') {
        failure = result.data as unknown as PauseFailure;
        if (failure.notice === 'date') {
          announce(t('pause.untilInvalid'), 'assertive');
        }
      } else {
        failure = { action: 'pause', scope: saved, notice: 'unexpected' };
      }
    };
  };
</script>

<!-- The Pause sheet as a DS Modal (UX-DR46): a native <dialog>, so Esc closes it, focus is trapped and returns to the opener. -->
<dialog bind:this={dialog} class="cf-pause" aria-labelledby="{id}-title" onclose={closed}>
  <form method="POST" {action} class="cf-pause__form" novalidate use:enhance={submitting}>
    <h2 id="{id}-title" class="cf-pause__title">{t('pause.title')}</h2>
    <input type="hidden" name="siteId" value={siteId} />
    <input type="hidden" name="scope" value={scope} />
    {#if scope === 'device' && device !== null}
      <input type="hidden" name="deviceId" value={device.id} />
    {/if}
    <!-- Committed by the primary button, not at once (EXPERIENCE.md, Segmented choice inside modal flows). -->
    <SegmentedChoice id="{id}-scope" label={t('pause.scope')} options={pauseScopeOptions(device)} value={scope} onchange={choose} />
    <TextInput id="{id}-until" label={t('pause.until')} type="date" name="until" bind:value={until} helper={t('pause.untilHelper')} invalid={dateRefused ? t('pause.untilInvalid') : null} />
    {#if notice !== null}
      <!-- Inside the modal: nothing behind it can be reached. Try again sends this form once more, with what was entered. -->
      <InlineNotice id="{id}-notice" message={notice.message} action={notice.tryAgain ? { label: t('notice.tryAgain') } : null} />
    {/if}
    <div class="cf-pause__actions">
      <Button variant="secondary" label={t('modal.cancel')} onclick={() => (open = false)} />
      <Button variant="primary" type="submit" label={pausePrimaryLabel(scope, siteName, device)} {working} />
    </div>
  </form>
</dialog>

<style>
  .cf-pause {
    box-sizing: border-box;
    width: min(100% - 2 * var(--cf-spacing-gutter-mobile), 32rem);
    max-width: none;
    padding: var(--cf-spacing-6);
    border: 1px solid var(--cf-color-border-subtle);
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-background);
    color: var(--cf-color-text-primary);
  }

  .cf-pause::backdrop {
    background: var(--cf-color-overlay);
  }

  .cf-pause__form {
    display: grid;
    gap: var(--cf-spacing-5);
    margin: 0;
  }

  .cf-pause__title {
    margin: 0;
    font-family: var(--cf-type-section-font-family);
    font-size: var(--cf-type-section-font-size);
    font-weight: var(--cf-type-section-font-weight);
    line-height: var(--cf-type-section-line-height);
  }

  .cf-pause__actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: var(--cf-spacing-3);
    margin-block-start: var(--cf-spacing-3);
  }
</style>
