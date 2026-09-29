<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    siteName: string;
    /** The one-sentence Site state, exposed as a heading ("No Readings yet"). */
    headline: string;
    subline: string;
    /** The Site menu trigger (mobile places it here; the web puts it on the Site tab). */
    menu?: Snippet;
  }

  let { siteName, headline, subline, menu }: Props = $props();
</script>

<!-- Site summary header (UX-DR21). No Readings yet, so there is no Site clock and no counts. -->
<section class="cf-site-summary" aria-labelledby="cf-site-summary-headline">
  <div class="cf-site-summary__top">
    <p class="cf-site-summary__name">{siteName}</p>
    {#if menu !== undefined}
      {@render menu()}
    {/if}
  </div>
  <h2 id="cf-site-summary-headline" class="cf-site-summary__headline">{headline}</h2>
  <p class="cf-site-summary__subline">{subline}</p>
</section>

<style>
  .cf-site-summary {
    display: grid;
    gap: var(--cf-spacing-3);
    margin-block-end: var(--cf-spacing-6);
    color: var(--cf-color-text-primary);
  }

  .cf-site-summary__top {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--cf-spacing-3);
  }

  .cf-site-summary__name {
    margin: 0;
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-site-summary__headline {
    margin: 0;
    font-family: var(--cf-type-headline-font-family);
    font-size: var(--cf-type-headline-font-size);
    font-weight: var(--cf-type-headline-font-weight);
    line-height: var(--cf-type-headline-line-height);
    overflow-wrap: anywhere;
  }

  .cf-site-summary__subline {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }
</style>
