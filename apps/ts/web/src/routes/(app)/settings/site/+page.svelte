<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import Modal from '$lib/components/Modal.svelte';
  import TextInput from '$lib/components/TextInput.svelte';
  import { announce } from '$lib/announcer.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import { lotsNoticeOf, siteSettingsOf, type Lot, type SiteSettingsAction, type SiteSettingsFailure, type SiteSettingsNotice } from '$lib/lots';
  import type { NameError } from '$lib/sites';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  const site = $derived(data.currentSite);
  const access = $derived(site === null ? null : siteSettingsOf(site.role));
  const lotsNotice = $derived(lotsNoticeOf(data.lotsNotice));
  const failed: SiteSettingsFailure | null = $derived(form === null || form.done === true ? null : form);

  /** Which form is working: an action, or `renameLot:<id>` / `removeLot:<id>` for a Lot row. */
  let working: string | null = $state(null);
  let removing: Lot | null = $state(null);
  let confirmOpen = $state(false);
  let removeForm: HTMLFormElement | undefined = $state();

  /** One key per Create Lot attempt: a failure hands back the key its retry must use. */
  const createKey = $derived(failed?.action === 'createLot' && failed.idempotencyKey !== null ? failed.idempotencyKey : data.createKey);

  const siteNameReasons: Readonly<Record<NameError, MessageKey>> = {
    blank: 'createSite.nameBlank',
    tooLong: 'createSite.nameTooLong',
    invalid: 'createSite.nameInvalid',
  };
  const lotNameReasons: Readonly<Record<NameError, MessageKey>> = {
    blank: 'siteSettings.lotNameBlank',
    tooLong: 'createSite.nameTooLong',
    invalid: 'createSite.nameInvalid',
  };

  const unexpected: Readonly<Record<SiteSettingsAction, MessageKey>> = {
    renameSite: 'siteSettings.renameSiteUnexpected',
    createLot: 'siteSettings.createLotUnexpected',
    renameLot: 'siteSettings.renameLotUnexpected',
    removeLot: 'siteSettings.removeLotUnexpected',
  };

  function noticeText(action: SiteSettingsAction, notice: SiteSettingsNotice, siteName: string, lotName: string | null): string {
    const lot = lotName ?? '';
    switch (notice) {
      case 'forbidden':
        return t('siteSettings.forbidden', { siteName });
      case 'lotClaimed':
        return t('siteSettings.lotClaimed', { lotName: lot });
      case 'lotNotFound':
        return t('siteSettings.lotNotFound', { lotName: lot, siteName });
      case 'siteNotFound':
        return t('siteSettings.siteNotFound', { siteName });
      case 'unavailable':
        return t('siteSettings.renameSiteUnavailable');
      case 'keyReused':
        return t('siteSettings.createLotKeyReused');
      case 'unreachable':
        return t('notice.unreachable');
      case 'certificate':
        return t('notice.certificate');
      case 'unexpected':
        return t(unexpected[action]);
    }
  }

  /** The failure of `action` (and of this Lot for row actions), if the last submit failed there. */
  function failureOf(action: SiteSettingsAction, lotId: string | null = null): SiteSettingsFailure | null {
    return failed !== null && failed.action === action && failed.lotId === lotId ? failed : null;
  }

  function nameErrorOf(action: SiteSettingsAction, lotId: string | null = null): string | null {
    const failure = failureOf(action, lotId);
    if (failure?.nameError === null || failure?.nameError === undefined) {
      return null;
    }
    return t(action === 'renameSite' ? siteNameReasons[failure.nameError] : lotNameReasons[failure.nameError]);
  }

  function noticeOf(action: SiteSettingsAction, lotId: string | null = null): string | null {
    const failure = failureOf(action, lotId);
    if (failure?.notice === null || failure?.notice === undefined) {
      return null;
    }
    return noticeText(action, failure.notice, failure.siteName, failure.lotName);
  }

  /** Every form keeps its values on failure; success reloads the data, so the Site tabs update too. */
  function submitting(key: string | (() => string)): SubmitFunction {
    return () => {
      working = typeof key === 'string' ? key : key();
      return async ({ result, update }) => {
        await update({ reset: false });
        working = null;
        if (result.type === 'success') {
          announce(t('siteSettings.saved'), 'polite');
        }
      };
    };
  }

  function askRemove(lot: Lot): void {
    removing = lot;
    confirmOpen = true;
  }

  function confirmRemove(): void {
    confirmOpen = false;
    removeForm?.requestSubmit();
  }

  $effect(() => {
    if (failed === null) {
      return;
    }
    const message = failed.notice !== null ? noticeText(failed.action, failed.notice, failed.siteName, failed.lotName) : null;
    const reason = nameErrorOf(failed.action, failed.lotId);
    const spoken = message ?? reason;
    if (spoken !== null) {
      announce(spoken, 'assertive');
    }
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('siteSettings.title') })}</title>
</svelte:head>

<p class="cf-site-settings__back"><a href="/settings">{t('siteSettings.back')}</a></p>

<h1 class="cf-page-title">{t('siteSettings.title')}</h1>

{#if site !== null && access !== null}
  <!-- Site settings (UX-DR74): one surface. Controls a Role cannot use are hidden (UX-DR84). -->
  <section class="cf-site-settings__section" aria-labelledby="cf-site-settings-site">
    <h2 id="cf-site-settings-site" class="cf-section-title">{t('siteSettings.site')}</h2>
    {#if access.canRenameSite}
      {#key site.name}
        <form class="cf-site-settings__form" method="POST" action="?/renameSite" novalidate use:enhance={submitting('renameSite')}>
          <input type="hidden" name="siteId" value={site.id} />
          <input type="hidden" name="siteName" value={site.name} />
          <TextInput
            id="cf-site-name"
            name="name"
            label={t('createSite.name')}
            helper={t('createSite.nameHelper')}
            value={failureOf('renameSite')?.name ?? site.name}
            invalid={nameErrorOf('renameSite')}
            autocomplete="off"
          />
          {#if noticeOf('renameSite') !== null}
            <InlineNotice id="cf-rename-site-notice" message={noticeOf('renameSite') ?? ''} />
          {/if}
          <div>
            <Button type="submit" label={t('siteSettings.renameSite')} working={working === 'renameSite'} workingLabel={t('siteSettings.renamingSite')} />
          </div>
        </form>
      {/key}
    {:else}
      <p class="cf-site-settings__label">{t('createSite.name')}</p>
      <p class="cf-site-settings__value" data-site-name>{site.name}</p>
    {/if}
  </section>

  <section class="cf-site-settings__section" aria-labelledby="cf-site-settings-lots">
    <h2 id="cf-site-settings-lots" class="cf-section-title">{t('siteSettings.lots')}</h2>

    {#if access.readOnlyNotice}
      <InlineNotice id="cf-lots-read-only" message={t('siteSettings.readOnly')} />
    {/if}

    {#if lotsNotice !== null}
      <InlineNotice
        id="cf-lots-notice"
        message={t(lotsNotice.message)}
        action={lotsNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/settings/site', reload: true } : null}
      />
    {:else}
      {#if access.canEditLots}
        {#key createKey}
          <form class="cf-site-settings__form" method="POST" action="?/createLot" novalidate use:enhance={submitting('createLot')}>
            <input type="hidden" name="siteId" value={site.id} />
            <input type="hidden" name="siteName" value={site.name} />
            <input type="hidden" name="idempotencyKey" value={createKey} />
            <TextInput
              id="cf-new-lot-name"
              name="name"
              label={t('siteSettings.lotName')}
              helper={t('siteSettings.lotNameHelper')}
              value={failureOf('createLot')?.name ?? ''}
              invalid={nameErrorOf('createLot')}
              autocomplete="off"
            />
            {#if noticeOf('createLot') !== null}
              <InlineNotice id="cf-create-lot-notice" message={noticeOf('createLot') ?? ''} />
            {/if}
            <div>
              <Button type="submit" label={t('siteSettings.createLot')} working={working === 'createLot'} workingLabel={t('siteSettings.creatingLot')} />
            </div>
          </form>
        {/key}
      {/if}

      {#if data.lots.length === 0}
        <p class="cf-site-settings__empty">{t('siteSettings.noLots')}</p>
      {:else}
        <!-- The Server's order, never re-sorted (UX-DR20). -->
        <ul class="cf-site-settings__lots" aria-label={t('siteSettings.lots')}>
          {#each data.lots as lot (lot.id)}
            <li class="cf-site-settings__lot" data-lot={lot.id}>
              {#if access.canEditLots}
                {#key lot.name}
                  <form class="cf-site-settings__form" method="POST" action="?/renameLot" novalidate use:enhance={submitting(`renameLot:${lot.id}`)}>
                    <input type="hidden" name="siteId" value={site.id} />
                    <input type="hidden" name="siteName" value={site.name} />
                    <input type="hidden" name="lotId" value={lot.id} />
                    <input type="hidden" name="lotName" value={lot.name} />
                    <TextInput
                      id="cf-lot-{lot.id}"
                      name="name"
                      label={t('siteSettings.lotNameOf', { lotName: lot.name })}
                      value={failureOf('renameLot', lot.id)?.name ?? lot.name}
                      invalid={nameErrorOf('renameLot', lot.id)}
                      autocomplete="off"
                    />
                    <div class="cf-site-settings__actions">
                      <Button
                        type="submit"
                        variant="secondary"
                        label={t('siteSettings.renameLot')}
                        working={working === `renameLot:${lot.id}`}
                        workingLabel={t('siteSettings.renamingLot')}
                      />
                      <Button
                        variant="ghost"
                        label={t('siteSettings.removeLot')}
                        working={working === `removeLot:${lot.id}`}
                        workingLabel={t('siteSettings.removingLot')}
                        onclick={() => {
                          askRemove(lot);
                        }}
                      />
                    </div>
                  </form>
                {/key}
                {#if noticeOf('renameLot', lot.id) !== null}
                  <InlineNotice id="cf-lot-{lot.id}-rename-notice" message={noticeOf('renameLot', lot.id) ?? ''} />
                {/if}
                {#if noticeOf('removeLot', lot.id) !== null}
                  <InlineNotice id="cf-lot-{lot.id}-remove-notice" message={noticeOf('removeLot', lot.id) ?? ''} />
                {/if}
              {:else}
                <span class="cf-site-settings__lot-name">{lot.name}</span>
              {/if}
            </li>
          {/each}
        </ul>
      {/if}
    {/if}
  </section>

  {#if access.canEditLots}
    <!-- Remove confirms in a Modal naming the Lot; the Modal's action submits this form. -->
    <form
      class="cf-site-settings__remove"
      method="POST"
      action="?/removeLot"
      bind:this={removeForm}
      use:enhance={submitting(() => `removeLot:${removing?.id ?? ''}`)}
    >
      <input type="hidden" name="siteId" value={site.id} />
      <input type="hidden" name="siteName" value={site.name} />
      <input type="hidden" name="lotId" value={removing?.id ?? ''} />
      <input type="hidden" name="lotName" value={removing?.name ?? ''} />
    </form>
    <Modal
      id="cf-remove-lot"
      bind:open={confirmOpen}
      title={t('siteSettings.removeQuestion', { lotName: removing?.name ?? '' })}
      detail={t('siteSettings.removeDetail')}
      actionLabel={t('siteSettings.removeLot')}
      onaction={confirmRemove}
    />
  {/if}
{/if}

<style>
  .cf-site-settings__back {
    margin: 0 0 var(--cf-spacing-5);
  }

  .cf-site-settings__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
  }

  .cf-site-settings__section {
    display: grid;
    gap: var(--cf-spacing-5);
    max-width: 40rem;
    margin-block-end: var(--cf-spacing-7);
  }

  .cf-site-settings__form {
    display: grid;
    gap: var(--cf-spacing-5);
  }

  .cf-site-settings__label,
  .cf-site-settings__empty {
    margin: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-site-settings__empty {
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-site-settings__value,
  .cf-site-settings__lot-name {
    margin: 0;
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-site-settings__lots {
    margin: 0;
    padding: 0;
    list-style: none;
    border-top: 1px solid var(--cf-color-border-subtle);
  }

  .cf-site-settings__lot {
    display: grid;
    gap: var(--cf-spacing-3);
    padding: var(--cf-spacing-5) 0;
    border-bottom: 1px solid var(--cf-color-border-subtle);
  }

  .cf-site-settings__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-3);
  }

  .cf-site-settings__remove {
    display: none;
  }
</style>
