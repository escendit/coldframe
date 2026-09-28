<script lang="ts">
  interface Props {
    /** A verb naming the result ("Sign in", "Pause Home garden"). */
    label: string;
    variant?: 'primary' | 'secondary' | 'ghost';
    type?: 'button' | 'submit';
    /** Renders a link that looks like a button. */
    href?: string;
    /** Full page load for links and forms that leave the SvelteKit router (OIDC routes). */
    reload?: boolean;
    /** While true the label is replaced in place by `workingLabel`, with no animated indicator. */
    working?: boolean;
    workingLabel?: string;
    id?: string;
    onclick?: (event: MouseEvent) => void;
  }

  let { label, variant = 'primary', type = 'button', href, reload = false, working = false, workingLabel, id, onclick }: Props = $props();

  const text = $derived(working && workingLabel !== undefined ? workingLabel : label);

  function click(event: MouseEvent): void {
    if (working) {
      event.preventDefault();
      return;
    }
    onclick?.(event);
  }
</script>

{#if href !== undefined}
  <a
    {id}
    class="cf-button cf-button--{variant}"
    {href}
    data-sveltekit-reload={reload ? '' : undefined}
    aria-disabled={working ? 'true' : undefined}
    onclick={click}
  >
    <span class="cf-button__label">{text}</span>
  </a>
{:else}
  <button {id} class="cf-button cf-button--{variant}" {type} aria-disabled={working ? 'true' : undefined} onclick={click}>
    <span class="cf-button__label">{text}</span>
  </button>
{/if}

<style>
  .cf-button {
    display: inline-flex;
    align-items: center;
    justify-content: flex-start;
    box-sizing: border-box;
    min-height: var(--cf-spacing-button-height);
    min-width: 44px;
    max-width: 100%;
    padding: var(--cf-spacing-4) var(--cf-spacing-7) var(--cf-spacing-4) var(--cf-spacing-5);
    border: 1px solid transparent;
    border-radius: var(--cf-radius-none);
    font-family: var(--cf-type-button-font-family);
    font-size: var(--cf-type-button-font-size);
    font-weight: var(--cf-type-button-font-weight);
    line-height: 1.25;
    letter-spacing: var(--cf-type-button-letter-spacing);
    text-transform: var(--cf-type-button-text-transform);
    text-align: start;
    text-decoration: none;
    white-space: normal;
    overflow-wrap: anywhere;
    cursor: pointer;
  }

  .cf-button--primary {
    background: var(--cf-color-primary);
    color: var(--cf-color-ink-on-bright);
  }

  .cf-button--primary:hover {
    background: var(--cf-color-primary-hover);
  }

  .cf-button--primary:active {
    background: var(--cf-color-primary-active);
  }

  .cf-button--secondary {
    background: var(--cf-color-button-secondary);
    color: var(--cf-color-text-on-color);
  }

  .cf-button--ghost {
    background: transparent;
    color: var(--cf-color-primary-text);
    padding-inline: var(--cf-spacing-5);
  }

  .cf-button--ghost:hover {
    background: var(--cf-color-layer-01);
  }

  .cf-button[aria-disabled='true'] {
    cursor: progress;
  }
</style>
