<script lang="ts">
  import { onMount } from 'svelte';
  import { announce } from '$lib/announcer.svelte';
  import SignInCard from '$lib/components/SignInCard.svelte';
  import { t } from '$lib/i18n';
  import { noticeCopy } from '$lib/notices';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  onMount(() => {
    // The notice is rendered on the server; announce it once the regions are live (UX-DR104).
    if (data.notice !== null) {
      const copy = noticeCopy[data.notice];
      announce(t(copy.message), copy.priority);
    }
  });
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('signin.pageTitle') })}</title>
</svelte:head>

<SignInCard notice={data.notice} returnTo={data.returnTo} />
