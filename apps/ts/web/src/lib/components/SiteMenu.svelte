<script lang="ts">
  import { tick } from 'svelte';
  import { t } from '$lib/i18n';
  import type { SiteMenuItem } from '$lib/site-menu';
  import Icon from './Icon.svelte';

  interface Props {
    siteName: string;
    /** From `siteMenuItems`: already gated by Role, Pause availability and stale mode. */
    items: readonly SiteMenuItem[];
    id?: string;
  }

  let { siteName, items, id = 'cf-site-menu' }: Props = $props();

  let open = $state(false);
  let root: HTMLDivElement | undefined = $state();
  let trigger: HTMLButtonElement | undefined = $state();

  function entries(): HTMLElement[] {
    return root === undefined ? [] : [...root.querySelectorAll<HTMLElement>('[role="menuitem"]')];
  }

  async function show(): Promise<void> {
    open = true;
    await tick();
    entries()[0]?.focus();
  }

  function hide(returnFocus: boolean): void {
    open = false;
    if (returnFocus) {
      trigger?.focus();
    }
  }

  function keydown(event: KeyboardEvent): void {
    if (!open) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      hide(true);
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const list = entries();
      const index = list.indexOf(document.activeElement as HTMLElement);
      const step = event.key === 'ArrowDown' ? 1 : -1;
      list[(index + step + list.length) % list.length]?.focus();
    }
  }

  /** Tab or Shift+Tab out of the menu closes it without moving focus back. */
  function focusout(event: FocusEvent): void {
    if (open && root !== undefined && !root.contains(event.relatedTarget as Node | null)) {
      hide(false);
    }
  }

  function outside(event: MouseEvent): void {
    if (open && root !== undefined && !root.contains(event.target as Node)) {
      hide(false);
    }
  }
</script>

<svelte:window onclick={outside} />

<!-- DS overflow menu (UX-DR22). Items a Role cannot use are not in `items` at all. -->
<div class="cf-site-menu" bind:this={root} onkeydown={keydown} onfocusout={focusout} role="presentation">
  <button
    bind:this={trigger}
    type="button"
    class="cf-site-menu__trigger"
    aria-label={t('siteMenu.open', { name: siteName })}
    aria-haspopup="menu"
    aria-expanded={open ? 'true' : 'false'}
    aria-controls={open ? `${id}-items` : undefined}
    onclick={() => {
      if (open) {
        hide(false);
      } else {
        void show();
      }
    }}
  >
    <Icon name="overflow-menu--vertical" size={20} />
  </button>
  {#if open}
    <ul id="{id}-items" class="cf-site-menu__items" role="menu" aria-label={t('siteMenu.open', { name: siteName })}>
      {#each items as item (item.action)}
        <li role="none">
          {#if item.disabled || item.href === null}
            <span class="cf-site-menu__item cf-site-menu__item--disabled" role="menuitem" aria-disabled="true" tabindex="-1">
              <span>{t(item.label, { name: siteName })}</span>
              {#if item.disabled}
                <span class="cf-site-menu__reason">{t('siteMenu.needsServer')}</span>
              {/if}
            </span>
          {:else}
            <a class="cf-site-menu__item" role="menuitem" tabindex="-1" href={item.href} onclick={() => {
              hide(false);
            }}>{t(item.label, { name: siteName })}</a>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .cf-site-menu {
    position: relative;
    display: inline-flex;
  }

  .cf-site-menu__trigger {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 44px;
    min-height: 44px;
    border: 0;
    border-radius: var(--cf-radius-none);
    background: transparent;
    color: inherit;
    cursor: pointer;
  }

  .cf-site-menu__items {
    position: absolute;
    inset-block-start: 100%;
    inset-inline-start: 0;
    z-index: 2;
    min-width: 14rem;
    margin: 0;
    padding: 0;
    list-style: none;
    background: var(--cf-color-background);
    border: 1px solid var(--cf-color-border-subtle);
    color: var(--cf-color-text-primary);
  }

  .cf-site-menu__item {
    display: grid;
    align-content: center;
    min-height: 48px;
    padding: var(--cf-spacing-3) var(--cf-spacing-5);
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    text-decoration: none;
    overflow-wrap: anywhere;
  }

  .cf-site-menu__item:hover {
    background: var(--cf-color-layer-01);
  }

  /* Rows abut, so the focus ring is drawn inset. */
  .cf-site-menu .cf-site-menu__item:focus-visible {
    outline: none;
    box-shadow:
      inset 0 0 0 var(--cf-spacing-focus-ring) var(--cf-color-focus),
      inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-color-focus-gap);
  }

  .cf-site-menu__item--disabled {
    color: var(--cf-color-text-secondary);
    cursor: not-allowed;
  }

  .cf-site-menu__reason {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
  }
</style>
