<script lang="ts">
  import { t, type MessageKey } from '$lib/i18n';

  interface Props {
    currentPath: string;
    /** Below 672 px the nav is shown only while the header menu is open. */
    open: boolean;
    onnavigate?: () => void;
  }

  let { currentPath, open, onnavigate }: Props = $props();

  interface Item {
    readonly href: string;
    readonly label: MessageKey;
  }

  const items: readonly Item[] = [
    { href: '/garden', label: 'nav.garden' },
    { href: '/alerts', label: 'nav.alerts' },
    { href: '/devices', label: 'nav.devices' },
    { href: '/members', label: 'nav.members' },
  ];

  const footer: readonly Item[] = [{ href: '/settings', label: 'nav.settings' }];

  function navigated(): void {
    onnavigate?.();
  }

  function isCurrent(href: string): boolean {
    return currentPath === href || currentPath.startsWith(`${href}/`);
  }
</script>

<nav id="cf-side-nav" class="cf-side-nav" class:cf-side-nav--open={open} aria-label={t('shell.navigation')}>
  <ul class="cf-side-nav__list">
    {#each items as item (item.href)}
      <li>
        <a class="cf-side-nav__item" href={item.href} aria-current={isCurrent(item.href) ? 'page' : undefined} onclick={navigated}>{t(item.label)}</a>
      </li>
    {/each}
  </ul>
  <ul class="cf-side-nav__list cf-side-nav__footer">
    {#each footer as item (item.href)}
      <li>
        <a class="cf-side-nav__item" href={item.href} aria-current={isCurrent(item.href) ? 'page' : undefined} onclick={navigated}>{t(item.label)}</a>
      </li>
    {/each}
  </ul>
</nav>

<style>
  .cf-side-nav {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    background: var(--cf-color-layer-01);
    border-right: 1px solid var(--cf-color-border-subtle);
  }

  .cf-side-nav__list {
    margin: 0;
    padding: var(--cf-spacing-3) 0;
    list-style: none;
  }

  .cf-side-nav__footer {
    border-top: 1px solid var(--cf-color-border-subtle);
  }

  .cf-side-nav__item {
    display: flex;
    align-items: center;
    min-height: 44px;
    padding: var(--cf-spacing-3) var(--cf-spacing-5);
    border-left: 3px solid transparent;
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    text-decoration: none;
    overflow-wrap: anywhere;
  }

  .cf-side-nav__item:hover {
    background: var(--cf-color-layer-02);
  }

  /* Rows abut, so the focus ring is drawn inset. */
  .cf-side-nav__item:focus-visible {
    outline: none;
    box-shadow:
      inset 0 0 0 var(--cf-spacing-focus-ring) var(--cf-color-focus),
      inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-color-focus-gap);
  }

  .cf-side-nav__item[aria-current='page'] {
    border-left-color: var(--cf-color-primary-text);
    color: var(--cf-color-primary-text);
  }

  @media (max-width: 671.98px) {
    .cf-side-nav {
      display: none;
      border-right: 0;
      border-bottom: 1px solid var(--cf-color-border-subtle);
    }

    .cf-side-nav--open {
      display: flex;
    }
  }
</style>
