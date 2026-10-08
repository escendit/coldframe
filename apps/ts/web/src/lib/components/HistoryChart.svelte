<script lang="ts">
  import type { HistoryChart } from '$lib/lot-detail';
  import { historyDays } from '$lib/lot-detail';
  import { t } from '$lib/i18n';

  interface Props {
    chart: HistoryChart;
  }

  let { chart }: Props = $props();

  const slotWidth = 10;
  const barWidth = 6;
  const height = 120;
  const width = historyDays * slotWidth;

  /** The slot the readout shows: the picked bar, else the newest day. Reset when the picked quantity changes. */
  let pickedSlot: number | null = $state(null);
  let pickedQuantity: string | null = $state(null);
  const picked = $derived(pickedQuantity === chart.quantity ? pickedSlot : null);
  const shown = $derived(chart.bars.find((bar) => bar.slot === picked) ?? chart.latest);

  function pick(slot: number): void {
    const bar = chart.bars.reduce<(typeof chart.bars)[number] | null>((best, candidate) => (best === null || Math.abs(candidate.slot - slot) < Math.abs(best.slot - slot) ? candidate : best), null);
    if (bar !== null) {
      pickedQuantity = chart.quantity;
      pickedSlot = bar.slot;
    }
  }

  function slotAt(event: PointerEvent): number {
    const box = (event.currentTarget as Element).getBoundingClientRect();
    return Math.min(historyDays - 1, Math.max(0, Math.floor(((event.clientX - box.left) / box.width) * historyDays)));
  }

  function step(event: KeyboardEvent): void {
    const direction = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
    if (direction === 0 || shown === null) {
      return;
    }
    event.preventDefault();
    const index = chart.bars.findIndex((bar) => bar.slot === shown.slot);
    const next = chart.bars[index + direction];
    if (next !== undefined) {
      pickedQuantity = chart.quantity;
      pickedSlot = next.slot;
    }
  }
</script>

<!--
  History chart (UX-DR32, UX-DR33): 30 daily bars, drawn from the Server's daily low. Days without
  Readings are gaps, never zero bars. A bar is normal-style, a 1 px outline; the picked bar is solid, so
  selection is not by colour alone. With a low Threshold on a percent History (UX-DR5) the chart draws the
  band between low and high, a 2 px low line and a dashed 1 px high line, and a day whose low is under the
  low Threshold is a solid bar in the below-low token: a non-colour cue, named in the legend and the text
  summary. A tap or drag picks a bar and the readout names it. Nothing animates, so Reduce Motion needs
  nothing more.
-->
<figure class="cf-chart" data-quantity={chart.quantity}>
  <!-- The chart is one image with a text alternative; tap, drag and the arrow keys pick a bar for the readout. -->
  <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
  <div
    class="cf-chart__plot"
    role="img"
    aria-label={chart.summary}
    tabindex="0"
    onpointerdown={(event) => {
      pick(slotAt(event));
    }}
    onpointermove={(event) => {
      if (event.buttons > 0) {
        pick(slotAt(event));
      }
    }}
    onkeydown={step}
  >
    <svg class="cf-chart__svg" viewBox="0 0 {width} {height}" preserveAspectRatio="none" aria-hidden="true" focusable="false">
      {#if chart.band !== null}
        {@const top = chart.band.high === null ? 0 : (1 - chart.band.high) * height}
        <rect class="cf-chart__band" x="0" y={top} {width} height={(1 - chart.band.low) * height - top} />
        <line class="cf-chart__low-line" x1="0" x2={width} y1={(1 - chart.band.low) * height} y2={(1 - chart.band.low) * height} />
        {#if chart.band.high !== null}
          <line class="cf-chart__high-line" x1="0" x2={width} y1={top} y2={top} />
        {/if}
      {/if}
      {#each chart.bars as bar (bar.slot)}
        {@const barHeight = Math.max(1, bar.height * (height - 2))}
        <rect
          class="cf-chart__bar"
          class:cf-chart__bar--picked={shown?.slot === bar.slot}
          class:cf-chart__bar--below-low={bar.belowLow}
          data-day={bar.day}
          x={bar.slot * slotWidth + (slotWidth - barWidth) / 2}
          y={height - 1 - barHeight}
          width={barWidth}
          height={barHeight}
        />
      {/each}
    </svg>
  </div>
  <div class="cf-chart__axis">
    <span>{chart.axis.start}</span>
    <span>{chart.axis.end}</span>
  </div>
  {#if chart.legend !== null}
    <p class="cf-chart__legend">{chart.legend}</p>
  {/if}
  <figcaption class="cf-chart__readout">{shown === null ? t('lotDetail.chart.noReadings') : shown.readout}</figcaption>
</figure>

<style>
  .cf-chart {
    display: grid;
    gap: var(--cf-spacing-3);
    margin: 0;
  }

  .cf-chart__plot {
    min-height: 44px;
    touch-action: pan-y;
    background: var(--cf-color-layer-01);
    border: 1px solid var(--cf-color-border-subtle);
  }

  .cf-chart__svg {
    display: block;
    inline-size: 100%;
    block-size: 7.5rem;
  }

  .cf-chart__bar {
    fill: none;
    stroke: var(--cf-color-chart-bar);
    stroke-width: 1px;
    vector-effect: non-scaling-stroke;
  }

  .cf-chart__bar--picked {
    fill: var(--cf-color-chart-bar);
  }

  .cf-chart__bar--below-low {
    fill: var(--cf-color-chart-bar-below-low);
    stroke: var(--cf-color-chart-bar-below-low);
  }

  .cf-chart__band {
    fill: var(--cf-color-chart-band);
  }

  .cf-chart__low-line {
    stroke: var(--cf-color-primary-text);
    stroke-width: 2px;
    vector-effect: non-scaling-stroke;
  }

  .cf-chart__high-line {
    stroke: var(--cf-color-chart-high-line);
    stroke-width: 1px;
    stroke-dasharray: 4 3;
    vector-effect: non-scaling-stroke;
  }

  .cf-chart__legend {
    margin: 0;
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-chart__axis {
    display: flex;
    justify-content: space-between;
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-secondary);
  }

  .cf-chart__readout {
    font-family: var(--cf-type-meta-mono-font-family);
    font-size: var(--cf-type-meta-mono-font-size);
    line-height: var(--cf-type-meta-mono-line-height);
    color: var(--cf-color-text-primary);
  }
</style>
