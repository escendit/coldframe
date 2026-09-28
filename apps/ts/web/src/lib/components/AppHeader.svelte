<script lang="ts">
  import { t } from '$lib/i18n';
  import type { DisplayUser } from '$lib/user';

  interface Props {
    user: DisplayUser;
    menuOpen: boolean;
    onmenu: () => void;
  }

  let { user, menuOpen, onmenu }: Props = $props();
</script>

<header class="cf-app-header">
  <button type="button" class="cf-app-header__menu" aria-expanded={menuOpen ? 'true' : 'false'} aria-controls="cf-side-nav" onclick={onmenu}>
    {t('shell.menu')}
  </button>
  <a class="cf-app-header__mark" href="/garden" aria-label={t('shell.home')}>{t('app.name')}</a>
  <span class="cf-app-header__spacer"></span>
  <span class="cf-app-header__initials" role="img" aria-label={t('shell.user', { name: user.displayName })}>{user.initials}</span>
</header>

<style>
  .cf-app-header {
    /* The header is always dark, so the focus ring inverts to stay visible on it. */
    --cf-header-focus: var(--cf-color-text-on-color);
    --cf-header-focus-gap: var(--cf-color-header-bg);

    position: sticky;
    top: 0;
    z-index: 1;
    display: flex;
    align-items: center;
    gap: var(--cf-spacing-3);
    min-height: var(--cf-spacing-header-height);
    padding-inline: var(--cf-spacing-3) var(--cf-spacing-5);
    background: var(--cf-color-header-bg);
    border-bottom: 1px solid var(--cf-color-header-border);
    color: var(--cf-color-text-on-color);
  }

  .cf-app-header :global(:focus-visible) {
    outline: var(--cf-spacing-focus-ring) solid var(--cf-header-focus);
    outline-offset: calc(-1 * var(--cf-spacing-focus-ring));
    box-shadow: inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-header-focus-gap);
  }

  .cf-app-header__menu {
    display: none;
    align-items: center;
    min-width: 44px;
    min-height: 44px;
    padding-inline: var(--cf-spacing-4);
    border: 1px solid var(--cf-color-header-border);
    border-radius: var(--cf-radius-none);
    background: transparent;
    color: inherit;
    font-family: var(--cf-type-button-font-family);
    font-size: var(--cf-type-button-font-size);
    letter-spacing: var(--cf-type-button-letter-spacing);
    text-transform: var(--cf-type-button-text-transform);
    cursor: pointer;
  }

  .cf-app-header__mark {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
    padding-inline: var(--cf-spacing-3);
    color: inherit;
    font-family: var(--cf-type-section-font-family);
    font-size: var(--cf-type-section-font-size);
    line-height: var(--cf-type-section-line-height);
    text-decoration: none;
  }

  .cf-app-header__spacer {
    flex: 1;
  }

  .cf-app-header__initials {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 32px;
    min-height: 32px;
    padding-inline: var(--cf-spacing-2);
    background: var(--cf-color-primary);
    color: var(--cf-color-ink-on-bright);
    font-family: var(--cf-type-button-font-family);
    font-size: var(--cf-type-button-font-size);
    text-transform: uppercase;
  }

  @media (max-width: 671.98px) {
    .cf-app-header__menu {
      display: inline-flex;
    }
  }
</style>
