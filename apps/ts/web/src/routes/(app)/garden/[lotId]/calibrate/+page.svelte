<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import { invalidateAll } from '$app/navigation';
  import { announce } from '$lib/announcer.svelte';
  import Button from '$lib/components/Button.svelte';
  import InlineNotice from '$lib/components/InlineNotice.svelte';
  import {
    calibrateNoticeKeys,
    calibrateStep,
    confirmation,
    freshReading,
    newestSeq,
    lastReadingText,
    readingAnnouncement,
    recentReadings,
    type Recorded,
  } from '$lib/calibrate';
  import { locale, t, type MessageKey } from '$lib/i18n';
  import type { PageProps } from './$types';

  let { data, form, params }: PageProps = $props();

  const pageNotice: Readonly<Record<NonNullable<PageProps['data']['notice']>, MessageKey>> = {
    notFound: 'lotDetail.notFound',
    unreachable: 'notice.unreachable',
    certificate: 'notice.certificate',
    unavailable: 'lots.unavailable',
    noSensor: 'calibrate.noSensor',
  };

  /** Without a chosen time zone the times are told in the browser's, once the page runs there. */
  let browserTimeZone: string | null = $state(null);
  const timeZone = $derived(data.timeZone ?? browserTimeZone ?? 'UTC');

  const lot = $derived(data.lot);
  const calibration = $derived(data.state);
  const siteName = $derived(data.currentSite?.name ?? '');

  /** Both points of a Calibration this page saved; the confirmation stays on screen until Done. */
  let recorded: Recorded | null = $state(null);

  const step = $derived(lot === null || calibration === null ? null : calibrateStep(lot, calibration, recorded));
  /**
   * The step's start, as the newest `readingSeq` seen when it began: only a Reading stored after it enables
   * Record. It moves when a point is recorded, in the same tick as the data and the step, so the Reading just
   * used never counts as fresh for the next step.
   */
  let baseline: number | null = $state(null);
  const loadedSeq = newestSeq(untrack(() => data.state?.readings ?? []));
  const baselineSeq = $derived(baseline ?? loadedSeq);

  const readings = $derived(calibration?.readings ?? []);
  const fresh = $derived(step === 'dry' || step === 'wet' ? freshReading(readings, baselineSeq) : null);
  const last = $derived(lastReadingText(readings, locale, timeZone));
  const recent = $derived(recentReadings(readings, locale, timeZone));
  const sure = $derived.by(() => {
    const saved = recorded;
    return lot === null || saved === null ? null : confirmation(lot.name, lot, saved.savedAt);
  });

  // A fresh Reading is announced politely, once; the waiting text never is (UX-DR106).
  let announcedSeq: number | null = null;
  $effect(() => {
    if (fresh !== null && (step === 'dry' || step === 'wet') && fresh.readingSeq !== announcedSeq) {
      announcedSeq = fresh.readingSeq;
      announce(readingAnnouncement(step, fresh, locale, timeZone));
    }
  });

  let announcedPercent = false;
  $effect(() => {
    const spoken = sure?.announcement ?? null;
    if (spoken !== null && !announcedPercent) {
      announcedPercent = true;
      announce(spoken);
    }
  });

  // A failure that saved the Calibration still moves to the confirmation: the recorded point is kept.
  $effect(() => {
    if (form?.notice === 'notDelivered' && form.dryRaw !== undefined && form.wetRaw !== undefined && recorded === null) {
      recorded = { dryRaw: form.dryRaw, wetRaw: form.wetRaw, savedAt: form.savedAt ?? new Date().toISOString() };
    }
  });

  const failure = $derived(form?.notice ?? null);

  let working = $state(false);
  const submitting: SubmitFunction = ({ formData }) => {
    const chosen = readings.find((reading) => String(reading.readingSeq) === formData.get('readingSeq'));
    working = true;
    return async ({ result, update }) => {
      if (result.type === 'success') {
        baseline = Math.max(chosen?.readingSeq ?? -1, newestSeq(readings), baselineSeq);
      }
      await update();
      working = false;
      if (result.type === 'success' && result.data?.calibrated === true && typeof result.data.dryRaw === 'number' && typeof result.data.wetRaw === 'number') {
        recorded = { dryRaw: result.data.dryRaw, wetRaw: result.data.wetRaw, savedAt: typeof result.data.savedAt === 'string' ? result.data.savedAt : new Date().toISOString() };
      }
    };
  };

  // While the flow waits it reads the Server again on a short interval; this is the one place the web
  // refetches on a timer (the Garden refetches on focus only). It stops with the flow.
  const waitingMs = 3000;
  onMount(() => {
    browserTimeZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
    const timer = setInterval(() => {
      const waiting = step === 'dry' || step === 'wet' || (step === 'confirm' && sure?.ready === false);
      if (waiting) {
        void invalidateAll();
      }
    }, waitingMs);
    return () => {
      clearInterval(timer);
    };
  });

  const backHref = $derived(`/garden/${params.lotId}`);
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('calibrate.title', { name: lot?.name ?? '' }) })}</title>
</svelte:head>

<p class="cf-calibrate__back"><a href={backHref}>{t('calibrate.back', { name: lot?.name ?? t('garden.title') })}</a></p>

{#if data.currentSite !== null}
  {#if data.notice !== null}
    <InlineNotice id="cf-calibrate-notice" message={t(pageNotice[data.notice], { siteName })} action={data.notice === 'unreachable' || data.notice === 'unavailable' ? { label: t('notice.tryAgain'), href: `${backHref}/calibrate`, reload: true } : null} />
  {:else if lot !== null && calibration !== null && step !== null}
    <section class="cf-calibrate" data-step={step} aria-labelledby="cf-calibrate-title">
      <h1 id="cf-calibrate-title" class="cf-calibrate__title">{t('calibrate.title', { name: lot.name })}</h1>

      {#if failure !== null}
        <InlineNotice id="cf-calibrate-failure" message={t(calibrateNoticeKeys[failure], { siteName })} />
      {/if}

      {#if step === 'paused'}
        <p class="cf-calibrate__text" id="cf-calibrate-paused">{lot.pausedBy?.includes('device') === true ? t('calibrate.paused') : t('calibrate.pausedBySite')}</p>
      {:else if step === 'confirm' && recorded !== null && sure !== null}
        <h2 class="cf-calibrate__step">{t('calibrate.confirm.title')}</h2>
        <p class="cf-calibrate__text" id="cf-calibrate-points">{t('calibrate.confirm.points', { dry: recorded.dryRaw, wet: recorded.wetRaw })}</p>
        <p class="cf-calibrate__result" id="cf-calibrate-result" data-ready={sure.ready}>{sure.text}</p>
        <div class="cf-calibrate__actions">
          <Button label={t('thresholds.next')} href="{backHref}/thresholds" id="cf-calibrate-thresholds" />
          <Button label={t('calibrate.done')} variant="secondary" href={backHref} />
        </div>
      {:else}
        <h2 class="cf-calibrate__step">{t(step === 'dry' ? 'calibrate.step.dry' : 'calibrate.step.wet')}</h2>
        <p class="cf-calibrate__text">{t(step === 'dry' ? 'calibrate.intro.dry' : 'calibrate.intro.wet')}</p>

        <div class="cf-calibrate__panel" id="cf-calibrate-waiting">
          <p class="cf-calibrate__waiting">{t('calibrate.waiting')}</p>
          <p class="cf-calibrate__last">{last ?? t('calibrate.noneYet')}</p>
          <p class="cf-calibrate__hint">{t('calibrate.hint')}</p>
        </div>

        <form method="POST" action="?/record" use:enhance={submitting}>
          <input type="hidden" name="siteId" value={data.currentSite.id} />
          <input type="hidden" name="sensorId" value={data.sensorId} />
          <input type="hidden" name="point" value={step} />
          <input type="hidden" name="readingSeq" value={fresh?.readingSeq ?? ''} />
          <Button type="submit" label={t(step === 'dry' ? 'calibrate.recordDry' : 'calibrate.recordWet')} {working} disabled={fresh === null} />
        </form>

        <section class="cf-calibrate__recent" aria-labelledby="cf-calibrate-recent">
          <h3 id="cf-calibrate-recent" class="cf-calibrate__recent-title">{t('calibrate.recent')}</h3>
          {#if recent.length === 0}
            <p class="cf-calibrate__text">{t('calibrate.recentEmpty')}</p>
          {:else}
            <ul class="cf-calibrate__list">
              {#each recent as item (item.readingSeq)}
                <li class="cf-calibrate__item" data-reading-seq={item.readingSeq}>
                  <span>{item.label}</span>
                  <form method="POST" action="?/record" use:enhance={submitting}>
                    <input type="hidden" name="siteId" value={data.currentSite.id} />
                    <input type="hidden" name="sensorId" value={data.sensorId} />
                    <input type="hidden" name="point" value={step} />
                    <input type="hidden" name="readingSeq" value={item.readingSeq} />
                    <Button type="submit" variant="secondary" label={t(step === 'dry' ? 'calibrate.useDry' : 'calibrate.useWet')} {working} />
                  </form>
                </li>
              {/each}
            </ul>
          {/if}
        </section>
      {/if}
    </section>
  {/if}
{/if}

<style>
  .cf-calibrate__back {
    margin: 0 0 var(--cf-spacing-5);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
  }

  .cf-calibrate__back a {
    display: inline-flex;
    align-items: center;
    min-height: 44px;
    color: var(--cf-color-primary-text);
  }

  .cf-calibrate {
    display: grid;
    gap: var(--cf-spacing-5);
    max-width: 40rem;
  }

  .cf-calibrate__title {
    margin: 0;
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-title-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-title-line-height);
    overflow-wrap: anywhere;
  }

  .cf-calibrate__step,
  .cf-calibrate__recent-title {
    margin: 0;
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-body-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-calibrate__text,
  .cf-calibrate__last,
  .cf-calibrate__hint,
  .cf-calibrate__waiting {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-calibrate__hint {
    color: var(--cf-color-text-secondary);
  }

  .cf-calibrate__panel {
    display: grid;
    gap: var(--cf-spacing-3);
    padding: var(--cf-spacing-5);
    background: var(--cf-color-layer-01);
    border: 1px dashed var(--cf-color-border-strong);
  }

  .cf-calibrate__last {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
  }

  .cf-calibrate__result {
    margin: 0;
    font-family: var(--cf-type-hero-value-font-family);
    font-size: var(--cf-type-title-font-size);
    font-weight: var(--cf-type-hero-value-font-weight);
    line-height: var(--cf-type-title-line-height);
    overflow-wrap: anywhere;
  }

  .cf-calibrate__recent {
    display: grid;
    gap: var(--cf-spacing-3);
  }

  .cf-calibrate__list {
    display: grid;
    gap: var(--cf-spacing-3);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .cf-calibrate__item {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--cf-spacing-3);
    padding: var(--cf-spacing-4) var(--cf-spacing-5);
    background: var(--cf-color-layer-01);
    border: 1px solid var(--cf-color-border-subtle);
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
  }
</style>
