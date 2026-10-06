<script lang="ts">
  import { onMount } from 'svelte';
  import { locale, t } from '$lib/i18n';
  import { formatDuration, formatWhen } from '$lib/i18n/format';
  import Icon from './Icon.svelte';

  interface Props {
    siteName: string;
    /** When the data below was last read from the Server. */
    staleSince: Date;
    /** The clock of the render; in the browser the age then ticks on its own. */
    now: Date;
    timeZone: string;
  }

  let { siteName, staleSince, now, timeZone }: Props = $props();

  /** The browser's clock, once it ticks; until then the age is told against `now`. */
  let ticked: Date | null = $state(null);
  const clock = $derived(ticked ?? now);
  const age = $derived(formatDuration(clock.getTime() - staleSince.getTime(), { withMinutes: true }));

  // The only timer of the web app: the age moves once a minute. It is plain text, never announced (UX-DR106).
  onMount(() => {
    const timer = setInterval(() => {
      ticked = new Date();
    }, 60_000);
    return () => {
      clearInterval(timer);
    };
  });
</script>

<!-- Stale header (UX-DR24): replaces the Site summary header while the Server cannot be reached. -->
<section class="cf-stale-header" aria-labelledby="cf-stale-header-age">
  <p class="cf-stale-header__title"><Icon name="cloud--offline" size={20} /><span>{t('stale.title', { siteName })}</span></p>
  <h2 id="cf-stale-header-age" class="cf-stale-header__age">{t('stale.age', { age })}</h2>
  <p class="cf-stale-header__detail">{t('stale.detail', { time: formatWhen(staleSince, clock, locale, timeZone) })}</p>
</section>

<style>
  .cf-stale-header {
    display: grid;
    gap: var(--cf-spacing-3);
    margin-block-end: var(--cf-spacing-6);
    color: var(--cf-color-text-primary);
  }

  .cf-stale-header__title {
    display: flex;
    align-items: center;
    gap: var(--cf-spacing-3);
    margin: 0;
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-stale-header__age {
    margin: 0;
    font-family: var(--cf-type-headline-font-family);
    font-size: var(--cf-type-headline-font-size);
    font-weight: var(--cf-type-headline-font-weight);
    line-height: var(--cf-type-headline-line-height);
    color: var(--cf-color-stale-ink);
    overflow-wrap: anywhere;
  }

  .cf-stale-header__detail {
    margin: 0;
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    color: var(--cf-color-text-secondary);
  }
</style>
