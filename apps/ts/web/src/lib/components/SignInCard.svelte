<script lang="ts">
  import { onMount } from 'svelte';
  import { t } from '$lib/i18n';
  import { noticeCopy, type Notice } from '$lib/notices';
  import Button from './Button.svelte';
  import InlineNotice from './InlineNotice.svelte';

  interface Props {
    notice: Notice | null;
    /** Same-origin path to return to after sign-in; null for Garden. */
    returnTo: string | null;
  }

  let { notice, returnTo }: Props = $props();

  let working = $state(false);

  const startHref = $derived(returnTo === null ? '/signin/start' : `/signin/start?${new URLSearchParams({ returnTo }).toString()}`);
  const copy = $derived(notice === null ? null : noticeCopy[notice]);
  const action = $derived(copy?.action == null ? null : { label: t(copy.action), href: startHref, reload: true });

  onMount(() => {
    // Coming back through the browser history restores the page as it was left.
    const reset = (event: PageTransitionEvent): void => {
      if (event.persisted) {
        working = false;
      }
    };
    window.addEventListener('pageshow', reset);
    return () => {
      window.removeEventListener('pageshow', reset);
    };
  });
</script>

<div class="cf-signin-surface">
  <main class="cf-signin-card">
    <h1 class="cf-signin-card__mark">{t('app.name')}</h1>
    {#if copy !== null}
      <InlineNotice id="cf-signin-notice" message={t(copy.message)} {action} />
    {/if}
    <!-- SIGN IN is a GET: the probe route redirects to the package's /.oidc/signin. -->
    <form class="cf-signin-card__form" method="GET" action="/signin/start" data-sveltekit-reload onsubmit={() => (working = true)}>
      {#if returnTo !== null}
        <input type="hidden" name="returnTo" value={returnTo} />
      {/if}
      <Button type="submit" label={t('signin.action')} {working} workingLabel={t('signin.working')} />
    </form>
  </main>
</div>

<style>
  .cf-signin-surface {
    display: grid;
    place-items: center;
    box-sizing: border-box;
    min-height: 100vh;
    min-height: 100dvh;
    padding: var(--cf-spacing-gutter-mobile);
    /* The DS signature radial, the only gradient in the product. */
    background: radial-gradient(circle at 5% 5%, var(--cf-color-primary), var(--cf-color-secondary));
  }

  .cf-signin-card {
    display: grid;
    gap: var(--cf-spacing-6);
    box-sizing: border-box;
    width: min(100%, 26rem);
    padding: var(--cf-spacing-7);
    background: var(--cf-color-background);
    border: 1px solid var(--cf-color-border-subtle);
    color: var(--cf-color-text-primary);
  }

  .cf-signin-card__mark {
    margin: 0;
    font-family: var(--cf-type-headline-font-family);
    font-size: var(--cf-type-headline-font-size);
    font-weight: var(--cf-type-headline-font-weight);
    line-height: var(--cf-type-headline-line-height);
  }

  .cf-signin-card__form {
    display: grid;
  }
</style>
