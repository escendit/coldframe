<script lang="ts">
  import { enhance } from '$app/forms';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import TextInput from '$lib/components/TextInput.svelte';
  import TimeZonePanel from '$lib/components/TimeZonePanel.svelte';
  import { announce } from '$lib/announcer.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import type { NameError } from '$lib/sites';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  let working = $state(false);
  let detected: string | null | undefined = $state();
  let zones: readonly string[] = $state([]);

  /** One key per attempt: a failed attempt hands back the key its retry must use. */
  const idempotencyKey = $derived(form?.idempotencyKey ?? data.idempotencyKey);

  const nameReasons: Readonly<Record<NameError, MessageKey>> = {
    blank: 'createSite.nameBlank',
    tooLong: 'createSite.nameTooLong',
    invalid: 'createSite.nameInvalid',
  };
  const nameError = $derived(form?.nameError !== undefined && form.nameError !== null ? t(nameReasons[form.nameError]) : null);

  const noticeKeys: Readonly<Record<'unavailable' | 'unexpected' | 'keyReused' | 'unreachable' | 'certificate', MessageKey>> = {
    unavailable: 'createSite.unavailable',
    unexpected: 'createSite.unexpected',
    keyReused: 'createSite.keyReused',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
  };
  const notice = $derived(form?.notice !== undefined && form.notice !== null ? t(noticeKeys[form.notice]) : null);

  $effect(() => {
    // Detection runs in the browser only; a zone this browser chose before is never replaced.
    detected = Intl.DateTimeFormat().resolvedOptions().timeZone;
    zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  });

  $effect(() => {
    const message = notice ?? nameError;
    if (message !== null) {
      announce(message, 'assertive');
    }
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('createSite.title') })}</title>
</svelte:head>

{#if data.sites.length > 0}
  <p class="cf-create-site__back"><a href="/garden">{t('createSite.back')}</a></p>
{/if}

<h1 class="cf-page-title">{t('createSite.title')}</h1>
<p class="cf-create-site__intro">{t('createSite.intro')}</p>

<form
  class="cf-create-site"
  method="POST"
  novalidate
  use:enhance={() => {
    working = true;
    return async ({ update }) => {
      await update({ reset: false });
      working = false;
    };
  }}
>
  <input type="hidden" name="idempotencyKey" value={idempotencyKey} />
  <TextInput id="cf-site-name" name="name" label={t('createSite.name')} helper={t('createSite.nameHelper')} value={form?.name ?? ''} invalid={nameError} autocomplete="off" />
  <TimeZonePanel {detected} chosen={data.chosenTimeZone} {zones} />
  {#if notice !== null}
    <InlineNotice id="cf-create-site-notice" message={notice} />
  {/if}
  <div>
    <Button type="submit" label={t('createSite.action')} {working} workingLabel={t('createSite.working')} />
  </div>
</form>

<style>
  .cf-create-site {
    display: grid;
    gap: var(--cf-spacing-6);
    max-width: 40rem;
  }

  .cf-create-site__intro {
    margin: 0 0 var(--cf-spacing-6);
  }

  .cf-create-site__back {
    margin: 0 0 var(--cf-spacing-5);
  }

  .cf-create-site__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
  }
</style>
