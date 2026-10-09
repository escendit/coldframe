<script lang="ts">
  import Button from '$lib/components/Button.svelte';
  import Modal from '$lib/components/Modal.svelte';
  import { t } from '$lib/i18n';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  let confirmSignOut = $state(false);
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('settings.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('settings.title')}</h1>

<!-- Settings index: My notifications sits above Site settings (UX-DR72). -->
<ul class="cf-settings">
  <li>
    <a class="cf-settings__row" href="/settings/notifications">
      <span class="cf-settings__name">{t('settings.myNotifications')}</span>
      <span class="cf-settings__helper">{t('settings.myNotificationsHelper')}</span>
    </a>
  </li>
  {#if data.currentSite !== null}
    <li>
      <a class="cf-settings__row" href="/settings/site">
        <span class="cf-settings__name">{t('settings.siteSettings')}</span>
        <span class="cf-settings__helper">{t('settings.siteSettingsHelper', { siteName: data.currentSite.name })}</span>
      </a>
    </li>
  {/if}
  <li>
    <a class="cf-settings__row" href="/settings/appearance">
      <span class="cf-settings__name">{t('settings.appearance')}</span>
      <span class="cf-settings__helper">{t('settings.appearanceHelper')}</span>
    </a>
  </li>
</ul>

<section class="cf-settings__account" aria-labelledby="cf-account">
  <h2 id="cf-account" class="cf-section-title">{t('settings.account')}</h2>
  <Button variant="secondary" label={t('settings.signOut')} onclick={() => (confirmSignOut = true)} />
</section>

<Modal
  id="cf-sign-out"
  bind:open={confirmSignOut}
  title={t('settings.signOutQuestion')}
  detail={t('settings.signOutDetail')}
  actionLabel={t('settings.signOut')}
  actionHref="/.oidc/signout?redirect_uri=/signin"
  reload
/>

<style>
  .cf-settings {
    margin: 0 0 var(--cf-spacing-7);
    padding: 0;
    list-style: none;
    border-top: 1px solid var(--cf-color-border-subtle);
  }

  .cf-settings__row {
    display: grid;
    gap: var(--cf-spacing-1);
    min-height: 44px;
    padding: var(--cf-spacing-4) var(--cf-spacing-5);
    border-bottom: 1px solid var(--cf-color-border-subtle);
    color: var(--cf-color-text-primary);
    text-decoration: none;
  }

  .cf-settings__row:hover {
    background: var(--cf-color-layer-01);
  }

  .cf-settings__row:focus-visible {
    outline: none;
    box-shadow:
      inset 0 0 0 var(--cf-spacing-focus-ring) var(--cf-color-focus),
      inset 0 0 0 calc(var(--cf-spacing-focus-ring) + var(--cf-spacing-focus-offset)) var(--cf-color-focus-gap);
  }

  .cf-settings__name {
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
  }

  .cf-settings__helper {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }
</style>
