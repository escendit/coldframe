<script lang="ts">
  import { tick } from 'svelte';
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import RetryNotice from '$lib/components/RetryNotice.svelte';
  import NotificationWindow from '$lib/components/NotificationWindow.svelte';
  import SegmentedChoice from '$lib/components/SegmentedChoice.svelte';
  import TimeZonePanel from '$lib/components/TimeZonePanel.svelte';
  import Toggle from '$lib/components/Toggle.svelte';
  import { announce } from '$lib/announcer.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import {
    cadenceLabel,
    canTryAgain,
    notificationsNoticeCopy,
    notificationsNoticeOf,
    reminderCadences,
    useSiteSetting,
    type CadenceChoice,
    type NotificationsAction,
    type NotificationsFailure,
    type WindowError,
  } from '$lib/notifications';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  const site = $derived(data.currentSite);
  const settings = $derived(data.settings);
  const siteSettings = $derived(data.siteSettings);
  const failed: NotificationsFailure | null = $derived(form === null || form.done === true ? null : form);

  /** The action that is being sent. */
  let working: NotificationsAction | null = $state(null);
  /** Counts answered submits: the controls are built again from the Server's values after each one. */
  let revision = $state(0);
  let zoneForm: HTMLFormElement | undefined = $state();
  let muteForm: HTMLFormElement | undefined = $state();
  /** The browser's zone; `undefined` until the page has asked the browser. */
  let browserZone: string | undefined = $state();
  let zones: readonly string[] = $state([]);

  const pageNotice = $derived(notificationsNoticeOf(data.notice, 'mine'));
  const siteNotice = $derived(notificationsNoticeOf(data.siteNotice, 'site'));

  /** Save window only: the times that were not sent stay in their fields, with the reason. */
  const windowFailure = $derived(failed !== null && failed.action === 'saveWindow' && failed.windowError !== null ? failed : null);

  const windowReasons: Readonly<Record<WindowError, MessageKey>> = {
    fromInvalid: 'notifications.windowFromInvalid',
    toInvalid: 'notifications.windowToInvalid',
    notBefore: 'notifications.windowNotBefore',
  };
  const fromInvalid = $derived(windowFailure?.windowError === 'fromInvalid' ? t(windowReasons.fromInvalid) : null);
  const toInvalid = $derived(windowFailure !== null && windowFailure.windowError !== null && windowFailure.windowError !== 'fromInvalid' ? t(windowReasons[windowFailure.windowError]) : null);

  /**
   * The zone proposed while the User has chosen none (UX-DR48): the browser's, then the one the
   * Server detected, then none, and the list is all there is. A chosen zone is never proposed over.
   */
  const serverProposal = $derived(settings?.timeZoneConfirmed === false ? (settings.timeZone ?? null) : null);
  const proposal = $derived.by(() => {
    if (browserZone !== undefined && browserZone !== '') {
      return browserZone;
    }
    if (serverProposal !== null) {
      return serverProposal;
    }
    return browserZone === undefined ? undefined : null;
  });
  const chosenZone = $derived(settings?.timeZoneConfirmed === true ? (settings.timeZone ?? null) : null);

  /** My Reminder cadence (UX-DR50): the Site setting, or one of my own. There is no "never". */
  const cadenceOptions: readonly { value: CadenceChoice; label: string }[] = [
    { value: useSiteSetting, label: t('notifications.cadenceUseSite') },
    ...reminderCadences.map((cadence) => ({ value: cadence, label: t(cadenceLabel[cadence]) })),
  ];

  function noticeText(failure: NotificationsFailure): string | null {
    return failure.notice === null ? null : t(notificationsNoticeCopy[failure.notice], { siteName: failure.siteName });
  }

  /** The notice of `action` if the last submit failed there, and the form action that repeats it. */
  function failureIn(action: NotificationsAction): { readonly message: string; readonly retry: string | null } | null {
    if (failed?.action !== action || failed.notice === null) {
      return null;
    }
    return { message: noticeText(failed) ?? '', retry: canTryAgain(failed.notice) ? `?/${action}` : null };
  }

  /**
   * Success loads the Server's settings again; a failure leaves them as they were. Either way the
   * controls are rebuilt from them, so a save that failed shows the Server's value again.
   */
  function submitting(action: NotificationsAction): SubmitFunction {
    return () => {
      working = action;
      return async ({ result, update }) => {
        await update({ reset: false });
        working = null;
        revision += 1;
        if (result.type === 'success') {
          announce(t('siteSettings.saved'), 'polite');
        }
      };
    };
  }

  /** Confirm and a pick apply at once: the panel's field holds the zone after the next render. */
  async function chooseZone(): Promise<void> {
    await tick();
    zoneForm?.requestSubmit();
  }

  $effect(() => {
    // Detection runs in the browser only; a zone the User chose is never replaced by it.
    browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  });

  $effect(() => {
    if (failed === null) {
      return;
    }
    const spoken = noticeText(failed) ?? (failed.windowError !== null ? t(windowReasons[failed.windowError]) : null);
    if (spoken !== null) {
      announce(spoken, 'assertive');
    }
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('notifications.title') })}</title>
</svelte:head>

<p class="cf-notifications__back"><a href="/settings">{t('siteSettings.back')}</a></p>

<h1 class="cf-page-title">{t('notifications.title')}</h1>

{#if pageNotice !== null}
  <InlineNotice
    id="cf-notifications-load-notice"
    message={t(pageNotice.message)}
    action={pageNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/settings/notifications', reload: true } : null}
  />
{:else if settings !== null}
  <!-- My notifications (UX-DR72). The window has a Save button; everything else applies at once. -->
  <section class="cf-notifications__section" aria-labelledby="cf-notifications-window">
    <h2 id="cf-notifications-window" class="cf-section-title">{t('notifications.window')}</h2>
    {#key revision}
      <form class="cf-notifications__form" method="POST" action="?/saveWindow" novalidate use:enhance={submitting('saveWindow')}>
        <NotificationWindow
          window={settings.window}
          from={windowFailure?.fields.from ?? settings.window.from}
          to={windowFailure?.fields.to ?? settings.window.to}
          {fromInvalid}
          {toInvalid}
        />
        <div>
          <Button type="submit" label={t('notifications.saveWindow')} working={working === 'saveWindow'} workingLabel={t('notifications.savingWindow')} />
        </div>
      </form>
    {/key}
    {#if failureIn('saveWindow') !== null}
      <RetryNotice id="cf-notifications" message={failureIn('saveWindow')?.message ?? ''} action={failureIn('saveWindow')?.retry ?? null} fields={failed?.fields} submitting={submitting('saveWindow')} />
    {/if}
  </section>

  <section class="cf-notifications__section">
    {#key revision}
      <form bind:this={zoneForm} method="POST" action="?/chooseTimeZone" use:enhance={submitting('chooseTimeZone')}>
        <TimeZonePanel detected={proposal} chosen={chosenZone} {zones} onchoose={chooseZone} />
      </form>
    {/key}
    {#if failureIn('chooseTimeZone') !== null}
      <RetryNotice
        id="cf-notifications"
        message={failureIn('chooseTimeZone')?.message ?? ''}
        action={failureIn('chooseTimeZone')?.retry ?? null}
        fields={failed?.fields}
        submitting={submitting('chooseTimeZone')}
      />
    {/if}
  </section>

  {#if site !== null}
    <!-- Mute and my cadence are mine on this Site only, so they need a current Site. -->
    <section class="cf-notifications__section" aria-labelledby="cf-notifications-site">
      <h2 id="cf-notifications-site" class="cf-section-title">{site.name}</h2>
      {#if siteNotice !== null}
        <InlineNotice
          id="cf-notifications-site-notice"
          message={t(siteNotice.message, { siteName: site.name })}
          action={siteNotice.tryAgain ? { label: t('notice.tryAgain'), href: '/settings/notifications', reload: true } : null}
        />
      {:else if siteSettings !== null}
        <!-- The Server replaces mute and cadence together, so each form carries the other as it is now. -->
        {#key revision}
          <form bind:this={muteForm} method="POST" action="?/setMute" use:enhance={submitting('setMute')}>
            <input type="hidden" name="siteId" value={site.id} />
            <input type="hidden" name="siteName" value={site.name} />
            <input type="hidden" name="reminderCadence" value={siteSettings.reminderCadence ?? useSiteSetting} />
            <Toggle
              id="cf-mute"
              name="muted"
              label={t('notifications.mute', { siteName: site.name })}
              helper={t('notifications.muteHelper', { siteName: site.name })}
              checked={siteSettings.muted}
              onchange={() => {
                muteForm?.requestSubmit();
              }}
            />
          </form>
        {/key}
        <form method="POST" action="?/setCadence" use:enhance={submitting('setCadence')}>
          <input type="hidden" name="siteId" value={site.id} />
          <input type="hidden" name="siteName" value={site.name} />
          {#if siteSettings.muted}
            <input type="hidden" name="muted" value="on" />
          {/if}
          <SegmentedChoice
            id="cf-my-cadence"
            name="reminderCadence"
            label={t('notifications.cadence')}
            helper={t('notifications.cadenceHelper', { cadence: t(cadenceLabel[siteSettings.siteReminderCadence]) })}
            options={cadenceOptions}
            value={siteSettings.reminderCadence ?? useSiteSetting}
          />
        </form>
        {#each ['setMute', 'setCadence'] as const as action (action)}
          {#if failureIn(action) !== null}
            <RetryNotice id="cf-notifications" message={failureIn(action)?.message ?? ''} action={failureIn(action)?.retry ?? null} fields={failed?.fields} submitting={submitting(action)} />
          {/if}
        {/each}
      {/if}
    </section>
  {/if}
{/if}

<style>
  .cf-notifications__back {
    margin: 0 0 var(--cf-spacing-5);
  }

  .cf-notifications__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
  }

  .cf-notifications__section {
    display: grid;
    gap: var(--cf-spacing-5);
    max-width: 40rem;
    margin-block-end: var(--cf-spacing-7);
  }

  .cf-notifications__form {
    display: grid;
    gap: var(--cf-spacing-5);
  }
</style>
