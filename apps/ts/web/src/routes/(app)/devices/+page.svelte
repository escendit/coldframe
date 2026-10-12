<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import { announce } from '$lib/announcer.svelte';
  import Button from '$lib/components/Button.svelte';
  import Icon from '$lib/components/Icon.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import Modal from '$lib/components/Modal.svelte';
  import {
    batteryText,
    chargingText,
    deviceRowId,
    devicesAccessOf,
    devicesNoticeOf,
    hubsOf,
    lastSeenText,
    lotChoicesOf,
    nodesOf,
    silenceWindowParts,
    silenceWindowSeconds,
    silenceWindowText,
    silenceWindowUnitLabel,
    silenceWindowUnits,
    silentText,
    statusOf,
    type DeviceActionFailure,
    type DeviceActionNotice,
    type DeviceListItem,
  } from '$lib/devices';
  import { locale, t, type MessageKey } from '$lib/i18n';
  import { lowBatteryPercent } from '$lib/lot-detail';
  import { onMount } from 'svelte';
  import type { PageProps } from './$types';

  let { data, form }: PageProps = $props();

  const access = $derived(data.currentSite === null ? null : devicesAccessOf(data.currentSite.role));
  const notice = $derived(devicesNoticeOf(data.devicesNotice));
  const hubs = $derived(hubsOf(data.devices));
  // Nodes keep the Server's order: by Lot name, unassigned last, then Device ID.
  const nodes = $derived(nodesOf(data.devices));
  const loadedAt = $derived(new Date(data.loadedAt));

  const failed: DeviceActionFailure | null = $derived(form === null || form.done === true ? null : form);

  /** Which Device's form is working: `moveNode:<id>`, `unassignNode:<id>`, `setSilenceWindow:<id>` or `useDefault:<id>`. */
  let working: string | null = $state(null);
  /**
   * Which Silence Window forms the caller opened or closed, by Device ID. The page holds it, so a save
   * or a refusal that draws the row again leaves the form as the caller had it. A form nobody touched is
   * closed, unless a refusal opens it.
   */
  let windowOpen: Record<string, boolean> = $state({});
  /**
   * What the caller typed or picked in a Silence Window form, by Device ID, until it is saved. The fields
   * are drawn from it, so drawing the row again (a refetch, a refusal) never replaces what was typed.
   */
  let typedAmount: Record<string, string> = $state({});
  let pickedUnit: Record<string, string> = $state({});
  /** The Device whose Silence Window was refused here, before anything was sent: the range is said under its field. */
  let refused: string | null = $state(null);
  let unassigning: DeviceListItem | null = $state(null);
  let confirmOpen = $state(false);
  let unassignForm: HTMLFormElement | undefined = $state();

  const noticeKeys: Readonly<Record<DeviceActionNotice, MessageKey>> = {
    forbidden: 'devices.action.forbidden',
    lotClaimed: 'devices.action.lotClaimed',
    notFound: 'devices.action.notFound',
    range: 'devices.silenceWindow.range',
    unexpected: 'devices.action.unexpected',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
  };

  function noticeOf(deviceId: string): string | null {
    if (failed?.deviceId !== deviceId) {
      return null;
    }
    if (failed.action === 'setSilenceWindow') {
      switch (failed.notice) {
        case 'range':
          // Said under the field, with what was entered.
          return null;
        case 'forbidden':
          // The caller lost the Role after the page loaded: the notice of every refused change (UX-DR84).
          return t('siteSettings.forbidden', { siteName: data.currentSite?.name ?? '' });
        case 'notFound':
          return t('devices.silenceWindow.notFound');
        default:
          break;
      }
    }
    return t(noticeKeys[failed.notice]);
  }

  /** The Silence Window failure of a Device's row, with what was entered. */
  function windowFailureOf(deviceId: string): DeviceActionFailure | null {
    return failed?.action === 'setSilenceWindow' && failed.deviceId === deviceId ? failed : null;
  }

  function field(formData: FormData, name: string): string {
    const value = formData.get(name);
    return typeof value === 'string' ? value : '';
  }

  /**
   * Saves a Silence Window, or returns it to the default. A value outside 1 min to 30 days is refused
   * here and nothing is sent; the Server stays the validator of what is.
   */
  function savingWindow(deviceId: string): SubmitFunction {
    return ({ formData, cancel }) => {
      refused = null;
      const useDefault = formData.has('useDefault');
      if (!useDefault && silenceWindowSeconds(field(formData, 'amount'), field(formData, 'unit')) === null) {
        refused = deviceId;
        announce(t('devices.silenceWindow.range'), 'assertive');
        cancel();
        return;
      }
      working = `${useDefault ? 'useDefault' : 'setSilenceWindow'}:${deviceId}`;
      return async ({ result, update }) => {
        await update({ reset: false });
        working = null;
        if (result.type === 'success') {
          // Saved: the fields show the window in force again.
          typedAmount = Object.fromEntries(Object.entries(typedAmount).filter(([id]) => id !== deviceId));
          pickedUnit = Object.fromEntries(Object.entries(pickedUnit).filter(([id]) => id !== deviceId));
          announce(t('devices.silenceWindow.saved'), 'polite');
        }
      };
    };
  }

  /** A change reloads the data, so the list shows the Server's answer, not a guess. */
  function submitting(key: () => string, saved: MessageKey): SubmitFunction {
    return () => {
      working = key();
      return async ({ result, update }) => {
        await update({ reset: false });
        working = null;
        if (result.type === 'success') {
          announce(t(saved), 'polite');
        }
      };
    };
  }

  function askUnassign(node: DeviceListItem): void {
    unassigning = node;
    confirmOpen = true;
  }

  function confirmUnassign(): void {
    confirmOpen = false;
    unassignForm?.requestSubmit();
  }

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');

  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('devices.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('devices.title')}</h1>

<!--
  The Silence Window of one Device (UX-DR84): the value in force, a number with a unit, Save, and Use default
  while the Device has its own. Rendered for Owners and Administrators only; a Member has no such control.
-->
{#snippet silenceWindow(device: DeviceListItem, siteId: string)}
  {@const current = silenceWindowText(device)}
  {@const parts = silenceWindowParts(device.silenceWindowSeconds)}
  {@const failure = windowFailureOf(device.id)}
  {@const outOfRange = refused === device.id || failure?.notice === 'range'}
  {@const chosenUnit = pickedUnit[device.id] ?? failure?.unit ?? parts?.unit ?? 'h'}
  {@const open = windowOpen[device.id] ?? failure !== null}
  <details class="cf-devices__window" {open}>
    <!-- The page opens and closes the form itself: `open` is a property Svelte writes on every draw, so it must always hold what the caller chose. -->
    <summary
      class="cf-devices__summary"
      onclick={(event) => {
        event.preventDefault();
        windowOpen[device.id] = !open;
      }}
    >
      {current === null ? t('devices.silenceWindow.label') : t('devices.silenceWindow.summary', { value: current })}
    </summary>
    <form method="POST" action="?/setSilenceWindow" class="cf-devices__window-form" novalidate use:enhance={savingWindow(device.id)}>
      <input type="hidden" name="siteId" value={siteId} />
      <input type="hidden" name="deviceId" value={device.id} />
      <label class="cf-devices__legend" for="cf-window-{device.id}">{t('devices.silenceWindow.label')}</label>
      <div class="cf-devices__window-fields">
        <input
          id="cf-window-{device.id}"
          class="cf-devices__window-amount"
          type="number"
          inputmode="numeric"
          min="1"
          step="1"
          name="amount"
          value={typedAmount[device.id] ?? failure?.amount ?? parts?.amount ?? ''}
          oninput={(event) => {
            typedAmount[device.id] = event.currentTarget.value;
          }}
          aria-invalid={outOfRange ? 'true' : undefined}
          aria-describedby="cf-window-{device.id}-helper"
        />
        <select
          class="cf-devices__window-unit"
          name="unit"
          aria-label={t('devices.silenceWindow.unit')}
          onchange={(event) => {
            pickedUnit[device.id] = event.currentTarget.value;
          }}
        >
          {#each silenceWindowUnits as unit (unit)}
            <option value={unit} selected={unit === chosenUnit}>{silenceWindowUnitLabel(unit)}</option>
          {/each}
        </select>
      </div>
      <p id="cf-window-{device.id}-helper" class="cf-devices__hint" class:cf-devices__hint--invalid={outOfRange}>
        {outOfRange ? t('devices.silenceWindow.range') : t('devices.silenceWindow.helper')}
      </p>
      <Button type="submit" variant="secondary" label={t('devices.silenceWindow.save')} working={working === `setSilenceWindow:${device.id}`} workingLabel={t('devices.silenceWindow.saving')} />
    </form>
    {#if device.silenceWindowOverridden === true}
      <form method="POST" action="?/setSilenceWindow" class="cf-devices__window-default" use:enhance={savingWindow(device.id)}>
        <input type="hidden" name="siteId" value={siteId} />
        <input type="hidden" name="deviceId" value={device.id} />
        <input type="hidden" name="useDefault" value="1" />
        <Button type="submit" variant="ghost" label={t('devices.silenceWindow.useDefault')} working={working === `useDefault:${device.id}`} />
      </form>
    {/if}
  </details>
{/snippet}

{#if data.currentSite !== null && access !== null}
  <div class="cf-devices">
    {#if notice !== null}
      <!-- A failed load shows no rows, so no Hub stays "Online" from an earlier answer. -->
      <InlineNotice
        id="cf-devices-notice"
        message={t(notice.message)}
        action={notice.tryAgain ? { label: t('notice.tryAgain'), href: '/devices', reload: true } : null}
      />
    {:else}
      {#if hubs.length === 0 && nodes.length === 0}
        <p class="cf-devices__empty">{t('devices.empty')}</p>
      {/if}
      {#if hubs.length > 0}
        <section aria-labelledby="cf-devices-hubs">
          <h2 id="cf-devices-hubs" class="cf-section-title">{t('devices.hubs')}</h2>
          <ul class="cf-devices__list" aria-labelledby="cf-devices-hubs">
            {#each hubs as hub (hub.id)}
              {@const status = statusOf(hub)}
              {@const silent = silentText(hub, loadedAt)}
              <li id={deviceRowId(hub.id)} class="cf-devices__row" data-device={hub.id} data-online={hub.online} data-silent={silent === null ? undefined : 'true'}>
                <span class="cf-devices__id">{hub.id}</span>
                <span class="cf-devices__meta">
                  <span class="cf-devices__status"><Icon name={status.icon} />{t(status.label)}</span>
                  {#if silent !== null}<span class="cf-devices__silent"><Icon name="help" />{silent}</span>{/if}
                  <span class="cf-devices__seen">{lastSeenText(hub, loadedAt, locale, timeZone)}</span>
                </span>
                {#if access.canSetSilenceWindow}
                  <!-- Admin+ only, hidden for Members (UX-DR84). -->
                  <div class="cf-devices__actions" role="group" aria-label={t('devices.hubActions', { id: hub.id })}>
                    <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
                    {@render silenceWindow(hub, data.currentSite.id)}
                  </div>
                {/if}
                {#if noticeOf(hub.id) !== null}
                  <!-- Shown whatever the Role is now: a refused change is the one case where the caller may have lost it. -->
                  <InlineNotice id="cf-hub-{hub.id}-notice" message={noticeOf(hub.id) ?? ''} />
                {/if}
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      {#if nodes.length > 0}
        <!-- Nodes (UX-DR30): the Server's order, the Lot's name beside the Device ID, battery, charging and last seen. -->
        <section aria-labelledby="cf-devices-nodes">
          <h2 id="cf-devices-nodes" class="cf-section-title">{t('devices.nodes')}</h2>
          <ul class="cf-devices__list" aria-labelledby="cf-devices-nodes">
            {#each nodes as node (node.id)}
              {@const charging = chargingText(node)}
              {@const silent = silentText(node, loadedAt)}
              <li id={deviceRowId(node.id)} class="cf-devices__row" data-device={node.id} data-silent={silent === null ? undefined : 'true'}>
                <span class="cf-devices__who">
                  <span class="cf-devices__id">{node.id}</span>
                  <span class="cf-devices__lot">{node.lotName ?? t('devices.noLot')}</span>
                </span>
                <span class="cf-devices__meta">
                  {#if silent !== null}<span class="cf-devices__silent"><Icon name="help" />{silent}</span>{/if}
                  <span class="cf-devices__seen">{lastSeenText(node, loadedAt, locale, timeZone)}</span>
                  <span class="cf-devices__battery">
                    {#if node.batteryPercent !== undefined && node.batteryPercent < lowBatteryPercent}<Icon name="battery--low" />{/if}
                    {batteryText(node, locale)}
                  </span>
                  {#if charging !== null}<span class="cf-devices__charging">{charging}</span>{/if}
                </span>
                {#if access.canManageNodes || access.canSetSilenceWindow}
                  <!-- Admin+ only, hidden for Members (UX-DR31, UX-DR84); no Bluetooth is needed to move, unassign or set the Silence Window. -->
                  <div class="cf-devices__actions" role="group" aria-label={t('devices.nodeActions', { id: node.id })}>
                    {#if access.canSetSilenceWindow}
                      <!-- eslint-disable-next-line @typescript-eslint/no-confusing-void-expression -- rendering a snippet declared in this file is typed as a void call -->
                      {@render silenceWindow(node, data.currentSite.id)}
                    {/if}
                    {#if access.canManageNodes}
                      <details class="cf-devices__move">
                        <summary class="cf-devices__summary" aria-label={t('devices.moveNodeFor', { id: node.id })}>{t('devices.moveNode')}</summary>
                        {#if data.lots.length === 0}
                          <p class="cf-devices__hint">{t('devices.noLotsToMoveTo')}</p>
                        {:else}
                          <form method="POST" action="?/moveNode" class="cf-devices__move-form" use:enhance={submitting(() => `moveNode:${node.id}`, 'devices.moved')}>
                            <input type="hidden" name="siteId" value={data.currentSite.id} />
                            <input type="hidden" name="deviceId" value={node.id} />
                            <fieldset class="cf-devices__lots">
                              <legend class="cf-devices__legend">{t('devices.moveLotLabel', { id: node.id })}</legend>
                              {#each lotChoicesOf(data.lots, node) as choice (choice.id)}
                                <label class="cf-devices__lot-choice" data-lot={choice.id} data-has-node={choice.hasNode}>
                                  <input type="radio" name="lotId" value={choice.id} required disabled={choice.hasNode || choice.current} />
                                  <span class="cf-devices__lot-name">{choice.name}</span>
                                  {#if choice.hasNode}<span class="cf-devices__badge">{t('devices.hasANode')}</span>{/if}
                                  {#if choice.current}<span class="cf-devices__badge">{t('devices.currentLot')}</span>{/if}
                                </label>
                              {/each}
                            </fieldset>
                            <Button type="submit" variant="secondary" label={t('devices.moveConfirm', { id: node.id })} working={working === `moveNode:${node.id}`} />
                          </form>
                        {/if}
                      </details>
                      {#if node.lotId !== undefined}
                        <Button
                          variant="ghost"
                          label={t('devices.unassignNode')}
                          working={working === `unassignNode:${node.id}`}
                          onclick={() => {
                            askUnassign(node);
                          }}
                        />
                      {/if}
                    {/if}
                  </div>
                {/if}
                {#if noticeOf(node.id) !== null}
                  <InlineNotice id="cf-node-{node.id}-notice" message={noticeOf(node.id) ?? ''} />
                {/if}
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      {#if access.canManageNodes && nodes.length > 0}
        <!-- Unassign confirms in a Modal naming the Node (EXPERIENCE.md); the Modal's action submits this form. -->
        <form
          class="cf-devices__unassign"
          method="POST"
          action="?/unassignNode"
          bind:this={unassignForm}
          use:enhance={submitting(() => `unassignNode:${unassigning?.id ?? ''}`, 'devices.unassigned')}
        >
          <input type="hidden" name="siteId" value={data.currentSite.id} />
          <input type="hidden" name="deviceId" value={unassigning?.id ?? ''} />
        </form>
        <Modal
          id="cf-unassign-node"
          bind:open={confirmOpen}
          title={t('devices.unassignQuestion', { id: unassigning?.id ?? '' })}
          detail={t('devices.unassignDetail', { lotName: unassigning?.lotName ?? t('devices.noLot') })}
          actionLabel={t('devices.unassignNode')}
          onaction={confirmUnassign}
        />
      {/if}

      {#if access.mobileAppNotice}
        <!-- No BLE on the web (UX-DR85): the notice stands in for the Add actions, which are Admin+ (UX-DR84). -->
        <InlineNotice id="cf-devices-web-notice" message={t('devices.webNotice')} />
      {/if}
    {/if}
  </div>
{/if}

<style>
  .cf-devices {
    display: grid;
    gap: var(--cf-spacing-6);
    max-width: 40rem;
  }

  .cf-devices__empty {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__list {
    margin: 0;
    padding: 0;
    list-style: none;
    border-top: 1px solid var(--cf-color-border-subtle);
  }

  /* A row a Health Alert linked to (`/devices#device-…`): marked by a stronger edge, not by colour alone. */
  .cf-devices__row:target {
    border-inline-start: 3px solid var(--cf-color-border-strong);
    padding-inline-start: var(--cf-spacing-4);
  }

  /* "Silent 6 h" beside last seen: the help icon and the words, in capitals by style only (UX-DR124). */
  .cf-devices__silent {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--cf-color-text-primary);
  }

  /* The open form takes the row, so the other actions stand under it and not beside its fields. */
  .cf-devices__window[open] {
    flex-basis: 100%;
  }

  .cf-devices__window-form {
    display: grid;
    justify-items: start;
    gap: var(--cf-spacing-3);
    margin: 0;
    padding: 0;
  }

  .cf-devices__window-form .cf-devices__legend,
  .cf-devices__window-form .cf-devices__hint {
    margin: 0;
  }

  .cf-devices__window-fields {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-3);
  }

  .cf-devices__window-amount,
  .cf-devices__window-unit {
    min-height: 44px;
    padding: 0 var(--cf-spacing-5);
    border: 0;
    border-bottom: 1px solid var(--cf-color-border-strong);
    border-radius: var(--cf-radius-none);
    background: var(--cf-color-field-01);
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
  }

  .cf-devices__window-amount {
    width: 8rem;
    max-width: 100%;
  }

  .cf-devices__window-amount[aria-invalid='true'] {
    outline: 2px solid var(--cf-color-support-error);
    outline-offset: -2px;
  }

  .cf-devices__hint--invalid {
    color: var(--cf-color-support-error-text);
  }

  .cf-devices__window-default {
    margin: 0;
  }

  .cf-devices__row {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--cf-spacing-3) var(--cf-spacing-5);
    padding: var(--cf-spacing-5) 0;
    background: var(--cf-color-background);
    border-bottom: 1px solid var(--cf-color-border-subtle);
  }

  .cf-devices__who {
    display: grid;
    gap: var(--cf-spacing-2);
  }

  .cf-devices__lot {
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    color: var(--cf-color-text-primary);
    overflow-wrap: anywhere;
  }

  .cf-devices__battery {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
  }

  .cf-devices__id {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-primary);
    overflow-wrap: anywhere;
  }

  .cf-devices__meta {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-2) var(--cf-spacing-5);
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-3) var(--cf-spacing-5);
    flex-basis: 100%;
  }

  .cf-devices__summary {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
    cursor: pointer;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-primary);
    text-decoration: underline;
  }

  .cf-devices__move-form,
  .cf-devices__lots {
    display: grid;
    gap: var(--cf-spacing-3);
    margin: 0;
    padding: 0;
    border: 0;
  }

  .cf-devices__legend,
  .cf-devices__hint {
    margin: 0 0 var(--cf-spacing-3);
    padding: 0;
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__lot-choice {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-3);
    min-height: 44px;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-primary);
  }

  .cf-devices__lot-choice[data-has-node='true'] {
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__lot-name {
    overflow-wrap: anywhere;
  }

  /* The badge is the word, in capitals by style only (UX-DR124); it never relies on colour. */
  .cf-devices__badge {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--cf-color-text-secondary);
  }

  .cf-devices__unassign {
    display: none;
  }

  .cf-devices__status {
    display: inline-flex;
    align-items: center;
    gap: var(--cf-spacing-2);
    color: var(--cf-color-text-primary);
  }
</style>
