<script lang="ts">
  import Button from '$lib/components/Button.svelte';
  import { locale, t } from '$lib/i18n';
  import { fractionOf, formatThreshold, turnOffAlerts, turnOnAlerts, validityMessage, validityOf, valueAt, type SensorThresholds } from '$lib/thresholds';

  interface Props {
    sensorId: string;
    /** The Sensor's name ("Soil moisture"). */
    label: string;
    thresholds: SensorThresholds;
    /** The step: 5 for the calibrated soil, whole units otherwise. */
    step: number;
    /** The latest converted Reading in the Sensor's unit, or null. */
    reading: number | null;
    /** Owner or Administrator: the Member's column has no control at all. */
    editable: boolean;
    low?: number | null;
    high?: number | null;
  }

  let { sensorId, label, thresholds, step, reading, editable, low = $bindable(null), high = $bindable(null) }: Props = $props();

  const unit = $derived(thresholds.unit);
  /** A percentage track is fixed at 0-100 and can be dragged; any other track fits its values and is typed or stepped. */
  const fixed = $derived(unit === '%');
  const scale = $derived.by(() => {
    if (fixed) {
      return { min: 0, max: 100 };
    }
    const known = [low, high, reading, thresholds.proposedLow].filter((value): value is number => value !== null && value !== undefined);
    if (known.length === 0) {
      return { min: 0, max: 100 };
    }
    const lowest = Math.min(...known);
    const highest = Math.max(...known);
    const pad = Math.max(step, (highest - lowest) * 0.25);
    return { min: Math.floor(lowest - pad), max: Math.ceil(highest + pad) };
  });

  const validity = $derived(validityOf({ low, high }));
  const message = $derived(validityMessage(validity));
  const at = (value: number): string => `${String(fractionOf(value, scale.min, scale.max) * 100)}%`;
  const describedBy = $derived(message === null ? undefined : `cf-threshold-${sensorId}-reason`);

  let track: HTMLElement | undefined = $state();
  let dragging: 'low' | 'high' | null = $state(null);

  function snapped(value: number): number {
    return Math.round(value / step) * step;
  }

  function move(side: 'low' | 'high', event: PointerEvent): void {
    if (track === undefined || !fixed) {
      return;
    }
    const box = track.getBoundingClientRect();
    const value = valueAt(1 - (event.clientY - box.top) / box.height, scale.min, scale.max, step);
    if (side === 'low') {
      low = value;
    } else {
      high = value;
    }
  }

  function press(side: 'low' | 'high', event: PointerEvent): void {
    if (!fixed) {
      return;
    }
    dragging = side;
    (event.currentTarget as Element).setPointerCapture(event.pointerId);
    move(side, event);
  }

  function key(side: 'low' | 'high', event: KeyboardEvent): void {
    const direction = event.key === 'ArrowUp' || event.key === 'ArrowRight' ? 1 : event.key === 'ArrowDown' || event.key === 'ArrowLeft' ? -1 : 0;
    if (direction === 0) {
      return;
    }
    event.preventDefault();
    const current = (side === 'low' ? low : high) ?? scale.min;
    const next = fixed ? Math.min(scale.max, Math.max(scale.min, current + direction * step)) : current + direction * step;
    if (side === 'low') {
      low = next;
    } else {
      high = next;
    }
  }

  function addHigh(): void {
    high = valueAt(fractionOf(low ?? scale.min, scale.min, scale.max) + 0.4, scale.min, scale.max, step);
    if (!fixed) {
      high = (low ?? scale.min) + step * 10;
    }
  }

  function turnOn(): void {
    ({ low, high } = turnOnAlerts({ low, high }, thresholds));
  }

  function turnOff(): void {
    ({ low, high } = turnOffAlerts());
  }

  const readingText = $derived(reading === null ? t('thresholds.noReading') : t('thresholds.now', { value: formatThreshold(reading, unit, locale) }));
  const lowName = $derived(t('thresholds.sliderLow', { name: label }));
  const highName = $derived(t('thresholds.sliderHigh', { name: label }));
</script>

<!--
  The Threshold column (UX-DR45): a vertical track on layer-01, a 2 px primary-text low line, the current
  Reading marker, a dashed "no high" marker while there is no high, the values to the right, drag or type.
  Rules live on the Server; this only edits two numbers and tells whether low is below high.
-->
<section class="cf-threshold" id="sensor-{sensorId}" data-sensor={sensorId} aria-labelledby="cf-threshold-{sensorId}-title">
  <h2 id="cf-threshold-{sensorId}-title" class="cf-threshold__title">{label}</h2>
  <div class="cf-threshold__body">
    <div class="cf-threshold__track" bind:this={track} class:cf-threshold__track--dragging={dragging !== null}>
      {#if low !== null && high !== null}
        <div class="cf-threshold__band" style="inset-block-start: {at(high)}; inset-block-end: {at(low)}"></div>
      {/if}
      {#if reading !== null}
        <div class="cf-threshold__reading" style="inset-block-end: {at(reading)}" title={readingText}></div>
      {/if}
      {#if low !== null}
        <div class="cf-threshold__low-line" style="inset-block-end: {at(low)}"></div>
        {#if editable}
          <div
            class="cf-threshold__handle"
            role="slider"
            tabindex="0"
            aria-label={lowName}
            aria-orientation="vertical"
            aria-valuemin={scale.min}
            aria-valuemax={scale.max}
            aria-valuenow={low}
            aria-valuetext={formatThreshold(low, unit, locale)}
            style="inset-block-end: {at(low)}"
            onpointerdown={(event) => {
              press('low', event);
            }}
            onpointermove={(event) => {
              if (dragging === 'low') {
                move('low', event);
              }
            }}
            onpointerup={() => {
              dragging = null;
            }}
            onkeydown={(event) => {
              key('low', event);
            }}
          ></div>
        {/if}
      {/if}
      {#if high !== null}
        <div class="cf-threshold__high-line" style="inset-block-end: {at(high)}"></div>
        {#if editable}
          <div
            class="cf-threshold__handle"
            role="slider"
            tabindex="0"
            aria-label={highName}
            aria-orientation="vertical"
            aria-valuemin={scale.min}
            aria-valuemax={scale.max}
            aria-valuenow={high}
            aria-valuetext={formatThreshold(high, unit, locale)}
            style="inset-block-end: {at(high)}"
            onpointerdown={(event) => {
              press('high', event);
            }}
            onpointermove={(event) => {
              if (dragging === 'high') {
                move('high', event);
              }
            }}
            onpointerup={() => {
              dragging = null;
            }}
            onkeydown={(event) => {
              key('high', event);
            }}
          ></div>
        {/if}
      {:else if low !== null}
        <div class="cf-threshold__no-high"></div>
      {/if}
    </div>

    <div class="cf-threshold__values">
      <p class="cf-threshold__now">{readingText}</p>

      {#if low === null}
        <p class="cf-threshold__off">{t('thresholds.alertsOff')}</p>
        {#if editable && thresholds.proposedLow !== undefined}
          <div class="cf-threshold__action"><Button label={t('thresholds.turnOn')} variant="secondary" onclick={turnOn} /></div>
        {/if}
        {#if editable && thresholds.proposedLow === undefined}
          <label class="cf-threshold__field" for="cf-threshold-{sensorId}-low">
            <span>{t('thresholds.low')}</span>
            <input id="cf-threshold-{sensorId}-low" type="number" step={step} min={fixed ? 0 : undefined} max={fixed ? 100 : undefined} bind:value={low} />
          </label>
        {/if}
      {:else if editable}
        <label class="cf-threshold__field" for="cf-threshold-{sensorId}-low">
          <span>{t('thresholds.low')}</span>
          <input
            id="cf-threshold-{sensorId}-low"
            type="number"
            step={step}
            min={fixed ? 0 : undefined}
            max={fixed ? 100 : undefined}
            aria-label={t('thresholds.lowFor', { name: label })}
            aria-invalid={validity === 'ok' ? undefined : 'true'}
            aria-describedby={describedBy}
            bind:value={low}
            onchange={() => {
              if (low !== null && fixed) {
                low = snapped(low);
              }
            }}
          />
          <span class="cf-threshold__unit">{unit}</span>
        </label>
        {#if high === null}
          <p class="cf-threshold__nohigh-text">{t('thresholds.noHigh')}</p>
          <div class="cf-threshold__action"><Button label={t('thresholds.addHigh')} variant="secondary" onclick={addHigh} /></div>
        {:else}
          <label class="cf-threshold__field" for="cf-threshold-{sensorId}-high">
            <span>{t('thresholds.high')}</span>
            <input
              id="cf-threshold-{sensorId}-high"
              type="number"
              step={step}
              min={fixed ? 0 : undefined}
              max={fixed ? 100 : undefined}
              aria-label={t('thresholds.highFor', { name: label })}
              aria-invalid={validity === 'ok' ? undefined : 'true'}
              aria-describedby={describedBy}
              bind:value={high}
              onchange={() => {
                if (high !== null && fixed) {
                  high = snapped(high);
                }
              }}
            />
            <span class="cf-threshold__unit">{unit}</span>
          </label>
          <div class="cf-threshold__action">
            <Button
              label={t('thresholds.clearHigh')}
              variant="ghost"
              onclick={() => {
                high = null;
              }}
            />
          </div>
        {/if}
        <div class="cf-threshold__action"><Button label={t('thresholds.turnOff')} variant="ghost" onclick={turnOff} /></div>
      {:else}
        <p class="cf-threshold__read">{t('thresholds.low')} <strong>{formatThreshold(low, unit, locale)}</strong></p>
        <p class="cf-threshold__read">{t('thresholds.high')} <strong>{high === null ? t('thresholds.noHigh') : formatThreshold(high, unit, locale)}</strong></p>
      {/if}

      {#if message !== null}
        <p id="cf-threshold-{sensorId}-reason" class="cf-threshold__error">{message}</p>
      {/if}
    </div>
  </div>
</section>

<style>
  .cf-threshold {
    display: grid;
    gap: var(--cf-spacing-4);
    padding: var(--cf-spacing-5);
    background: var(--cf-color-layer-01);
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-threshold__title {
    margin: 0;
    font-family: var(--cf-type-title-font-family);
    font-size: var(--cf-type-body-font-size);
    font-weight: var(--cf-type-title-font-weight);
    line-height: var(--cf-type-body-line-height);
  }

  .cf-threshold__body {
    display: flex;
    flex-wrap: wrap;
    gap: var(--cf-spacing-6);
  }

  .cf-threshold__track {
    position: relative;
    flex: none;
    inline-size: 3.5rem;
    block-size: 12rem;
    margin-block: var(--cf-spacing-4);
    background: var(--cf-color-background);
    border: 1px solid var(--cf-color-border-strong);
    touch-action: none;
  }

  .cf-threshold__band {
    position: absolute;
    inset-inline: 0;
    background: var(--cf-color-chart-band);
  }

  .cf-threshold__low-line,
  .cf-threshold__high-line,
  .cf-threshold__reading {
    position: absolute;
    inset-inline: -0.5rem;
    block-size: 0;
    pointer-events: none;
  }

  .cf-threshold__low-line {
    border-block-start: 2px solid var(--cf-color-primary-text);
  }

  .cf-threshold__high-line {
    border-block-start: 1px dashed var(--cf-color-chart-high-line);
  }

  .cf-threshold__reading {
    inset-inline: 0.75rem;
    border-block-start: 6px solid var(--cf-color-text-primary);
    border-radius: var(--cf-radius-none);
  }

  .cf-threshold__no-high {
    position: absolute;
    inset-block-start: 0;
    inset-inline: -0.5rem;
    border-block-start: 1px dashed var(--cf-color-border-strong);
    pointer-events: none;
  }

  .cf-threshold__handle {
    position: absolute;
    inset-inline: -0.5rem;
    block-size: 1.5rem;
    margin-block-end: -0.75rem;
    cursor: ns-resize;
  }

  .cf-threshold__handle:focus-visible {
    outline: var(--cf-spacing-focus-ring) solid var(--cf-color-focus);
    outline-offset: var(--cf-spacing-focus-offset);
  }

  .cf-threshold__values {
    display: grid;
    flex: 1 1 14rem;
    align-content: start;
    gap: var(--cf-spacing-3);
    min-inline-size: 0;
  }

  .cf-threshold__values p {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    overflow-wrap: anywhere;
  }

  .cf-threshold__now,
  .cf-threshold__off,
  .cf-threshold__nohigh-text {
    color: var(--cf-color-text-secondary);
  }

  .cf-threshold__field {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-3);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
  }

  .cf-threshold__field input {
    box-sizing: border-box;
    inline-size: 6rem;
    min-block-size: 44px;
    padding: 0 var(--cf-spacing-3);
    background: var(--cf-color-background);
    color: var(--cf-color-text-primary);
    border: 1px solid var(--cf-color-border-strong);
    font: inherit;
  }

  .cf-threshold__field input:focus-visible {
    outline: var(--cf-spacing-focus-ring) solid var(--cf-color-focus);
    outline-offset: var(--cf-spacing-focus-offset);
  }

  .cf-threshold__error {
    color: var(--cf-color-support-error-text);
  }

</style>
