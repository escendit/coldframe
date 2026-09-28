<script lang="ts">
  import Button from './Button.svelte';

  interface Action {
    readonly label: string;
    readonly href: string;
    /** Full page load (the action leaves the SvelteKit router). */
    readonly reload?: boolean;
  }

  interface Props {
    message: string;
    /** At most one action; none where nothing can be done here. Never dismissable. */
    action?: Action | null;
    id?: string;
  }

  let { message, action = null, id }: Props = $props();
</script>

<div {id} class="cf-inline-notice">
  <p class="cf-inline-notice__message">{message}</p>
  {#if action !== null}
    <div class="cf-inline-notice__action">
      <Button variant="ghost" label={action.label} href={action.href} reload={action.reload ?? false} />
    </div>
  {/if}
</div>

<style>
  .cf-inline-notice {
    display: grid;
    gap: var(--cf-spacing-3);
    padding: var(--cf-spacing-4) var(--cf-spacing-5);
    background: var(--cf-color-layer-01);
    border-left: 3px solid var(--cf-color-border-strong);
    color: var(--cf-color-text-primary);
  }

  .cf-inline-notice__message {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-inline-notice__action {
    margin-inline-start: calc(-1 * var(--cf-spacing-5));
  }
</style>
