<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    /** Puts each child on a solid plate of the hatch ground, so text never sits on the lines. */
    plate?: boolean;
    children?: Snippet;
  }

  let { plate = false, children }: Props = $props();

  // One pattern per instance: two hatched blocks on a page must not share an id.
  const pattern = $props.id();
</script>

<!--
  Hatch fill (UX-DR12): 1.5 px lines every 8 px at 135° on the hatch ground, drawn as an inline SVG
  pattern coloured by the tokens. It fills its own box behind its children; give it the size and
  layout of the block it hatches.
-->
<div class="cf-hatch" class:cf-hatch--plate={plate}>
  <svg class="cf-hatch__fill" aria-hidden="true" focusable="false">
    <defs>
      <pattern id={pattern} patternUnits="userSpaceOnUse" width="8" height="8" patternTransform="rotate(45)">
        <line class="cf-hatch__line" x1="0" y1="0" x2="0" y2="8" />
      </pattern>
    </defs>
    <rect class="cf-hatch__ground" width="100%" height="100%" />
    <rect width="100%" height="100%" fill="url(#{pattern})" />
  </svg>
  {@render children?.()}
</div>

<style>
  .cf-hatch {
    position: relative;
    isolation: isolate;
    display: grid;
  }

  .cf-hatch__fill {
    position: absolute;
    inset: 0;
    z-index: -1;
    inline-size: 100%;
    block-size: 100%;
  }

  .cf-hatch__ground {
    fill: var(--cf-color-status-hatch-ground);
  }

  .cf-hatch__line {
    stroke: var(--cf-color-status-hatch-line);
    stroke-width: 1.5px;
  }

  .cf-hatch--plate > :global(:not(.cf-hatch__fill)) {
    justify-self: start;
    padding: var(--cf-spacing-2) var(--cf-spacing-3);
    background: var(--cf-color-status-hatch-ground);
  }
</style>
