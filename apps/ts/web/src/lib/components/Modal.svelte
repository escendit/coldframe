<script lang="ts">
  import { t } from '$lib/i18n';
  import Button from './Button.svelte';
  import { claimModal, releaseModal } from './modal-stack';

  interface Props {
    open?: boolean;
    /** Names the object and the result, e.g. "Remove Lot Herbs?". */
    title: string;
    detail?: string;
    /** The result action, a verb naming the result. */
    actionLabel: string;
    actionHref?: string;
    /** Full page load for the action link. */
    reload?: boolean;
    onaction?: () => void;
    id?: string;
  }

  let { open = $bindable(false), title, detail, actionLabel, actionHref, reload = false, onaction, id = 'cf-modal' }: Props = $props();

  let dialog: HTMLDialogElement | undefined = $state();

  $effect(() => {
    if (dialog === undefined) {
      return;
    }
    if (open && !dialog.open) {
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

  function closed(): void {
    if (dialog !== undefined) {
      releaseModal(dialog);
    }
    open = false;
  }
</script>

<!-- Native <dialog>: Esc closes it, focus is trapped and returns to the opener. -->
<dialog bind:this={dialog} class="cf-modal" aria-labelledby="{id}-title" aria-describedby={detail !== undefined ? `${id}-detail` : undefined} onclose={closed}>
  <h2 id="{id}-title" class="cf-modal__title">{title}</h2>
  {#if detail !== undefined}
    <p id="{id}-detail" class="cf-modal__detail">{detail}</p>
  {/if}
  <div class="cf-modal__actions">
    <Button variant="secondary" label={t('modal.cancel')} onclick={() => (open = false)} />
    {#if actionHref !== undefined}
      <Button variant="primary" label={actionLabel} href={actionHref} {reload} />
    {:else}
      <Button variant="primary" label={actionLabel} onclick={() => onaction?.()} />
    {/if}
  </div>
</dialog>

<style>
  .cf-modal {
    box-sizing: border-box;
    width: min(100% - 2 * var(--cf-spacing-gutter-mobile), 32rem);
    max-width: none;
    padding: var(--cf-spacing-6);
    border: 1px solid var(--cf-color-border-subtle);
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-background);
    color: var(--cf-color-text-primary);
  }

  .cf-modal::backdrop {
    background: var(--cf-color-overlay);
  }

  .cf-modal__title {
    margin: 0 0 var(--cf-spacing-5);
    font-family: var(--cf-type-section-font-family);
    font-size: var(--cf-type-section-font-size);
    font-weight: var(--cf-type-section-font-weight);
    line-height: var(--cf-type-section-line-height);
  }

  .cf-modal__detail {
    margin: 0 0 var(--cf-spacing-6);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-modal__actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: var(--cf-spacing-3);
  }
</style>
