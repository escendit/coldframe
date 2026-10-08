<script lang="ts">
  import { untrack } from 'svelte';
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import ThresholdColumn from '$lib/components/ThresholdColumn.svelte';
  import { t, type MessageKey } from '$lib/i18n';
  import { quantityName } from '$lib/lot-detail';
  import { editOf, requestOf, stepOf, validityOf, type Edit } from '$lib/thresholds';
  import type { PageProps } from './$types';

  let { data, form, params }: PageProps = $props();

  const pageNotice: Readonly<Record<NonNullable<PageProps['data']['notice']>, MessageKey>> = {
    notFound: 'lotDetail.notFound',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
    unavailable: 'lots.unavailable',
  };

  const failureNotice: Readonly<Record<string, MessageKey>> = {
    invalid: 'thresholds.notice.invalid',
    forbidden: 'thresholds.notice.forbidden',
    notFound: 'thresholds.notice.notFound',
    notSaved: 'thresholds.notice.notSaved',
    certificate: 'notice.certificate',
    unexpected: 'thresholds.notice.unexpected',
  };

  const siteName = $derived(data.currentSite?.name ?? '');
  const backHref = $derived(`/garden/${params.lotId}`);

  /** The edits, one per Sensor, started from the Thresholds in force; a failed save keeps them. */
  const edits: Edit[] = $state(untrack(() => data.columns.map((column) => editOf(column.thresholds))));

  const invalid = $derived(edits.some((edit) => validityOf(edit) !== 'ok'));
  const changes = $derived(
    data.columns.flatMap((column, index) => {
      const edit = edits[index];
      const body = edit === undefined ? null : requestOf(column.thresholds, edit);
      return body === null ? [] : [{ sensorId: column.sensorId, body }];
    }),
  );

  let working = $state(false);
  const submitting: SubmitFunction = () => {
    working = true;
    return async ({ update }) => {
      // Edits stay on a refusal or a failure: nothing is reset.
      await update({ reset: false });
      working = false;
    };
  };
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('thresholds.title', { name: data.lot?.name ?? '' }) })}</title>
</svelte:head>

<p class="cf-thresholds__back"><a href={backHref}>{t('thresholds.back', { name: data.lot?.name ?? t('garden.title') })}</a></p>

{#if data.currentSite !== null}
  {#if data.notice !== null}
    <InlineNotice id="cf-thresholds-notice" message={t(pageNotice[data.notice], { siteName })} action={data.notice === 'unreachable' || data.notice === 'unavailable' ? { label: t('notice.tryAgain'), href: `${backHref}/thresholds`, reload: true } : null} />
  {:else if data.lot !== null}
    <form class="cf-thresholds" method="POST" action="?/save" use:enhance={submitting} aria-labelledby="cf-thresholds-title">
      <h1 id="cf-thresholds-title" class="cf-thresholds__title">{t('thresholds.title', { name: data.lot.name })}</h1>

      {#if form?.notice !== undefined}
        <InlineNotice id="cf-thresholds-failure" message={t(failureNotice[form.notice] ?? 'thresholds.notice.unexpected', { siteName })} />
      {/if}

      {#if !data.canEdit}
        <p class="cf-thresholds__readonly" id="cf-thresholds-readonly">{t('thresholds.readOnly')}</p>
      {/if}

      {#if data.columns.length === 0}
        <p class="cf-thresholds__readonly">{t('thresholds.noSensors')}</p>
      {:else}
        <div class="cf-thresholds__columns">
          {#each data.columns as column, index (column.sensorId)}
            {@const edit = edits[index]}
            {#if edit !== undefined}
              <ThresholdColumn
                sensorId={column.sensorId}
                label={quantityName(column.quantity)}
                thresholds={column.thresholds}
                step={stepOf(column.quantity, column.thresholds.unit)}
                reading={column.reading}
                editable={data.canEdit}
                bind:low={edit.low}
                bind:high={edit.high}
              />
            {/if}
          {/each}
        </div>
      {/if}

      <input type="hidden" name="siteId" value={data.currentSite.id} />
      <input type="hidden" name="lotId" value={params.lotId} />
      <input type="hidden" name="changes" value={JSON.stringify(changes)} />

      <div class="cf-thresholds__actions">
        {#if data.canEdit}
          <Button label={t('thresholds.cancel')} variant="secondary" href={backHref} />
          <Button type="submit" label={t('thresholds.save')} workingLabel={t('thresholds.saving')} {working} disabled={invalid} />
        {:else}
          <Button label={t('thresholds.back', { name: data.lot.name })} variant="secondary" href={backHref} />
        {/if}
      </div>
    </form>
  {/if}
{/if}

<style>
  .cf-thresholds__back {
    margin: 0 0 var(--cf-spacing-5);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
  }

  .cf-thresholds__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
    color: var(--cf-color-primary-text);
  }

  .cf-thresholds {
    display: grid;
    gap: var(--cf-spacing-5);
    max-width: 48rem;
  }

  .cf-thresholds__title {
    margin: 0;
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-title-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-title-line-height);
    overflow-wrap: anywhere;
  }

  .cf-thresholds__readonly {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-thresholds__columns {
    display: grid;
    gap: var(--cf-spacing-tile-gap);
  }

  .cf-thresholds__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-4);
  }
</style>
