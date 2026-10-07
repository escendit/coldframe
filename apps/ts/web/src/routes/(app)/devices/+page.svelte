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
    devicesAccessOf,
    devicesNoticeOf,
    hubsOf,
    lastSeenText,
    lotChoicesOf,
    nodesOf,
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

  /** Which Node's form is working: `moveNode:<id>` or `unassignNode:<id>`. */
  let working: string | null = $state(null);
  let unassigning: DeviceListItem | null = $state(null);
  let confirmOpen = $state(false);
  let unassignForm: HTMLFormElement | undefined = $state();

  const noticeKeys: Readonly<Record<DeviceActionNotice, MessageKey>> = {
    forbidden: 'devices.action.forbidden',
    lotClaimed: 'devices.action.lotClaimed',
    notFound: 'devices.action.notFound',
    unexpected: 'devices.action.unexpected',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
  };

  function noticeOf(deviceId: string): string | null {
    return failed !== null && failed.deviceId === deviceId ? t(noticeKeys[failed.notice]) : null;
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
              <li class="cf-devices__row" data-device={hub.id} data-online={hub.online}>
                <span class="cf-devices__id">{hub.id}</span>
                <span class="cf-devices__meta">
                  <span class="cf-devices__status"><Icon name={status.icon} />{t(status.label)}</span>
                  <span class="cf-devices__seen">{lastSeenText(hub, loadedAt, locale, timeZone)}</span>
                </span>
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
              <li class="cf-devices__row" data-device={node.id}>
                <span class="cf-devices__who">
                  <span class="cf-devices__id">{node.id}</span>
                  <span class="cf-devices__lot">{node.lotName ?? t('devices.noLot')}</span>
                </span>
                <span class="cf-devices__meta">
                  <span class="cf-devices__seen">{lastSeenText(node, loadedAt, locale, timeZone)}</span>
                  <span class="cf-devices__battery">
                    {#if node.batteryPercent !== undefined && node.batteryPercent < lowBatteryPercent}<Icon name="battery--low" />{/if}
                    {batteryText(node, locale)}
                  </span>
                  {#if charging !== null}<span class="cf-devices__charging">{charging}</span>{/if}
                </span>
                {#if access.canManageNodes}
                  <!-- Admin+ only, hidden for Members (UX-DR31); no Bluetooth is needed to move or unassign. -->
                  <div class="cf-devices__actions" role="group" aria-label={t('devices.nodeActions', { id: node.id })}>
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
                  </div>
                  {#if noticeOf(node.id) !== null}
                    <InlineNotice id="cf-node-{node.id}-notice" message={noticeOf(node.id) ?? ''} />
                  {/if}
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
